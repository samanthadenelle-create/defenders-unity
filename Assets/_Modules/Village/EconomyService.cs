// =============================================================================
// EconomyService — DEF-78: full multi-resource tracking.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES:
//   Owns the four build-economy resources — Wood, Stone, Iron, Crystals — and
//   exposes a clean API for spending, earning, and checking affordability across
//   any combination of them. Previously a Wood-only stub; this pass completes
//   the full spec.
//
// API:
//   bool CanAfford(ResourceCost)        — multi-resource affordability check
//   bool TrySpend(ResourceCost)         — spend atomically; returns false + no-ops on failure
//   void Grant(ResourceCost)            — add resources (wave rewards, harvesting, etc.)
//   void Grant(int w, int s, int i, int c) — convenience overload
//   event Action<ResourceSnapshot> OnChanged — fires after every mutation
//
// BACKWARDS COMPAT:
//   The old CanAfford(int cost) and Spend(int cost) (Wood-only) remain as
//   deprecated aliases so TowerPlacementSystem / TowerUpgradeButton don't break.
//
// STARTING RESOURCES: editable in the Inspector. Defaults match the existing
//   stub (Wood 200, Iron 80) so existing scenes are unaffected.
//
// DEF-121 / WO-230 — the four harvestables are Wood / Stone / Iron / Crystals.
//   The retired "Stone" axis is repurposed to FOOD. Like Crystals, Stone is now
//   GameState-backed (GameState.Resources.Stone) — the single wallet field the HUD,
//   BuildingUpgradePanel and harvest paths all share — so the build economy and the
//   harvest/upgrade economy never diverge. No "Magic" harvestable exists: Magic is
//   a building-UPGRADE tech axis only (see ResourceBuildingProgression).
//
// PERSISTENCE (WO-842 — SINGLE WALLET, all five resources): Wood/Iron are now
//   GameState-backed (GameState.Wood / GameState.Iron — the SAME fields the
//   building-upgrade flow's ResourceLedger reads and spends), exactly like Stone/
//   Crystals/Coins (GameState.Resources.*). Every read/spend/grant here reads/
//   writes THROUGH GameStateService, so the wallet the HUD shows, the wallet the
//   shop charges, and the wallet the upgrade ledger spends are ONE store and can
//   never diverge again.
//
//   THE BUG THIS KILLED (owner F8 2026-08-02, [Flow:Upgrade] "TryUpgrade FALSE
//   (needed W800..., have W985646...)"): Wood/Iron used to live in a divergent
//   in-session pool here (starter 200/80, reset every scene load). Grant mirrored
//   INTO GameState but TrySpend/CanAfford ran against the pool, so wood granted
//   GameState-side (dev tools, save load, promo) was riches the HUD showed but the
//   pool-checked paths refused to spend. WO-842 unifies the authority.
//
//   The serialized _wood/_iron fields survive ONLY as the no-GameState FALLBACK
//   pool (EditMode tests / headless boots with no save service) — when
//   GameStateService.Instance.State exists it is ALWAYS the authority.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// Snapshot of all four economy resources — passed with <see cref="EconomyService.OnChanged"/>.
    /// </summary>
    public readonly struct ResourceSnapshot
    {
        public readonly int Wood;
        [Newtonsoft.Json.JsonProperty("Food")] public readonly int Stone;
        public readonly int Iron;
        public readonly int Crystals;

        public ResourceSnapshot(int wood, int stone, int iron, int crystals)
        {
            Wood     = wood;
            Stone     = stone;
            Iron     = iron;
            Crystals = crystals;
        }
    }

    /// <summary>
    /// A multi-resource cost or reward. Zero means "no requirement" for that slot.
    /// </summary>
    [Serializable]
    public struct ResourceCost
    {
        [Min(0)] public int Wood;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [Newtonsoft.Json.JsonProperty("Food"), Min(0)] public int Stone;
        [Min(0)] public int Iron;
        [Min(0)] public int Crystals;
        // GOLD (Coins) — the player-facing currency the vendor SHOPS charge (gear + potions).
        // Backed by GameState.Resources.Coins (the canonical coin store the town HUD reads),
        // NOT an in-session pool. Added last with a default of 0 so every existing caller of
        // the (wood,stone,iron,crystals) constructor compiles unchanged (split-economy: building
        // UPGRADES stay on Wood/Iron/Crystals; shops move to Gold).
        [Min(0)] public int Coins;

        public ResourceCost(int wood = 0, int stone = 0, int iron = 0, int crystals = 0, int coins = 0)
        {
            Wood     = wood;
            Stone     = stone;
            Iron     = iron;
            Crystals = crystals;
            Coins    = coins;
        }

        /// <summary>True when all values are zero — a free action.</summary>
        public bool IsZero => Wood == 0 && Stone == 0 && Iron == 0 && Crystals == 0 && Coins == 0;

        public static ResourceCost WoodOnly(int amount)     => new ResourceCost(wood:     amount);
        public static ResourceCost StoneOnly(int amount)     => new ResourceCost(stone:     amount);
        public static ResourceCost IronOnly(int amount)     => new ResourceCost(iron:     amount);
        public static ResourceCost CrystalsOnly(int amount) => new ResourceCost(crystals: amount);
    }

    /// <summary>
    /// Singleton resource tracker for the build economy. Provides multi-resource
    /// affordability checks, atomic spending, and a change event for the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EconomyService : MonoBehaviour, IEconomy
    {
        public static EconomyService Instance { get; private set; }

        // ── Starting amounts (Inspector) ──────────────────────────────────────

        [Header("Starting Resources (Wood/Iron — FALLBACK pool, used only when no GameState exists)")]
        [SerializeField, Min(0)] private int _wood     = 200; // WO-842: fallback only (EditMode/no-save boots); GameState.Wood is the authority
        [SerializeField, Min(0)] private int _iron     = 80;  // WO-842: fallback only; GameState.Iron is the authority
        // NOTE (WO-131 / DEF-121 / WO-842): ALL five resources are GameState-backed
        // read/write-through when a save service exists — see the properties below.

        // ── Public read-only properties ───────────────────────────────────────

        /// <summary>
        /// WO-842 — Wood is GameState-backed (GameState.Wood, the SAME field the
        /// building-upgrade ResourceLedger spends). Reads through GameStateService;
        /// falls back to the in-session pool only when no save service exists
        /// (EditMode tests / headless boots).
        /// </summary>
        public int Wood
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Wood : _wood;
            }
        }

        /// <summary>WO-842 — Iron is GameState-backed (GameState.Iron). See <see cref="Wood"/>.</summary>
        public int Iron
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Iron : _iron;
            }
        }

        /// <summary>
        /// DEF-121 — Stone is GameState-backed (GameState.Resources.Stone), the single
        /// wallet field the HUD / BuildingUpgradePanel / harvest paths share. Reads
        /// through GameStateService; returns 0 when state is absent.
        /// </summary>
        public int Stone
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Stone : 0;
            }
        }

        /// <summary>
        /// WO-131 — crystals are the SINGLE source of truth on
        /// GameState.Resources.Crystals (the store the HUD/BuildMenu display), NOT a
        /// local pool. Reads through GameStateService; returns 0 when state is absent.
        /// </summary>
        public int Crystals
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Crystals : 0;
            }
        }

        /// <summary>
        /// GOLD (Coins) — the player-facing currency the vendor shops charge. Reads the
        /// canonical store on GameState.Resources.Coins (the same field the town HUD's
        /// SetGold displays), NOT a local pool. Returns 0 when state is absent.
        /// </summary>
        public int Coins
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Coins : 0;
            }
        }

        public ResourceSnapshot Snapshot => new ResourceSnapshot(Wood, Stone, Iron, Crystals);

        // ── Pet / Outpost territory (WO-106: pet resource farming + outpost system) ──
        // These let the economy be the single source for passive rates, secured count,
        // and a simple difficulty/scaling multiplier consumed by wave systems later.
        // Incremented by ClaimableCamp on successful defense secure. Pet harvest and
        // outpost trickle both route through Grant so in-session mirrors + listeners
        // (HUD) stay consistent with GameState.

        /// <summary>Number of outposts successfully claimed + defended (secured) this run/save.</summary>
        public int SecuredOutpostCount { get; private set; }

        /// <summary>
        /// Simple territory control multiplier for difficulty scaling and passive
        /// economy power. Starts at 1.0; each secured outpost adds a small bonus.
        /// Tune via code or later ProgressionConstants. Read-only for consumers
        /// (WaveManager, enemy spawners, offline accrual).
        /// </summary>
        public float TerritoryMultiplier => 1f + 0.05f * SecuredOutpostCount;

        /// <summary>
        /// Call when an outpost camp is successfully defended and secured.
        /// Increments the count (and thus the multiplier) and notifies listeners.
        /// Idempotent per camp via the caller (ClaimableCamp persists the secured flag).
        /// </summary>
        public void OnOutpostSecured()
        {
            SecuredOutpostCount = Mathf.Max(0, SecuredOutpostCount + 1);
            NotifyChanged();
        }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fires after any resource change with the new totals.</summary>
        public event Action<ResourceSnapshot> OnChanged;

        // ── Bootstrap ─────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
            => EnsureAvailable();

        /// <summary>Restore the normal economy authority when a scene arrives before bootstrap.</summary>
        public static EconomyService EnsureAvailable()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[EconomyService]");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            Instance = go.AddComponent<EconomyService>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private bool _bridgeAttached;

        private void OnEnable()  => AttachResourcesBridge();
        private void Start()     => AttachResourcesBridge();   // retry: GameStateService may not have existed at OnEnable

        /// <summary>
        /// HUD-refresh bridge: crystal/stone gains from harvest/mine/empower/camp paths
        /// write GameState.Resources and raise GameStateService.ResourcesChanged (Core),
        /// NOT this service's OnChanged. The village HUD listens to OnChanged, so without
        /// this bridge it would not visually update on those GameState-backed gains.
        /// Re-emit OnChanged whenever the GameState resource wallet changes. Idempotent —
        /// guards against double-subscribe across OnEnable + Start.
        /// </summary>
        private void AttachResourcesBridge()
        {
            if (_bridgeAttached) return;
            var gs = GameStateService.Instance;
            if (gs == null) return;
            gs.ResourcesChanged.AddListener(OnGameStateResourcesChanged);
            _bridgeAttached = true;
        }

        private void OnDisable()
        {
            var gs = GameStateService.Instance;
            if (gs != null) gs.ResourcesChanged.RemoveListener(OnGameStateResourcesChanged);
            _bridgeAttached = false;
        }

        /// <summary>Re-emit <see cref="OnChanged"/> when the Core resource wallet changes
        /// (crystal/stone gains route through GameStateService, not this service's methods).</summary>
        private void OnGameStateResourcesChanged() => NotifyChanged();

        private void OnDestroy()
        {
            var gs = GameStateService.Instance;
            if (gs != null) gs.ResourcesChanged.RemoveListener(OnGameStateResourcesChanged);
            _bridgeAttached = false;
            if (Instance == this) Instance = null;
        }

        // ── Multi-resource API ────────────────────────────────────────────────

        /// <summary>Returns true when the (unified, GameState-backed) wallet covers <paramref name="cost"/>.
        /// WO-842: every slot reads through the read-through properties, so the check runs against the
        /// SAME store the spend debits — no more pool-vs-ledger asymmetry.</summary>
        public bool CanAfford(ResourceCost cost)
        {
            return Wood   >= cost.Wood        // WO-842 — GameState-backed (fallback pool when no save service)
                && Stone   >= cost.Stone        // DEF-121 — GameState-backed
                && Iron   >= cost.Iron        // WO-842 — GameState-backed
                && Crystals >= cost.Crystals  // WO-131 — GameState-backed
                && Coins  >= cost.Coins;      // GOLD — GameState.Resources.Coins (shops)
        }

        /// <summary>
        /// Atomically spends <paramref name="cost"/> if affordable. Returns true on
        /// success, false (no mutation) when any resource is short. WO-842: EVERY slot
        /// is deducted from the single GameState-backed wallet (Wood/Iron from
        /// GameState.Wood/Iron — the same fields ResourceLedger spends; Stone/Crystals/
        /// Coins from GameState.Resources). The in-session pool is debited only in the
        /// no-GameState fallback (EditMode tests / headless boots).
        /// </summary>
        public bool TrySpend(ResourceCost cost)
        {
            if (!CanAfford(cost)) return false;
            // WO-1374 — FUNNEL STEP 5 ("raid reward spent"). Cheap and silent until armed:
            // RaidFunnel.RewardSpent reads one PlayerPrefs int and returns unless a raid has
            // actually been won since the step last fired, so this is safe on a spend path.
            // Placed AFTER the affordability gate so a refused spend never counts as one.
            DeNelle.Core.Diagnostics.Guard.Try("Funnel", "reward spent (economy)",
                () => DeNelle.Core.Analytics.RaidFunnel.RewardSpent("EconomyService.TrySpend"));
            if (cost.Wood > 0 || cost.Iron > 0)
            {
                var gs = GameStateService.Instance;
                var state = gs?.State;
                if (state != null)
                {
                    state.Wood = Mathf.Max(0, state.Wood - cost.Wood);
                    state.Iron = Mathf.Max(0, state.Iron - cost.Iron);
                    gs.Save();
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                        $"TrySpend debited GameState (single wallet, WO-842) -W{cost.Wood} -I{cost.Iron} -> Wood={state.Wood} Iron={state.Iron}");
                    gs.ResourcesChanged?.Invoke();   // upgrade panel / HUD readers of the ledger refresh
                }
                else
                {
                    _wood -= cost.Wood;
                    _iron -= cost.Iron;
                    // ── ECON-SWEEP 2026-08-16 (defect 1) ──────────────────────────────────
                    // The header calls this the "EditMode / headless boot" path, but NOTHING
                    // enforced that: it is a plain runtime null check, so in a real play session
                    // a missing GameStateService silently moved the UNSAVED _wood/_iron fields
                    // while the wallet the HUD and ResourceLedger read stayed untouched. At Warn
                    // it never reached the F8 break-log either. IN PLAY THIS IS A HARD FAILURE —
                    // the charge is not persisted and does not match what the player is shown —
                    // so it is FlowTrace.Fail. Outside play (EditMode fixtures, editor tooling)
                    // the fallback pool IS the intended wallet, so it stays a Warn there.
                    ReportFallbackPoolMutation(
                        $"TrySpend debited FALLBACK pool (no GameState) -W{cost.Wood} -I{cost.Iron} -> W{_wood} I{_iron}");
                }
            }
            if (cost.Stone > 0)
                GameStateService.Instance?.AddStone(-cost.Stone);           // DEF-121 — GameState-backed spend
            if (cost.Crystals > 0)
                GameStateService.Instance?.AddCrystals(-cost.Crystals);   // GameState-backed spend
            if (cost.Coins > 0)
                AddCoins(-cost.Coins);                                    // GOLD — GameState.Resources.Coins (shops)
            NotifyChanged();
            return true;
        }

        /// <summary>Adds resources — for wave rewards, harvesting, etc. Negative values are clamped to 0.
        /// WO-842: Wood/Iron are granted ONCE, directly into the single GameState-backed wallet
        /// (GameState.Wood/Iron — the store CanAfford/TrySpend and ResourceLedger all read); the old
        /// pool-plus-mirror double write is gone. Deliberately does NOT Save() on this hot path
        /// (wave rewards / harvest ticks) — persistence rides the next Save; GrantSpendable is the
        /// persist-now dev seam. Falls back to the in-session pool when no save service exists.
        ///
        /// <para>WO-857 / WO-901 Phase F — THE TOWN BANK CAP. Wood/Iron/Stone are clamped to the
        /// storage ceiling (<see cref="DeNelle.Core.Economy.TownBankCapacity"/>): baseCap + the
        /// storageCapacity of every built lumberyard / foundry / silo. Overflow is LOST and the
        /// player is WARNED (owner ruling 2026-08-04 — clamp-and-warn, uniformly). CRYSTALS AND
        /// COINS ARE NEVER CLAMPED — premium currency is uncapped by design. This is the single
        /// choke every income source in the game flows through; use <see cref="GrantUncapped"/>
        /// for dev/AutoPilot funding that must not be storage-gated.</para></summary>
        public ResourceCost Grant(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.EarnedIncome);

        /// <summary>
        /// A grant of a quantity the player PAID FOR or was PROMISED AN EXACT NUMBER OF — pack-store
        /// entitlements, promo-code redemptions, referral payouts, battle-pass tiers. It is NEVER
        /// clamped by the town bank cap: an advertised quantity always arrives in full.
        /// <para>WHY THIS EXISTS (2026-08-04): the first cut clamped this path too, and a pack
        /// advertising 5,000 stone delivered 1,920 into a starter wallet — caught by
        /// PackGrantRegression. That is not balance, it is selling something and not delivering it.
        /// The owner's clamp-and-warn ruling (WO-901 §5) governs what the player EARNS; storage
        /// pressure must never become a mechanism that under-delivers a purchase.</para>
        /// </summary>
        public ResourceCost GrantPurchased(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.PurchasedOrPromised);

        /// <summary>
        /// DEV / HEADLESS-HARNESS grant that BYPASSES the town bank cap. Identical to
        /// <see cref="Grant(ResourceCost)"/> in every other respect.
        /// <para>Exists so the AutoPilot fleet and the dev resource tools can fund a gate they are
        /// only trying to walk THROUGH, without the storage ceiling turning an infrastructure
        /// top-up into a test failure (WO-857 §3.3 "dev grant — optional bypass flag for AutoPilot").
        /// NEVER call this from a player-facing income path: doing so silently re-opens the
        /// uncapped economy the cap exists to close. For a PAID grant use
        /// <see cref="GrantPurchased"/> — the intent is different and it is named separately so a
        /// reader can tell a purchase from a cheat.</para>
        /// </summary>
        public ResourceCost GrantUncapped(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.DevHarness);

        /// <summary>
        /// ECON-SWEEP 2026-08-16 (defect 2) — the grant path now RETURNS THE APPLIED BASKET, i.e.
        /// what actually landed in the wallet after <see cref="DeNelle.Core.Economy.TownBankCapacity"/>
        /// clamping, not what was requested. Callers that LOG or POP a "+N" for the player must read
        /// this, never their own request locals: with a full store the two differ, and a readout of
        /// the pre-clamp number is how a silent loss hides (the pattern OfflineHarvestService already
        /// documents). Crystals and Coins are never clamped, so they always come back unchanged.
        /// </summary>
        private ResourceCost GrantInternal(ResourceCost amount, DeNelle.Core.Economy.BankGrantKind kind)
        {
            // ONE place decides whether the cap applies at all (Law 5).
            bool applyBankCap = DeNelle.Core.Economy.TownBankCapacity.IsClampable(kind);
            int wood = Mathf.Max(0, amount.Wood);
            int iron = Mathf.Max(0, amount.Iron);
            if (wood > 0 || iron > 0)
            {
                var gsw = GameStateService.Instance;
                if (gsw != null && gsw.State != null)
                {
                    if (applyBankCap)
                    {
                        // Clamp against the LIVE total for each axis (the one authority, WO-842).
                        if (wood > 0)
                            wood = DeNelle.Core.Economy.TownBankCapacity.ClampGrant(
                                DeNelle.Core.Economy.BankResource.Wood, gsw.State.Wood, wood, "Grant", out _);
                        if (iron > 0)
                            iron = DeNelle.Core.Economy.TownBankCapacity.ClampGrant(
                                DeNelle.Core.Economy.BankResource.Iron, gsw.State.Iron, iron, "Grant", out _);
                    }
                    if (wood > 0) gsw.State.Wood = Mathf.Max(0, gsw.State.Wood + wood);
                    if (iron > 0) gsw.State.Iron = Mathf.Max(0, gsw.State.Iron + iron);
                    DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                        $"Grant +W{wood} +I{iron} -> GameState Wood={gsw.State.Wood} Iron={gsw.State.Iron} (single wallet, WO-842; kind={kind}, bankCap={applyBankCap})");
                }
                else
                {
                    // No save service (EditMode / headless boot): the in-session fallback pool IS the
                    // wallet here, so the cap must be measured against IT, not against a zero GameState.
                    if (applyBankCap)
                    {
                        if (wood > 0)
                            wood = DeNelle.Core.Economy.TownBankCapacity.ClampGrant(
                                DeNelle.Core.Economy.BankResource.Wood, _wood, wood, "Grant(fallback)", out _);
                        if (iron > 0)
                            iron = DeNelle.Core.Economy.TownBankCapacity.ClampGrant(
                                DeNelle.Core.Economy.BankResource.Iron, _iron, iron, "Grant(fallback)", out _);
                    }
                    _wood += wood;
                    _iron += iron;
                    // ECON-SWEEP 2026-08-16 (defect 1) — same ruling as TrySpend above: income that
                    // lands in the unsaved fallback pool DURING PLAY is money the player never keeps
                    // and never sees, so it Fails (F8-visible). See ReportFallbackPoolMutation.
                    ReportFallbackPoolMutation(
                        $"Grant(+W{wood} +I{iron}) landed in the FALLBACK pool (no GameState) -> W{_wood} I{_iron}");
                }
            }

            int stone = Mathf.Max(0, amount.Stone);
            if (stone > 0)
            {
                if (applyBankCap)
                    stone = DeNelle.Core.Economy.TownBankCapacity.ClampGrant(
                        DeNelle.Core.Economy.BankResource.Stone,
                        DeNelle.Core.Economy.TownBankCapacity.CurrentOf(DeNelle.Core.Economy.BankResource.Stone),
                        stone, "Grant", out _);
                if (stone > 0)
                    GameStateService.Instance?.AddStone(stone);       // DEF-121 — GameState-backed grant
            }
            // CRYSTALS + COINS: NEVER upper-clamped. Owner ruling 2026-08-04 (WO-901 §6) — premium /
            // bottleneck currency is uncapped (CoC precedent: gems uncapped, gold/elixir storage-capped).
            // TownBankCapacity.UncappableResources is the named enforcement; TownBankCapRegression
            // case [no-crystal-cap] FAILS the build if a crystal cap is ever introduced.
            int crystals = Mathf.Max(0, amount.Crystals);
            if (crystals > 0)
                GameStateService.Instance?.AddCrystals(crystals);   // WO-131 — GameState-backed grant
            int coins = Mathf.Max(0, amount.Coins);
            if (coins > 0)
                AddCoins(coins);                                    // GOLD — GameState.Resources.Coins (sell refunds)
            NotifyChanged();
            // ECON-SWEEP 2026-08-16 (defect 2) — every local above is POST-clamp, so this is the
            // APPLIED basket. Return it so a caller can log/pop what actually landed.
            return new ResourceCost(wood, stone, iron, crystals, coins);
        }

        /// <summary>
        /// ECON-SWEEP 2026-08-16 (defect 1) — the ONE reporter for a mutation that landed in the
        /// unsaved in-session <c>_wood</c>/<c>_iron</c> fallback pool.
        /// <para>
        /// The fallback exists for EditMode fixtures and editor tooling, where no GameStateService
        /// is installed and the pool IS the wallet — legitimate, so it logs at Warn there. IN PLAY
        /// it is a HARD FAILURE and is logged with <c>FlowTrace.Fail</c>: the resources moved into
        /// a field that is never persisted and that neither the HUD nor ResourceLedger reads, so a
        /// wave reward / harvest banking / build charge is silently wrong and lost on reload. Fail
        /// is also the severity the F8 BreakCaptureHarness surfaces; the old Warn was invisible to
        /// it, which is why this ran unnoticed. Never downgrade it back to Warn.
        /// </para>
        /// </summary>
        private static void ReportFallbackPoolMutation(string detail)
        {
            if (Application.isPlaying)
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Eco",
                    detail + " -- IN PLAY: no GameStateService, so this mutation is NOT PERSISTED and " +
                    "is invisible to the HUD/ResourceLedger wallet. The economy is diverging.");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Eco",
                    detail + " (edit mode / no play session -- the fallback pool is the intended wallet here).");
        }

        /// <summary>Convenience overload — specify only the resources you want to grant.</summary>
        public void Grant(int wood = 0, int stone = 0, int iron = 0, int crystals = 0)
        {
            Grant(new ResourceCost(wood, stone, iron, crystals));
        }

        /// <summary>
        /// DEV grant with a persist-now guarantee (WO-842: the wallets are UNIFIED, so
        /// this is no longer a "write both stores" shim).
        /// <para>
        /// <see cref="Grant(ResourceCost)"/> already lands Wood/Iron in the single
        /// GameState-backed wallet (and Stone/Crystals/Coins via AddStone/AddCrystals/
        /// AddCoins), but on the hot income path it deliberately does not Save() the
        /// Wood/Iron write. This dev seam adds the stronger guarantee: persist + announce
        /// immediately so a dev grant survives reload and every GameState-bound listener
        /// refreshes at once.
        /// </para>
        /// </summary>
        /// <remarks>
        /// ECON-SWEEP 2026-08-16 (defect 2) — RETURNS THE APPLIED BASKET (post town-bank-cap), not
        /// the request. Any caller that shows the player a number for this grant (a log line, a
        /// "+N" pop, a toast) MUST read the return value: with a full store the applied wood/iron/
        /// stone are smaller than asked, and popping the requested figure tells her she received
        /// resources she did not get. Statement-style calls that ignore the value still compile.
        /// </remarks>
        public ResourceCost GrantSpendable(int wood = 0, int stone = 0, int iron = 0, int crystals = 0)
        {
            var applied = Grant(new ResourceCost(wood, stone, iron, crystals));
            var gs = GameStateService.Instance;
            if (gs != null && gs.State != null && (wood > 0 || iron > 0))
            {
                gs.Save();
                gs.ResourcesChanged.Invoke();
            }
            return applied;
        }

        /// <summary>
        /// <see cref="GrantSpendable"/> for a PAID / ADVERTISED quantity — the pack-store entitlement
        /// path (PackStoreVM.ApplyPackContents resolves THIS method by name). Never clamped by the
        /// town bank cap: what the player bought lands in full. See <see cref="GrantPurchased"/>.
        /// </summary>
        public ResourceCost GrantSpendablePurchased(int wood = 0, int stone = 0, int iron = 0, int crystals = 0)
        {
            var applied = GrantPurchased(new ResourceCost(wood, stone, iron, crystals));
            var gs = GameStateService.Instance;
            if (gs != null && gs.State != null && (wood > 0 || iron > 0))
            {
                gs.Save();
                gs.ResourcesChanged.Invoke();
            }
            return applied;
        }

        /// <summary>
        /// <see cref="GrantSpendable"/> that BYPASSES the town bank cap — DEV / AutoPilot only.
        /// See <see cref="GrantUncapped"/> for why this exists and why no player-facing income path
        /// may call it. Kept as a separate NAME (not a defaulted parameter) so the choice of capped
        /// vs. uncapped is explicit at every call site.
        /// <para/>
        /// ⚠ THE DEV OVERLAYS RESOLVE **THIS** METHOD (fixed 2026-08-15). The retired note here said
        /// the separate name existed so the reflected lookups in AdminOverlay / OwnerDevToolsOverlay
        /// "keep resolving to the capped method unchanged" — that was the DEFECT, not the design: a
        /// 50,000 wood dev grant into a 2,500 TownBankCapacity silently lost ~95% of itself, and
        /// because both lookups are reflection-BY-STRING no compile error or source lint could show
        /// it. Both overlays now resolve <c>GrantSpendableUncapped</c>, and
        /// DevGrantUncappedRegression FAILS if a dev surface is re-bound to the capped grant.
        /// </summary>
        public ResourceCost GrantSpendableUncapped(int wood = 0, int stone = 0, int iron = 0, int crystals = 0)
        {
            var applied = GrantUncapped(new ResourceCost(wood, stone, iron, crystals));
            var gs = GameStateService.Instance;
            if (gs != null && gs.State != null && (wood > 0 || iron > 0))
            {
                gs.Save();
                gs.ResourcesChanged.Invoke();
            }
            return applied;
        }

        // ── Unified resource income API (WO-106 continuation) ─────────────────
        // All new pet harvesting, outpost ticks, node claims, troop rewards, etc.
        // MUST go through here (or the Grant overloads) so in-session mirrors,
        // persisted GameState, and OnChanged listeners stay consistent.
        // This is the single source of truth for the entire resource economy.

        /// <summary>
        /// Preferred unified entry point for all harvesting / passive income.
        /// Maps the Core-canonical ResourceType (Iron/Wood/Stone/AetherCrystal) to
        /// the correct bucket and calls Grant. Negative amounts are clamped.
        /// </summary>
        public void AddResource(DeNelle.Core.ResourceType type, int amount)
        {
            if (amount <= 0) return;
            switch (type)
            {
                case DeNelle.Core.ResourceType.Iron:
                    Grant(iron: amount);
                    break;
                case DeNelle.Core.ResourceType.Wood:
                    Grant(wood: amount);
                    break;
                case DeNelle.Core.ResourceType.Stone:
                    Grant(stone: amount);
                    break;
                case DeNelle.Core.ResourceType.AetherCrystal:
                    Grant(crystals: amount);
                    break;
            }
        }

        /// <summary>
        /// Overload accepting the local MineResource enum (used by MineNode/Outpost)
        /// so existing harvest code can migrate to the single AddResource style.
        /// </summary>
        public void AddResource(MineResource type, int amount)
        {
            if (amount <= 0) return;
            switch (type)
            {
                case MineResource.Iron:
                    Grant(iron: amount);
                    break;
                case MineResource.Wood:
                    Grant(wood: amount);
                    break;
                case MineResource.Stone:
                    Grant(stone: amount);
                    break;
                case MineResource.AetherCrystal:
                    Grant(crystals: amount);
                    break;
            }
        }

        // ── Backwards-compatible single-resource API (Wood only) ─────────────
        // These remain so TowerPlacementSystem / TowerUpgradeButton don't break
        // while they migrate to the ResourceCost overloads.

        /// <inheritdoc cref="CanAfford(ResourceCost)"/>
        [Obsolete("Use CanAfford(ResourceCost) for multi-resource checks.")]
        public bool CanAfford(int woodCost) => Wood >= woodCost;   // WO-842: unified wallet

        /// <summary>Spends Wood only. Prefer <see cref="TrySpend(ResourceCost)"/>.</summary>
        [Obsolete("Use TrySpend(ResourceCost) for multi-resource spending.")]
        public void Spend(int woodCost)
        {
            // WO-842: route through the unified debit (GameState-backed; same
            // silent-no-op-when-short + NotifyChanged-on-success contract as before).
            if (woodCost <= 0) return;
            TrySpend(ResourceCost.WoodOnly(woodCost));
        }

        // ── Internal ──────────────────────────────────────────────────────────

        /// <summary>
        /// GOLD mover — applies a signed delta to GameState.Resources.Coins (the single
        /// coin store the town HUD's SetGold reads), persists it, and raises
        /// GameStateService.ResourcesChanged so the HUD's gold readout updates. Mirrors the
        /// PromoCodeService coin-credit pattern: ResourceBalance is a struct, so read →
        /// modify → assign back. Clamped to >= 0. Null-safe (no-op when state is absent).
        /// </summary>
        public void AddCoins(int delta)
        {
            if (delta == 0) return;
            var gs = GameStateService.Instance;
            var state = gs?.State;
            if (gs == null || state == null) return;
            var r = state.Resources;
            r.Coins = Mathf.Max(0, r.Coins + delta);
            state.Resources = r;
            gs.Save();
            gs.ResourcesChanged?.Invoke();   // bridge re-emits OnChanged -> HUD gold refresh
        }

        private void NotifyChanged()
        {
            var snap = Snapshot;
            int subs = OnChanged?.GetInvocationList()?.Length ?? 0;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                $"OnChanged fired W{snap.Wood} I{snap.Iron} F{snap.Stone} C{snap.Crystals} (subscribers={subs})");
            OnChanged?.Invoke(snap);
        }
    }
}

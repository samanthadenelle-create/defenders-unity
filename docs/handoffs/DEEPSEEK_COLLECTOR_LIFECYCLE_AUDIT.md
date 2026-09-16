# DeepSeek: collector identity, reload and payout audit

READ-ONLY source audit, independent of the CLI's current footprint and storage-sizing implementation. You have no filesystem access. Embedded code is your context; do not request that you open paths. Return one final audit of at most 1200 words. Do not rewrite the collector or propose new architecture.

## Why this matters
The owner preserved the original Tripo castle layout and restored its Cathedral of Learning. Quarry must produce stone, IronMine iron, LumberMill wood. A collector should not duplicate pending resources or lose them when enabled, replaced, reconfigured, or reloaded. These are review questions, not newly reproduced failures.

## Focus
1. Trace the lifecycle for two collector instances with the same BuildingId: enable A, enable B, disable A, reconfigure B to another ID, disable B, reload. Which instance owns registry state, accrues, collects, loads and saves? Can a displaced instance remain a payout source through the second harvest registry?
2. Trace repeated Configure calls, Configure while active, resource-type changes and registry displacement. Distinguish intentional one-per-building-ID semantics from unsupported multiple instances. Do not assume IDs are per placed instance.
3. Trace pending resource conservation through Collect, capped bank headroom, fractional remainder, SaveState/LoadState and last-accrual timestamps. Separate a possible duplicate grant from display aggregation disagreement.
4. Determine what disable/re-enable, scene teardown and new-game reset actually guarantee from source. Do not infer that PlayerPrefs keys are per town, per player or per save slot unless shown.
5. Give at most five deterministic regression scenarios and the smallest candidate repair direction ONLY for a supported finding. Cite methods and preconditions. No code patch needed.

## Constraints and evidence limits
- No gameplay balancing, payout/rate changes, art, scene, prefab, save-schema, capture/ownership, or movement changes are authorized by this packet.
- Original models, materials, saved layout and chosen collector functions remain authoritative.
- No runtime evidence or claim of a current exploit is supplied. All proposed tests UNRUN. Never ask the owner to delete saves or reset the game to reproduce.
- Core collector/registry/bootstrap/service and relevant authority source are supplied. Full scene-instantiation and capture/owned-town lifecycle are not: identify missing call-path proof rather than inventing it.
- GameStateService is supplied only for collector persistence/reset declarations and methods. Full save synchronization and server authority are outside this audit; do not certify end-to-end atomicity.
- Code comments may be stale. Standalone // and XML comment lines and blank lines are omitted in C# blocks to reduce context cost; executable lines are unchanged. Source ranges and full-file hashes refer to original files.

## Required output
Severity-ranked findings table: evidence, preconditions, source confidence, missing runtime proof. Then a concise lifecycle ownership account, at most five tests, and exact missing dependencies that would change the verdict. Avoid hypothetical warnings unsupported by the supplied code. No further full rewrite round.

## Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs (original lines 1-1110/1110; SHA256 582E5601E177A02686EA1E17B8130D13D88AB3F0BE3446083D6CD1E8A48F88C5)
```csharp
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.World;
using DeNelle.Village;
namespace DeNelle.Village.Buildings.Progression
{
    [DisallowMultipleComponent]
    public sealed class ResourceCollector : MonoBehaviour, IDamageableStructure, ISiegeLootTarget, IHarvestSource
    {
        private const string PendingPrefsPrefix = GameStateService.CollectorPendingPrefPrefix;
        private const string HpPrefsPrefix = GameStateService.CollectorHpPrefPrefix;
        private const string LastAccrualPrefsPrefix = GameStateService.CollectorLastAccrualPrefPrefix;
        private const double MaxAwaySeconds = 30.0 * 24.0 * 3600.0;
        public const double OverflowEpsilon = 0.5;
        public const int MaxOverflowPasses = 64;
        private const float DefaultMaxHp = 120f;
        [SerializeField] private string _buildingId = ResourceBuildingProgression.FarmId;
        [SerializeField] private float _maxHp = DefaultMaxHp;
        private float _hp;
        private double _pending;
        private bool _broken;
        private double _lastAccrualMs;
        private bool _started;
        private ResourceCollector _displacedOnEnable;
        private bool _suppressNextDisableSave;
        public string BuildingId => _buildingId;
        public string SourceId => _buildingId;
        public HarvestResource Resource => ResolveResource();
        public bool IsActive => IsAlive && !_broken;
        public bool CanAccrue => IsActive;
        public double PendingAmount => _pending;
        public bool IsBroken => _broken;
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;
        public float LastLootStolen { get; private set; }
        public double LastLootStolenAtUnixMs { get; private set; }
        public double Capacity => ComputeCapacity();
        public const int StepCount = 20;
        public int FilledSteps => Mathf.Clamp(Mathf.FloorToInt(FillFraction * StepCount), 0, StepCount);
        public bool IsFull => FillFraction >= 0.999f;
        public event System.Action<ResourceCollector> StepChanged;
        private void RaiseStepChangedIfMoved(int oldSteps)
        {
            int now = FilledSteps;
            if (now != oldSteps) StepChanged?.Invoke(this);
        }
        public bool IsAlive => _hp > 0f && !_broken;
        public CombatFaction Faction =>
            SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly;
        public Transform LootTransform => transform;
        public bool IsLootTargetAlive => IsAlive;
        public float PendingLoot => (float)_pending;
        public float FillFraction
        {
            get
            {
                double cap = Capacity;
                return cap > 0.0 ? Mathf.Clamp01((float)(_pending / cap)) : 0f;
            }
        }
        public float SiegeRoleValue => 0.85f * (1f + FillFraction * 0.75f);
        private void Awake()
        {
            LoadState();
            if (_hp <= 0f) _hp = _maxHp;
        }
        private void OnEnable()
        {
            var prior = ResourceCollectorRegistry.Get(_buildingId);
            _displacedOnEnable = prior != null && prior != this ? prior : null;
            ResourceCollectorRegistry.Register(this);
            HarvestSourceRegistry.Register(this);
            PoiBeacon.Attach(gameObject, PoiBeacon.PoiTier.Node,
                calloutRadius: 28f, handoffRadius: 3.5f,
                tint: new Color(1f, 0.94f, 0.72f, 1f),
                isSpent: () => !IsActive);
        }
        private void Start()
        {
            _started = true;
            CatchUpAway();
        }
        private void OnDisable()
        {
            bool ownedRegistrySlot = ResourceCollectorRegistry.Get(_buildingId) == this;
            HarvestSourceRegistry.Unregister(this);
            ResourceCollectorRegistry.Unregister(this);
            if (ownedRegistrySlot && !_suppressNextDisableSave) SaveState();
            _suppressNextDisableSave = false;
        }
        internal void ParkWithoutPersisting()
        {
            _suppressNextDisableSave = true;
            gameObject.SetActive(false);
        }
        private void CatchUpAway()
        {
            double nowMs = TimeSource.NowUnixMs();
            if (_lastAccrualMs <= 0.0)
            {
                _lastAccrualMs = nowMs;
                SaveState();
                FlowTrace.Step("Harvest",
                    $"away catch-up '{_buildingId}': fresh stamp (<=0) - seeded to now, nothing back-filled.");
                return;
            }
            double awaySec = (nowMs - _lastAccrualMs) / 1000.0;
            if (awaySec < 0.0)
            {
                FlowTrace.Warn("Harvest",
                    $"away catch-up '{_buildingId}': clock ran BACKWARDS (now={nowMs:0} < stamp={_lastAccrualMs:0}) - " +
                    "clamped to 0, no accrual, no re-claim.");
                awaySec = 0.0;
            }
            if (awaySec > MaxAwaySeconds)
            {
                FlowTrace.Warn("Harvest",
                    $"away catch-up '{_buildingId}': away {awaySec:0}s exceeds the {MaxAwaySeconds:0}s int-overflow " +
                    "guard - clamped (this is NOT the design cap; capacity is).");
                awaySec = MaxAwaySeconds;
            }
            int amount = AwayAmount(_buildingId, awaySec);
            double before = _pending;
            _lastAccrualMs = nowMs;
            if (amount > 0) Accrue(amount);
            else SaveState();
            FlowTrace.Step("Harvest",
                $"away catch-up '{_buildingId}': away={awaySec:0}s owed={amount} " +
                $"pending {before:F0} -> {_pending:F0} / cap {Capacity:F0}" +
                (_pending >= Capacity - 0.001 ? " (AT CAP - the cap is what bounds the away window)" : ""));
        }
        private static int AwayAmount(string buildingId, double awaySec)
        {
            if (awaySec <= 0.0) return 0;
            float interval = ResourceBuildingState.CurrentHarvestInterval(buildingId);
            if (interval <= 0f) return 0;
            int perTick = ResourceBuildingHarvester.EffectiveYieldPerTick(buildingId);
            if (perTick <= 0) return 0;
            double owed = perTick * (awaySec / interval);
            if (owed <= 0.0) return 0;
            return owed >= int.MaxValue ? int.MaxValue : (int)owed;
        }
        public void Configure(string buildingId, float maxHp = DefaultMaxHp)
        {
            string previousId = _buildingId;
            bool live = isActiveAndEnabled;
            var displaced = _displacedOnEnable;
            _displacedOnEnable = null;
            if (live) ResourceCollectorRegistry.Unregister(this);
            if (displaced != null && displaced.isActiveAndEnabled &&
                !string.Equals(previousId, buildingId, System.StringComparison.Ordinal))
                ResourceCollectorRegistry.Register(displaced);
            _buildingId = buildingId;
            _maxHp = Mathf.Max(1f, maxHp);
            LoadState();
            if (_hp <= 0f) _hp = _maxHp;
            if (live)
            {
                ResourceCollectorRegistry.Register(this);
                if (!string.Equals(previousId, buildingId, System.StringComparison.Ordinal))
                    FlowTrace.Step("Harvest",
                        $"collector re-keyed '{previousId}' -> '{buildingId}' on Configure " +
                        "(AddComponent registers under the serialized default before Configure runs).");
                ResourceCollectorBootstrap.NotifyCollectorConfigured(this);
            }
            if (_started) CatchUpAway();
        }
        private float _lastLoggedAccrualScale = 1f;
        public void Accrue(int amount)
        {
            if (!CanAccrue || amount <= 0) return;
            float health = HpFraction;
            if (health <= 0f) return;   // defensive — CanAccrue should already gate this
            if (Mathf.Abs(health - _lastLoggedAccrualScale) > 0.005f)
            {
                _lastLoggedAccrualScale = health;
                FlowTrace.Step("Harvest",
                    $"collector '{_buildingId}' accrual scaled x{health:0.##} (hp {_hp:F0}/{_maxHp:F0})");
            }
            double cap = Capacity;
            double before = _pending;
            int stepsBefore = FilledSteps;
            double owed = amount * (double)health;
            int spilled = 0;
            int passes = 0;
            while (true)
            {
                double poolBefore = _pending;
                _pending = System.Math.Min(cap, _pending + owed);
                owed -= _pending - poolBefore;                   // what the pool actually absorbed
                if (owed <= OverflowEpsilon) { owed = 0.0; break; }
                if (++passes > MaxOverflowPasses)
                {
                    FlowTrace.Warn("Harvest",
                        $"auto-overflow '{_buildingId}': hit the {MaxOverflowPasses}-pass guard with {owed:F0} still " +
                        "owed - the remainder is not produced this tick (guard only; it bounds a pathological " +
                        "capacity/headroom ratio, it is NOT a balance dial).");
                    break;
                }
                int moved = TryOverflowToBank(cap);
                if (moved <= 0) break;                           // no storage room: the collector STALLS
                spilled += moved;
            }
            _lastAccrualMs = TimeSource.NowUnixMs();
            if (_pending > before)
            {
                FlowTrace.Throttle("Harvest", $"accrue-{_buildingId}", 2f,
                    $"accrue-pending building={_buildingId} pending={_pending:F0}/{cap:F0}" +
                    (spilled > 0 ? $" (+{spilled} auto-overflowed to storage this tick)" : ""));
                SaveState();
            }
            else
            {
                SaveState();
                if (spilled <= 0)
                    FlowTrace.Throttle("Harvest", $"atcap-{_buildingId}", 30f,
                        $"collector '{_buildingId}' is AT CAP ({_pending:F0}/{cap:F0}) and its storage has NO ROOM - " +
                        "production is STALLED (not banked, not burned: what is held stays here) and the " +
                        "last-accrual stamp still advances (no frozen backlog). Ruling 26b: build or upgrade " +
                        "the matching storage, or spend, and it resumes by itself.");
            }
            RaiseStepChangedIfMoved(stepsBefore);
        }
        public int Collect() => Collect(out _, out _);
        public int Collect(out int requested, out int leftPending)
        {
            requested = 0;
            leftPending = 0;
            if (_pending <= 0.0) return 0;
            int amount = (int)System.Math.Floor(_pending);
            if (amount <= 0) return 0;
            requested = amount;
            int stepsBefore = FilledSteps;
            var eco = EconomyService.Instance;
            var res = ResolveResource();
            int banked = amount;
            if (eco != null)
            {
                switch (res)
                {
                    case HarvestResource.Wood:     banked = eco.GrantSpendable(wood: amount).Wood;         break;
                    case HarvestResource.Iron:     banked = eco.GrantSpendable(iron: amount).Iron;         break;
                    case HarvestResource.Stone:     banked = eco.GrantSpendable(stone: amount).Stone;         break;
                    case HarvestResource.Crystals: banked = eco.GrantSpendable(crystals: amount).Crystals; break;
                }
            }
            else
            {
                ResourceLedger.Credit(res, amount);
            }
            _pending = SettleCollect(_pending, banked, out leftPending);
            SaveState();
            if (banked < amount)
            {
                FlowTrace.Warn("Harvest",
                    $"collect building={_buildingId} +{banked} of {amount} {res} banked; {leftPending} STILL PENDING " +
                    "here (bank full) - NOT burned, banks on the next collect once there is room (WO-1392).");
                if (banked <= 0)
                {
                    RaiseStepChangedIfMoved(stepsBefore);
                    return 0;                         // nothing moved: no "+0" popup
                }
            }
            else
            {
                FlowTrace.Step("Harvest", $"collect building={_buildingId} +{banked} {res} wallet");
            }
            amount = banked;
            DeNelle.Village.World.ResourceGainPopup.Spawn(
                transform.position + Vector3.up * 1.6f,
                $"+{amount} {ResourceBuildingProgression.LabelFor(res)}",
                PopupTint(res));
            RaiseStepChangedIfMoved(stepsBefore);
            return amount;
        }
        public static double SettleCollect(double pendingBefore, int banked, out int leftPending)
        {
            if (pendingBefore < 0.0) pendingBefore = 0.0;
            int requested = (int)System.Math.Floor(pendingBefore);
            if (banked < 0) banked = 0;
            if (banked > requested) banked = requested;
            double after = pendingBefore - banked;
            if (after < 0.0) after = 0.0;
            leftPending = (int)System.Math.Floor(after);
            return after;
        }
        public static double SettleOverflow(double pendingBefore, int bankRoom, out int moved, out int leftPending)
        {
            if (pendingBefore < 0.0) pendingBefore = 0.0;
            if (bankRoom < 0) bankRoom = 0;
            int whole = (int)System.Math.Floor(pendingBefore);
            moved = whole < bankRoom ? whole : bankRoom;
            return SettleCollect(pendingBefore, moved, out leftPending);
        }
        public static double SimulateAccrueWithOverflow(
            double pending, double capacity, double owed, int bankRoom,
            out int banked, out double unproduced)
        {
            banked = 0;
            if (pending < 0.0) pending = 0.0;
            if (capacity < 0.0) capacity = 0.0;
            if (owed < 0.0) owed = 0.0;
            if (bankRoom < 0) bankRoom = 0;
            int passes = 0;
            while (true)
            {
                double poolBefore = pending;
                pending = System.Math.Min(capacity, pending + owed);
                owed -= pending - poolBefore;
                if (owed <= OverflowEpsilon) { owed = 0.0; break; }
                if (++passes > MaxOverflowPasses) break;
                double after = SettleOverflow(pending, bankRoom, out int moved, out _);
                if (moved <= 0) break;                       // storage full: the collector STALLS
                pending = after;
                banked += moved;
                bankRoom -= moved;
            }
            unproduced = owed;
            return pending;
        }
        private int TryOverflowToBank(double cap)
        {
            var eco = EconomyService.Instance;
            var gs = GameStateService.Instance;
            if (eco == null || gs == null || gs.State == null)
            {
                FlowTrace.Once("Harvest", "overflow-no-wallet",
                    $"auto-overflow '{_buildingId}': SKIPPED - no EconomyService/GameState yet " +
                    $"(eco={(eco != null ? "yes" : "no")} state={(gs != null && gs.State != null ? "yes" : "no")}). " +
                    "The pool is left intact and the collector simply stays capped; spilling here would " +
                    "clamp against a different wallet than it reads and land in the unsaved fallback pool.");
                return 0;
            }
            if (_pending < 1.0) return 0;
            var res = ResolveResource();
            int want = (int)System.Math.Floor(_pending);
            int room = ResourceCollectorService.HeadroomFor(res);
            if (room < 0) room = 0;
            int ask = want < room ? want : room;     // THE NO-BURN / NO-FALSE-TOAST LINE
            if (ask <= 0)
            {
                FlowTrace.Throttle("Harvest", $"overflow-full-{_buildingId}", 30f,
                    $"auto-overflow '{_buildingId}': storage for {res} has NO ROOM (headroom={room}) - " +
                    $"0 moved, {want} STAYS PENDING here and the collector STALLS at {_pending:F0}/{cap:F0}. " +
                    "Nothing is burned; it spills by itself the moment storage frees up (ruling 26b).");
                return 0;
            }
            int banked = 0;
            switch (res)
            {
                case HarvestResource.Wood:     banked = eco.GrantSpendable(wood: ask).Wood;         break;
                case HarvestResource.Iron:     banked = eco.GrantSpendable(iron: ask).Iron;         break;
                case HarvestResource.Stone:     banked = eco.GrantSpendable(stone: ask).Stone;         break;
                case HarvestResource.Crystals: banked = eco.GrantSpendable(crystals: ask).Crystals; break;
            }
            if (banked <= 0)
            {
                FlowTrace.Warn("Harvest",
                    $"auto-overflow '{_buildingId}': asked storage for {ask} {res} against headroom {room} and " +
                    $"NOTHING applied - {want} stays pending here, nothing burned. The collector stalls.");
                return 0;
            }
            _pending = SettleOverflowPool(banked, out int leftPending);
            FlowTrace.Step("Harvest",
                $"auto-overflow '{_buildingId}': moved {banked} {res} -> storage " +
                $"(held {want}, storage headroom {room}, asked {ask}); {leftPending} LEFT PENDING here " +
                (leftPending > 0
                    ? "because the storage could not take the rest - it is NOT burned and spills on a later tick "
                    : "- the whole pool fit ") +
                $"(collector {_pending:F0}/{cap:F0}). Owner ruling 26b: production resumes now there is room.");
            return banked;
        }
        private double SettleOverflowPool(int banked, out int leftPending)
            => SettleCollect(_pending, banked, out leftPending);
        private static Color PopupTint(HarvestResource res)
        {
            switch (res)
            {
                case HarvestResource.Wood:     return new Color(0.55f, 0.38f, 0.22f);
                case HarvestResource.Iron:     return new Color(0.62f, 0.64f, 0.70f);
                case HarvestResource.Stone:     return new Color(0.72f, 0.62f, 0.28f);
                case HarvestResource.Crystals: return new Color(0.35f, 0.72f, 0.95f);
                default:                       return Color.white;
            }
        }
        public void ApplyContactDamage(float amount)
        {
            if (!IsAlive) return;
            amount = Mathf.Max(0f, amount);
            _hp = Mathf.Max(0f, _hp - amount);
            FlowTrace.Step("Harvest", $"collector-hit building={_buildingId} hp={_hp:F0}/{_maxHp:F0} pending={_pending:F0}");
            if (_hp <= 0f) OnSiegeDestroyed();
            else SaveState();
        }
        public void Repair()
        {
            if (_broken) return;
            _hp = _maxHp;
            LastLootStolen = 0f;   // F8-45: the loot report is per-break; a repair clears it
            LastLootStolenAtUnixMs = 0.0;   // ...and so does its siege stamp, or the pair could disagree
            SaveState();
            FlowTrace.Step("Harvest", $"collector-repair building={_buildingId}");
            StepChanged?.Invoke(this);
        }
        private void OnSiegeDestroyed()
        {
            int stepsBefore = FilledSteps;
            _broken = true;
            LastLootStolen = 0f;                            // removed by ruling -- never non-zero again
            LastLootStolenAtUnixMs = TimeSource.NowUnixMs();  // the break stamp survives as evidence
            SaveState();
            FlowTrace.Warn("Harvest",
                $"collector-destroyed building={_buildingId} resource={Resource} " +
                $"pending-kept={_pending:F0} (COLLECTOR LOOTING IS REMOVED, owner ruling 2026-08-27 -- " +
                "the siege bills the BANK once, through StakeRules; nothing is taken from this collector).");
            StepChanged?.Invoke(this);
        }
        private HarvestResource ResolveResource()
        {
            var def = ResourceBuildingProgression.Find(_buildingId);
            return def != null ? def.Yields : HarvestResource.Wood;
        }
        private double ComputeCapacity()
        {
            double baseCap;
            double catalogCap = CatalogCapacity();
            if (catalogCap > 0.0)
            {
                baseCap = catalogCap * ThroughputScale();
            }
            else
            {
                int yield = ResourceBuildingState.CurrentEffectiveYield(_buildingId);
                float interval = ResourceBuildingState.CurrentHarvestInterval(_buildingId);
                baseCap = (yield <= 0 || interval <= 0f)
                    ? 50.0
                    : System.Math.Max(50.0, yield * (3600.0 / interval) * 2.0);
            }
            float capBonus = DeNelle.Village.Talents.HeroTalentModifiers.StatSum(
                HeroTalentClassReader.Slug(), "collectorCap");
            if (capBonus > 0f)
            {
                baseCap *= 1.0 + capBonus;
                FlowTrace.Once("Talent", "collectorCap",
                    $"collectorCap x{1f + capBonus:0.###} applied to collector capacity (WO-676 Deep Reserves).");
            }
            return baseCap;
        }
        private double ThroughputScale()
        {
            double basePerHour = ResourceBuildingProgression.ProductionPerHour(_buildingId, 1, 1f, 1.0);
            if (basePerHour <= 0.0) return 1.0;
            double nowPerHour = ResourceBuildingProgression.ProductionPerHour(
                _buildingId,
                ResourceBuildingState.GetLevel(_buildingId),
                DeNelle.Core.State.ModifierService.ProductionMultFor(_buildingId),
                ResourceBuildingHarvester.EchoHarvestMultiplier());
            if (nowPerHour <= 0.0) return 1.0;
            return System.Math.Max(1.0, nowPerHour / basePerHour);
        }
        private double CatalogCapacity()
        {
            foreach (var e in DeNelle.Core.Catalog.CatalogRegistry.OfType(DeNelle.Core.Catalog.CatalogType.Collector))
            {
                if (e == null || e.repo == null) continue;
                string bid = !string.IsNullOrEmpty(e.repo.collectorBuildingId) ? e.repo.collectorBuildingId : e.id;
                if (bid == _buildingId) return e.repo.capacity;
            }
            return 0.0;
        }
        private void LoadState()
        {
            _pending = PlayerPrefs.GetFloat(PendingPrefsPrefix + _buildingId, 0f);
            _hp = PlayerPrefs.GetFloat(HpPrefsPrefix + _buildingId, _maxHp);
            _broken = _hp <= 0f;
            string stamp = PlayerPrefs.GetString(LastAccrualPrefsPrefix + _buildingId, string.Empty);
            if (string.IsNullOrEmpty(stamp) ||
                !double.TryParse(stamp, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out _lastAccrualMs))
                _lastAccrualMs = 0.0;
            if (_lastAccrualMs < 0.0) _lastAccrualMs = 0.0;
        }
        private void SaveState()
        {
            PlayerPrefs.SetFloat(PendingPrefsPrefix + _buildingId, (float)_pending);
            PlayerPrefs.SetFloat(HpPrefsPrefix + _buildingId, _hp);
            PlayerPrefs.SetString(LastAccrualPrefsPrefix + _buildingId,
                _lastAccrualMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
            GameStateService.RegisterCollectorId(_buildingId);
            PlayerPrefs.Save();
        }
        public void ResetToFullHp()
        {
            _hp = _maxHp;
            _broken = false;
            _lastAccrualMs = TimeSource.NowUnixMs();
            SaveState();
            StepChanged?.Invoke(this);   // health/broken state moved — let the fill/damage views re-read
            FlowTrace.Step("Harvest", $"collector '{_buildingId}' HP reset to full on fresh placement (stale persisted damage cleared)");
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallNewGameHook()
        {
            GameStateService.NewGameStarted -= OnNewGameStarted;
            GameStateService.NewGameStarted += OnNewGameStarted;
            FlowTrace.Step("Harvest",
                "collector New Game hook installed - a reset now zeroes LIVE collectors as well as " +
                "their PlayerPrefs (WO-1371).");
        }
        private static void OnNewGameStarted()
        {
            var live = FindObjectsByType<ResourceCollector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < live.Length; i++) live[i]?.ResetForNewGame();
            ResourceBuildingState.ResetAll();
            FlowTrace.Step("Harvest",
                $"New Game: zeroed {live.Length} live collector(s) and reset every resource-building " +
                "level + tech node. Pending is 0 and the away stamp is re-seeded, so the first " +
                "collector status of the new game must read pending=0 (WO-1371).");
        }
        internal void ResetForNewGame()
        {
            int oldSteps = FilledSteps;
            double before = _pending;
            _pending = 0.0;
            _hp = _maxHp;
            _broken = false;
            _lastAccrualMs = TimeSource.NowUnixMs();
            SaveState();
            RaiseStepChangedIfMoved(oldSteps);
            FlowTrace.Step("Harvest",
                $"New Game: collector '{_buildingId}' pending {before:F0} -> 0, HP restored, away " +
                "clock re-seeded to now (nothing to back-fill).");
        }
    }
}
```

## Assets/_Modules/Village/Buildings/Progression/ResourceCollectorRegistry.cs (original lines 1-36/36; SHA256 7DE7103103E438D936643E91E70CA6268C6B47ED3F340B89C532AB9793067AE5)
```csharp
using System.Collections.Generic;
namespace DeNelle.Village.Buildings.Progression
{
    public static class ResourceCollectorRegistry
    {
        private static readonly Dictionary<string, ResourceCollector> s_byId =
            new Dictionary<string, ResourceCollector>(4);
        public static IReadOnlyCollection<ResourceCollector> All => s_byId.Values;
        public static void Register(ResourceCollector collector)
        {
            if (collector == null || string.IsNullOrEmpty(collector.BuildingId)) return;
            s_byId[collector.BuildingId] = collector;
        }
        public static void Unregister(ResourceCollector collector)
        {
            if (collector == null || string.IsNullOrEmpty(collector.BuildingId)) return;
            if (s_byId.TryGetValue(collector.BuildingId, out var cur) && cur == collector)
                s_byId.Remove(collector.BuildingId);
        }
        public static ResourceCollector Get(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;
            s_byId.TryGetValue(buildingId, out var c);
            return c;
        }
    }
}
```

## Assets/_Modules/Village/Buildings/Progression/ResourceCollectorBootstrap.cs (original lines 1-247/247; SHA256 FB66E4369D81C5CA836F1492FFAC753D41416B3F076A670FC3435AC9B55FBF04)
```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
namespace DeNelle.Village.Buildings.Progression
{
    public static class ResourceCollectorBootstrap
    {
        private const string HostName = "ResourceCollectorHost";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            EnsureHost();
            WireScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            WireScene(scene);
            var host = GameObject.Find(HostName);
            var retry = host != null ? host.GetComponent<ResourceCollectorFallbackRetry>() : null;
            if (retry != null) retry.QueueAll();
            else FlowTrace.Warn("Harvest", "scene loaded but collector fallback retry driver is unavailable");
        }
        private static void EnsureHost()
        {
            var host = GameObject.Find(HostName);
            if (host == null)
            {
                host = new GameObject(HostName);
                Object.DontDestroyOnLoad(host);
                FlowTrace.Step("Harvest", "ResourceCollectorHost DDOL created");
            }
            if (host.GetComponent<CollectorStatusPublisher>() == null)
                host.AddComponent<CollectorStatusPublisher>();
            if (host.GetComponent<ResourceCollectorFallbackRetry>() == null)
                host.AddComponent<ResourceCollectorFallbackRetry>();
        }
        internal static void NotifyCollectorConfigured(ResourceCollector collector)
        {
            if (!Application.isPlaying || collector == null) return;
            var host = GameObject.Find(HostName);
            if (host == null || collector.transform.IsChildOf(host.transform)) return;
            Transform fallback = host.transform.Find("Collector_" + collector.BuildingId);
            if (fallback == null || !fallback.gameObject.activeSelf) return;
            var fallbackCollector = fallback.GetComponent<ResourceCollector>();
            if (fallbackCollector != null) fallbackCollector.ParkWithoutPersisting();
            else fallback.gameObject.SetActive(false);
            FlowTrace.Step("Harvest",
                $"placed collector '{collector.BuildingId}' took ownership; DDOL fallback parked");
        }
        internal static void RetryAllAfterStateReady() => WireScene(SceneManager.GetActiveScene());
        private static void WireScene(Scene scene)
        {
            if (!scene.IsValid()) return;
            FlowTrace.Once("Harvest", "wo673-namewire-standdown",
                "storefront name-wire stood down (strategic placement always on; fallback-only)");
            EnsureFallbackCollector(ResourceBuildingProgression.FarmId);
            EnsureFallbackCollector(ResourceBuildingProgression.LumbermillId);
            EnsureFallbackCollector(ResourceBuildingProgression.ForgeId);
        }
        private static void EnsureFallbackCollector(string buildingId)
        {
            var registered = ResourceCollectorRegistry.Get(buildingId);
            if (registered != null && !IsFallback(registered)) return;
            if (!HasEverBuilt(buildingId))
            {
                if (registered != null)
                    registered.ParkWithoutPersisting();
                FlowTrace.Once("Harvest", $"fallback-skipped-{buildingId}",
                    $"NO fallback collector for '{buildingId}' - it is not in the WO-834 ever-built ledger " +
                    "(a live collector would open the existence gate, so an unbuilt building must never get one).");
                return;
            }
            var host = GameObject.Find(HostName);
            if (host == null)
            {
                FlowTrace.Warn("Harvest",
                    $"cannot create fallback collector for '{buildingId}' - '{HostName}' is MISSING");
                return;
            }
            if (registered != null) return;
            var childName = "Collector_" + buildingId;
            Transform child = host.transform.Find(childName);
            GameObject go;
            if (child != null)
                go = child.gameObject;
            else
            {
                go = new GameObject(childName);
                go.transform.SetParent(host.transform, false);
            }
            if (!go.activeSelf) go.SetActive(true);
            var col = go.GetComponent<ResourceCollector>();
            if (col == null) col = go.AddComponent<ResourceCollector>();
            col.Configure(buildingId);
            if (go.GetComponent<Collider>() == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(3f, 2f, 3f);
                box.isTrigger = false;
            }
            FlowTrace.Once("Harvest", $"fallback-{buildingId}",
                $"collector fallback host for building={buildingId} (ever-built ledger confirms it exists)");
        }
        private static bool HasEverBuilt(string buildingId)
        {
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            if (state == null) return false;
            var catalogIds = ResourceBuildingHarvester.CatalogIdsForBuilding(buildingId);
            if (catalogIds == null) return false;
            for (int i = 0; i < catalogIds.Count; i++)
                if (state.HasEverBuilt(catalogIds[i])) return true;
            return false;
        }
        private static bool IsFallback(ResourceCollector collector)
        {
            if (collector == null) return false;
            var host = GameObject.Find(HostName);
            return host != null && collector.transform.IsChildOf(host.transform);
        }
    }
    internal sealed class ResourceCollectorFallbackRetry : MonoBehaviour
    {
        private GameStateService _stateService;
        private bool _retryAll;
        internal void QueueAll() => _retryAll = true;
        private void Update()
        {
            if (_stateService == null && GameStateService.Instance != null)
            {
                _stateService = GameStateService.Instance;
                _stateService.StateReplaced.AddListener(OnStateReplaced);
                _retryAll = true; // State may already have loaded before this listener bound.
                FlowTrace.Step("Harvest", "fallback retry bound to GameStateService.StateReplaced");
            }
            if (_retryAll)
            {
                _retryAll = false;
                ResourceCollectorBootstrap.RetryAllAfterStateReady();
            }
        }
        private void OnStateReplaced() => _retryAll = true;
        private void OnDestroy()
        {
            if (_stateService != null)
                _stateService.StateReplaced.RemoveListener(OnStateReplaced);
        }
    }
}
```

## Assets/_Modules/Village/Buildings/Progression/ResourceCollectorService.cs (original lines 1-253/253; SHA256 32958F3FC09B1222846F3639E4296DEF8CA7D360464C09E66932DC724D641469)
```csharp
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.Ops;
using DeNelle.Core.UI;
using DeNelle.Village;
namespace DeNelle.Village.Buildings.Progression
{
    public static class ResourceCollectorService
    {
        public sealed class PendingLine
        {
            public HarvestResource Resource;
            public int Pending;
            public int Collectors;
        }
        public static readonly HarvestResource[] RailOrder =
            { HarvestResource.Wood, HarvestResource.Iron, HarvestResource.Stone, HarvestResource.Crystals };
        public static List<PendingLine> PendingByResource()
        {
            using var _perf = FlowTrace.Measure("Perf", "ResourceCollectorService.PendingByResource", 4f, 1f);
            var samples = new List<KeyValuePair<HarvestResource, double>>();
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c == null) continue;
                samples.Add(new KeyValuePair<HarvestResource, double>(c.Resource, c.PendingAmount));
            }
            return AggregatePending(samples);
        }
        public static List<PendingLine> AggregatePending(IEnumerable<KeyValuePair<HarvestResource, double>> samples)
        {
            var pending = new Dictionary<HarvestResource, int>();
            var count = new Dictionary<HarvestResource, int>();
            if (samples != null)
            {
                foreach (var s in samples)
                {
                    int whole = (int)System.Math.Floor(s.Value);
                    if (whole <= 0) continue;
                    pending.TryGetValue(s.Key, out int had);
                    pending[s.Key] = had + whole;
                    count.TryGetValue(s.Key, out int n);
                    count[s.Key] = n + 1;
                }
            }
            var lines = new List<PendingLine>(RailOrder.Length);
            foreach (var res in RailOrder)
            {
                if (!pending.TryGetValue(res, out int held) || held <= 0) continue;
                count.TryGetValue(res, out int n);
                lines.Add(new PendingLine { Resource = res, Pending = held, Collectors = n });
            }
            return lines;
        }
        public static BankResource BankResourceOf(HarvestResource r)
        {
            switch (r)
            {
                case HarvestResource.Wood:     return BankResource.Wood;
                case HarvestResource.Iron:     return BankResource.Iron;
                case HarvestResource.Stone:     return BankResource.Stone;
                default:                       return BankResource.Crystals;
            }
        }
        public static int HeadroomFor(HarvestResource r) => TownBankCapacity.RoomFor(BankResourceOf(r));
        public static int CollectAll(bool showResult = true)
        {
            using var _ = FlowTrace.Enter("Harvest", "CollectAll");
            if (MaintenanceCatalog.Refuses(MaintenanceArea.Farming, "collect-all", out string sealedMsg))
            {
                ElarionUiKit.ShowToast(sealedMsg, ElarionUiKit.ToastTone.Info);
                return 0;
            }
            var before = PendingByResource();
            var storeBefore = new Dictionary<HarvestResource, int>();
            foreach (var line in before)
                storeBefore[line.Resource] = TownBankCapacity.CurrentOf(BankResourceOf(line.Resource));
            int total = 0;
            var bankedBy = new Dictionary<HarvestResource, int>();
            using (HarvestOverflowModal.BeginBatch(showResult ? "CollectAll" : null))
            {
                foreach (var c in ResourceCollectorRegistry.All)
                {
                    if (c == null) continue;
                    int banked = c.Collect(out int requestedHere, out int leftHere);
                    total += banked;
                    bankedBy.TryGetValue(c.Resource, out int had);
                    bankedBy[c.Resource] = had + banked;
                }
                var rows = BuildCollectorRows(before, bankedBy, storeBefore);
                if (showResult && rows.Count > 0) HarvestOverflowModal.Present(rows);
                var echo = EchoService.Instance;
                if (echo != null)
                    total += echo.DumpSilos(showResult);
            }
            FlowTrace.Step("Harvest", $"collect-all total-banked={total}");
            return total;
        }
        public static List<BankOverflowStatus> BuildCollectorRows(
            IReadOnlyList<PendingLine> before,
            IReadOnlyDictionary<HarvestResource, int> bankedBy,
            IReadOnlyDictionary<HarvestResource, int> storeBefore)
        {
            var rows = new List<BankOverflowStatus>();
            if (before == null) return rows;
            foreach (var line in before)
            {
                if (line == null || line.Pending <= 0) continue;
                int banked = 0;
                if (bankedBy != null) bankedBy.TryGetValue(line.Resource, out banked);
                if (banked < 0) banked = 0;
                if (banked > line.Pending) banked = line.Pending;
                int waiting = line.Pending - banked;
                if (waiting <= 0) continue;                  // everything fit: no row, no scold
                var bank = BankResourceOf(line.Resource);
                int current = 0;
                if (storeBefore != null) storeBefore.TryGetValue(line.Resource, out current);
                rows.Add(new BankOverflowStatus
                {
                    Available = true,
                    Resource = bank,
                    ResourceName = ResourceBuildingProgression.LabelFor(line.Resource),
                    ContainerName = TownBankCapacity.ContainerNameFor(bank),
                    Requested = line.Pending,
                    Granted = banked,
                    Lost = waiting,
                    Max = TownBankCapacity.MaxOf(bank),
                    Current = current < 0 ? 0 : current,
                    OverCap = current > TownBankCapacity.MaxOf(bank),
                    Source = HarvestOverflowModal.CollectorSource,
                });
                FlowTrace.Warn("Harvest",
                    $"collect-all {ResourceBuildingProgression.LabelFor(line.Resource)}: {banked} of {line.Pending} " +
                    $"banked, {waiting} STILL WAITING in the collectors (bank {current}/{TownBankCapacity.MaxOf(bank)}) " +
                    "- nothing burned (WO-1392); it banks on the next collect once there is room.");
            }
            return rows;
        }
        public static int TotalPending()
        {
            int sum = 0;
            foreach (var line in PendingByResource()) sum += line.Pending;
            return sum;
        }
        public static float MaxFillFraction()
        {
            float max = 0f;
            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c != null && c.FillFraction > max) max = c.FillFraction;
            }
            return max;
        }
    }
}
```

## Assets/_Modules/Village/Harvest/HarvestSourceRegistry.cs (original lines 1-32/32; SHA256 811F6DC28FC9FCC0878CACAFD69DD72C4038049AC0A5F898D00098081FE9C77B)
```csharp
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.World;
namespace DeNelle.Village
{
    public static class HarvestSourceRegistry
    {
        private static readonly List<IHarvestSource> s_sources = new List<IHarvestSource>(8);
        public static IReadOnlyList<IHarvestSource> Active => s_sources;
        public static void Register(IHarvestSource source)
        {
            if (source == null || s_sources.Contains(source)) return;
            s_sources.Add(source);
            FlowTrace.Step("Harvest", $"register id={source.SourceId} pending={source.PendingAmount:F0}/{source.Capacity:F0}");
        }
        public static void Unregister(IHarvestSource source)
        {
            if (source == null) return;
            if (s_sources.Remove(source))
                FlowTrace.Step("Harvest", $"unregister id={source.SourceId}");
        }
    }
}
```

## Assets/_Modules/Village/Harvest/OfflineHarvestService.cs (original lines 1-1509/1509; SHA256 6C3C955C511D5B6F174782BBFC39C3EA836FF29FC65B17B4667289376AE5DC15)
```csharp
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Defense;   // WO-1408 — the defence-report ledger, READ for the away summary's "attacked" row
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;   // WO-857 Phase F — the town bank cap (this path writes the wallet directly)
using DeNelle.Core.State;
using DeNelle.Core.World;
using DeNelle.Village.Buildings.Progression;   // LANE G — the collector registry, read for the away summary's "waiting" row
using DeNelle.Village.Monetization;   // WO-1119 — HarvestBoostService (Version B rate boost)
using DeNelle.Village.UI;
namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class OfflineHarvestService : MonoBehaviour, IOfflineClaimConsumer
    {
        public static OfflineHarvestService Instance { get; private set; }
        public const double MinAwaySummarySeconds = 30.0 * 60.0;
        public static bool ShouldRevealAwaySummary(OfflineHarvestResult result)
            => result != null && result.HasSummaryContent && result.AwaySeconds >= MinAwaySummarySeconds;
        public string OfflineConsumerName => "harvest-nodes";
        [Header("Cap")]
        [Tooltip("Offline hours credited in one claim. The retention dial: long enough that a " +
                 "twice-a-day check-in feels rewarded, short enough that the mines still want " +
                 "defending. WO-115 suggests 8–12h; default 10h. Owner-tunable in playtest.")]
        [Min(0f)] public float OfflineCapHours = 10f;
        [Header("Resume policy")]
        [Tooltip("Also claim on OnApplicationPause(false) — i.e. when the app returns to the " +
                 "foreground on mobile, not only on a cold load. Off would only accrue on a full " +
                 "relaunch.")]
        public bool ClaimOnResume = true;
        public event System.Action<OfflineHarvestResult> Claimed;
        private float OfflineCapSeconds => Mathf.Max(0f, OfflineCapHours) * 3600f;
        private readonly HashSet<MineNode> _workerOwnedThisClaim = new HashSet<MineNode>();
        private OfflineHarvestResult _lastResult;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();                       // WO-1231: the away summary's reveal seam
            EnsureJobSubscription();                  // LANE G: the away summary's finished-jobs seam
            EnsureNewGameSubscription();              // WO-1414: a New Game drops any parked reveal
        }
        private void OnDestroy()
        {
            OfflineClaimCoordinator.Unregister(this);
            DisarmSceneHook();                        // the deferred welcome-back reveal (Title-screen guard)
            if (_subscribedToNewGame)
            {
                GameStateService.NewGameStarted -= OnNewGameStarted;
                _subscribedToNewGame = false;
            }
            if (_subscribedToJobs)
            {
                var timers = BuildTimerService.Instance;
                if (timers != null) timers.JobCompleted -= OnAnyJobCompleted;
                _subscribedToJobs = false;
            }
            if (_subscribedToCompletion)
            {
                OfflineClaimCoordinator.ClaimCompleted -= OnClaimCompleted;
                _subscribedToCompletion = false;
            }
            if (Instance == this) Instance = null;
        }
        private void Start()
        {
            ClaimDeferred("cold-load");
        }
        private void OnApplicationPause(bool paused)
        {
            OfflineClaimCoordinator.NotePaused(paused);
            if (paused) OpenClaimWindow("app paused");
            if (!paused && ClaimOnResume) ClaimDeferred("resume");
        }
        private bool _claimPending;
        private bool _windowClaimed;
        private string _pendingReason;
        private float _windowClaimedAtRealtime;
        public int WindowClaimSequence { get; private set; }
        public string WindowClaimReason { get; private set; }
        public bool LastDeferredClaimSkipped { get; private set; }
        public void OpenClaimWindow(string why)
        {
            if (!_windowClaimed && WindowClaimSequence == 0)
            {
                FlowTrace.Step("Offline", $"claim window opened ({why}) -- nothing claimed yet this process.");
                return;
            }
            FlowTrace.Step("Offline",
                $"claim window re-opened ({why}) -- claim #{WindowClaimSequence} ({WindowClaimReason}) covered the previous " +
                $"window; the next trigger will claim again.");
            _windowClaimed = false;
            WindowClaimSequence = 0;
            WindowClaimReason = null;
        }
        private bool TryLatchClaim(string reason)
        {
            if (_claimPending)
            {
                LastDeferredClaimSkipped = true;
                FlowTrace.Step("Offline",
                    $"claim({reason}) SKIPPED - claim ({_pendingReason}) is already pending for this window " +
                    "(the second trigger of a cold launch; see the 2026-09-04 22:29 '0m' capture).");
                return false;
            }
            if (_windowClaimed)
            {
                float ageMs = (Time.realtimeSinceStartup - _windowClaimedAtRealtime) * 1000f;
                LastDeferredClaimSkipped = true;
                FlowTrace.Step("Offline",
                    $"claim({reason}) SKIPPED - claim #{WindowClaimSequence} ({WindowClaimReason}) already covered this window " +
                    $"{ageMs:0} ms ago");
                return false;
            }
            LastDeferredClaimSkipped = false;
            _claimPending = true;
            _pendingReason = reason;
            return true;
        }
        private OfflineClaimWindow RunLatchedClaim(string reason)
        {
            OfflineClaimWindow window = default;
            try
            {
                window = OfflineClaimCoordinator.Claim(reason);
            }
            finally
            {
                _claimPending = false;
                _pendingReason = null;
                _windowClaimed = true;
                _windowClaimedAtRealtime = Time.realtimeSinceStartup;
                WindowClaimSequence = window.Sequence;
                WindowClaimReason = reason;
            }
            return window;
        }
        private void ClaimDeferred(string reason)
        {
            if (!isActiveAndEnabled) return;
            if (!TryLatchClaim(reason)) return;
            StartCoroutine(ClaimAfterTwoFrames(reason));
        }
        public OfflineClaimWindow ClaimDeferredNow(string reason)
        {
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();
            if (!TryLatchClaim(reason)) return default;
            return RunLatchedClaim(reason);
        }
        private System.Collections.IEnumerator ClaimAfterTwoFrames(string reason)
        {
            EnsureJobSubscription();
            yield return null;
            EnsureJobSubscription();
            yield return null;
            EnsureJobSubscription();
            RunLatchedClaim(reason);
        }
        public OfflineHarvestResult ClaimAccrual()
        {
            FlowTrace.Step("Offline", "ClaimAccrual");
            OfflineClaimCoordinator.Register(this);
            EnsureSubscribed();
            EnsureJobSubscription();
            _lastResult = OfflineHarvestResult.None;
            OfflineClaimCoordinator.Claim("OfflineHarvestService.ClaimAccrual");
            return _lastResult ?? OfflineHarvestResult.None;
        }
        public void ApplyOfflineWindow(OfflineClaimWindow window)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                FlowTrace.Warn("Offline", "no GameStateService — node accrual skipped (None)");
                _lastResult = OfflineHarvestResult.None;
                return;
            }
            double elapsedSec = window.ElapsedSeconds;
            double cappedSec = window.CappedSeconds(OfflineCapHours);
            bool wasCapped = window.ExceedsCap(OfflineCapHours);
            if (wasCapped) FlowTrace.Warn("Offline", $"away {elapsedSec:0}s exceeds cap {OfflineCapSeconds:0}s — capped");
            var result = new OfflineHarvestResult
            {
                AwaySeconds = elapsedSec,
                WasCapped = wasCapped,
                ServerAnchored = TimeSource.IsServerAnchored,
                WindowStartUnixMs = window.WindowStartUnixMs,
                NowUnixMs = window.NowUnixMs,
            };
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: clock = {result.ClockSource} " +
                $"(ServerClock.IsTrusted={TimeSource.IsServerAnchored}); window {window.WindowStartUnixMs:0} -> " +
                $"{window.NowUnixMs:0}. " +
                (result.IsProvisional
                    ? "PROVISIONAL — the server reconciles this window on the next save."
                    : "server-anchored — a wall-clock edit could not have moved it."));
            double boostedSec = HarvestBoostService.BoostedSeconds(window.NowUnixMs, cappedSec, OfflineCapSeconds);
            if (cappedSec > 0.0)
            {
                AccrueWorkerNodes(result, cappedSec, boostedSec);
                AccrueSettlements(result, cappedSec, boostedSec);
                AccruePets(result, cappedSec, boostedSec);
                FlowTrace.Step("Offline", $"accrued over {cappedSec:0}s" +
                    (boostedSec > cappedSec ? $" (boosted to {boostedSec:0}s for non-crystal sources)" : "") +
                    $": worker-owned={_workerOwnedThisClaim.Count} node(s), total={result.Total}");
            }
            if (result.Total > 0) Grant(result, state);
            else FlowTrace.Step("Offline", "zero haul — clock still advances (prevents retroactive first-claim)");
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: 'harvest-nodes' share = {cappedSec:0}s of the {elapsedSec:0}s window " +
                $"(cap {OfflineCapHours:0.##}h) -> total {result.Total}.");
            _lastResult = result;
            _lastResultSeq = window.Sequence;
        }
        private bool _subscribedToCompletion;
        private int _lastResultSeq = -1;
        private void EnsureSubscribed()
        {
            if (_subscribedToCompletion) return;
            OfflineClaimCoordinator.ClaimCompleted += OnClaimCompleted;
            _subscribedToCompletion = true;
        }
        private static readonly List<OfflineHarvestResult.OfflineJobLine> s_completedJobs =
            new List<OfflineHarvestResult.OfflineJobLine>();
        private const int MaxRecordedJobs = 32;
        private const double JobWindowSlackMs = 5000.0;
        private bool _subscribedToJobs;
        private void EnsureJobSubscription()
        {
            if (_subscribedToJobs) return;
            var timers = BuildTimerService.Instance;
            if (timers == null) return;
            timers.JobCompleted -= OnAnyJobCompleted;   // belt-and-braces: never double-attach
            timers.JobCompleted += OnAnyJobCompleted;
            _subscribedToJobs = true;
            FlowTrace.Step("Offline",
                "away summary attached to BuildTimerService.JobCompleted -- finished jobs will be " +
                "reported on the next reveal.");
        }
        private void OnAnyJobCompleted(BuildJobData job)
        {
            Guard.Try("Offline", $"record completed job '{job.StructureId}' for the away summary", () =>
            {
                var entry = BuildTimerService.EntryFor(job);
                s_completedJobs.Add(new OfflineHarvestResult.OfflineJobLine
                {
                    Verb = string.IsNullOrEmpty(entry.Verb) ? "COMPLETE" : entry.Verb,
                    Label = string.IsNullOrEmpty(entry.Label) ? "Job" : entry.Label,
                    FinishedUnixMs = job.FinishMs,
                });
                while (s_completedJobs.Count > MaxRecordedJobs) s_completedJobs.RemoveAt(0);
                FlowTrace.Step("Offline",
                    $"away summary recorded a finished job: {entry.Verb} '{entry.Label}' " +
                    $"(finishMs={job.FinishMs:0}); {s_completedJobs.Count} awaiting a reveal.");
            });
        }
        private static void AttachCompletedJobs(OfflineHarvestResult result, OfflineClaimWindow window)
        {
            result.CompletedJobs.Clear();
            if (s_completedJobs.Count == 0) return;
            double from = window.WindowStartUnixMs - JobWindowSlackMs;
            double to = window.NowUnixMs + JobWindowSlackMs;
            int skipped = 0;
            for (int i = s_completedJobs.Count - 1; i >= 0; i--)
            {
                var j = s_completedJobs[i];
                if (j == null) { s_completedJobs.RemoveAt(i); continue; }
                if (j.FinishedUnixMs < from || j.FinishedUnixMs > to) { skipped++; continue; }
                result.CompletedJobs.Insert(0, j);        // oldest-first, the order they landed
                s_completedJobs.RemoveAt(i);
            }
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary claims {result.CompletedJobCount} finished job(s) " +
                $"from window {from:0}..{to:0}; {skipped} recorded job(s) fell outside it and were left " +
                "for a later window.");
        }
        private static void AttachPendingCollectors(OfflineHarvestResult result)
        {
            result.PendingCollectors.Clear();
            List<ResourceCollectorService.PendingLine> lines = null;
            Guard.Try("Offline", "read pending collectors for the away summary",
                () => lines = ResourceCollectorService.PendingByResource());
            result.PendingCollectors.AddRange(LinesFrom(lines));
            int total = 0, count = 0;
            foreach (var line in result.PendingCollectors) { total += line.Pending; count += line.Collectors; }
            result.PendingCollectorTotal = total;
            result.PendingCollectorCount = count;
        }
        public static List<OfflineHarvestResult.OfflineCollectorLine> LinesFrom(
            IReadOnlyList<ResourceCollectorService.PendingLine> lines)
        {
            var rows = new List<OfflineHarvestResult.OfflineCollectorLine>();
            if (lines == null) return rows;
            foreach (var line in lines)
            {
                if (line == null || line.Pending <= 0) continue;
                rows.Add(new OfflineHarvestResult.OfflineCollectorLine
                {
                    Resource = ResourceBuildingProgression.LabelFor(line.Resource),
                    Pending = line.Pending,
                    Collectors = line.Collectors,
                });
            }
            return rows;
        }
        private static void AttachSiloPending(OfflineHarvestResult result)
        {
            result.SiloPending.Clear();
            result.SiloTotal = 0;
            result.SiloAtCap = false;
            int[] split = null;
            Guard.Try("Offline", "read the Echo silo split for the away summary",
                () => split = EchoService.PredictDumpSplit());
            if (split == null || split.Length < 5) return;
            AddSiloLine(result, HarvestResource.Wood,     split[EchoService.ShareWood]);
            AddSiloLine(result, HarvestResource.Iron,     split[EchoService.ShareIron]);
            AddSiloLine(result, HarvestResource.Stone,     split[EchoService.ShareFood]);
            AddSiloLine(result, HarvestResource.Crystals, split[EchoService.ShareCrystals]);
            var echo = EchoService.Instance;
            result.SiloAtCap = echo != null && echo.SiloAtCap;
            FlowTrace.Step("Offline",
                $"away summary: Echo silo holds {result.SiloTotal} across {result.SiloPending.Count} resource row(s)" +
                (result.SiloAtCap ? " -- AT CAP, the Echoes have stopped gathering until the player collects." : "."));
        }
        private static void AttachAttacks(OfflineHarvestResult result, OfflineClaimWindow window)
        {
            result.AttackCount = 0;
            result.AttackBreachName = string.Empty;
            result.AttackOutcomeWord = string.Empty;
            double from = window.WindowStartUnixMs - JobWindowSlackMs;
            double to = window.NowUnixMs + JobWindowSlackMs;
            Guard.Try("Offline", "read the defence ledger for the away summary", () =>
            {
                var all = DefenseReportLedger.All();
                if (all == null) return;
                DefenseOutcomeRecord newest = null;
                for (int i = 0; i < all.Count; i++)
                {
                    var rec = all[i];
                    if (rec == null) continue;
                    if (rec.Timestamp < from || rec.Timestamp > to) continue;
                    result.AttackCount++;
                    if (newest == null || rec.Timestamp >= newest.Timestamp) newest = rec;
                }
                if (newest == null) return;
                result.AttackOutcomeWord = newest.Outcome.ToString().ToUpperInvariant();
                if (newest.Breaches == null) return;
                for (int i = 0; i < newest.Breaches.Count; i++)
                {
                    var b = newest.Breaches[i];
                    if (b == null || string.IsNullOrEmpty(b.DisplayName)) continue;
                    result.AttackBreachName = b.DisplayName;   // the FIRST crossing -- "they came from the north first"
                    break;
                }
            });
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary counted {result.AttackCount} defence report(s) in " +
                $"window {from:0}..{to:0}" +
                (result.AttackCount > 0
                    ? $" -- newest outcome={result.AttackOutcomeWord} breach='{result.AttackBreachName}'."
                    : " -- the town was not attacked (away time becomes PRESSURE today, not a resolved battle)."));
        }
        private static void AddSiloLine(OfflineHarvestResult result, HarvestResource res, int amount)
        {
            if (amount <= 0) return;
            result.SiloPending.Add(new OfflineHarvestResult.OfflineSiloLine
            {
                Resource = ResourceBuildingProgression.LabelFor(res),
                Pending = amount,
            });
            result.SiloTotal += amount;
        }
        public struct ReturnRow
        {
            public HarvestResource Resource;
            public string Word;
            public int FromCollectors;
            public int FromSilo;
            public int Pending;
            public int Headroom;
            public int Banks;
            public int Waits;
            public bool NothingBanks => Banks <= 0 && Pending > 0;
        }
        public static List<ReturnRow> BuildReturnRows(OfflineHarvestResult result)
            => BuildReturnRows(result, ResourceCollectorService.HeadroomFor);
        public static List<ReturnRow> BuildReturnRows(OfflineHarvestResult result,
            System.Func<HarvestResource, int> headroom)
        {
            var rows = new List<ReturnRow>();
            if (result == null || headroom == null) return rows;
            foreach (var res in ResourceCollectorService.RailOrder)
            {
                string word = ResourceBuildingProgression.LabelFor(res);
                int collectors = SumFor(result.PendingCollectors, word);
                int silo = SumSiloFor(result.SiloPending, word);
                int pending = collectors + silo;
                if (pending <= 0) continue;
                int room = headroom(res);
                if (room < 0) room = 0;
                int banks = room >= pending ? pending : room;
                rows.Add(new ReturnRow
                {
                    Resource = res,
                    Word = word,
                    FromCollectors = collectors,
                    FromSilo = silo,
                    Pending = pending,
                    Headroom = room,
                    Banks = banks,
                    Waits = pending - banks,
                });
            }
            return rows;
        }
        private static int SumFor(List<OfflineHarvestResult.OfflineCollectorLine> lines, string word)
        {
            int sum = 0;
            if (lines == null) return 0;
            foreach (var line in lines)
                if (line != null && string.Equals(line.Resource, word, System.StringComparison.OrdinalIgnoreCase))
                    sum += line.Pending;
            return sum;
        }
        private static int SumSiloFor(List<OfflineHarvestResult.OfflineSiloLine> lines, string word)
        {
            int sum = 0;
            if (lines == null) return 0;
            foreach (var line in lines)
                if (line != null && string.Equals(line.Resource, word, System.StringComparison.OrdinalIgnoreCase))
                    sum += line.Pending;
            return sum;
        }
        public static string ReturnRowLabel(ReturnRow r)
            => r.NothingBanks
                ? $"{r.Word.ToUpperInvariant()} {r.Pending} WAITING"
                : $"{r.Word.ToUpperInvariant()} +{r.Banks}";
        public static string ReturnRowDestiny(ReturnRow r)
        {
            if (r.Waits <= 0) return "COLLECT NOW";
            if (r.Banks <= 0) return "STORAGE FULL - STAYS PUT";
            return $"{r.Waits} MORE WAITS";
        }
        public static string ReturnFooterLine(IReadOnlyList<ReturnRow> rows)
        {
            if (rows == null) return null;
            int waiting = 0;
            for (int i = 0; i < rows.Count; i++) waiting += rows[i].Waits;
            if (waiting <= 0) return null;
            int fromCollectors = 0, fromSilo = 0;
            for (int i = 0; i < rows.Count; i++) { fromCollectors += rows[i].FromCollectors; fromSilo += rows[i].FromSilo; }
            string where = fromCollectors > 0 && fromSilo > 0 ? "your collectors and Echo silo"
                         : (fromSilo > 0 ? "your Echo silo" : "your collectors");
            string n = waiting.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            return $"{n} is already waiting in {where} - nothing is lost. Spend, or upgrade storage.";
        }
        public static string SiloStalledLine(OfflineHarvestResult result)
            => result != null && result.SiloAtCap && result.SiloTotal > 0
                ? "Echo silo is full - your Echoes gather nothing more until you collect."
                : null;
        private void OnClaimCompleted(OfflineClaimWindow window)
        {
            var result = _lastResult;
            if (result == null) return;
            if (_lastResultSeq != window.Sequence)
            {
                FlowTrace.Step("Offline",
                    $"claim #{window.Sequence}: no share applied to 'harvest-nodes' this claim " +
                    $"(held result is from #{_lastResultSeq}) -- away summary NOT re-revealed.");
                return;
            }
            var mend = EchoRepairService.LastOfflineMendReport;
            result.Mend = (mend != null && mend.ClaimSequence == window.Sequence) ? mend : EchoMendReport.None;
            AttachCompletedJobs(result, window);
            AttachPendingCollectors(result);
            AttachSiloPending(result);   // WO-1434 -- the fifth axis
            AttachAttacks(result, window);   // WO-1408 -- the row that carries a DOOR, not a sixth gate axis
            bool hasContent = result.HasSummaryContent;
            bool show = ShouldRevealAwaySummary(result);
            FlowTrace.Step("Offline",
                $"claim #{window.Sequence}: away summary gate -> haul={result.Total}, " +
                $"mendNews={result.HasMendNews}, jobs={result.CompletedJobCount}, " +
                $"collectorsPending={result.PendingCollectorTotal} across {result.PendingCollectorCount} " +
                $"collector(s), siloPending={result.SiloTotal} (atCap={result.SiloAtCap}), " +
                $"away={result.AwaySeconds:0}s minimum={MinAwaySummarySeconds:0}s " +
                $"=> {(show ? "REVEAL" : hasContent ? "stored normally; short absence, no modal" : "no reveal")}.");
            if (!show) return;
            Claimed?.Invoke(result);
            TryShowPopup(result);
        }
        private static double SecondsFor(MineResource resource, double cappedSec, double boostedSec) =>
            HarvestBoostService.IsBoostable(resource) ? boostedSec : cappedSec;
        private void AccrueWorkerNodes(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
            _workerOwnedThisClaim.Clear();
            var wm = WorkerManager.Instance;
            if (wm == null) return;
            IReadOnlyList<MineNode> nodes = wm.ActiveAssignments();
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                _workerOwnedThisClaim.Add(node);
                if (node.IsDepleted) continue;
                float rate = node.RatePerSecond;
                if (rate <= 0f) continue;
                int accrued = (int)(rate * SecondsFor(node.Resource, cappedSec, boostedSec));
                result.Add(node.Resource, accrued);
            }
        }
        private void AccrueSettlements(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
            var all = Settlement.All;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || s.Phase != SettlementPhase.Active) continue;
                var node = s.ClaimedNode;
                if (node == null || !node.UseFiniteReserve || node.IsReserveEmpty) continue;
                int owed = (int)(s.HarvestRatePerSecond * SecondsFor(node.Resource, cappedSec, boostedSec));
                if (owed <= 0) continue;
                int banked = Mathf.Min(owed, node.ReserveRemaining);
                result.Add(node.Resource, banked);
            }
        }
        private void AccruePets(OfflineHarvestResult result, double cappedSec, double boostedSec)
        {
#if UNITY_2023_1_OR_NEWER
            var nodes = Object.FindObjectsByType<MineNode>();
#else
            var nodes = Object.FindObjectsByType<MineNode>();
#endif
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || node.IsDepleted) continue;
                if (!node.IsClaimedByWorker) continue;             // unclaimed → no harvester
                if (_workerOwnedThisClaim.Contains(node)) continue; // a Worker owns it → already credited
                float rate = node.RatePerSecond;                   // finite-reserve nodes report 0 (settlement path)
                if (rate <= 0f) continue;
                int accrued = (int)(rate * SecondsFor(node.Resource, cappedSec, boostedSec));
                result.Add(node.Resource, accrued);
            }
        }
        private void Grant(OfflineHarvestResult result, GameState state)
        {
            int iron, wood, food, lostIron, lostWood, lostFood;
            using (DeNelle.Core.UI.BankOverflowToastPresenter.BeginWarnScope("OfflineHarvest"))
            {
                iron = TownBankCapacity.ClampGrant(BankResource.Iron, state.Iron, result.Iron, "OfflineHarvest", out lostIron);
                wood = TownBankCapacity.ClampGrant(BankResource.Wood, state.Wood, result.Wood, "OfflineHarvest", out lostWood);
                food = TownBankCapacity.ClampGrant(BankResource.Stone, state.Resources.Stone, result.Stone, "OfflineHarvest", out lostFood);
            }
            WarnDiscarded("Iron", state.Iron, result.Iron, iron, lostIron);
            WarnDiscarded("Wood", state.Wood, result.Wood, wood, lostWood);
            WarnDiscarded("Stone", state.Resources.Stone, result.Stone, food, lostFood);
            if (iron > 0) state.Iron += iron;
            if (wood > 0) state.Wood += wood;
            if (food > 0)
            {
                var bal = state.Resources;
                bal.Stone += food;
                state.Resources = bal;
            }
            if (result.AetherCrystals > 0)
            {
                var cbal = state.Resources;
                cbal.Crystals += result.AetherCrystals;
                state.Resources = cbal;
            }
            GameStateService.Instance?.ResourcesChanged?.Invoke();
            bool bankTruncated = iron != result.Iron || wood != result.Wood || food != result.Stone;
            Debug.Log($"[OfflineHarvest] Banked +{iron} iron, +{wood} wood, " +
                      $"+{food} food, +{result.AetherCrystals} crystals over " +
                      $"{Mathf.RoundToInt((float)result.AwaySeconds)}s away" +
                      (result.WasCapped ? " (away-cap)." : ".") +
                      $" clock={result.ClockSource}{(result.IsProvisional ? " (provisional until sync)" : "")}." +
                      (bankTruncated
                          ? $" BANK FULL - accrued {result.Iron} iron / {result.Wood} wood / {result.Stone} food; " +
                            "the surplus was NOT ADDED (there is no store that holds away node yield - see the WO-1445 block above)."
                          : ""));
            FlowTrace.Step("OfflineHarvest",
                $"away claim banked: away={result.AwaySeconds:0}s capped={result.WasCapped} | " +
                $"WOOD asked={result.Wood} banked={wood} notAdded={lostWood} cap={TownBankCapacity.MaxOf(BankResource.Wood)} | " +
                $"IRON asked={result.Iron} banked={iron} notAdded={lostIron} cap={TownBankCapacity.MaxOf(BankResource.Iron)} | " +
                $"STONE asked={result.Stone} banked={food} notAdded={lostFood} cap={TownBankCapacity.MaxOf(BankResource.Stone)} | " +
                $"CRYSTALS asked={result.AetherCrystals} banked={result.AetherCrystals} notAdded=0 cap=uncapped. " +
                "The HARVEST RESULT screen composes its figures from these same clamp events " +
                "(HarvestResultVM.Build), so screen == banked by construction.");
        }
        private static void WarnDiscarded(string word, int current, int asked, int banked, int lost)
        {
            if (lost <= 0) return;
            FlowTrace.Warn("OfflineHarvest",
                $"away haul NOT ADDED [{word}]: accrued {asked}, banked {banked}, {lost} could not be " +
                $"stored (wallet {current}). Away node/settlement/pet yield has NO pending pool to wait " +
                "in - unlike the collectors and the Echo silo, which both retain - so these units are " +
                "not added at all. The HARVEST RESULT row says \"not added\", never \"waiting, safe\".");
        }
        private OfflineHarvestResult _deferredReveal;
        private bool _sceneHookArmed;
        private bool _tutorialDeferred;
        public bool HasDeferredReveal => _deferredReveal != null;
        private bool _subscribedToNewGame;
        private void EnsureNewGameSubscription()
        {
            if (_subscribedToNewGame) return;
            GameStateService.NewGameStarted -= OnNewGameStarted;
            GameStateService.NewGameStarted += OnNewGameStarted;
            _subscribedToNewGame = true;
        }
        private void OnNewGameStarted()
        {
            var dropped = _deferredReveal;
            _deferredReveal = null;
            _tutorialDeferred = false;
            DisarmSceneHook();
            _lastResult = null;
            _lastResultSeq = -1;
            WelcomeBackPopup.DismissIfOpen("new game");
            FlowTrace.Step("Offline",
                dropped != null
                    ? $"New Game: DROPPED a parked welcome-back reveal (away={dropped.AwaySeconds:0}s " +
                      $"haul={dropped.Total} collectorsPending={dropped.PendingCollectorTotal}) - it was measured " +
                      "on the PREVIOUS save and must never be released onto the new town (WO-1414 A)."
                    : "New Game: no parked welcome-back reveal to drop; held share cleared (WO-1414 A).");
        }
        private void Update()
        {
            if (WelcomeBackPopup.IsOpen && TutorialFlow.IsMandatoryChainLive)
            {
                var onScreen = WelcomeBackPopup.ActiveResult;
                WelcomeBackPopup.DismissIfOpen("mandatory tutorial chain live under the report (WO-1414 D)");
                if (onScreen != null)
                {
                    _deferredReveal = onScreen;
                    _tutorialDeferred = true;
                    FlowTrace.Warn("Offline",
                        $"welcome-back RE-PARKED: the report was already on screen (WelcomeBackUI/ObsidianPanel, " +
                        $"sortingOrder 32020) when the mandatory tutorial chain went live at " +
                        $"'{TutorialFlow.LiveChainStateLine}' -- it covers the ONE skip control (sortingOrder 6000) " +
                        $"and the beat's dialogue, so it is deferred until the chain finishes " +
                        $"(AwaySeconds={onScreen.AwaySeconds:0} haul={onScreen.Total} " +
                        $"collectorsPending={onScreen.PendingCollectorTotal}). The haul is already banked; nothing " +
                        "is lost by waiting.");
                }
                else
                {
                    FlowTrace.Warn("Offline",
                        "welcome-back RE-PARK: the open report carried NO result to park (it was dismissed to clear " +
                        $"the tutorial chain at '{TutorialFlow.LiveChainStateLine}'); nothing will be re-shown.");
                }
                return;
            }
            if (_deferredReveal == null || !_tutorialDeferred) return;
            if (TutorialFlow.IsMandatoryChainLive) return;
            var pending = _deferredReveal;
            _deferredReveal = null;
            _tutorialDeferred = false;
            FlowTrace.Step("Offline",
                $"welcome-back tutorial deferral RELEASED: the mandatory tutorial chain is no longer live " +
                $"(AwaySeconds={pending.AwaySeconds:0}).");
            TryShowPopup(pending);   // re-runs the combat + hub checks on the way in
        }
        private void TryShowPopup(OfflineHarvestResult result)
        {
            if (IsCombatActive())
            {
                Debug.Log("[OfflineHarvest] Combat active — welcome-back reveal suppressed (haul already banked).");
                return;
            }
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _deferredReveal = result;
                _tutorialDeferred = false;
                if (!_sceneHookArmed)
                {
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedForReveal;
                    _sceneHookArmed = true;
                }
                FlowTrace.Step("Offline",
                    $"welcome-back DEFERRED: active scene '{scene}' is not a hub -- reveal waits for the next hub load. " +
                    $"AwaySeconds={result.AwaySeconds:0} haul={result.Total} collectorsPending={result.PendingCollectorTotal}.");
                return;
            }
            if (TutorialFlow.IsMandatoryChainLive)
            {
                _deferredReveal = result;
                _tutorialDeferred = true;
                string awaited = TutorialFlow.IsAwaitingDialogue
                    ? $"a tutorial step is awaiting '{TutorialFlow.AwaitedDialogueSignal}'"
                    : $"the mandatory tutorial chain is live at '{TutorialFlow.LiveChainStateLine}' " +
                      "(no step is awaiting a dialogue yet -- this is the Settle window the narrow WO-1414 C key missed)";
                FlowTrace.Step("Offline",
                    $"welcome-back DEFERRED: {awaited} -- the reveal waits for the chain to finish " +
                    $"(AwaySeconds={result.AwaySeconds:0} haul={result.Total}).");
                return;
            }
            _deferredReveal = null;
            _tutorialDeferred = false;
            FlowTrace.Step("Offline",
                $"welcome-back SHOW: scene='{scene}' is a hub. AwaySeconds={result.AwaySeconds:0} " +
                $"clock={result.ClockSource} haul={result.Total} collectorsPending={result.PendingCollectorTotal}.");
            WelcomeBackPopup.Show(result);
        }
        private void OnSceneLoadedForReveal(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            var pending = _deferredReveal;
            if (pending == null) { DisarmSceneHook(); return; }
            if (!DeNelle.Core.HubScenes.IsHub(scene.name))
            {
                FlowTrace.Step("Offline",
                    $"welcome-back still DEFERRED: loaded scene '{scene.name}' is not a hub (AwaySeconds={pending.AwaySeconds:0}).");
                return;
            }
            _deferredReveal = null;
            _tutorialDeferred = false;
            DisarmSceneHook();
            FlowTrace.Step("Offline",
                $"welcome-back deferred reveal RELEASED: hub scene '{scene.name}' loaded ({mode}). AwaySeconds={pending.AwaySeconds:0}.");
            TryShowPopup(pending);   // re-runs the combat check on the hub it just landed in
        }
        private void DisarmSceneHook()
        {
            if (!_sceneHookArmed) return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedForReveal;
            _sceneHookArmed = false;
        }
        private static bool IsCombatActive()
        {
            var wm = FindAnyObjectByType<WaveManager>();
            if (wm != null && wm.Phase == WavePhase.Active) return true;
            return false;
        }
    }
}
```

## Assets/_Modules/Village/EconomyService.cs (original lines 1-706/706; SHA256 2596159A75DB0086CEDB0F4EEFE8AD99C68AAA8EFD8E592B2A15533E8AD3C4BA)
```csharp
using System;
using UnityEngine;
using DeNelle.Core.State;
namespace DeNelle.Village
{
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
    [Serializable]
    public struct ResourceCost
    {
        [Min(0)] public int Wood;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [Newtonsoft.Json.JsonProperty("Food"), Min(0)] public int Stone;
        [Min(0)] public int Iron;
        [Min(0)] public int Crystals;
        [Min(0)] public int Coins;
        public ResourceCost(int wood = 0, int stone = 0, int iron = 0, int crystals = 0, int coins = 0)
        {
            Wood     = wood;
            Stone     = stone;
            Iron     = iron;
            Crystals = crystals;
            Coins    = coins;
        }
        public bool IsZero => Wood == 0 && Stone == 0 && Iron == 0 && Crystals == 0 && Coins == 0;
        public static ResourceCost WoodOnly(int amount)     => new ResourceCost(wood:     amount);
        public static ResourceCost StoneOnly(int amount)     => new ResourceCost(stone:     amount);
        public static ResourceCost IronOnly(int amount)     => new ResourceCost(iron:     amount);
        public static ResourceCost CrystalsOnly(int amount) => new ResourceCost(crystals: amount);
    }
    [DisallowMultipleComponent]
    public sealed class EconomyService : MonoBehaviour, IEconomy
    {
        public static EconomyService Instance { get; private set; }
        [Header("Starting Resources (Wood/Iron — FALLBACK pool, used only when no GameState exists)")]
        [SerializeField, Min(0)] private int _wood     = 200; // WO-842: fallback only (EditMode/no-save boots); GameState.Wood is the authority
        [SerializeField, Min(0)] private int _iron     = 80;  // WO-842: fallback only; GameState.Iron is the authority
        public int Wood
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Wood : _wood;
            }
        }
        public int Iron
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Iron : _iron;
            }
        }
        public int Stone
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Stone : 0;
            }
        }
        public int Crystals
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Crystals : 0;
            }
        }
        public int Coins
        {
            get
            {
                var state = GameStateService.Instance?.State;
                return state != null ? state.Resources.Coins : 0;
            }
        }
        public ResourceSnapshot Snapshot => new ResourceSnapshot(Wood, Stone, Iron, Crystals);
        public int SecuredOutpostCount { get; private set; }
        public float TerritoryMultiplier => 1f + 0.05f * SecuredOutpostCount;
        public void OnOutpostSecured()
        {
            SecuredOutpostCount = Mathf.Max(0, SecuredOutpostCount + 1);
            NotifyChanged();
        }
        public event Action<ResourceSnapshot> OnChanged;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
            => EnsureAvailable();
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
        private void OnGameStateResourcesChanged() => NotifyChanged();
        private void OnDestroy()
        {
            var gs = GameStateService.Instance;
            if (gs != null) gs.ResourcesChanged.RemoveListener(OnGameStateResourcesChanged);
            _bridgeAttached = false;
            if (Instance == this) Instance = null;
        }
        public bool CanAfford(ResourceCost cost)
        {
            return Wood   >= cost.Wood        // WO-842 — GameState-backed (fallback pool when no save service)
                && Stone   >= cost.Stone        // DEF-121 — GameState-backed
                && Iron   >= cost.Iron        // WO-842 — GameState-backed
                && Crystals >= cost.Crystals  // WO-131 — GameState-backed
                && Coins  >= cost.Coins;      // GOLD — GameState.Resources.Coins (shops)
        }
        public bool TrySpend(ResourceCost cost)
        {
            if (!CanAfford(cost)) return false;
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
        public ResourceCost Grant(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.EarnedIncome);
        public ResourceCost GrantPurchased(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.PurchasedOrPromised);
        public ResourceCost GrantUncapped(ResourceCost amount)
            => GrantInternal(amount, DeNelle.Core.Economy.BankGrantKind.DevHarness);
        private ResourceCost GrantInternal(ResourceCost amount, DeNelle.Core.Economy.BankGrantKind kind)
        {
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
            int crystals = Mathf.Max(0, amount.Crystals);
            if (crystals > 0)
                GameStateService.Instance?.AddCrystals(crystals);   // WO-131 — GameState-backed grant
            int coins = Mathf.Max(0, amount.Coins);
            if (coins > 0)
                AddCoins(coins);                                    // GOLD — GameState.Resources.Coins (sell refunds)
            NotifyChanged();
            return new ResourceCost(wood, stone, iron, crystals, coins);
        }
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
        public void Grant(int wood = 0, int stone = 0, int iron = 0, int crystals = 0)
        {
            Grant(new ResourceCost(wood, stone, iron, crystals));
        }
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
        [Obsolete("Use CanAfford(ResourceCost) for multi-resource checks.")]
        public bool CanAfford(int woodCost) => Wood >= woodCost;   // WO-842: unified wallet
        [Obsolete("Use TrySpend(ResourceCost) for multi-resource spending.")]
        public void Spend(int woodCost)
        {
            if (woodCost <= 0) return;
            TrySpend(ResourceCost.WoodOnly(woodCost));
        }
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
```

## Assets/_Modules/Village/AuthoredCastleStorefront.cs (original lines 1-132/132; SHA256 BBF52319BFA2FAA5354CD124B1DF23091C4783259E6C6233778DBB20AC6EFA0B)
```csharp
using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class AuthoredCastleStorefront : MonoBehaviour
    {
        [SerializeField] private string canonicalId;
        [SerializeField] private string legacyName;
        [SerializeField] private bool preserveAuthoredVisual = true;
        [SerializeField] private bool repairTripoMaterials = true;
        [SerializeField] private Texture2D forcedAlbedo;
        public string CanonicalId => canonicalId;
        public string LegacyName => legacyName;
        public bool PreserveAuthoredVisual => preserveAuthoredVisual;
        public bool RepairTripoMaterials => repairTripoMaterials;
        public Texture2D ForcedAlbedo => forcedAlbedo;
        public static bool IsAuthoredBarracksRecord(DeNelle.Core.State.PlacedStructureData data) =>
            data.itemId == "barracks" && data.authoredSourceId == "barracks" && data.authoredPose != null;
        public static bool IsBoundAuthoredRoot(Transform target, string canonicalId)
        {
            if (target == null || canonicalId != "barracks") return false;
            var marker = target.GetComponent<AuthoredCastleStorefront>();
            if (marker == null || !marker.PreserveAuthoredVisual || marker.CanonicalId != canonicalId ||
                Find(marker.LegacyName, includeInactive: true) != target) return false;
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            if (state?.BaseLayout == null) return false;
            int matches = 0;
            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                var record = state.BaseLayout[i];
                if (record.itemId != canonicalId) continue;
                if (!IsAuthoredBarracksRecord(record)) return false;
                matches++;
            }
            return matches == 1;
        }
        public void SetMaterialPolicy(bool repair, Texture2D albedo)
        {
            repairTripoMaterials = repair;
            forcedAlbedo = albedo;
        }
        public void Configure(string id, string legacyName)
        {
            canonicalId = id;
            this.legacyName = legacyName;
        }
        private static readonly List<GameObject> Roots = new List<GameObject>(64);
        private static readonly List<Transform> Transforms = new List<Transform>(256);
        public static Transform Find(string legacyName, bool includeInactive = false)
        {
            if (string.IsNullOrEmpty(legacyName)) return null;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            Roots.Clear();
            scene.GetRootGameObjects(Roots);
            Transform marked = null;
            Transform legacy = null;
            int markedCount = 0, legacyCount = 0;
            for (int r = 0; r < Roots.Count; r++)
            {
                Transforms.Clear();
                Roots[r].GetComponentsInChildren(true, Transforms);
                for (int i = 0; i < Transforms.Count; i++)
                {
                    var candidate = Transforms[i];
                    var identity = candidate.GetComponent<AuthoredCastleStorefront>();
                    if (identity != null && string.Equals(identity.LegacyName, legacyName, StringComparison.Ordinal))
                    {
                        marked = candidate;
                        markedCount++;
                    }
                    if (candidate.name == legacyName && (includeInactive || candidate.gameObject.activeInHierarchy))
                    {
                        legacy = candidate;
                        legacyCount++;
                    }
                }
            }
            Roots.Clear();
            Transforms.Clear();
            if (markedCount > 1)
            {
                FlowTrace.Fail("Hub", $"Authored storefront identity '{legacyName}' has {markedCount} marked hosts in '{scene.name}'; refusing ambiguous lookup.");
                return null;
            }
            if (markedCount == 1)
                return includeInactive || marked.gameObject.activeInHierarchy ? marked : null;
            if (legacyCount > 1)
            {
                FlowTrace.Fail("Hub", $"Legacy storefront name '{legacyName}' has {legacyCount} hosts in '{scene.name}'; refusing ambiguous lookup.");
                return null;
            }
            return legacy;
        }
        public static bool MatchesAncestor(Transform target, string canonicalId)
        {
            if (target == null || string.IsNullOrEmpty(canonicalId) ||
                target.gameObject.scene != SceneManager.GetActiveScene()) return false;
            for (var current = target; current != null; current = current.parent)
            {
                var identity = current.GetComponent<AuthoredCastleStorefront>();
                if (identity != null && string.Equals(identity.CanonicalId, canonicalId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
```

## Assets/_Modules/Core/State/GameStateService.cs (original lines 1735-1890/4052; SHA256 20703D255B7ECE30AAB50AB9213EE06C6CBD4A3486E4EA6EB60E306C7730BF0B)
```csharp
        public const string CollectorPendingPrefPrefix    = "dotr.collector.pending.";
        public const string CollectorHpPrefPrefix         = "dotr.collector.hp.";
        public const string CollectorLastAccrualPrefPrefix = "dotr.collector.lastaccrual.";
        public const string ResourceBuildingLevelPrefPrefix = "dotr.resbuilding.level.";
        public const string CollectorKnownIdsPrefKey = "dotr.collector.ids";
        public static readonly string[] CollectorPrefPrefixes =
        {
            CollectorPendingPrefPrefix,
            CollectorHpPrefPrefix,
            CollectorLastAccrualPrefPrefix,
        };
        public static void RegisterCollectorId(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return;
            string raw = PlayerPrefs.GetString(CollectorKnownIdsPrefKey, string.Empty);
            foreach (var existing in raw.Split(','))
                if (string.Equals(existing, buildingId, StringComparison.Ordinal)) return;
            PlayerPrefs.SetString(CollectorKnownIdsPrefKey,
                string.IsNullOrEmpty(raw) ? buildingId : raw + "," + buildingId);
            PlayerPrefs.Save();
        }
        public static List<string> KnownCollectorIds()
        {
            var ids = new List<string>();
            void Add(string id)
            {
                if (string.IsNullOrEmpty(id)) return;
                for (int i = 0; i < ids.Count; i++)
                    if (string.Equals(ids[i], id, StringComparison.Ordinal)) return;
                ids.Add(id);
            }
            foreach (var id in PlayerPrefs.GetString(CollectorKnownIdsPrefKey, string.Empty).Split(','))
                Add(id);
            Guard.Try("Save", "enumerate collector catalog ids", () =>
            {
                foreach (var e in DeNelle.Core.Catalog.CatalogRegistry.OfType(
                             DeNelle.Core.Catalog.CatalogType.Collector))
                {
                    if (e == null) continue;
                    string bid = e.repo != null && !string.IsNullOrEmpty(e.repo.collectorBuildingId)
                        ? e.repo.collectorBuildingId
                        : e.id;
                    Add(bid);
                }
            });
            return ids;
        }
        private static void ClearHarvestPrefs()
        {
            var ids = KnownCollectorIds();
            int deleted = 0;
            foreach (var id in ids)
            {
                foreach (var prefix in CollectorPrefPrefixes)
                {
                    string key = prefix + id;
                    if (PlayerPrefs.HasKey(key)) { PlayerPrefs.DeleteKey(key); deleted++; }
                }
                string levelKey = ResourceBuildingLevelPrefPrefix + id;
                if (PlayerPrefs.HasKey(levelKey)) { PlayerPrefs.DeleteKey(levelKey); deleted++; }
            }
            if (PlayerPrefs.HasKey(CollectorKnownIdsPrefKey))
            {
                PlayerPrefs.DeleteKey(CollectorKnownIdsPrefKey);
                deleted++;
            }
            PlayerPrefs.Save();
            FlowTrace.Step("Save",
                $"ResetToNewGame: cleared {deleted} stale harvest PlayerPrefs key(s) across {ids.Count} " +
                "collector id(s) (dotr.collector.pending/hp/lastaccrual.* + dotr.resbuilding.level.*) - " +
                "a new game starts with an EMPTY collector and a base-level cap, never the previous " +
                "save's 14,089-resource fill (WO-1371).");
        }
```

// =============================================================================
// ResourceCollector — CoC-style typed town collector (WO-663 / WO-664).
// Accrues into Pending; Collect() banks to wallet; siege raids steal uncollected.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.World;
using DeNelle.Village;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>
    /// One resource-type collector (Farm / Lumbermill / Forge). Pending fills locally;
    /// <see cref="Collect"/> pipes value home through <see cref="EconomyService.GrantSpendable"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceCollector : MonoBehaviour, IDamageableStructure, ISiegeLootTarget, IHarvestSource
    {
        // WO-1371 - the key SPELLINGS now live in DeNelle.Core (GameStateService), because
        // ResetToNewGame has to delete exactly these keys and a key restated in two files is this
        // repo's most repeated defect. Aliased as consts here so every use site below is unchanged
        // and there is still only ONE authority for the string.
        private const string PendingPrefsPrefix = GameStateService.CollectorPendingPrefPrefix;
        private const string HpPrefsPrefix = GameStateService.CollectorHpPrefPrefix;

        /// <summary>
        /// WO-859 - per-collector LAST-ACCRUAL stamp (unix ms), the whole basis of away/offline
        /// accrual. Deliberately a PlayerPrefs key beside the two this collector already owns:
        /// pending and HP persist this way, so there is NO GameState field, NO SaveSchema change
        /// and NO version bump. Stored as a STRING, not a float: unix-ms is ~1.7e12 and a float
        /// carries only ~7 significant digits, which would quantise the stamp to ~100-second
        /// buckets and make every catch-up wrong by up to two minutes.
        /// </summary>
        private const string LastAccrualPrefsPrefix = GameStateService.CollectorLastAccrualPrefPrefix;

        /// <summary>
        /// WO-859 overflow guard (NOT the design cap - capacity is the cap). Purely so a
        /// tampered/rolled-forward system clock cannot overflow the int handed to
        /// <see cref="Accrue"/>. Anything past this is clamped; the pool clamps again anyway.
        /// </summary>
        private const double MaxAwaySeconds = 30.0 * 24.0 * 3600.0;

        private const float DefaultMaxHp = 120f;

        [SerializeField] private string _buildingId = ResourceBuildingProgression.FarmId;
        [SerializeField] private float _maxHp = DefaultMaxHp;

        private float _hp;
        private double _pending;
        private bool _broken;

        // WO-859 - unix ms of the last time production was ACCOUNTED FOR on this collector.
        // 0 = never stamped (a fresh collector: seed to now, back-fill nothing).
        private double _lastAccrualMs;

        // True once Start() has run. Configure() lands BETWEEN OnEnable and Start (AddComponent
        // fires Awake+OnEnable synchronously, the factory configures on the next line), so the
        // away catch-up runs in Start where the building id is finally correct - see CatchUpAway.
        private bool _started;
        // AddComponent invokes OnEnable before Configure. Preserve an existing owner of the
        // serialized-default key so a lumbermill/forge collector cannot temporarily replace the
        // farm fallback and then orphan it while re-keying.
        private ResourceCollector _displacedOnEnable;
        private bool _suppressNextDisableSave;

        public string BuildingId => _buildingId;
        public string SourceId => _buildingId;
        public HarvestResource Resource => ResolveResource();

        /// <summary>
        /// A standing, unbroken collector is PRODUCING. The retired
        /// <c>GetLevel(_buildingId) &gt; 1</c> clause was removed 2026-08-04: it predates
        /// (this file has carried it since creation) the owner's 2026-07-13 evening ruling
        /// already recorded in <see cref="ResourceBuildingHarvester"/> - "LEVEL 1 PRODUCES:
        /// CoC-style, a placed collector earns from the moment it stands". Levels start at 1
        /// (<see cref="ResourceBuildingState.GetLevel"/> defaults to 1), so the clause made
        /// EVERY freshly placed collector accrue ZERO until its first paid upgrade, while an
        /// UNPLACED building silently paid the wallet through the harvester's direct-grant
        /// fallback - placing a collector strictly REDUCED income. With that phantom fallback
        /// removed, this is now the only accrual gate, so the retired rule would have zeroed
        /// all collector income and dead-locked the zero-seed founding bootstrap.
        /// </summary>
        public bool IsActive => IsAlive && !_broken;
        public bool CanAccrue => IsActive;
        public double PendingAmount => _pending;
        public bool IsBroken => _broken;

        /// <summary>Health 0..1 — the accrual scale (F8-45: damage reduces economy) and
        /// the wave damage-report fraction. Derived from this collector's own HP fields
        /// (data-driven — no per-type hardcode).</summary>
        public float HpFraction => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;

        /// <summary>
        /// RETIRED, AND PINNED AT ZERO -- COLLECTOR LOOTING IS REMOVED (owner ruling 2026-08-27).
        ///
        /// <para>The property survives because <c>WaveDamageReport</c> reads it to build the row's
        /// "looted N" line; it is now always 0, so that line never appears. It is NOT deleted
        /// outright because a report row that silently loses a field is harder to reason about
        /// than one whose field is provably zero.</para>
        ///
        /// <para>THE RULING: "BANK THEFT REPLACES COLLECTOR LOOTING. A siege bills ONCE per
        /// attack." A siege now takes a bounded percentage of the UNPROTECTED BANK
        /// (<c>DeNelle.Core.Defense.StakeRules</c>) and nothing at all from a collector's pending.
        /// If a collector ever starts taking again while bank theft is live, the player is charged
        /// TWICE for one siege -- which is precisely the defect the superseded WO-1139 ruling
        /// existed to prevent, and the reason its oracle was re-pointed rather than deleted.</para>
        /// </summary>
        public float LastLootStolen { get; private set; }

        /// <summary>
        /// House-clock stamp (unix ms) of the last break; 0 = this collector has not broken this
        /// session. Session-scoped, not persisted, cleared by <see cref="Repair"/>.
        ///
        /// <para>It was the scope key that stopped a still-broken shell being re-reported by every
        /// later siege. With collector looting removed there is no loot to re-report, but the stamp
        /// is kept: it is the only record that a given collector broke during a given siege, and
        /// deleting it would discard evidence rather than a mechanic (CLAUDE.md section 12).</para>
        /// </summary>
        public double LastLootStolenAtUnixMs { get; private set; }

        public double Capacity => ComputeCapacity();

        // ── WO-665a: diegetic collector-fill stack seams (model-only; the view renders) ──
        /// <summary>Number of discrete fill steps a full collector shows (each = 5% of capacity).</summary>
        public const int StepCount = 20;

        /// <summary>How many of the <see cref="StepCount"/> stack items should be shown (0..StepCount).</summary>
        public int FilledSteps => Mathf.Clamp(Mathf.FloorToInt(FillFraction * StepCount), 0, StepCount);

        /// <summary>True when pending is at (or effectively at) capacity — the FULL tell fires.</summary>
        public bool IsFull => FillFraction >= 0.999f;

        /// <summary>
        /// Raised ONLY when <see cref="FilledSteps"/> actually changes (event-driven off the
        /// accrue/collect/siege ticks — never per-frame). A separate view subscribes and re-poses
        /// its pooled props; the model never builds UI (presentation-separate law).
        /// </summary>
        public event System.Action<ResourceCollector> StepChanged;

        // Fire StepChanged if the discrete step count moved since the caller captured it.
        private void RaiseStepChangedIfMoved(int oldSteps)
        {
            int now = FilledSteps;
            if (now != oldSteps) StepChanged?.Invoke(this);
        }

        // IDamageableStructure
        public bool IsAlive => _hp > 0f && !_broken;

        /// <summary>
        /// WO-1439 — DERIVED from scene ownership, same expression as WallSegment/Gate/Building.
        /// A collector in the player's town is Friendly and the CoC-style siege loot targeting
        /// (WO-664) still prioritises it for a Hostile raider; a collector baked into an
        /// enemy-owned base belongs to that garrison and is no longer a target for it.
        /// </summary>
        public CombatFaction Faction =>
            SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : CombatFaction.Friendly;

        // ISiegeLootTarget
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
            SeedHpAfterLoad("Awake");
        }

        /// <summary>
        /// The ONE post-<see cref="LoadState"/> HP seed (WO-1865). Both load seams (Awake and
        /// <see cref="Configure"/>) call THIS — the rule is not copy-pasted, because the two
        /// copies are how it drifted in the first place.
        ///
        /// <para>⛔ THE GUARD MUST NOT HEAL A <see cref="_broken"/> COLLECTOR, AND THAT IS THE
        /// WHOLE POINT. It used to read `if (_hp &lt;= 0f) _hp = _maxHp;`, one line below
        /// `_broken = _hp &lt;= 0f` — two mutually contradictory statements born in the SAME
        /// commit (b08293c93, 2026-07-09), so the flag said destroyed and the very next line
        /// undid the only evidence for it. <see cref="LoadState"/> defaults an ABSENT pref to
        /// <see cref="_maxHp"/>, so `_hp &lt;= 0` after a load means exactly one thing:
        /// THIS COLLECTOR IS PERSISTED-DESTROYED. The guard therefore never protected an
        /// uninitialised collector (there is no such state here) — it only ever revived a dead
        /// one, half-way.</para>
        ///
        /// <para>WHAT IT COST (owner F8 2026-09-18, device log
        /// `Logs/device/raid-trace-20260918-080200.txt`): the Forge broke at 07:27:13 and the
        /// Lumber Mill at 07:28:26, both honestly `hp=0.00 broken=True`. At the next scene load
        /// (07:51:47) the guard fired and the RepairProbe printed the impossible state
        /// <c>BURNING 'forge' hp=1.00 broken=True</c> — FULL HP and flagged destroyed at once. In
        /// that state <see cref="IsAlive"/> is false, <see cref="IsActive"/> is false (no
        /// accrual), <see cref="Repair"/> returns at its `if (_broken)` guard (WO-753) so nothing
        /// can clear it, <see cref="WallRepairController"/> excludes it from Repair-All as
        /// DESTROYED, and <c>WaveDamageReport</c> — which reads <see cref="IsBroken"/> live and
        /// was never the defect — printed `2 destroyed` at EVERY wave clear for the next 31
        /// minutes (wave 27, 07:59:48: `0 damaged, 2 destroyed`) while the buildings stood in the
        /// town and Manage read `forge 4/4 Max`. The owner's report was "the report lies"; the
        /// report was the only honest voice.</para>
        ///
        /// <para>⚠ THIS DOES NOT CLOSE THE OTHER HALF, and must not be read as if it did: a
        /// destroyed collector is still a STANDING, fully-levelled building with no recovery
        /// door. WO-753 rules that it "returns ONLY via a full-cost build-mode placement", but
        /// both of these are <c>bake-owned</c> (BuildMode census 07:29:22), and the single caller
        /// of <see cref="ResetToFullHp"/> is the fresh-placement path
        /// (<c>BuildModeController.cs:2200</c>) — which a baked, already-built id never reaches.
        /// That is an OWNER RULING, not a code bug to guess at; the Warn below exists so the next
        /// capture names it in one read instead of an hour.</para>
        /// </summary>
        private void SeedHpAfterLoad(string via)
        {
            if (_hp <= 0f && !_broken)
            {
                _hp = _maxHp;
                return;
            }
            if (!_broken) return;

            int level = 0;
            try { level = ResourceBuildingState.GetLevel(_buildingId); } catch { level = -1; }
            FlowTrace.Warn("Harvest",
                $"collector '{_buildingId}' loaded DESTROYED via {via}: hp={_hp:F0}/{_maxHp:F0} broken=True " +
                $"pref={PlayerPrefs.GetFloat(HpPrefsPrefix + _buildingId, -1f):F0} progressionLevel={level}. " +
                "HP is NOT being reseeded (WO-1865): reseeding it produced hp=1.00 broken=True, an impossible " +
                "state in which the wave-clear damage report correctly reads DESTROYED forever while the " +
                "building stands at full health and Manage calls it built. If progressionLevel > 0 the town " +
                "still believes this structure exists, so WO-753's rebuild door is unreachable for it " +
                "(bake-owned ids never hit BuildModeController's fresh-placement ResetToFullHp) - that half " +
                "is an OWNER RULING, and this line is the evidence for it.");
        }

        private void OnEnable()
        {
            var prior = ResourceCollectorRegistry.Get(_buildingId);
            _displacedOnEnable = prior != null && prior != this ? prior : null;
            ResourceCollectorRegistry.Register(this);
            HarvestSourceRegistry.Register(this);

            // WO-VFX-POI — opt in to the near-field harvest CALLOUT aura (colorblind-safe:
            // motion/shape/luminance, not hue). Spent while the collector is not producing.
            PoiBeacon.Attach(gameObject, PoiBeacon.PoiTier.Node,
                calloutRadius: 28f, handoffRadius: 3.5f,
                tint: new Color(1f, 0.94f, 0.72f, 1f),
                isSpent: () => !IsActive);
        }

        /// <summary>
        /// WO-859 - the away/offline catch-up seam. Deliberately <c>Start</c>, not <c>OnEnable</c>:
        /// <c>AddComponent</c> fires Awake+OnEnable synchronously, and both call sites call
        /// <see cref="Configure"/> on the NEXT LINE, so an OnEnable catch-up would integrate the
        /// wrong building's away window (the serialized default "farm") into a collector that is
        /// about to become the lumbermill - the same ordering trap that produced the measured
        /// register-as-farm defect documented on <see cref="Configure"/>. Start runs on the first
        /// frame AFTER Configure, so the id is settled. A scene-unload/reload, a hub->dungeon->hub
        /// round trip and an app relaunch all DESTROY and re-create the component, so Start is the
        /// once-per-lifetime hook every away case passes through.
        /// </summary>
        private void Start()
        {
            _started = true;
            CatchUpAway();
        }

        private void OnDisable()
        {
            // A parked DDOL fallback is not the registry owner once a real placed collector has
            // taken over. It must not overwrite that owner's newer pending/HP/accrual PlayerPrefs.
            bool ownedRegistrySlot = ResourceCollectorRegistry.Get(_buildingId) == this;
            HarvestSourceRegistry.Unregister(this);
            ResourceCollectorRegistry.Unregister(this);
            if (ownedRegistrySlot && !_suppressNextDisableSave) SaveState();
            _suppressNextDisableSave = false;
        }

        /// <summary>Park a DDOL fallback during ownership handoff or state replacement without
        /// allowing its stale snapshot to overwrite the real/new state's PlayerPrefs.</summary>
        internal void ParkWithoutPersisting()
        {
            _suppressNextDisableSave = true;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// WO-859 - pay this collector for the wall-clock it was not being ticked (app closed,
        /// dungeon, raid: sec.3 rules them ONE case, and keying off the collector's own stamp is what
        /// collapses them, with no dependence on service ordering or on which scene the harvester
        /// happens to live in). Pays through the EXISTING <see cref="Accrue"/>, so the capacity
        /// clamp, the <see cref="CanAccrue"/> gate and the manual Collect tap all still apply -
        /// no new route to the wallet. Never touches GameState.LastHarvestClaimMs (WO-859 sec.2 R2:
        /// that clock already has an ordering race between OfflineHarvestService and EchoService,
        /// and this must not become a third consumer).
        /// </summary>
        private void CatchUpAway()
        {
            double nowMs = TimeSource.NowUnixMs();

            // Fresh collector: seed the clock to now and back-fill NOTHING this launch
            // (mirrors OfflineHarvestService's fresh-save arm, :141-148).
            if (_lastAccrualMs <= 0.0)
            {
                _lastAccrualMs = nowMs;
                SaveState();
                FlowTrace.Step("Harvest",
                    $"away catch-up '{_buildingId}': fresh stamp (<=0) - seeded to now, nothing back-filled.");
                return;
            }

            // Anti-tamper monotonic guard: a clock set BACKWARDS yields 0, never a negative and
            // never a re-claim (mirrors OfflineHarvestService.cs:152-154).
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

            // ALWAYS stamp, even when the window earns nothing - Accrue's unconditional stamp
            // write covers the paying case; this covers amount<=0 and the CanAccrue-false case
            // (a broken collector must not bank a frozen backlog if it is ever revived).
            _lastAccrualMs = nowMs;

            if (amount > 0) Accrue(amount);
            else SaveState();

            FlowTrace.Step("Harvest",
                $"away catch-up '{_buildingId}': away={awaySec:0}s owed={amount} " +
                $"pending {before:F0} -> {_pending:F0} / cap {Capacity:F0}" +
                (_pending >= Capacity - 0.001 ? " (AT CAP - the cap is what bounds the away window)" : ""));
        }

        /// <summary>
        /// Units owed for <paramref name="awaySec"/> of unattended production. The rate comes from
        /// the SHARED <see cref="ResourceBuildingHarvester.EffectiveYieldPerTick"/> - the very
        /// function the online tick uses - so the offline path can never drift from the online one
        /// by re-implementing the multiplier stack. Clamped to int range before the cast.
        /// </summary>
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

        /// <summary>
        /// Wire from the bootstrap / <see cref="StructureFactory"/> after AddComponent.
        /// <para>
        /// ! MEASURED DEFECT (2026-08-04 headless capture, WO-859 Phase 0): registration happens in
        /// <see cref="OnEnable"/>, and <c>AddComponent</c> on an ACTIVE GameObject runs Awake+OnEnable
        /// SYNCHRONOUSLY - i.e. BEFORE this method has been called. At that moment
        /// <see cref="_buildingId"/> is still its serialized default (<c>FarmId</c>), so EVERY
        /// collector registered itself under the key "farm". The capture is unambiguous - three
        /// consecutive lines, one per fallback collector:
        ///   <c>[Flow:Harvest] register id=farm pending=1088/2000</c> (x3)
        /// followed by <c>existence gate CLOSED for 'lumbermill' (liveCollector=no)</c> and the same
        /// for 'forge', while the FARM tick was paid into a collector whose id had since become
        /// 'forge' (<c>accrue-pending building=forge</c> rising in steps of ~12 = the FARM's yield 13
        /// x HpFraction, not the forge's 6). Consequences: lumbermill/forge income was silently
        /// WITHHELD by the no-live-collector branch, and farm income landed in the wrong pool and
        /// banked as the wrong RESOURCE.
        /// </para>
        /// The fix belongs HERE, at the one seam where the id changes, so both call sites
        /// (ResourceCollectorBootstrap and StructureFactory) are corrected by construction: drop the
        /// stale key BEFORE the id moves, then re-register under the new one.
        /// </summary>
        public void Configure(string buildingId, float maxHp = DefaultMaxHp)
        {
            string previousId = _buildingId;
            bool live = isActiveAndEnabled;
            var displaced = _displacedOnEnable;
            _displacedOnEnable = null;

            // Unregister while BuildingId still returns the OLD key (the registry removes by
            // current id), otherwise the stale "farm" entry is orphaned forever.
            if (live) ResourceCollectorRegistry.Unregister(this);

            // OnEnable registered this new component under its serialized default before the
            // factory supplied its real id. If that displaced a different live collector, restore
            // it before registering this component under the final id.
            if (displaced != null && displaced.isActiveAndEnabled &&
                !string.Equals(previousId, buildingId, System.StringComparison.Ordinal))
                ResourceCollectorRegistry.Register(displaced);

            _buildingId = buildingId;
            _maxHp = Mathf.Max(1f, maxHp);
            LoadState();
            SeedHpAfterLoad("Configure");

            if (live)
            {
                ResourceCollectorRegistry.Register(this);
                if (!string.Equals(previousId, buildingId, System.StringComparison.Ordinal))
                    FlowTrace.Step("Harvest",
                        $"collector re-keyed '{previousId}' -> '{buildingId}' on Configure " +
                        "(AddComponent registers under the serialized default before Configure runs).");
                ResourceCollectorBootstrap.NotifyCollectorConfigured(this);
            }

            // A collector configured AFTER Start (re-purposed host) still owes its away catch-up
            // for the id it now carries; before Start, Start() will do it with the correct id.
            if (_started) CatchUpAway();
        }

        // Last accrual scale that was FlowTraced — edge-logged (per state change), never per tick.
        private float _lastLoggedAccrualScale = 1f;

        /// <summary>
        /// Add production into pending (clamped to capacity). F8-45 (owner 2026-07-11:
        /// "if they did damage to collectors then those need to reduce economy"):
        /// effective accrual = amount × HpFraction — a 40%-damaged collector produces
        /// 40% less. Broken (<see cref="IsBroken"/>) already yields ZERO accrual via
        /// the <see cref="CanAccrue"/> gate (IsActive requires IsAlive &amp;&amp; !_broken).
        /// </summary>
        public void Accrue(int amount)
        {
            if (!CanAccrue || amount <= 0) return;
            float health = HpFraction;
            if (health <= 0f) return;   // defensive — CanAccrue should already gate this
            if (Mathf.Abs(health - _lastLoggedAccrualScale) > 0.005f)
            {
                // Edge-only trace: fires when the scale CHANGES (post-hit / post-repair),
                // not on every accrual tick.
                _lastLoggedAccrualScale = health;
                FlowTrace.Step("Harvest",
                    $"collector '{_buildingId}' accrual scaled x{health:0.##} (hp {_hp:F0}/{_maxHp:F0})");
            }
            double cap = Capacity;
            double before = _pending;
            int stepsBefore = FilledSteps;

            // =================================================================
            //  WO-1883 - PENDING STAYS AT THE COLLECTOR UNTIL Collect.
            // -----------------------------------------------------------------
            //  Owner, verbatim (2026-09-19): "also in town when you have more resources than you
            //  can store I think we only leave them there short term, otherwise there is no reason
            //  to need to come back and collect them"
            //
            //  Ruling 26b's Accrue auto-spill (TryOverflowToBank / SettleOverflow while accruing)
            //  is RETIRED. Away/offline catch-up still fills _pending up to collector cap, then
            //  stalls. Bank room alone does not empty the mill - the player must tap Collect
            //  (or Collect All / DumpSilos). Bank-full still stalls and never burns (WO-1392);
            //  Collect still banks up to headroom and leaves the remainder pending.
            // =================================================================
            _pending = System.Math.Min(cap, _pending + amount * (double)health);

            // ! WO-859 sec.4, THE HIGHEST-RISK LINE IN THE WHOLE CHANGE - and it is deliberately
            // OUTSIDE the `_pending > before` block below. The stamp records "production up to
            // HERE has been accounted for", which is true even when the pool was already FULL and
            // this call added nothing. If the stamp only advanced when the pool grew, it would
            // FREEZE the moment a collector caps; the player would then tap Collect and the very
            // next catch-up would re-pay the entire frozen backlog instantly, so the capacity cap
            // would bound nothing at all and the away window would be unlimited. Mirrors
            // OfflineHarvestService.cs:176-181 ("ALWAYS advance the clock - even on a zero haul").
            // Pinned by CollectorIncomeRegression case 8 [stamp-advances-at-cap]; moving this line
            // inside the block below FAILS that oracle.
            _lastAccrualMs = TimeSource.NowUnixMs();

            if (_pending > before)
            {
                FlowTrace.Throttle("Harvest", $"accrue-{_buildingId}", 2f,
                    $"accrue-pending building={_buildingId} pending={_pending:F0}/{cap:F0}");
                SaveState();
            }
            else
            {
                // At cap: nothing banked, but the advanced stamp above MUST be persisted, or a
                // relaunch would read the pre-cap stamp off disk and refill from the backlog.
                SaveState();
                FlowTrace.Throttle("Harvest", $"atcap-{_buildingId}", 30f,
                    $"collector '{_buildingId}' is AT CAP ({_pending:F0}/{cap:F0}) - production is STALLED " +
                    "(not banked, not burned: what is held stays here until Collect) and the " +
                    "last-accrual stamp still advances (no frozen backlog). WO-1883: tap Collect " +
                    "(or Collect All) to bank headroom; bank room alone does not auto-spill.");
            }
            RaiseStepChangedIfMoved(stepsBefore);
        }

        /// <summary>CoC collect - pending to spendable wallet at home. Returns what BANKED.</summary>
        public int Collect() => Collect(out _, out _);

        /// <summary>
        /// WO-1392 - NEVER BURN. Banks up to the town bank's headroom and LEAVES THE REMAINDER
        /// PENDING on this collector, re-collectable once the player spends or builds storage.
        ///
        /// <para>THE DEFECT (owner captures 2026-09-04 23:41): this method granted the whole
        /// floor(pending) through EconomyService.GrantSpendable - which CLAMPS earned income at
        /// TownBankCapacity.ClampGrant - and then subtracted the whole REQUEST from the pool
        /// (`_pending -= amount`), so every unit the cap refused was silently discarded. It ran
        /// outside any BankOverflowToastPresenter scope too, so the loss never reached a screen.
        /// The grant path has returned the APPLIED basket since 2026-08-16 precisely so callers
        /// read what landed; this was the one caller still trusting its own request local.</para>
        ///
        /// <para>The arithmetic is <see cref="SettleCollect"/> (pure, pinned by
        /// CollectorIncomeRegression [overflow-stays-pending]). The collector's own capacity is
        /// untouched - the pool simply does not drain below what the bank could not take.</para>
        /// </summary>
        /// <param name="requested">The whole units this collector held before the tap.</param>
        /// <param name="leftPending">Whole units still waiting here after the tap.</param>
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

            // WO-890 - THE COLLECT TAP HAD NO FEEDBACK AT ALL, and it is the most repeated
            // action in the town loop. Verified at source before this line existed: Collect
            // moved a wallet number and printed a trace, and that was the entire response -
            // no popup, no sound, no effect. Every OTHER banking path in the game already
            // emits this exact popup (MineNode.SpawnGainPopup on tap / worker extract,
            // HarvestSite via EconomyService.AddResource), so a collector was the one income
            // source that paid you silently.
            //
            // This is the SHARED income language, not a new one, and deliberately not a new
            // VFX pick: ResourceGainPopup is the code-built world text those paths already
            // use, so "+N Wood" means the same thing wherever the wood came from. It is also
            // free of the loop budget - it is a self-destroying GameObject, not a pooled
            // loop, so the most-repeated action in the game cannot starve the 20 aura slots.
            // The COLLECTOR's own state tells (the Collector_Ready beacon going out, the
            // pile emptying, the "N/20" readout) are driven by the StepChanged raised below.
            //
            // The model spawning this mirrors MineNode.Extract, the established precedent in
            // this same domain: the popup needs the AMOUNT, which only lives here for the
            // duration of this call, and ResourceGainPopup is a shared static spawner rather
            // than UI this class builds or owns.
            DeNelle.Village.World.ResourceGainPopup.Spawn(
                transform.position + Vector3.up * 1.6f,
                $"+{amount} {ResourceBuildingProgression.LabelFor(res)}",
                PopupTint(res));

            RaiseStepChangedIfMoved(stepsBefore);
            return amount;
        }

        /// <summary>
        /// WO-1392 - the never-burn arithmetic, PURE so it can be pinned without a wallet.
        /// The pool drains by exactly what BANKED, never by what was asked for; the remainder
        /// (whole units) is reported as still pending. A negative or over-request bank is clamped
        /// so a mis-reporting grant can never mint or over-drain.
        /// </summary>
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

        // =====================================================================
        //  WO-1883 - pure Accrue / Collect-headroom models for the overflow oracle.
        //  Placed AFTER SettleCollect deliberately: CollectorIncomeRegression Case 8 slices
        //  "Accrue's body" as IndexOf("public void Accrue(") -> IndexOf("public int Collect("),
        //  so anything wedged between those two members pollutes that oracle's window.
        // =====================================================================

        /// <summary>
        /// PURE model of what <see cref="Collect"/> banks given storage headroom:
        /// <c>min(floor(pending), bankRoom)</c>, then <see cref="SettleCollect"/>. Accrue itself
        /// never calls this (WO-1883 retired auto-spill); Collect asks the wallet and settles by
        /// what applied. Kept pure so the oracle can pin headroom banking without a live wallet.
        /// </summary>
        public static double SettleOverflow(double pendingBefore, int bankRoom, out int moved, out int leftPending)
        {
            if (pendingBefore < 0.0) pendingBefore = 0.0;
            if (bankRoom < 0) bankRoom = 0;
            int whole = (int)System.Math.Floor(pendingBefore);
            moved = whole < bankRoom ? whole : bankRoom;
            return SettleCollect(pendingBefore, moved, out leftPending);
        }

        /// <summary>
        /// PURE model of one <see cref="Accrue"/> call under WO-1883: fill the pool up to capacity
        /// and stall. Never banks. <paramref name="bankRoom"/> is accepted so call sites that used
        /// to assert auto-spill can prove room alone does not reduce pending; it is ignored.
        ///
        /// <para>Books: <c>pendingAfter + unproduced == pendingBefore + owed</c>. Already-held
        /// units are never lost (<c>pendingAfter &gt;= min(pendingBefore, capacity)</c> when
        /// capacity does not shrink under the pool).</para>
        /// </summary>
        public static double SimulateAccrue(
            double pending, double capacity, double owed, int bankRoom,
            out int banked, out double unproduced)
        {
            banked = 0;
            if (pending < 0.0) pending = 0.0;
            if (capacity < 0.0) capacity = 0.0;
            if (owed < 0.0) owed = 0.0;
            // bankRoom deliberately unused: Accrue does not spill (WO-1883).
            _ = bankRoom;

            double poolBefore = pending;
            pending = System.Math.Min(capacity, pending + owed);
            unproduced = owed - (pending - poolBefore);
            return pending;
        }

        /// <summary>
        /// Popup tint per resource. Deliberately the SAME palette MineNode.ResourceTint
        /// uses so wood reads as wood whether it came from a node or a lumbermill. The
        /// colour is a redundant channel only - the popup always names the resource in
        /// words, which is what carries the meaning for a red/green-colourblind reader.
        /// </summary>
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

        /// <summary>Restore collector after siege break (simple repair).</summary>
        public void Repair()
        {
            // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672's repair-back-online): a DESTROYED
            // collector is LOST - it returns ONLY via a full-cost build-mode placement, never an
            // in-place repair. Mirrors the guard Building.Repair already carries.
            if (_broken) return;
            _hp = _maxHp;
            LastLootStolen = 0f;   // F8-45: the loot report is per-break; a repair clears it
            LastLootStolenAtUnixMs = 0.0;   // ...and so does its siege stamp, or the pair could disagree
            SaveState();
            FlowTrace.Step("Harvest", $"collector-repair building={_buildingId}");
            // Leave the broken/scatter state — re-render the stack from live pending.
            StepChanged?.Invoke(this);
        }

        /// <summary>
        /// The collector BREAKS -- and, since the owner ruling of 2026-08-27, IT LOSES NOTHING.
        ///
        /// <para>! COLLECTOR LOOTING IS REMOVED. BANK THEFT REPLACES IT, and a siege bills ONCE
        /// per attack (StakeRules / DefenseReportBuilder.ApplyStakes take a bounded percentage of
        /// the UNPROTECTED BANK). The take that used to live here -- half of the uncollected
        /// pending -- is DELETED, along with RaidLootFraction, LootTakenFrom and the crystal
        /// carve-out that only existed to bound it.</para>
        ///
        /// <para>! DO NOT RE-ADD A TAKE HERE while bank theft is live. The two together charge the
        /// player twice for one siege, which is exactly the defect the superseded WO-1139 ruling
        /// was written to prevent; its oracle (SiegeLossStakesRegression) now fails the gate if a
        /// break moves a collector's pending at all.</para>
        ///
        /// <para>The pending stays in the broken shell. A destroyed collector is not repairable
        /// (WO-753) so it is not recoverable either -- but that is the STRUCTURE being lost, which
        /// is stake (1) of the ruling, not a second theft.</para>
        /// </summary>
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
            // Fire even if the raw step count is unchanged: IsBroken flipped, and the view
            // must switch to its scatter/hidden state. StepChanged is the collector's single
            // "re-render your visual" signal, so raise it on the break edge too.
            StepChanged?.Invoke(this);
        }

        private HarvestResource ResolveResource()
        {
            var def = ResourceBuildingProgression.Find(_buildingId);
            return def != null ? def.Yields : HarvestResource.Wood;
        }

        /// <summary>
        /// Collector reserve capacity, expressed in HOURS OF PRODUCTION (WO-859 sec.5).
        /// <para>
        /// PRIMARY path: the DATA-authored base reserve from the structures-catalog `capacity`
        /// field is the collector's L1 pool, and it is scaled by <see cref="ThroughputScale"/> -
        /// the ratio of what this collector produces NOW to what it produced at level 1 with one
        /// echo. That makes HOURS-TO-FULL CONSTANT across level and echo count, so `capacity`
        /// reads as "hours the town keeps working unattended" and stays correct through any future
        /// rate re-scale.
        /// </para>
        /// <para>
        /// WHAT THIS REPLACED, and why: the old scale was a flat <c>1 + 0.5x(level-1)</c>, i.e.
        /// capacity grew x3 from L1->L5 while throughput grew x5.6 - so UPGRADING A COLLECTOR
        /// SHORTENED how long it could run unattended (the curve ran BACKWARDS), and the echo
        /// multiplier was not in the capacity basis at all, so a 6-echo L5 farm filled in under
        /// six minutes. Capacity now grows MORE on upgrade than it used to (x5.6 at L5, not x3),
        /// so "upgrade to hold more" is strengthened, not weakened.
        /// </para>
        /// The STEWARD `collectorCap` talent sum (WO-676 Deep Reserves; x1 at sum 0) still
        /// multiplies ON TOP. FALLBACK (field absent/0): the legacy ~2-hours-of-production
        /// formula, unchanged.
        /// </summary>
        private double ComputeCapacity()
        {
            double baseCap;
            double catalogCap = CatalogCapacity();
            if (catalogCap > 0.0)
            {
                // DATA base reserve for level 1 / one echo, scaled by live throughput so the
                // FILL TIME - not the unit count - is the authored quantity.
                baseCap = catalogCap * ThroughputScale();
            }
            else
            {
                // Legacy fallback: ~2 hours of max production at current level.
                int yield = ResourceBuildingState.CurrentEffectiveYield(_buildingId);
                float interval = ResourceBuildingState.CurrentHarvestInterval(_buildingId);
                baseCap = (yield <= 0 || interval <= 0f)
                    ? 50.0
                    : System.Math.Max(50.0, yield * (3600.0 / interval) * 2.0);
            }

            // WO-676 STEWARD (Deep Reserves): ONE HeroTalentModifiers read at this existing
            // capacity calc — `collectorCap` grows how much pending the collector holds
            // before it is full. StatSum is internally null-safe (0 with no service/tree/
            // nodes), so capacity is unchanged at sum 0.
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

        /// <summary>
        /// WO-859 sec.5 - how much this collector produces per hour RIGHT NOW, divided by what it
        /// produced per hour at level 1 with a single echo. Multiplying the authored `repo.capacity`
        /// by this keeps hours-to-full constant, which is the whole point.
        /// <para>
        /// INCLUDED: the level's yield + interval (both upgrade axes) and the echo
        /// <c>GlobalHarvestMultiplier</c>. EXCLUDED, deliberately: the STEWARD `harvestRate` talent.
        /// That is not an oversight - it mirrors the identical, already-shipped ruling on the Echo
        /// silo (<c>EchoService.SiloCapacity</c>, "capacity is `collectorCap`'s seam, not
        /// `harvestRate`'s"), so the game's two capacity systems obey ONE rule instead of two.
        /// </para>
        /// The denominator is the AUTHORED level-1 table row, never a live read: it is the fixed
        /// reference `repo.capacity` was authored against, so perks/echoes move the numerator only.
        /// Floored at 1.0 so capacity can never shrink below its authored base.
        /// </summary>
        private double ThroughputScale()
        {
            // RE-POINTED 2026-09-07 (WO-1567 panel row 3). The per-hour formula that used to be
            // written out HERE is now ResourceBuildingProgression.ProductionPerHour - the ONE
            // producer - because the Manage detail card has to state the same number and a second
            // copy of a live formula is the failure CLAUDE.md sections 2/5/8/16 each record in
            // their own words. This method's JOB is unchanged: it is the RATIO of what the
            // collector makes now to what it made at the authored level-1 baseline.
            //
            // The two calls below are the SAME reads this method made inline before:
            //   base : level 1, no perk mult, no echo - the authored reference repo.capacity was
            //          written against, so perks/echoes move the numerator only.
            //   now  : the live level, ModifierService.ProductionMultFor (what
            //          ResourceBuildingState.CurrentEffectiveYield folds in) and the echo mult.
            // Floored at 1.0 so capacity can never shrink below its authored base.
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

        /// <summary>
        /// The DATA-authored base collector reserve (structures-catalog `repo.capacity`) for
        /// this collector's building, or 0 when none is authored. A collector is registered
        /// under its catalog id (e.g. "collector_farm") but keyed on the bare
        /// <see cref="_buildingId"/> ("farm"), so we match on <c>repo.collectorBuildingId</c>
        /// (falling back to the entry id) across the Collector catalog type — mirroring
        /// StructureFactory's collector resolution. Null-safe: 0 if the registry is empty.
        /// </summary>
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

            // WO-859: the away stamp. Stored as a string (see LastAccrualPrefsPrefix) - an
            // unparsable/absent value reads as 0 = "never stamped", which seeds to now and
            // back-fills nothing rather than paying a bogus window.
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
            // WO-1371 - the three keys above are `prefix + arbitrary building id`, and PlayerPrefs
            // has no key enumeration, so a New Game could not find them to delete them (that is
            // how the owner's 11-second-old game banked 14,089 inherited resources). Recording the
            // id here makes the key space ENUMERABLE at its ONE write seam - so the index can
            // never name a key that was not actually written.
            GameStateService.RegisterCollectorId(_buildingId);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// F8 2026-07-13 "lumbermill came in damaged": collector HP persists in
        /// PlayerPrefs keyed ONLY by buildingId, so a freshly PLACED structure
        /// inherited the previous building's wave damage (owner's Lumbermill spawned
        /// at hp=0.41 — proving line `[Flow:DamageVis] bar attached: 'lumbermill'
        /// (collector) hp=0.41`). A new placement is a NEW building (owner ruling:
        /// destroyed = pay to rebuild, fresh) — call this from the fresh-placement
        /// path to restore full HP + clear the stale persisted key. Reload replay of
        /// a STANDING building keeps its damage (the repair loop's domain).
        /// </summary>
        public void ResetToFullHp()
        {
            _hp = _maxHp;
            _broken = false;
            // WO-859: a fresh placement is a NEW building, so it also gets a FRESH away clock.
            // Without this it would inherit the destroyed building's stale stamp and instantly
            // back-fill a backlog it never earned (the PlayerPrefs keys are keyed by buildingId,
            // which is exactly the stale-state trap this method already exists to close for HP).
            _lastAccrualMs = TimeSource.NowUnixMs();
            SaveState();
            StepChanged?.Invoke(this);   // health/broken state moved — let the fill/damage views re-read
            FlowTrace.Step("Harvest", $"collector '{_buildingId}' HP reset to full on fresh placement (stale persisted damage cleared)");
        }

        // =====================================================================
        //  WO-1371 — the LIVE half of the New Game reset
        // =====================================================================

        /// <summary>
        /// WO-1371 — clearing the PlayerPrefs is only half a reset. A collector already in memory
        /// holds pending/HP/stamp in FIELDS and writes them straight back out on its next save
        /// (OnDisable, or any Accrue), which would restore the very fill
        /// <c>GameStateService.ClearHarvestPrefs</c> just deleted. This is the other half — the
        /// same two-half shape WO-1220 established for the talent store.
        ///
        /// <para>Subscribed STATICALLY, at BeforeSceneLoad, so the hook exists whether or not a
        /// collector happens to be alive when "Start New" is pressed. A static handler on a static
        /// event needs no unsubscribe (the "subscribers MUST unsubscribe" note on
        /// <see cref="GameStateService.NewGameStarted"/> guards INSTANCE handlers, which this is
        /// deliberately not).</para>
        /// </summary>
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
            // Inactive included: a parked DDOL fallback is still holding a pending figure it would
            // persist the moment it is re-enabled.
            var live = FindObjectsByType<ResourceCollector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < live.Length; i++) live[i]?.ResetForNewGame();

            // ⭐ WO-1371 - ResourceBuildingState.ResetAll had ZERO CALLERS despite its own summary
            // saying "used by a New Game / dev reset", which is why the owner's fresh town had a
            // farm CAPACITY of 7500 instead of base: the inherited LEVEL raised the cap the
            // inherited fill then filled. It also reaches TechTree.ResetAll, unreachable for the
            // same reason. This is that call site.
            ResourceBuildingState.ResetAll();

            FlowTrace.Step("Harvest",
                $"New Game: zeroed {live.Length} live collector(s) and reset every resource-building " +
                "level + tech node. Pending is 0 and the away stamp is re-seeded, so the first " +
                "collector status of the new game must read pending=0 (WO-1371).");
        }

        /// <summary>WO-1371 — this collector's New Game state: nothing pending, full HP, and a
        /// FRESH away clock (stamped to now, so <see cref="CatchUpAway"/> back-fills nothing on the
        /// next load). Writes through <see cref="SaveState"/> so memory and prefs agree.</summary>
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

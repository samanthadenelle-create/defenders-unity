// =============================================================================
// MineNode — a harvestable resource node in the outer world (WO-142 + the owner's
// "add mine nodes" step). Player walks up, presses [F], extracts; the node banks
// the yield into GameState and goes on cooldown / depletes.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Lean + self-contained: it does NOT depend on the (still-unbuilt) WO-141
// ResourceNode SO pipeline — it banks directly into GameState (Iron/Wood/Food/
// AetherCrystals) the same way the rest of the economy does, so it works today.
// When WO-141's full HarvestNodeData lands, MineNode can be folded into it.
//
// Region-aware: the node reads ZoneManager.DangerTierAt(position) so a designer
// can scale yield by region danger (deadlier region = richer node) — the
// danger=reward spine shared with raids (WO-143) and crystal grades (WO-144).
// =============================================================================
using UnityEngine;
using DeNelle.Core.World;
using DeNelle.Village.World;

namespace DeNelle.Village
{
    /// <summary>What a mine node yields. Maps 1:1 to a GameState wallet field.
    /// DEF-121 / WO-230: the four harvestables are Wood / Food / Iron / Crystals.
    /// Food replaces the retired "Stone" harvest axis and banks into
    /// GameState.Resources.Stone (the existing wallet field) — Stone is no longer a
    /// player-facing harvestable (Magic is a building-upgrade tech axis, not a node).</summary>
    public enum MineResource { Iron = 0, Wood = 1, [System.Runtime.Serialization.EnumMember(Value = "Food")] Stone = 2, AetherCrystal = 3 }

    [DisallowMultipleComponent]
    public sealed class MineNode : MonoBehaviour
    {
        /// <summary>Player-facing harvest verb per resource (ticket F8-21, owner-specified):
        /// Wood="Chop Wood", Food="Harvest Food", Iron="Mine Iron",
        /// AetherCrystal="Mine Crystals". EVERY harvest interact prompt routes through
        /// this so the verb never regresses to a generic "Mine &lt;Resource&gt;".
        /// Zero-allocation: returns interned string literals (safe in per-frame polls).</summary>
        public static string HarvestVerbFor(MineResource r)
        {
            switch (r)
            {
                case MineResource.Wood:          return "Chop Wood";
                case MineResource.Stone:          return "Mine Stone";
                case MineResource.Iron:          return "Mine Iron";
                case MineResource.AetherCrystal: return "Mine Crystals";
                default:                         return "Harvest";
            }
        }

        [Header("Yield")]
        [Tooltip("Which resource this node banks into GameState.")]
        public MineResource Resource = MineResource.Iron;

        [Tooltip("Base amount granted per extract (before the region danger bonus).")]
        [Min(1)] public int YieldPerExtract = 5;

        [Tooltip("Seconds before the node can be extracted again.")]
        [Min(0f)] public float ExtractCooldown = 8f;

        [Tooltip("Total extracts before the node depletes. 0 = infinite.")]
        [Min(0)] public int TotalExtracts = 6;

        [Tooltip("Seconds to respawn after depletion. 0 = never respawns. " +
                 "Ignored when UseFiniteReserve is on (a finite reserve never respawns — " +
                 "the node despawns when mined empty, per WO-159).")]
        [Min(0f)] public float RespawnSeconds = 60f;

        // ── WO-159 — finite-reserve reframe (additive, opt-in) ────────────────
        [Header("Finite reserve (WO-159)")]
        [Tooltip("When ON the node is a persistent FINITE RESERVE (territory-control model): " +
                 "it holds ReserveTotal units, persists in the world until mined empty, then " +
                 "DESPAWNS (no cooldown-respawn). A player-built Settlement auto-drains it. " +
                 "When OFF the node keeps the legacy extract-count + cooldown-respawn model " +
                 "so existing scenes / the worker demo are unchanged.")]
        public bool UseFiniteReserve = false;

        [Tooltip("Total reserve units the node holds (e.g. 500 iron). Drained by a settlement " +
                 "over time; when it hits 0 the node despawns. Region/danger scales this " +
                 "(deadlier region = richer reserve) via ReserveTotalScaled.")]
        [Min(1)] public int ReserveTotal = 500;

        [Tooltip("If true the node GameObject is destroyed when the reserve empties (the " +
                 "node 'vanishes' per WO-159 — the settlement remains). If false it stays as " +
                 "a spent marker (useful in the editor).")]
        public bool DespawnOnEmpty = true;

        private int  _reserveRemaining;
        private bool _reserveInitialised;

        [Header("Interaction")]
        [Tooltip("How close the player must be to press [F].")]
        [Min(0.5f)] public float InteractRadius = 2.5f;

        [Header("Visual")]
        [Tooltip("Auto-attach a MineNodeVisual so the node renders a DISTINCT, readable " +
                 "silhouette per resource (log/ore/grain/crystal) instead of a bare tinted " +
                 "primitive. Turn OFF only if the node's GameObject already supplies its own " +
                 "art (e.g. a placed prefab). Visual-only — never affects harvest/banking.")]
        public bool AutoBuildVisual = true;

        private float _cooldown;
        private int   _extractsLeft;
        private float _respawnTimer;
        private bool  _depleted;
        private Transform _player;
        private bool  _claimedByWorker;

        // PERF (leak fix): the per-frame interaction poll allocated every frame for every
        // node — a method-group delegate (Extract), a concatenated label string, and (when
        // the hero wasn't resolved) a GameObject.FindWithTag scan EACH frame. With 8 nodes
        // in OuterWorld that churn outpaced the GC and bloated the managed heap to a freeze.
        // Cache the delegate + label once, and throttle the missing-player re-find. Verified
        // via PerfLeakHunter (MineNode was the 90% culprit).
        private System.Action _extractAction;
        private System.Action _extractReserveAction;
        private float _playerFindTimer;
        private const float PlayerFindInterval = 0.5f;

        private void Awake()
        {
            _extractsLeft = TotalExtracts;
            InitReserve();
            var p = GameObject.FindWithTag("Player");
            _player = p != null ? p.transform : null;
            EnsureVisual();

            // PERF (leak fix): cache the interaction delegates ONCE so the per-frame poll
            // never re-allocates a method-group delegate. The label is NOT cached here:
            // spawners AddComponent<MineNode>() (Awake fires) BEFORE assigning Resource,
            // so an Awake-cached label froze on the default (F8-21 "wrong verb" root).
            // HarvestVerbFor returns interned literals — zero per-frame allocation.
            _extractAction        = Extract;
            _extractReserveAction = ExtractReserve;
        }

        // Give the node a distinct, readable per-resource look (the owner's "we need an
        // image for the nodes" gap). Visual-only: MineNodeVisual hides any placeholder
        // primitive on this GameObject and builds a log/ore/grain/crystal silhouette (or a
        // real Resources prop if one is present). Never touches collider/radius/harvest.
        private void EnsureVisual()
        {
            if (!AutoBuildVisual) return;
            var vis = GetComponent<MineNodeVisual>();
            if (vis == null) vis = gameObject.AddComponent<MineNodeVisual>();
            vis.Resource = Resource;   // MineNodeVisual.Start() reads this and builds.

            // WO-VFX-POI — opt in to the near-field harvest CALLOUT aura (colorblind-safe:
            // motion/shape/luminance, not hue). Spent = depleted or (finite-reserve) mined empty.
            PoiBeacon.Attach(gameObject, PoiBeacon.PoiTier.Node,
                calloutRadius: 28f, handoffRadius: InteractRadius + 1f,
                tint: new Color(1f, 0.94f, 0.72f, 1f),
                isSpent: () => IsDepleted || IsReserveEmpty);

            // WO-890 — the PER-RESOURCE HARVEST AURA, seated on the node itself.
            //
            // WHY HERE AND NOT ON NodeFillIndicator, which is what WO-890 names:
            // VERIFIED AT SOURCE, NodeFillIndicator is NOT ATTACHED IN THE LIVE GAME.
            // Its only attach site is WorkerManager.Start, behind
            // WorkerManager.AttachFillIndicators, which is DEFAULT FALSE and carries an
            // explicit DEF-258 comment turning it off ("reads in-game as a green disc
            // floating on the node ... re-enable only with the proper node UX"). Hosting
            // the aura there would have shipped code that never runs. This method,
            // by contrast, runs unconditionally for every node (it is already where
            // MineNodeVisual and the PoiBeacon are composed on), so the aura is live on
            // every node, worker-driven or hand-tapped, regardless of that flag.
            //
            // The state delegate is the whole contract: HarvestAura polls it and owns the
            // loop slot, its budget and every stop path. This class stays the MODEL - it
            // never plays, holds or stops a VFX.
            HarvestAura.Attach(gameObject,
                () => IsBeingHarvested ? HarvestAura.Beat.Harvesting : HarvestAura.Beat.None,
                () => HarvestAura.TypeForResource(Resource),
                "node:" + Resource);
        }

        // ── WO-890 harvest-activity seam (read-only; the aura polls it) ──────────

        /// <summary>
        /// How long after a successful extract a node still reads as "being worked".
        /// A manual tap is instantaneous, so without a hold the aura would flicker for a
        /// single frame and read as nothing at all. Sized under the shortest sensible
        /// ExtractCooldown so a worker on station reads as CONTINUOUS harvesting rather
        /// than a stutter between extracts.
        /// </summary>
        private const float HarvestGlowSeconds = 3.0f;

        private float _lastExtractTime = -999f;

        /// <summary>
        /// True while this node is actively being worked - a worker is on station, or a
        /// manual/auto extract landed within the last <see cref="HarvestGlowSeconds"/>.
        /// A depleted or mined-out node is never harvesting. Read-only: this is the
        /// surface the presentation layer polls, exactly like <see cref="ExtractFraction"/>.
        /// </summary>
        public bool IsBeingHarvested
        {
            get
            {
                if (_depleted || IsReserveEmpty) return false;
                if (_claimedByWorker) return true;
                return Time.time - _lastExtractTime <= HarvestGlowSeconds;
            }
        }

        // =====================================================================
        // WO-159 — finite reserve seam. The node is a depleting reserve worked by
        // a Settlement (StructureFactory-placed via build-mode), NOT a per-tap
        // clicker. The reserve is region/danger-scaled (deadlier region = richer)
        // sharing the same danger⇄reward dial as the per-extract yield bonus.
        // Drain reuses BankYield() so there is ONE banking path into GameState.
        // =====================================================================

        private void InitReserve()
        {
            if (_reserveInitialised) return;
            _reserveInitialised = true;
            _reserveRemaining = ReserveTotalScaled;
        }

        /// <summary>Region/danger-scaled total reserve (deadlier region = richer node):
        /// <c>ReserveTotal × (1 + 0.25 × dangerTier)</c> — mirrors the per-extract bonus dial.</summary>
        public int ReserveTotalScaled
        {
            get
            {
                int tier = Mathf.Max(0, ZoneManager.DangerTierAt(transform.position));
                return Mathf.RoundToInt(ReserveTotal * (1f + 0.25f * tier));
            }
        }

        /// <summary>Reserve units still in the ground (finite-reserve model). 0 = mined out.</summary>
        public int ReserveRemaining
        {
            get { InitReserve(); return Mathf.Max(0, _reserveRemaining); }
        }

        /// <summary>0..1 fill of the finite reserve (for the world UI / settlement readout).</summary>
        public float ReserveFraction
        {
            get
            {
                InitReserve();
                int total = ReserveTotalScaled;
                return total <= 0 ? 0f : Mathf.Clamp01((float)_reserveRemaining / total);
            }
        }

        /// <summary>True once a finite-reserve node has been mined empty (WO-159).</summary>
        public bool IsReserveEmpty => UseFiniteReserve && ReserveRemaining <= 0;

        /// <summary>
        /// WO-159 settlement drain: pull up to <paramref name="amount"/> units from the
        /// reserve into the player's wallet (the auto-harvest faucet — no manual tap).
        /// Returns the amount actually banked (clamped to what's left). When the reserve
        /// empties the node despawns (DespawnOnEmpty) — the settlement remains. Only does
        /// anything when UseFiniteReserve is on; otherwise returns 0 so the legacy worker
        /// path stays the sole banking route. Reuses BankYield (one GameState path).
        /// </summary>
        public int DrainReserve(int amount)
        {
            if (!UseFiniteReserve || amount <= 0) return 0;
            InitReserve();
            if (_reserveRemaining <= 0) return 0;

            int banked = Mathf.Min(amount, _reserveRemaining);
            _reserveRemaining -= banked;
            BankYield(banked);
            SpawnGainPopup(banked);   // WO-229 floating "+N <Resource>" at the node
            _lastExtractTime = Time.time;   // WO-890 harvest-activity window (see IsBeingHarvested)

            if (_reserveRemaining <= 0)
            {
                _depleted = true;   // also satisfies IsDepleted for any worker watcher
                Debug.Log($"[MineNode] Reserve mined empty ({Resource}) — node despawns; settlement remains.");
                if (DespawnOnEmpty) Destroy(gameObject);
            }
            return banked;
        }

        // =====================================================================
        // WO-117 — auto-collect seam. A dispatched Worker drives the SAME extract
        // path the player's [F] uses (one banking source of truth — no parallel
        // economy). The worker claims the node, then ticks TryAutoExtract() on the
        // node's own cooldown; extraction reuses Extract() so yield, region-danger
        // bonus, cooldown and depletion all behave identically to a manual tap.
        // =====================================================================

        /// <summary>True once a worker has claimed this node for auto-collect.
        /// A second worker should not be dispatched to a claimed node.</summary>
        public bool IsClaimedByWorker => _claimedByWorker;

        /// <summary>True when the node has run out of extracts (waiting to respawn,
        /// or permanently spent if RespawnSeconds == 0). No further yield until it
        /// respawns.</summary>
        public bool IsDepleted => _depleted;

        /// <summary>Seconds remaining before the next extract is allowed. 0 = ready.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _cooldown);

        /// <summary>Extracts remaining before depletion (TotalExtracts==0 ⇒ infinite,
        /// reported as int.MaxValue). Read-only progress for the fill indicator.</summary>
        public int ExtractsRemaining => TotalExtracts <= 0 ? int.MaxValue : Mathf.Max(0, _extractsLeft);

        /// <summary>0..1 fill toward depletion for the world UI. Infinite nodes report 1
        /// (always "full of resource"). Depleted reports 0.</summary>
        public float ExtractFraction
        {
            get
            {
                if (_depleted) return 0f;
                if (TotalExtracts <= 0) return 1f;
                return Mathf.Clamp01((float)_extractsLeft / TotalExtracts);
            }
        }

        /// <summary>The node's effective yield-per-extract right now, including the
        /// region danger bonus. Read-only — used by offline catch-up and UI.</summary>
        public int EffectiveYield
        {
            get
            {
                // Crystal nodes are a deliberately narrow premium-currency faucet.
                // Live harvests roll 1-2 (see RollYield); expose the safe upper bound
                // so reserve/UI/offline callers cannot restore the old scaled payout.
                if (Resource == MineResource.AetherCrystal) return 2;
                int tier = Mathf.Max(0, ZoneManager.DangerTierAt(transform.position));
                return Mathf.RoundToInt(YieldPerExtract * (1f + 0.25f * tier));
            }
        }

        /// <summary>Average resource banked per second when a worker is on station
        /// (one extract every ExtractCooldown). The read-only rate the offline-accrual
        /// seam (WorkerManager.ActiveAssignments) integrates while the player is away.</summary>
        public float RatePerSecond =>
            (UseFiniteReserve || _depleted || ExtractCooldown <= 0f) ? 0f : EffectiveYield / ExtractCooldown;

        /// <summary>Claim (or release) the node for a worker. Idempotent.</summary>
        public void SetWorkerClaim(bool claimed) => _claimedByWorker = claimed;

        /// <summary>
        /// WO-106 continuation: convenience to turn this raw MineNode into a
        /// claimed HarvestSite (visual structure + pet-assignable + Economy-routed
        /// income). Returns the created site (or existing one if already wrapped).
        /// All future income from this node position should prefer the site.
        /// </summary>
        public HarvestSite ClaimAsHarvestSite()
        {
            var existing = GetComponent<HarvestSite>();
            if (existing != null) return existing;

            var site = gameObject.AddComponent<HarvestSite>();
            site.ResourceType = Resource;           // map MineResource 1:1
            site.BaseYield = Resource == MineResource.AetherCrystal
                ? 1
                : Mathf.Max(3, YieldPerExtract);
            site.HarvestInterval = Mathf.Max(3f, ExtractCooldown * 0.7f);
            site.ApplyWorldScaling = true;
            site.Claim();

            // Link back so the site can optionally drain us (finite nodes).
            // The site will also route all its yields through EconomyService.AddResource.
            return site;
        }

        /// <summary>Worker-driven collect. Banks one extract IF the node is ready
        /// (not depleted, cooldown elapsed); returns the amount banked (0 if not ready).
        /// Reuses the exact same Extract() path as the manual [F] tap, so there is no
        /// second banking code path to keep in sync.</summary>
        public int TryAutoExtract()
        {
            // Finite-reserve nodes (WO-159) are drained by a Settlement via DrainReserve,
            // not the legacy worker/cooldown path — keep ONE banking route per node.
            if (UseFiniteReserve) return 0;
            if (_depleted || _cooldown > 0f) return 0;
            int before = EffectiveYield;
            Extract();
            return before;
        }

        /// <summary>Offline catch-up collect — banks one extract ignoring the LIVE
        /// cooldown (the elapsed offline time already "paid" it), but still respects
        /// depletion so an offline node runs dry exactly as a live one would. Returns
        /// the amount banked (0 if depleted). Used only by WorkerManager's offline
        /// integration; live collection uses TryAutoExtract().</summary>
        public int ForceAutoExtract()
        {
            if (UseFiniteReserve) return 0;   // settlement-drained; see DrainReserve.
            if (_depleted) return 0;
            int before = EffectiveYield;
            Extract();          // advances depletion + sets the next live cooldown
            return before;
        }

        private void Update()
        {
            // Build mode: the player is authoring, not interacting — release the shared
            // button and skip both the prompt request and the [F] extract press for the
            // whole session. Restored automatically when build mode exits.
            if (MobileInteractButton.Suppressed)
            {
                MobileInteractButton.Release(this);
                return;
            }

            // WO-325 — finite-reserve nodes (WO-159) are ALSO auto-drained by a
            // Settlement (DrainReserve), but the settlement loop isn't always present in
            // the live OuterWorld, which left these nodes with NO player verb at all (a
            // tap/[F] did nothing). So instead of skipping Update entirely we keep a
            // MANUAL harvest verb for them: in-range prompt + tap/[F] on the same
            // ExtractCooldown, routed to DrainReserve(EffectiveYield) (→ BankYield, the
            // single banking path). The auto settlement-drain path is unchanged.
            if (UseFiniteReserve)
            {
                UpdateFiniteReserveInteraction();
                return;
            }

            if (_cooldown > 0f) _cooldown -= Time.deltaTime;

            if (_depleted)
            {
                if (RespawnSeconds <= 0f) return;
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f) { _depleted = false; _extractsLeft = TotalExtracts; }
                return;
            }

            if (_player == null)
            {
                // PERF (leak fix): when the hero isn't resolved, throttle the FindWithTag
                // scan to PlayerFindInterval instead of scanning EVERY frame (×8 nodes =
                // the per-frame churn that starved the GC into a freeze).
                _playerFindTimer -= Time.deltaTime;
                if (_playerFindTimer > 0f) return;
                _playerFindTimer = PlayerFindInterval;
                var p = GameObject.FindWithTag("Player");
                _player = p != null ? p.transform : null;
                if (_player == null) return;
            }

            bool inRange = (_player.position - transform.position).sqrMagnitude
                           <= InteractRadius * InteractRadius;

            // DEF-203: register the shared on-screen Interact button while in range and
            // off cooldown so touch/mobile (no keyboard) can extract too. Desktop F kept.
            if (inRange && _cooldown <= 0f)
                MobileInteractButton.Request(this, HarvestVerbFor(Resource), _extractAction);
            else
                MobileInteractButton.Release(this);

            // Mobile-first: extraction fires through the shared on-screen Interact
            // button (requested above). The desktop F-key trigger was removed.
        }

        // WO-325 — manual harvest verb for finite-reserve nodes. Mirrors the legacy
        // non-finite interaction (in-range prompt + tap/[F], gated by ExtractCooldown)
        // but drains the persistent reserve instead of the per-tap extract count. The
        // reserve auto-drain (DrainReserve from a settlement) path is untouched; this
        // just gives the PLAYER a verb so a tap on a finite node banks resources.
        private void UpdateFiniteReserveInteraction()
        {
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;

            // No prompt / no-op once the reserve is mined empty (the node usually
            // despawns via DrainReserve, but guard the marker case too).
            if (ReserveRemaining <= 0)
            {
                MobileInteractButton.Release(this);
                return;
            }

            if (_player == null)
            {
                // PERF (leak fix): throttle the missing-hero scan (see Update()).
                _playerFindTimer -= Time.deltaTime;
                if (_playerFindTimer > 0f) return;
                _playerFindTimer = PlayerFindInterval;
                var p = GameObject.FindWithTag("Player");
                _player = p != null ? p.transform : null;
                if (_player == null) return;
            }

            bool inRange = (_player.position - transform.position).sqrMagnitude
                           <= InteractRadius * InteractRadius;

            if (inRange && _cooldown <= 0f)
                MobileInteractButton.Request(this, HarvestVerbFor(Resource), _extractReserveAction);
            else
                MobileInteractButton.Release(this);

            // Mobile-first: the reserve harvest fires through the shared on-screen
            // Interact button (requested above). The desktop F-key trigger was removed.
        }

        // WO-325 — one manual tap on a finite-reserve node: drain EffectiveYield from
        // the reserve (region-danger-scaled, same dial as Extract) through DrainReserve
        // → BankYield, then arm the cooldown. DrainReserve handles depletion + despawn.
        private void ExtractReserve()
        {
            int banked = DrainReserve(RollYield());
            if (banked > 0)
            {
                GameSfx.PlayPetHarvest();   // same harvest "ding" as the legacy tap
                _cooldown = ExtractCooldown;
                MobileInteractButton.Release(this);
            }
        }

        private void OnDisable()
        {
            MobileInteractButton.Release(this);
        }

        private void Extract()
        {
            int tier = Mathf.Max(0, ZoneManager.DangerTierAt(transform.position));
            // Region danger bonus: +25% yield per danger tier (Goldfields ×1.25 …
            // Ashwood ×2.0). Danger = reward.
            int amount = RollYield();

            BankYield(amount);
            if (amount > 0)
            {
                GameSfx.PlayPetHarvest();   // harvest "ding" on a successful extract
                SpawnGainPopup(amount);     // WO-229 floating "+N <Resource>" at the node
                // WO-890: stamp the activity window the harvest aura reads. Model-side
                // state only - this class never touches VFX (see IsBeingHarvested).
                _lastExtractTime = Time.time;
            }
            _cooldown = ExtractCooldown;

            if (TotalExtracts > 0)
            {
                _extractsLeft--;
                if (_extractsLeft <= 0)
                {
                    _depleted = true;
                    _respawnTimer = RespawnSeconds;
                }
            }

            Debug.Log($"[MineNode] +{amount} {Resource} (tier {tier}) — {_extractsLeft} left" +
                      (_depleted ? ", depleted." : "."));
        }

        private int RollYield()
        {
            // Premium currency stays useful but scarce: every crystal-node harvest
            // pays a visible 1 or 2, regardless of region scaling or legacy scene
            // YieldPerExtract values (several authored nodes were reaching 8).
            if (Resource == MineResource.AetherCrystal)
                return UnityEngine.Random.Range(1, 3);

            int tier = Mathf.Max(0, ZoneManager.DangerTierAt(transform.position));
            return Mathf.RoundToInt(YieldPerExtract * (1f + 0.25f * tier));
        }

        // Route through EconomyService (the Economy class) so:
        // - In-session Wood/Iron mirrors stay in sync for CanAfford/build.
        // - OnChanged fires for HUD / resource listeners.
        // - Pet harvest (via TryAutoExtract) and player/ worker taps all use one faucet.
        // Grant handles the GameState mutation for persisted (Food/Crystals) + ResourcesChanged.
        // We still tolerate a direct state path as fallback for very early bootstrap.
        private void BankYield(int amount)
        {
            if (amount <= 0) return;

            var econ = EconomyService.Instance;
            // WO-424: trace the harvest-complete SOURCE so a headless run can split
            // "extract never fired" from "fired but the economy→HUD bridge dropped it".
            // The economy (EconomyService.NotifyChanged) and HUD (SetResources /
            // HeartHudBridge.PushResources) ends are already traced [Flow:Eco]; this is
            // the missing first link — node banked N → did OnChanged → HUD follow?
            DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                $"MineNode banked +{amount} {Resource} (econ={(econ != null)}) — expect OnChanged → HUD.SetResources next");
            if (econ != null)
            {
                switch (Resource)
                {
                    // V1 farm-offline fix: Wood/Iron MUST persist + be visible to the
                    // building-upgrade ResourceLedger. Plain Grant() only fills the
                    // non-persisted in-session pool, so offline/worker/player-banked
                    // Wood/Iron was lost on reload and invisible to upgrades. Route
                    // through GrantSpendable() which writes BOTH the in-session pool
                    // (HUD/shop) AND GameState.Wood/Iron (persisted + upgrade ledger).
                    case MineResource.Iron:
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                            $"MineNode routing +{amount} Iron via GrantSpendable -> persistent GameState.Iron + ledger");
                        econ.GrantSpendable(iron: amount);
                        break;
                    case MineResource.Wood:
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Eco",
                            $"MineNode routing +{amount} Wood via GrantSpendable -> persistent GameState.Wood + ledger");
                        econ.GrantSpendable(wood: amount);
                        break;
                    // Food/Crystals are already GameState-backed (single wallet) — plain Grant is correct.
                    case MineResource.Stone:          econ.Grant(stone: amount); break;
                    case MineResource.AetherCrystal: econ.Grant(crystals: amount); break;
                }
                return;
            }

            // Fallback (should be rare): direct to GameState so persistence isn't lost.
            var state = DeNelle.Core.State.GameStateService.Instance != null
                ? DeNelle.Core.State.GameStateService.Instance.State : null;
            if (state == null)
            {
                Debug.LogWarning("[MineNode] EconomyService null and GameStateService.State null — yield dropped.");
                return;
            }
            switch (Resource)
            {
                case MineResource.Iron:          state.Iron += amount;          break;
                case MineResource.Wood:          state.Wood += amount;          break;
                case MineResource.Stone:
                {
                    var bal = state.Resources;
                    bal.Stone += amount;
                    state.Resources = bal;
                    break;
                }
                case MineResource.AetherCrystal:
                {
                    // Crystals are unified onto Resources.Crystals (the single wallet).
                    var bal = state.Resources;
                    bal.Crystals += amount;
                    state.Resources = bal;
                    break;
                }
            }
            DeNelle.Core.State.GameStateService.Instance?.ResourcesChanged?.Invoke();
        }

        // WO-229 — visual harvest feedback. Emit ONE floating "+N <Resource>" popup at
        // the node on every successful bank, so the player sees income whether it came
        // from a manual [F]-tap (Extract), a finite-reserve tap (ExtractReserve), or an
        // autonomous pet/worker auto-extract (TryAutoExtract → Extract → here). Uses the
        // shared ResourceGainPopup (same code-built, WebGL-safe, prefab-free world text
        // HarvestSite uses) so the income visual language is unified. The HUD resource
        // tick is already covered: BankYield → EconomyService/GameState → OnChanged.
        // HarvestSite spawns its own popup via EconomyService.AddResource (a separate
        // banking path), so there is no double-popup with this MineNode-path emission.
        private void SpawnGainPopup(int amount)
        {
            if (amount <= 0) return;
            Vector3 pos = transform.position + Vector3.up * 1.6f;
            // WO-953: the popup names the PLAYER word ("Crystals"), never the enum
            // ("AetherCrystal") — the word is the meaning channel (colorblind law).
            ResourceGainPopup.Spawn(pos, $"+{amount} {ResourceDisplayLabel(Resource)}", ResourceTint(Resource));
        }

        /// <summary>Player-facing popup word per resource (WO-953 — matches the collector's
        /// "Crystals" label so wood/iron/food/crystals read identically across every income path).</summary>
        internal static string ResourceDisplayLabel(MineResource res)
        {
            return res switch
            {
                MineResource.Wood          => "Wood",
                MineResource.Iron          => "Iron",
                MineResource.Stone          => "Stone",
                MineResource.AetherCrystal => "Crystals",
                _                          => res.ToString()
            };
        }

        /// <summary>Resource-themed popup tint (matches HarvestSite's palette so Wood/
        /// Iron/Food/Crystal read the same colour across both harvest visuals).</summary>
        private static Color ResourceTint(MineResource res)
        {
            return res switch
            {
                MineResource.Wood          => new Color(0.55f, 0.38f, 0.22f),
                MineResource.Iron          => new Color(0.62f, 0.64f, 0.70f),
                MineResource.Stone          => new Color(0.72f, 0.62f, 0.28f),
                MineResource.AetherCrystal => new Color(0.35f, 0.72f, 0.95f),
                _                          => Color.white
            };
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 0.95f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
#endif
    }
}

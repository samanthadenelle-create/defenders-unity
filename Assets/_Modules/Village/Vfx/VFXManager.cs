// =============================================================================
// VFXManager — central singleton for all runtime VFX. DEF-VFX-01.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES:
//   • Object-pools prefab-based effects (Mirza Beig, Lana Studio, Spells Pack).
//   • Falls back to procedural AbilityVfxKit when no prefab is wired.
//   • Exposes a clear, minimal public API so any system can trigger VFX in one line.
//   • Respects VFXQuality (Low / Medium / High) for mobile performance.
//
// QUICK REFERENCE — how to call from anywhere:
//
//   // Oneshot at a world position:
//   VFXManager.Play(VFXType.Impact_Aether, position);
//   VFXManager.Play(VFXType.Death_Skeleton, enemy.transform.position);
//
//   // Typed helpers (match what the work order specifies):
//   VFXManager.Instance.PlayImpact(VFXType.Impact_Flame, pos, rot);
//   VFXManager.Instance.PlayCasting(VFXType.Cast_MageCharge, heroTransform);
//   VFXManager.Instance.PlayDeath(VFXType.Death_Boss, pos);
//
//   // Persistent loop — keep the handle and Stop() when done:
//   var handle = VFXManager.Instance.PlayAura(VFXType.Aura_Necromancer, transform);
//   handle.Stop();
//
//   // Environment (same as PlayAura, sugar name for readability):
//   var torchHandle = VFXManager.Instance.PlayEnvironment(VFXType.Env_TorchFlame, transform);
//
//   // Projectile (attaches to the projectile's transform):
//   var projHandle = VFXManager.Instance.PlayProjectile(VFXType.Projectile_ArcaneBolt, projTransform);
//   projHandle.Stop(); // call when projectile hits
//
//   // Pet aura that auto-selects level:
//   (PlayPetAura was deleted 2026-08-20 with the unshipped pet-aura system.)
//
//   // Quality control:
//   VFXManager.Instance.SetQuality(VFXQuality.Low);   // call from settings
//
// SETUP:
//   1. Add VFXManager to a persistent GameObject in your boot/loading scene.
//   2. Assign a VFXCatalog asset (Assets → Create → Defenders / VFX Catalog).
//   3. Wire prefabs in the catalog. Any un-wired entry uses procedural fallback.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Village
{
    // ── Quality enum ─────────────────────────────────────────────────────────

    /// <summary>Mobile-aware VFX quality level. Set via VFXManager.SetQuality().</summary>
    public enum VFXQuality
    {
        Low    = 0,   // oneshot impacts only; no auras, no environment loops
        Medium = 1,   // impacts + critical auras; limited particle counts
        High   = 2,   // all effects at full fidelity (default on desktop/high-end mobile)
    }

    // ── VFXManager ───────────────────────────────────────────────────────────

    /// <summary>
    /// Central singleton for all runtime VFX. Manages per-type object pools,
    /// quality gating, procedural fallback, and hit-stop + screen shake hooks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class VFXManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        public static VFXManager Instance { get; private set; }

        // WO-504: VFXManager is not authored into any scene/prefab, so without a
        // self-bootstrap its Instance is null and SpellVfxFactory / Enemy / Env calls
        // SILENTLY no-op or stay procedural. Self-create on load (mirrors VfxPool) so the
        // pooled, catalog-driven authored VFX path is always live. Idempotent - a scene
        // that DOES author a VFXManager wins (the first Awake claims Instance).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[VFXManager]");
            go.AddComponent<VFXManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ApplyLoopBudget();      // WO-889: scene-tier the loop ceiling BEFORE any pool warms
            EnsureCatalog();
            InitialisePools();
            // WO-VFX-002: the Hovl-prefab-by-key subsystem shares this singleton, the
            // _poolRoot, and the oneshot/loop caps. Load its catalog + pre-warm its pools
            // alongside the VFXType catalog. See VFXManager.Hovl.cs.
            EnsureHovlCatalog();
            InitialiseHovlPools();

            // WO-1057: every F8 capture prints the live loop table. Subscribing here (not from a
            // debug menu, not behind a build fence) is what makes the instrument present at the
            // moment the owner actually flags "random vfx stuck around" — she presses F8 and moves
            // on, and the table is already in the log.
            BreakCaptureHarness.CaptureSnapshotRequested += DumpLiveLoops;

            // WO-1348: this singleton is DontDestroyOnLoad and its Hovl pools are keyed by VFX key,
            // so an IDLE pooled body built from the OLD prefab would outlive a town load and keep
            // being handed out after the owner re-picked that key from the Command Center - the
            // change would "not work" for no visible reason. Dropping IDLE bodies for exactly the
            // keys whose pick changed is NOT the live hot-swap the work order forbids: nothing that
            // is currently playing is touched, re-parented, or stopped.
            DeNelle.Core.Vfx.VfxPickOverrides.SnapshotChanged += OnVfxPickSnapshotChanged;
        }

        // WO-504: the VFXCatalog is a ScriptableObject asset (VFXType -> authored prefab).
        // VFXManager is NOT placed in any scene/prefab, so _catalog is never wired in an
        // inspector - without this, every effect falls back to procedural AbilityVfxKit.
        // Auto-load the script-generated catalog through the VfxAssetLoader seam when none is
        // assigned, so the authored Lana/Spells/custom prefabs take effect with zero drag-drop.
        // Only the single asset (+ the prefabs it references) ships - no whole pack in Resources.
        private void EnsureCatalog()
        {
            if (_catalog != null) return;
            // Addressables-first / Resources-fallback seam (VfxAssetLoader). The key is the
            // FULL Resources-relative path, used verbatim as BOTH the Addressable address and
            // the Resources.Load key — see VfxAssetLoader's KEY CONVENTION header.
            _catalog = DeNelle.Core.VfxAssetLoader.LoadVfxAsset<VFXCatalog>("VFX/VFXCatalog");
            if (_catalog == null)
                FlowTrace.Warn("VFXManager",
                    "EnsureCatalog: no _catalog assigned and 'VFX/VFXCatalog' resolved via NEITHER " +
                    "Addressables NOR Resources (VfxAssetLoader tried both) - ALL effects use procedural " +
                    "fallback. Run DeNelle.Editor.VFXCatalogGenerator.Generate, and if the VFX content has " +
                    "been migrated out of Resources, confirm DeNelle.Editor.VfxAddressablesGrouper marked it.");
            else
                FlowTrace.Step("VFXManager",
                    $"EnsureCatalog: loaded VFXCatalog via VfxAssetLoader key 'VFX/VFXCatalog' ({_catalog.Entries?.Length ?? 0} entries).");
        }

        private void OnDestroy()
        {
            VfxLoopBudget.CapChanged -= OnLoopBudgetChanged;
            // WO-1057: a static event holding a destroyed MonoBehaviour would dump against a dead
            // manager on the next capture (and leak it across an editor domain reload).
            BreakCaptureHarness.CaptureSnapshotRequested -= DumpLiveLoops;
            // WO-1348: same reason - a static event holding a destroyed manager would flush pools
            // on a dead instance and leak it across an editor domain reload.
            DeNelle.Core.Vfx.VfxPickOverrides.SnapshotChanged -= OnVfxPickSnapshotChanged;
            if (Instance == this) Instance = null;
        }

        // ── WO-889: the scene-tiered loop ceiling ─────────────────────────────
        //
        // _maxActiveLoops shipped as a flat serialized 20 that nobody had computed, and
        // six F8 captures show it saturated and starving tower projectiles, the Tree of
        // Life aura and every POI marker (the lines are cited at source in
        // VfxLoopFlagRegression's header). THIS is the single place the tier is read: the
        // budget object owns the arithmetic and the tier state, this method owns applying
        // it to the pool's limit. Subscribed rather than polled so a dungeon load or a
        // boss spawn re-tiers the live manager without anything having to remember to ask.
        private void ApplyLoopBudget()
        {
            VfxLoopBudget.CapChanged -= OnLoopBudgetChanged;
            VfxLoopBudget.CapChanged += OnLoopBudgetChanged;
            OnLoopBudgetChanged(VfxLoopBudget.CurrentCap);
        }

        private void OnLoopBudgetChanged(int cap)
        {
            if (cap <= 0 || cap == _maxActiveLoops) return;
            int prev = _maxActiveLoops;
            _maxActiveLoops = cap;
            FlowTrace.Step("VFXManager",
                "loop ceiling " + prev + " -> " + _maxActiveLoops + " (tier=" + VfxLoopBudget.TierName +
                "). Headroom is NOT a licence to leak - every loop still needs an owner that stops it " +
                "on every exit path; the enemy/pet aura POPULATION is bounded separately by the " +
                "nearest-N ring (VfxAuraProximityCuller).");
        }

        // ── Inspector fields ──────────────────────────────────────────────────

        [Header("Catalog (wire in Inspector)")]
        [Tooltip("ScriptableObject mapping VFXType → prefab + pool config.")]
        [SerializeField] private VFXCatalog _catalog;

        [Header("Quality")]
        [Tooltip("Starting quality level. Can be changed at runtime via SetQuality().")]
        [SerializeField] private VFXQuality _quality = VFXQuality.High;

        [Header("Performance Limits")]
        [Tooltip("Maximum simultaneous active oneshot GameObjects before new ones are skipped.")]
        [SerializeField, Min(1)] private int _maxActiveOneshots = 40;

        [Tooltip("Maximum simultaneous active aura/loop GameObjects.")]
        [SerializeField, Min(1)] private int _maxActiveLoops = 20;

        // WO-1229: READ-ONLY windows onto the loop budget. Before these existed the only
        // way anything outside this class could learn the pool was full was to ASK FOR A
        // LOOP AND BE REFUSED - which is exactly how 44 dungeon candles discovered
        // saturation 44 times a second while the colourblind low-HP aura was refused
        // alongside them. A consumer that can SEE the budget can yield before it starves
        // something. Getters only: nothing outside VFXManager may write either number
        // (the ceiling is VfxLoopBudget's, the count is derived from the registries).

        /// <summary>Live loop count across both loop registries. Read-only.</summary>
        public int ActiveLoopCount => _activeLoops;

        /// <summary>The ceiling currently in force for loops. Read-only.</summary>
        public int MaxActiveLoops => _maxActiveLoops;

        [Header("Dungeon (WO-59)")]
        [Tooltip("ScriptableObject with darker prefab overrides for dungeon scenes. " +
                 "Call ApplyDungeonMode(true) on dungeon load to activate.")]
        public DungeonVFXSettings dungeonSettings;

        [Header("Pool Root")]
        [Tooltip("Parent transform for pooled (inactive) objects. Auto-created if null.")]
        [SerializeField] private Transform _poolRoot;

        // ── Pool state ────────────────────────────────────────────────────────

        // Per-type queue of dormant (inactive) GameObjects ready to reuse.
        private readonly Dictionary<VFXType, Queue<GameObject>> _pools
            = new Dictionary<VFXType, Queue<GameObject>>();

        // Tracking how many active effects we currently have.
        // WO-VFX oneshot-leak fix: the oneshot count is NO LONGER a raw ++/-- int (which pinned at
        // the cap when a return never ran) - it is DERIVED from _oneshotSlots (the live checked-out
        // set) via ActiveOneshotCount(). SweepOneshots also reclaims loops whose host GameObject
        // was destroyed, so neither bucket can pin at its cap.
        //
        // WO-1057 — THE LOOP BUCKET IS NOW DERIVED TOO, AND IT HAS NAMES.
        // _activeLoops used to be a bare `int` and the loop sets held BARE GameObjects, so the pool
        // knew HOW MANY loops were live and never WHICH. A leaked loop was, by construction,
        // invisible: the number climbed and nothing in the repo could name the culprit. Every
        // "random VFX stuck around" F8 flag (seq 3583, 2026-08-22) therefore arrived with no RCA
        // attached and no way to get one.
        //
        // Both loop sets are now keyed registries (host GameObject -> LoopRecord) carrying key,
        // owner, start time and start position, and the count is DERIVED from them. Consequences,
        // all deliberate:
        //   • the cap check is bit-identical (same value, read the same way, same message);
        //   • the `Mathf.Max(0, ...)` clamp in ReclaimDestroyedLoops is GONE — a Count cannot go
        //     negative, so there is no longer a floor that could hide accounting drift;
        //   • DumpLiveLoops() can print an age-sorted table on every F8 capture (see the
        //     BreakCaptureHarness.CaptureSnapshotRequested subscription in Awake);
        //   • AuditLoopAges() self-reports a loop that outlives its owner or a sane age, once per
        //     handle, so the NEXT occurrence does not need the owner to notice it on screen.
        // This is instrumentation and it is PERMANENT (CLAUDE.md §12) — never fence it behind
        // DEVELOPMENT_BUILD, and never fork a second set alongside these two.
        //
        // WO-1473 — A REGISTERED LOOP IS NOT NECESSARILY A HELD SLOT.
        // The release policy (see TickLoopReleasePolicy) can SUSPEND a loop: its particles are
        // stopped-and-cleared while the host stays parented and pooled-to-nobody, so the effect
        // costs no slot and can be resumed byte-identically when its owner comes back on camera.
        // A suspended record therefore stays in its registry (so ReturnToPool's Remove still finds
        // it on every existing return path, and no third collection can drift from these two) but
        // must NOT be counted against the cap. Counting is a walk of two ≤32-entry dictionaries
        // with a struct enumerator - no allocation - and it is DERIVED rather than cached on
        // purpose: a cached "held" int is exactly the drift WO-1057 deleted from this class.
        private int _activeLoops => CountHeld(_loopObjects) + CountHeld(_hovlLoopObjects);

        /// <summary>Held (non-suspended) loops in one registry — the number that counts against
        /// the cap. Registry.Count is the REGISTERED total and is deliberately not the same thing.</summary>
        private static int CountHeld(Dictionary<GameObject, LoopRecord> registry)
        {
            if (registry == null || registry.Count == 0) return 0;
            int held = 0;
            foreach (var kv in registry)
                if (kv.Value == null || !kv.Value.Suspended) held++;
            return held;
        }

        /// <summary>Identity of ONE live loop. Allocated once at loop start (never per frame) so a
        /// capture can name what is playing, who started it, and how long it has been running.
        /// A class (not a struct) so <see cref="AuditLoopAges"/> can stamp Warned in place while
        /// enumerating the registry.</summary>
        internal sealed class LoopRecord
        {
            public VFXType   Type;        // VFXType-pool identity (None on the Hovl string-key path)
            public string    HovlKey;     // owner-authored Hovl key, printed VERBATIM (never resolved)
            public GameObject Host;       // the pooled instance — the registry key, kept for live position
            public Transform Owner;       // the parent the caller attached to (Unity-null once destroyed)
            public bool      Unparented;  // world-positioned loop: it never HAD an owner, so a null
                                          // Owner here is normal and must not read as "orphaned"
            public string    OwnerName;   // captured AT START — survives the owner's destruction, which
                                          // is precisely the case worth naming
            public int       OwnerId;     // GetInstanceID(), so two same-named owners stay distinct
            public float     StartedAt;   // Time.realtimeSinceStartup — AGE IS THE LEAK SIGNAL
            public Vector3   StartPos;    // fallback for the dump when the host is already gone
            public bool      Warned;      // one-per-handle Warn latch (age or orphaned owner)

            // ── WO-1473 release-policy state ──────────────────────────────────────────────
            public bool      Suspended;      // particles stopped+cleared; holds NO slot; resumable
            public float     SuspendedAt;    // realtime of the suspend, for the dump/audit lines
            public float     OffscreenSince; // realtime the host was first judged off-camera;
                                             // <0 means on-camera OR not judgeable (no camera)

            public string Key => string.IsNullOrEmpty(HovlKey) ? Type.ToString() : HovlKey;
        }

        // bug-triage P1: track which pooled objects are loops so ReturnToPool decrements the
        // RIGHT bucket (the old code guessed oneshot-first, drifting counters until VFX of a
        // class hit their cap and were silently skipped — combat went quiet mid-run).
        // WO-1057: the value side is the identity that makes a leak nameable.
        private readonly Dictionary<GameObject, LoopRecord> _loopObjects
            = new Dictionary<GameObject, LoopRecord>();

        // ── WO-1057 loop audit / dump state ───────────────────────────────────
        // A loop still running this long after it started is reportable on its own. Generous on
        // purpose: real endless loops exist (the Heart aura, POI markers), so this is a "nothing
        // legitimate in a town session runs this long unnoticed" threshold, not a lifetime cap.
        // WO-1473: the audit still never stops anything — RELEASE is TickLoopReleasePolicy's job
        // and this stays the oracle that proves the policy works. What changed is what it MEANS:
        // age alone no longer reports (a five-minute aura in front of the camera is the feature),
        // so this threshold is now only the "and it is ancient too" note on a line the policy
        // failure already earned. See AuditRegistryAges.
        private const float STUCK_LOOP_AGE_SECONDS = 300f;
        private const float LOOP_AUDIT_INTERVAL    = 5f;   // the audit is NOT a per-frame cost
        // Rows printed per dump. The tail ring the F8 harvest reads keeps 80 lines total
        // (BreakCaptureHarness.TailCap), so an unbounded 48-row dungeon dump would evict the very
        // context it is meant to sit next to. Sorted OLDEST FIRST, so the suspect is always inside
        // the printed window and only the young tail is elided (and the elision is counted).
        private const int   LOOP_DUMP_MAX_ROWS     = 24;
        private float _nextLoopAudit;

        // ── WO-1473 release policy ────────────────────────────────────────────
        // Evidence (device log, 2026-09-06): 20 occurrences of
        //   STUCK LOOP ArcaneTower_Aura owner='Arcane Spire'#N age=303s ... 14/24
        // FOURTEEN of the twenty-four slots held by ambient tower auras, all past the report
        // threshold, while the raid that followed pinned at 24/24 and dropped Damage_Ruin 31x.
        // Every one of those auras was BEHAVING: ArcaneAura holds exactly one handle and stops
        // it on OnDisable/OnDestroy/orphan. Nothing was leaked in the WO-1057 sense. The town
        // simply grew more permanent ambient loops than the pool has slots, and WO-1057
        // deliberately shipped the DETECTOR with no policy ("Reported only — release policy is
        // the follow-up"). This is that follow-up.
        //
        // ⚠ THE RECONCILIATION WITH WO-889, stated here because a reader will reach for it:
        // VfxAuraProximityCuller's header says nearest-N must NEVER cull tower/Heart/boss auras,
        // and that still holds - a DISTANCE cull deletes information the player can still read
        // (a far-away landmark is exactly when you want to see it). This policy is not a distance
        // cull. It releases only what is OUTSIDE THE CAMERA FRUSTUM, i.e. what the player cannot
        // see at all, and it RESUMES on re-entry. No information the player could read is
        // deleted, so the two rules do not collide.
        private const float OFFSCREEN_RELEASE_GRACE = 6f;    // off-camera this long -> suspend
        private const float LOOP_POLICY_INTERVAL    = 0.5f;  // policy cadence (NOT per frame)
        private const float LOOP_VISIBILITY_RADIUS  = 3f;    // AABB half-extent for the frustum test;
                                                             // hysteresis on the LEAVE edge only, so a
                                                             // loop at the screen edge cannot flicker
        // See EnforceOwnerLoopCap for how this number is derived (3 proven + 1).
        private const int   LOOPS_PER_OWNER_CAP     = 4;
        private float _nextLoopPolicy;
        private readonly List<LoopRecord> _loopPolicyScratch = new List<LoopRecord>();
        // Its own list, not a share of the one above: EnforceOwnerLoopCap runs from RegisterLoop,
        // and a scratch shared with a walker is one refactor away from being cleared mid-walk.
        private readonly List<LoopRecord> _ownerCapScratch   = new List<LoopRecord>();
        private readonly Plane[] _loopFrustum = new Plane[6];
        private Camera _policyCamera;
        // Reused scratch — the per-frame prune and the capture-time sort must not allocate.
        private readonly List<GameObject> _loopPruneScratch = new List<GameObject>();
        private readonly List<LoopRecord> _loopDumpScratch  = new List<LoopRecord>();

        // ── Leak-proof oneshot accounting (WO-VFX) ────────────────────────────
        // ROOT CAUSE the old code hit: PlayOneshot did _activeOneshots++ then relied on a return
        // coroutine calling ReturnToPool to do _activeOneshots--. When the effect's host transform
        // was destroyed FIRST (enemy death / scene change), ReturnToPool's `if (go == null) return;`
        // early-outed BEFORE the decrement, so the budget slot was never given back. Enough of those
        // and _activeOneshots pinned at _maxActiveOneshots and EVERY oneshot (heal, Arcane_Cast,
        // Dash_Blink, Impact_Physical) was skipped for the rest of the session.
        //
        // FIX: every checked-out oneshot (BOTH the VFXType pool AND the Hovl string-key pool) is
        // registered here with its host GameObject + a hard deadline. The active count is DERIVED
        // from this live set (prune-on-read), and SweepOneshots (per frame) force-reclaims any slot
        // whose host was destroyed or whose deadline elapsed. So a missed return can no longer pin.
        private struct OneshotSlot
        {
            public GameObject Go;       // host instance (Unity-null once destroyed)
            public float      Deadline; // Time.time past which the slot is force-reclaimed
            public VFXType    Type;     // VFXType-pool return key (the Hovl path leaves this None)
            public string     HovlKey;  // Hovl string-key-pool return key (non-null => Hovl path)
        }
        private readonly List<OneshotSlot> _oneshotSlots = new List<OneshotSlot>();
        // Backstop grace on top of a oneshot's own lifetime before the sweep force-returns a still-
        // alive-but-never-returned slot (the coroutine is the timely path; this only covers a lost one).
        private const float ONESHOT_RECLAIM_GRACE = 2f;

        // WO-59: dungeon mode flag — ApplyDungeonMode() toggles this.
        private bool _dungeonMode = false;

        // ── Quality ───────────────────────────────────────────────────────────

        /// <summary>Current VFX quality. Effects with MinQuality above this are skipped.</summary>
        public VFXQuality CurrentQuality => _quality;

        /// <summary>Change quality at runtime (e.g. from the Settings screen).</summary>
        public void SetQuality(VFXQuality q) => _quality = q;

        // ─────────────────────────────────────────────────────────────────────
        // ── PUBLIC API ────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────

        // ── Static shorthand (no Instance null-check boilerplate at call sites) ─

        /// <summary>
        /// Play a oneshot VFX at a world position. Null-safe — does nothing if
        /// VFXManager hasn't been initialised yet.
        /// </summary>
        /// <param name="playSound">
        /// When true (default) a matching SFX is fired through AudioService at the
        /// same position. Pass false to suppress audio without affecting the visual
        /// (e.g. when the caller handles audio itself). WO-62.
        /// </param>
        public static void Play(VFXType type, Vector3 position,
                                Quaternion rotation = default, bool playSound = true)
        {
            Instance?.PlayOneshot(type, position, rotation);
            if (playSound)
                PlayAudio(type, position);
        }

        /// <summary>Play a oneshot and specify an optional parent (effect will move with it).</summary>
        public static void PlayAt(VFXType type, Transform parent)
            => Instance?.PlayOneshot(type, parent.position, parent.rotation, parent);

        // ── Audio bridge (WO-62) ──────────────────────────────────────────────

        /// <summary>
        /// Maps a VFXType to its paired SfxId and fires the sound through
        /// AudioService (if available). Called automatically by Play() unless
        /// <c>playSound:false</c> is passed. Null-safe throughout. WO-62.
        /// </summary>
        private static void PlayAudio(VFXType type, Vector3 position)
        {
            var sfxId = VfxToSfx(type);
            if (sfxId == DeNelle.Audio.SfxId.None) return;
            DeNelle.Audio.AudioService.Instance?.PlaySfxAtPosition(sfxId, position);
        }

        /// <summary>
        /// Maps every VFXType that has a sound to its paired SfxId.
        /// Types without a sound mapping return SfxId.None (silent). WO-62.
        /// </summary>
        private static DeNelle.Audio.SfxId VfxToSfx(VFXType type) => type switch
        {
            VFXType.Impact_ExplosionFire    => DeNelle.Audio.SfxId.FireExplosion,
            VFXType.Impact_Flame            => DeNelle.Audio.SfxId.FireExplosion,
            VFXType.Impact_ExplosionAether  => DeNelle.Audio.SfxId.ArcaneExplosion,
            VFXType.Impact_Aether           => DeNelle.Audio.SfxId.ArcaneExplosion,
            VFXType.Impact_ShockwaveRing    => DeNelle.Audio.SfxId.Shockwave,
            VFXType.Impact_Heal             => DeNelle.Audio.SfxId.Heal,
            VFXType.Cast_MageCharge         => DeNelle.Audio.SfxId.WizardCast,
            VFXType.Cast_KnightSlam         => DeNelle.Audio.SfxId.Shockwave,
            VFXType.Projectile_FlameArrow   => DeNelle.Audio.SfxId.FlameArrowLaunch,
            VFXType.Projectile_TowerArcane  => DeNelle.Audio.SfxId.TowerShot,
            VFXType.Projectile_TowerFire    => DeNelle.Audio.SfxId.TowerShot,
            VFXType.Death_Skeleton          => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Death_Boss              => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Death_Brute             => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Death_Wolf              => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Death_Tiefling          => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Death_Generic           => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.WaveClear_Celebration   => DeNelle.Audio.SfxId.WaveClear,
            VFXType.Juice_WaveClear         => DeNelle.Audio.SfxId.WaveClear,
            VFXType.LevelUp_Celebration     => DeNelle.Audio.SfxId.LevelUp,
            VFXType.Juice_LevelUp           => DeNelle.Audio.SfxId.LevelUp,
            VFXType.Combo_Tier1             => DeNelle.Audio.SfxId.ComboSmall,
            VFXType.Juice_KillStreak        => DeNelle.Audio.SfxId.ComboSmall,
            VFXType.Combo_Tier2             => DeNelle.Audio.SfxId.ComboBig,
            VFXType.Pet_Aura_Fire           => DeNelle.Audio.SfxId.PetFireAura,
            VFXType.Pet_Attack              => DeNelle.Audio.SfxId.PetAttack,
            VFXType.ShootingStar            => DeNelle.Audio.SfxId.None,  // WO-52: no SFX entry yet
            // WO-59 dungeon
            VFXType.Death_EnemyExplosion_Dungeon => DeNelle.Audio.SfxId.EnemyDeath,
            // WO-66 elite / boss
            VFXType.Elite_Spawn             => DeNelle.Audio.SfxId.None,
            VFXType.Elite_Death             => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Boss_Spawn              => DeNelle.Audio.SfxId.None,
            VFXType.Boss_Death              => DeNelle.Audio.SfxId.EnemyDeath,
            VFXType.Boss_AttackImpact       => DeNelle.Audio.SfxId.Shockwave,
            // WO-66 boss phase VFX
            VFXType.Boss_PhaseTransition    => DeNelle.Audio.SfxId.Shockwave,
            VFXType.Boss_Telegraph          => DeNelle.Audio.SfxId.None,
            _                               => DeNelle.Audio.SfxId.None,
        };

        // ── Typed entry points (match work-order method names exactly) ─────────

        /// <summary>
        /// Oneshot impact VFX at a world position and optional rotation.
        /// Use for hits, explosions, shockwaves, death bursts.
        /// </summary>
        public void PlayImpact(VFXType type, Vector3 position, Quaternion rotation = default)
            => PlayOneshot(type, position, rotation);

        /// <summary>
        /// Attach a projectile trail to a moving Transform. Returns a VFXHandle —
        /// call handle.Stop() when the projectile hits its target.
        /// </summary>
        public VFXHandle PlayProjectile(VFXType type, Transform projectileTransform)
            => PlayLoop(type, projectileTransform.position, projectileTransform);

        /// <summary>
        /// Play a casting / wind-up effect on the caster's Transform.
        /// The effect is a short oneshot that auto-destroys.
        /// </summary>
        public void PlayCasting(VFXType type, Transform casterTransform)
            => PlayOneshot(type, casterTransform.position, casterTransform.rotation, casterTransform);

        /// <summary>
        /// Oneshot death burst at a world position. Automatically picks a larger
        /// explosion for boss-tier enemies.
        /// </summary>
        public void PlayDeath(VFXType type, Vector3 position)
            => PlayOneshot(type, position, Quaternion.identity);

        /// <summary>
        /// Start a persistent aura loop attached to a Transform. Returns a VFXHandle —
        /// call handle.Stop() when the enemy dies or the effect ends.
        /// </summary>
        public VFXHandle PlayAura(VFXType type, Transform parent)
            => PlayLoop(type, parent.position, parent);

        /// <summary>
        /// Start a persistent environment loop (torch, fog, portal …) attached to a
        /// Transform. Returns a VFXHandle. Call handle.Stop() if the object is destroyed.
        /// </summary>
        public VFXHandle PlayEnvironment(VFXType type, Transform parent)
            => PlayLoop(type, parent.position, parent);

        // PlayPetAura DELETED 2026-08-20 (owner: "delete the pet aura system but keep
        // Aura_PetLevel2"). It selected Aura_PetLevel1/2/3 by pet level for PetAuraVFX,
        // which was its ONLY caller and is itself deleted. The pet aura feature never
        // shipped: PetAuraVFX had zero runtime references anywhere in the tree.
        //
        // ⚠ Aura_PetLevel2 IS STILL LIVE and must not be removed with the rest — it was
        // repurposed as the TALENT-TREE NODE aura (TalentNodeVfxRig.AuraResourcePath).
        // Its name is the misleading part, and renaming it to what it actually is is a
        // separate, deliberate pass; deleting it would break the talent tree.

        // ── WO-59: Dungeon mode ───────────────────────────────────────────────

        /// <summary>
        /// Swap to dungeon prefab overrides when <paramref name="active"/> is true,
        /// or restore village prefabs when false. Call on dungeon scene load/unload.
        /// Null-safe — does nothing when <see cref="dungeonSettings"/> is not assigned.
        /// WO-59.
        /// </summary>
        public void ApplyDungeonMode(bool active)
        {
            _dungeonMode = active;

            // WO-889: this is the existing dungeon entry/exit seam, so it is also where the
            // loop tier is declared. A dressed dungeon's AMBIENT loops alone (candles, fog,
            // steam vents - handbook section 10 names "30 candle IsLoop" as the motivating
            // risk) can outnumber the village ceiling before a single enemy spawns. Declared
            // even when dungeonSettings is null: the tier is about the SCENE's loop
            // population, not about whether prefab overrides happen to be wired.
            VfxLoopBudget.SetDungeon(active);

            if (dungeonSettings == null) return;

            foreach (var ov in dungeonSettings.overrides)
            {
                if (ov.prefab == null) continue;

                if (active)
                {
                    // Replace pool entry with dungeon variant.
                    if (_pools.ContainsKey(ov.type)) _pools.Remove(ov.type);
                    // Pre-warm a single instance of the dungeon prefab.
                    _pools[ov.type] = new Queue<GameObject>();
                    _pools[ov.type].Enqueue(CreatePooledInstance(ov.prefab, ov.type));
                }
                else
                {
                    // Remove dungeon pool — next acquire will re-warm from catalog.
                    if (_pools.ContainsKey(ov.type)) _pools.Remove(ov.type);

                    // Restore from catalog if a village prefab is wired.
                    if (_catalog != null && _catalog.TryGet(ov.type, out var entry)
                        && entry.Prefab != null)
                    {
                        _pools[ov.type] = new Queue<GameObject>();
                        _pools[ov.type].Enqueue(CreatePooledInstance(entry.Prefab, ov.type));
                    }
                }
            }
        }

        /// <summary>
        /// Play a VFX in dungeon mode. Routing is automatic — dungeon overrides
        /// are already swapped into the pool by <see cref="ApplyDungeonMode"/>.
        /// Delegates to <see cref="Play"/> for a one-liner call site. WO-59.
        /// </summary>
        public static void PlayDungeon(VFXType type, Vector3 position,
                                       Quaternion rotation = default)
            => Play(type, position, rotation);

        // ─────────────────────────────────────────────────────────────────────
        // ── INTERNAL PLAY LOGIC ───────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Core oneshot play — gets from pool (or instantiates), positions, plays.</summary>
        private void PlayOneshot(VFXType type, Vector3 position, Quaternion rotation,
                                 Transform parent = null)
        {
            if (type == VFXType.None) return;

            // T+U §12: a cap hit means combat VFX go SILENTLY quiet mid-fight (the classic
            // "screen stops popping" bug). Throttle-report the drop so a capped run self-detects
            // instead of looking like nothing fired. Hot path → Throttle (~1/sec per cause).
            int activeOneshots = ActiveOneshotCount();   // derived + prune-on-read (leak-proof)
            if (activeOneshots >= _maxActiveOneshots)
            {
                FlowTrace.Throttle("VFXManager", "oneshot-cap", 1f,
                    $"PlayOneshot('{type}') SKIPPED — active oneshots {activeOneshots}/{_maxActiveOneshots} " +
                    "(cap hit; combat VFX dropping). Counter-leak or too-low cap?");
                return;
            }

            // Quality gate.
            if (_catalog != null && _catalog.TryGet(type, out var entry))
            {
                if ((int)_quality < entry.MinQuality)
                {
                    FlowTrace.Throttle("VFXManager", $"oneshot-quality:{type}", 2f,
                        $"PlayOneshot('{type}') SKIPPED by quality gate (need {entry.MinQuality}, " +
                        $"have {(int)_quality}) — effect intentionally absent at this quality.");
                    return;
                }

                if (entry.Prefab != null)
                {
                    // Prefab path — use pool.
                    var go = Acquire(type, entry);
                    if (go == null)
                    {
                        FlowTrace.Warn("VFXManager",
                            $"PlayOneshot('{type}'): Acquire returned null — falling back to procedural.");
                        ProceduralFallback(type, position, rotation);
                        return;
                    }
                    go.transform.position = position;
                    go.transform.rotation = rotation;
                    if (parent != null) go.transform.SetParent(parent, true);
                    go.SetActive(true);
                    VerifyHasParticles(go, type, "oneshot");
                    // The row says ONESHOT - make the instance obey before it is measured or
                    // played, so an ambient-looping prefab cannot emit for its whole pooled
                    // lifetime (owner F8 seq 4644). MUST precede DetectDuration + PlayAllParticles.
                    EnforceOneshotEmission(go, type.ToString());
                    // WO-1327: clamp the art pack's world collision + real-time light bill BEFORE
                    // the systems start. MUST precede PlayAllParticles or the first frame emits
                    // with the authored values.
                    NormalizeSpawnedHost(go, type.ToString());
                    PlayAllParticles(go);
                    float lifetime = entry.LifetimeOverride > 0f
                        ? entry.LifetimeOverride
                        : DetectDuration(go) + 0.3f;   // 0.3 s buffer after last particle
                    // A wind-up is bounded by the cast, never by the art pack's authored length.
                    if (IsCastBeat(type) && lifetime > CAST_BEAT_MAX_SECONDS)
                    {
                        FlowTrace.Once("VFXManager", "cast-beat-clamp:" + type,
                            $"cast beat '{type}' measured {lifetime:0.00}s - CLAMPED to " +
                            $"{CAST_BEAT_MAX_SECONDS:0.00}s (a Cast_* wind-up must finish inside the " +
                            "cast that spawned it; see CAST_BEAT_MAX_SECONDS).");
                        lifetime = CAST_BEAT_MAX_SECONDS;
                    }
                    // §12 (owner F8 seq 4644 'casts at me'): name WHERE a beat resolved. The
                    // spawn point is unparented world space, so this is the world position the
                    // player actually sees the effect sitting at, plus how long it will sit there.
                    FlowTrace.Throttle("VFXManager", $"oneshot-at:{type}", 1f,
                        $"PlayOneshot('{type}') at {position} parent=" +
                        $"'{(parent != null ? parent.name : "<none, world-space>")}' lifetime={lifetime:0.00}s.");
                    // Leak-proof: register the checked-out oneshot in the live set + a hard deadline
                    // (instead of a raw ++), so a host destroyed before ReturnAfterSeconds runs cannot
                    // pin the count - SweepOneshots reclaims the orphaned slot.
                    RegisterOneshot(go, type, null, lifetime);
                    StartCoroutine(ReturnAfterSeconds(go, type, lifetime));
                    return;
                }
            }

            // Procedural fallback — no prefab wired for this type.
            ProceduralFallback(type, position, rotation);
        }

        /// <summary>Core loop play — gets from pool (or instantiates), positions, parents, plays.</summary>
        private VFXHandle PlayLoop(VFXType type, Vector3 position, Transform parent = null)
        {
            if (type == VFXType.None) return null;

            // T+U §12: a loop cap hit means auras/trails silently stop appearing. Throttle-report.
            //
            // WO-1229 ruling 2 (owner, 2026-08-26): the decision is DELEGATED to
            // VfxLoopBudget.WouldRefuseLoop so that the accessibility allowlist cannot be true
            // there and false here. The two colourblind low-HP types bypass this cap entirely.
            // They are NOT tested by name at this site on purpose - an id written inline where
            // it is checked is the duplicated-state drift this repo keeps paying for (the stale
            // WO number block, the retired dependency table, the hardcoded repo root). One list,
            // one predicate, in VfxLoopBudget.
            bool refused = VfxLoopBudget.WouldRefuseLoop(type, _activeLoops, _maxActiveLoops);
            if (refused)
            {
                FlowTrace.Throttle("VFXManager", "loop-cap", 1f,
                    $"PlayLoop('{type}') SKIPPED — active loops {_activeLoops}/{_maxActiveLoops} " +
                    "(cap hit; auras/trails dropping). Handles not Stop()'d, or cap too low? " +
                    "NOTE: the colourblind low-HP tell is NOT subject to this check (WO-1229) - if " +
                    "an Aura_LowHealth / Aura_NearDeath refusal ever appears in a log again, the " +
                    "allowlist in VfxLoopBudget.AccessibilityLoops has been broken.");
                return null;
            }

            // The allowlist actually biting is worth a line of its own: it is the only way a
            // loop count can legitimately exceed its ceiling, and a reader who finds 25/24 in a
            // capture must be able to see WHY without reading this file.
            if (_activeLoops >= _maxActiveLoops)
                FlowTrace.Throttle("VFXManager", "loop-cap-exempt", 1f,
                    $"PlayLoop('{type}') GRANTED OVER THE CAP ({_activeLoops}/{_maxActiveLoops}) - " +
                    "accessibility allowlist (WO-1229). The colourblind low-HP tell is a LOOP, so a " +
                    "refusal leaves the hero with no non-colour danger signal at all. Bounded: " +
                    "HeroHpStateAura holds exactly one handle, so this can exceed the ceiling by at " +
                    "most the length of AccessibilityLoops, only across a recipe swap.");

            if (_catalog != null && _catalog.TryGet(type, out var entry))
            {
                if ((int)_quality < entry.MinQuality)
                {
                    FlowTrace.Throttle("VFXManager", $"loop-quality:{type}", 2f,
                        $"PlayLoop('{type}') SKIPPED by quality gate (need {entry.MinQuality}, " +
                        $"have {(int)_quality}) — loop intentionally absent at this quality.");
                    return null;
                }

                if (entry.Prefab != null)
                {
                    var go = Acquire(type, entry);
                    if (go == null)
                    {
                        FlowTrace.Warn("VFXManager",
                            $"PlayLoop('{type}'): Acquire returned null — falling back to procedural loop.");
                        var fb = ProceduralLoopFallback(type, position, parent);
                        return fb != null ? new VFXHandle(fb, type) : null;
                    }
                    go.transform.position = position;
                    if (parent != null) go.transform.SetParent(parent, true);
                    go.SetActive(true);
                    VerifyHasParticles(go, type, "loop");
                    // WO-1327: a LOOP host bounces and lights exactly like a oneshot one, and for
                    // longer. Same clamp, same reason, before anything emits.
                    NormalizeSpawnedHost(go, type.ToString());
                    PlayAllParticles(go);
                    // WO-1057: registering IS the increment (the count is derived from the two loop
                    // registries) — there is no int left to bump out of step with the set.
                    RegisterLoop(_loopObjects, go, type, null, parent);
                    return new VFXHandle(go, type);
                }
            }

            // Bridge: some VFXType loops have NO VFXType-catalog prefab but DO have a
            // curated Hovl loop wired by string key (Aura_HeartPulse -> "Aura_HeartPulse",
            // a soft glow loop). Prefer the real pooled Hovl effect over the textureless
            // procedural fallback (which renders as bare additive billboard SQUARES, not a
            // glow). BOTH the Heart-of-Elarion tree aura and the founding-Echo aura route
            // here via PlayAura(Aura_HeartPulse), so this one bridge fixes both.
            if (TryGetHovlKeyForType(type, out var hovlKey))
            {
                var bridged = PlayKeyInternal(hovlKey, position, default, parent, null, 0f, 0f, null);
                if (bridged != null)
                {
                    FlowTrace.Step("VFXManager",
                        $"PlayLoop('{type}') resolved via Hovl key '{hovlKey}' (real pooled glow loop, not procedural).");
                    return bridged;
                }
                FlowTrace.Warn("VFXManager",
                    $"PlayLoop('{type}'): Hovl bridge key '{hovlKey}' present but PlayKey returned null " +
                    "(loop cap hit or catalog/prefab missing) -- falling to the soft procedural loop.");
            }

            // Procedural fallback for loop — create a placeholder and return a handle to it.
            var fallback = ProceduralLoopFallback(type, position, parent);
            return fallback != null ? new VFXHandle(fallback, type) : null;
        }

        // ── Pool management ───────────────────────────────────────────────────

        private void InitialisePools()
        {
            if (_poolRoot == null)
            {
                var poolGo = new GameObject("[VFXPool]");
                poolGo.transform.SetParent(transform);
                _poolRoot = poolGo.transform;
            }

            if (_catalog == null)
            {
                // U: route through FlowTrace so it lands in the F8 break-log alongside the rest
                // of the VFX flow. Not a hard Fail — procedural fallback still renders — but a
                // Warn so a build shipping with NO catalog self-reports why every effect is procedural.
                FlowTrace.Warn("VFXManager",
                    "InitialisePools: no VFXCatalog assigned — ALL effects fall back to procedural AbilityVfxKit.");
                return;
            }

            _catalog.BuildLookup();

            foreach (var entry in _catalog.Entries)
            {
                if (entry.Prefab == null || entry.PoolSize <= 0) continue;
                if (!_pools.ContainsKey(entry.Type))
                    _pools[entry.Type] = new Queue<GameObject>();

                for (int i = 0; i < entry.PoolSize; i++)
                    _pools[entry.Type].Enqueue(CreatePooledInstance(entry.Prefab, entry.Type));
            }
        }

        private GameObject CreatePooledInstance(GameObject prefab, VFXType type)
        {
            if (prefab == null)
            {
                FlowTrace.Warn("VFXManager",
                    $"CreatePooledInstance('{type}'): null prefab — no pooled instance built (will use procedural).");
                return null;
            }
            var go = Instantiate(prefab, _poolRoot);
            go.name = $"[VFX_{type}]";

            // WO-602 §12: the authored catalog cast prefabs (Lana Studio orbs, etc.) were
            // built against LEGACY built-in particle shaders ("Particles/Additive" 10720 /
            // "Particles/Alpha Blended" 10721). URP cannot render those — it substitutes the
            // magenta error shader, drawn as opaque billboard quads = the hard purple/blue
            // "cubes" the hero spell-cast showed. WO-420 only magenta-proofed the PROCEDURAL
            // AbilityVfxKit path; these authored prefabs were never proofed. Re-shade every
            // ParticleSystemRenderer to URP/Particles/Unlit ONCE per instance (this method is
            // the single instantiation choke point for warm + on-demand + dungeon paths), blend
            // mode + texture + tint preserved. Guarded — a stripped shader degrades, never throws.
            Guard.Try("VFXManager", $"ProofUrpParticleShaders('{type}')",
                () => ProofUrpParticleShaders(go, type));

            // Owner Seeker frame 2026-09-09 09:46:58 (proof/seeker-broken-vfx-raid.png):
            // Slash_stone_once (Impact_Physical) is a MESH quad whose URP particle mat
            // has no authored texture. Healing it with SoftDot still draws a giant grey
            // CARD in the raid courtyard. The contact read is HitSurfaceVfx; this mesh
            // is not a slash, it is a billboard. Kill it on the pooled instance.
            if (type == VFXType.Impact_Physical)
                Guard.Try("VFXManager", "suppress untextured Impact_Physical mesh",
                    () => SuppressUntexturedImpactMesh(go));

            // V §12: a wired prefab that ships NO ParticleSystem (or visible Renderer) pools and
            // "plays" but renders nothing — the silent-invisible-effect class. Verify ONCE per type
            // at warm time so a bad catalog entry self-reports instead of going quiet in combat.
            VerifyHasParticles(go, type, "pooled-warm");

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// Lana Slash_stone_once draws a MESH quad. With 1AB_mat's empty _BaseMap it is a
        /// white rectangle; after SoftDot heal it is a giant grey card (owner Seeker
        /// 2026-09-09 09:46:58). Disable those mesh slots. Billboard particles, if any,
        /// stay. Contact feedback is HitSurfaceVfx at the call site.
        /// </summary>
        private static void SuppressUntexturedImpactMesh(GameObject go)
        {
            if (go == null) return;
            int killed = 0;
            var meshes = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null) continue;
                meshes[i].enabled = false;
                killed++;
            }
            var psrs = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < psrs.Length; i++)
            {
                if (psrs[i] == null) continue;
                if (psrs[i].renderMode == ParticleSystemRenderMode.Mesh)
                {
                    psrs[i].enabled = false;
                    killed++;
                }
            }
            if (killed > 0)
                FlowTrace.Step("VFXManager",
                    "Impact_Physical: disabled " + killed + " untextured mesh renderer(s) "
                    + "(Slash_stone_once grey card).");
        }

        // V: a VFX GameObject MUST carry at least one ParticleSystem OR a visible Renderer to be
        // seen. Traced Once per type (warm) / Throttled (play) so a content-side "invisible effect"
        // surfaces in the break-log with the exact type, not as a vague "combat went quiet".
        private static void VerifyHasParticles(GameObject go, VFXType type, string phase)
        {
            if (go == null) return;
            int particles = go.GetComponentsInChildren<ParticleSystem>(true).Length;
            int renderers = go.GetComponentsInChildren<Renderer>(true).Length;
            if (particles == 0 && renderers == 0)
                FlowTrace.Once("VFXManager", $"novisual:{type}",
                    $"VerifyHasParticles({phase}, '{type}'): prefab has NO ParticleSystem and NO Renderer — " +
                    "effect plays but renders nothing (invisible VFX). Check the catalog prefab for this type.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── WO-602: URP particle-shader proof (kill the magenta "cubes") ──────
        // ─────────────────────────────────────────────────────────────────────
        // The authored catalog VFX prefabs (Lana Studio Casual RPG VFX orbs, Spells
        // Pack, etc.) reference Unity's LEGACY built-in particle shaders. Under URP those
        // can't render, so Unity swaps in Hidden/InternalErrorShader → opaque magenta
        // billboards (the purple/blue cube cluster). We mirror AbilityVfxKit's proven
        // runtime fix (WO-420) but EXTEND it: instead of stamping a blank material we
        // preserve the source blend mode (additive vs alpha), main texture and tint so the
        // intended look survives. Reuses AbilityVfxKit.ResolveParticleShader() (already
        // caches "Universal Render Pipeline/Particles/Unlit" in a static + logs fallbacks).

        // URP fixed-function blend factor enum values (UnityEngine.Rendering.BlendMode).
        private const int BLEND_ONE                 = 1;   // One
        private const int BLEND_SRC_ALPHA           = 5;   // SrcAlpha
        private const int BLEND_ONE_MINUS_SRC_ALPHA = 10;  // OneMinusSrcAlpha

        /// <summary>
        /// WO-602: walk every ParticleSystemRenderer on <paramref name="go"/> (and children)
        /// and replace any LEGACY/built-in/error particle shader with URP/Particles/Unlit,
        /// preserving blend mode + main texture + tint. Idempotent by construction — called
        /// once per instance from CreatePooledInstance (the only instantiation site), so no
        /// per-Play re-processing. Degrades gracefully (FlowTrace.Warn, leave as-is) when the
        /// URP particle shader is stripped from the build. Never throws.
        /// </summary>
        private static void ProofUrpParticleShaders(GameObject go, VFXType type)
        {
            if (go == null) return;

            // #44: query ALL renderers, not just ParticleSystemRenderer. The authored VFX prefabs
            // (Lana Studio "Casual RPG VFX") carry MESH geometry on MeshRenderers using legacy
            // built-in particle shaders (Particles/Additive, Particles/Alpha Blended) -> those render
            // as Hidden/InternalErrorShader magenta blocks under URP ("blocky purple cubes"). The old
            // particle-only query skipped them. The reshade below is material-gated by
            // IsLegacyParticleShader, so widening the net only touches genuinely-legacy materials and
            // leaves every legitimate mesh/particle material alone.
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Shader urp = AbilityVfxKit.ResolveParticleShader();
            if (urp == null || urp.name.IndexOf("Universal Render Pipeline", System.StringComparison.Ordinal) < 0)
            {
                // ResolveParticleShader already FlowTrace.Warn'd the miss. We require the real
                // URP particle shader for a correct soft-particle result — a non-URP fallback
                // (Sprites/Default etc.) would not honour additive blend the same way, so rather
                // than risk a worse look we leave the authored materials untouched and report.
                FlowTrace.Warn("VFXManager",
                    $"ProofUrpParticleShaders('{type}'): URP Particles/Unlit unavailable (got " +
                    $"'{(urp != null ? urp.name : "null")}') — leaving authored materials as-is " +
                    "(may render magenta if legacy). Add it to GraphicsSettings AlwaysIncludedShaders.");
                return;
            }

            int reshaded = 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    if (!IsLegacyParticleShader(src.shader))
                    {
                        // Owner F8 "spells so pixelated" (§12, read from the asset YAML):
                        // the Spells Pack mats our mirrored projectile/explosion prefabs use
                        // (Glow.mat, Spell 4.mat, Trail.mat, …) are ALREADY on the URP
                        // particle shader but only HALF-upgraded — texture stranded in the
                        // legacy _MainTex slot (_BaseMap NULL) and surface left OPAQUE
                        // (_Surface 0, _ZWrite 1, One/Zero blend) → untextured opaque
                        // billboard SQUARES. The legacy-only skip above left them broken;
                        // finish the migration in place (shared heal, idempotent).
                        if (AbilityVfxKit.HealHalfUpgradedParticleMaterial(src)) reshaded++;

                        // A ParticleSystemRenderer trail slot bulk-stomped with the opaque
                        // MagentaFix_DefaultLit URP/Lit material renders solid grey ribbons —
                        // point the trail at the (healed) slot-0 particle material instead.
                        if (i > 0 && r is ParticleSystemRenderer && mats[0] != null &&
                            src.name.StartsWith("MagentaFix", System.StringComparison.Ordinal))
                        {
                            mats[i] = mats[0];
                            changed = true;
                            reshaded++;
                        }
                        continue;
                    }

                    bool additive = SourceWantsAdditive(src);

                    // Carry over the look from the legacy material BEFORE we lose its props.
                    Texture mainTex = SafeGetMainTexture(src);
                    Color tint = SafeGetTintColor(src);

                    var nm = new Material(urp) { name = $"{src.name}_URPProofed" };
                    ConfigureUrpParticleBlend(nm, additive);
                    if (mainTex != null)
                    {
                        // URP Particles/Unlit samples _BaseMap and declares NO _MainTex — the
                        // `.mainTexture` alias write logged "doesn't have a texture property
                        // '_MainTex'". Only use the alias when the shader lacks _BaseMap (a non-URP
                        // fallback that samples _MainTex). Kills the warning; look preserved.
                        if (nm.HasProperty("_BaseMap")) nm.SetTexture("_BaseMap", mainTex);
                        else nm.mainTexture = mainTex;
                    }
                    if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", tint);
                    if (nm.HasProperty("_Color"))     nm.SetColor("_Color", tint);
                    nm.color = tint;

                    mats[i] = nm;
                    changed = true;
                    reshaded++;
                }

                if (changed) r.sharedMaterials = mats;
            }

            if (reshaded > 0)
                FlowTrace.Step("VFXManager",
                    $"ProofUrpParticleShaders('{type}'): re-shaded {reshaded} legacy particle " +
                    $"material(s) to URP/Particles/Unlit across {renderers.Length} renderer(s).");
        }

        /// <summary>
        /// True when <paramref name="sh"/> is a legacy/built-in/error particle shader that URP
        /// can't render — null, Hidden/InternalErrorShader, "Legacy Shaders/*", or any
        /// "*Particles/*" that is NOT the URP one.
        /// </summary>
        private static bool IsLegacyParticleShader(Shader sh)
        {
            if (sh == null) return true;
            string n = sh.name ?? string.Empty;
            if (n.IndexOf("Universal Render Pipeline", System.StringComparison.Ordinal) >= 0) return false;
            if (n == "Hidden/InternalErrorShader") return true;
            if (n.StartsWith("Legacy Shaders/", System.StringComparison.Ordinal)) return true;
            if (n.IndexOf("Particles/", System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Decide additive vs alpha-blend from the legacy source material. Reads the shader name
        /// (Particles/Additive 10720 → additive; Particles/Alpha Blended 10721 → alpha) and, when
        /// the shader is the error shader (original lost), falls back to the material's _DstBlend
        /// if present, else defaults to ADDITIVE (cast orbs/glows are overwhelmingly additive).
        /// </summary>
        private static bool SourceWantsAdditive(Material src)
        {
            if (src == null) return true;
            string n = src.shader != null ? (src.shader.name ?? string.Empty) : string.Empty;

            if (n.IndexOf("Additive", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Alpha Blended", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("AlphaBlend", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // Shader name uninformative (error shader / unknown) — peek at the captured dst blend.
            if (src.HasProperty("_DstBlend"))
            {
                int dst = (int)src.GetFloat("_DstBlend");
                if (dst == BLEND_ONE) return true;                   // additive
                if (dst == BLEND_ONE_MINUS_SRC_ALPHA) return false;  // alpha
            }
            return true;   // default additive
        }

        /// <summary>Configure a URP Particles/Unlit material for transparent additive or alpha blend.</summary>
        private static void ConfigureUrpParticleBlend(Material m, bool additive)
        {
            if (m == null) return;

            // _Surface 1 = Transparent (URP Particles/Unlit). _Blend: 0 = Alpha, 2 = Additive.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", additive ? 2f : 0f);

            int dst = additive ? BLEND_ONE : BLEND_ONE_MINUS_SRC_ALPHA;
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", BLEND_SRC_ALPHA);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", dst);
            if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);

            // URP transparent keyword + don't write depth + render in the transparent queue.
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;   // 3000
        }

        private static Texture SafeGetMainTexture(Material src)
        {
            if (src == null) return null;
            if (src.HasProperty("_MainTex")) return src.GetTexture("_MainTex");
            if (src.HasProperty("_BaseMap")) return src.GetTexture("_BaseMap");
            // No _MainTex and no _BaseMap: reading `.mainTexture` here would log
            // "doesn't have a texture property '_MainTex'" — return null instead (any real
            // particle material declares one of the two above).
            return null;
        }

        private static Color SafeGetTintColor(Material src)
        {
            if (src == null) return Color.white;
            // Legacy Particles/Additive + Alpha Blended use _TintColor; others _Color.
            if (src.HasProperty("_TintColor")) return src.GetColor("_TintColor");
            if (src.HasProperty("_Color"))     return src.GetColor("_Color");
            if (src.HasProperty("_BaseColor")) return src.GetColor("_BaseColor");
            return Color.white;
        }

        /// <summary>Get a dormant instance from the pool or create a new one.</summary>
        private GameObject Acquire(VFXType type, in VFXCatalog.Entry entry)
        {
            if (_pools.TryGetValue(type, out var q))
            {
                // WO-955: a scene/arena teardown can destroy a pooled host while it still
                // sits in this free list, and the poisoned list PERSISTS across scene loads
                // (two captured NREs, 2026-08-10: HeroHpStateAura in town after arena
                // deaths, then EnemyAuraVFX in dg_ember_deep). A destroyed host is a dead
                // slot: drain past it — never dereference a corpse, never throw out of a
                // Play call. Capacity self-heals via the fresh instantiate below. The drain
                // (and its Warn) lives in VfxPoolGuard so the string-keyed Hovl pool cannot
                // drift away from the rule; see that file's header for the write-side half.
                var reused = VfxPoolGuard.DrainToLiveHost(q, type.ToString(), out _);
                if (reused != null)
                {
                    reused.transform.SetParent(null, false);   // un-parent from pool root
                    return reused;
                }
            }
            // Pool empty — instantiate a fresh one (will be pooled after use).
            return CreatePooledInstance(entry.Prefab, type);
        }

        // WO-929: returns whose reparent must wait out a host (de)activation window.
        // Swept (and cleared) at the top of the next Update, where the cascade is over.
        private readonly List<(GameObject go, VFXType type)> _pendingReturns =
            new List<(GameObject, VFXType)>();

        /// <summary>
        /// Return a GameObject to its type pool (called internally after lifetime ends or
        /// via VFXHandle.Stop(immediate:true)).
        /// </summary>
        public void ReturnToPool(GameObject go, VFXType type)
        {
            if (go == null) return;

            // WO-929 (the seam, cited by F8 seq 2291): this method is reached from component
            // OnDisable while a POOLED HOST deactivates (EnemyAuraVFX.OnDisable -> VFXHandle.Stop
            // -> here), and SetParent is ILLEGAL inside that window — Unity refuses it, the aura
            // never reaches the pool, and the error fires on every pooled despawn that carries an
            // aura (proven across four host classes: pooled enemies, a building, the hero
            // near-death aura, enemy casters). While the object still hangs under an inactive
            // parent hierarchy we are (or may be) inside that window: silence the particles NOW,
            // DEFER the reparent + enqueue to the next Update sweep. One frame late into the pool
            // is invisible; the thrown reparent was not.
            if (go.transform.parent != null && !go.transform.parent.gameObject.activeInHierarchy)
            {
                go.GetComponent<VfxLoopModulator>()?.Restore();
                StopAllParticles(go);
                bool alreadyPending = false;
                for (int i = 0; i < _pendingReturns.Count; i++)
                    if (_pendingReturns[i].go == go) { alreadyPending = true; break; }
                if (!alreadyPending)
                {
                    _pendingReturns.Add((go, type));
                    FlowTrace.Throttle("VFXManager", "deferred-return", 5f,
                        $"ReturnToPool({type}): host is mid-(de)activation — reparent deferred one frame (WO-929).");
                }
                return;
            }

            CompleteReturn(go, type);
        }

        // The reparent + enqueue + counter tail of a pool return. Runs only OUTSIDE a host
        // (de)activation window (directly, or from the deferred sweep one frame later).
        private void CompleteReturn(GameObject go, VFXType type)
        {
            if (go == null) return;

            // WO-888: a HELD loop may have been modulated while it played (emission density,
            // simulation speed, body scale — the colourblind low-HP pulse read). Nothing else
            // in this method resets instance state, so without this the NEXT user of this pool
            // slot would silently inherit the last owner's modulation, with no error anywhere.
            // Doing it HERE covers every return path — VFXHandle.Stop, the timed return, and
            // the destroyed-host sweep reclaim — not just the ones a handle owns. Idempotent
            // (a deferred return already ran it once in the guard above — harmless twice).
            go.GetComponent<VfxLoopModulator>()?.Restore();

            StopAllParticles(go);
            go.transform.SetParent(_poolRoot, false);
            go.SetActive(false);

            // WO-955 (the write side — this is where the corpses in the free list come from).
            // Transform.SetParent is REFUSED-AND-LOGGED, not thrown, when the current parent is
            // mid-(de)activation or mid-teardown (proven from data in ReturnHovlToPool's header:
            // owner F8 2026-07-17, a Guard.Try wrapper caught nothing for 55 minutes because
            // nothing was ever thrown). The line above therefore has a silent failure mode, and
            // the enqueue below used to run REGARDLESS: a host still parented under a scene
            // object would enter the free list, the scene would unload, and the pool would be
            // holding a corpse — the WO-955 NRE, one scene load later, in an unrelated caller.
            // The WO-929 defer above covers the window we know about; this is the backstop that
            // proves it. It fires ONLY when the reparent did not take, and it names the parent,
            // which is the destroyer this ticket is hunting.
            if (!VfxPoolGuard.IsPoolSafe(go, _poolRoot))
            {
                FlowTrace.Warn("VFXManager",
                    $"CompleteReturn({type}): the reparent to the pool root DID NOT TAKE — host is still " +
                    $"under {VfxPoolGuard.DescribeParent(go)}. NOT enqueued: a free-list slot that is not " +
                    "under the DontDestroyOnLoad pool root dies with its scene and poisons the pool " +
                    "(WO-955). The slot is dropped and capacity self-heals on the next Acquire; the " +
                    "named parent is the teardown to fix.");
            }
            else
            {
                if (!_pools.ContainsKey(type))
                    _pools[type] = new Queue<GameObject>();
                _pools[type].Enqueue(go);
            }

            // bug-triage P1: release the slot in the bucket this object actually came from
            // (tracked in _loopObjects at acquire), instead of guessing oneshot-first and
            // drifting until a class hit its cap and went silent.
            // WO-1057: the registry Remove IS the decrement for the loop bucket, exactly as the
            // oneshot line below has always worked — there is no separate int to keep in step.
            bool wasLoop = _loopObjects.Remove(go);
            if (!wasLoop) UnregisterOneshot(go);   // removing the live-set slot IS the decrement
        }

        /// <summary>Defer pool return by <paramref name="delay"/> seconds (for graceful stop).</summary>
        public void ReturnAfterDelay(GameObject go, VFXType type, float delay)
        {
            if (go == null) return;
            StartCoroutine(ReturnAfterSeconds(go, type, delay));
        }

        private IEnumerator ReturnAfterSeconds(GameObject go, VFXType type, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool(go, type);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── Leak-proof oneshot registry + per-frame sweep (WO-VFX) ────────────
        // ─────────────────────────────────────────────────────────────────────

        // Per-frame backstop that keeps the DERIVED oneshot count honest and reclaims the loop
        // bucket. Cheap: both collections are tiny (<= cap + grace) and it early-outs when empty.
        private void Update()
        {
            SweepOneshots();

            // WO-929: complete any returns deferred out of a host (de)activation window — the
            // cascade that made SetParent illegal is over by the next frame. A host destroyed
            // mid-defer takes its aura child with it: the null entry is dropped here and the
            // prune paths (dead-oneshot sweep / loop-set removal on destroy) own its counters.
            if (_pendingReturns.Count > 0)
            {
                for (int i = 0; i < _pendingReturns.Count; i++)
                {
                    var (go, type) = _pendingReturns[i];
                    if (go == null) continue;
                    CompleteReturn(go, type);
                }
                _pendingReturns.Clear();
            }

            // WO-955: the same one-frame completion for the string-keyed Hovl pool, which has
            // its own return path (VFXManager.Hovl.cs) and therefore its own deferred list.
            SweepPendingHovlReturns();
        }

        /// <summary>Live oneshot count, DERIVED from the tracked slot set after pruning any whose
        /// host GameObject was destroyed. Prune-on-read means the cap check can never see a stale,
        /// leaked count - a destroyed host frees its budget slot the instant we look.</summary>
        private int ActiveOneshotCount()
        {
            PruneDeadOneshots();
            return _oneshotSlots.Count;
        }

        /// <summary>Register a freshly checked-out oneshot so its slot - not a paired ++/-- - owns the
        /// count. A non-null <paramref name="hovlKey"/> marks the Hovl string-key pool return path.</summary>
        private void RegisterOneshot(GameObject go, VFXType type, string hovlKey, float lifetime)
        {
            if (go == null) return;
            _oneshotSlots.Add(new OneshotSlot
            {
                Go       = go,
                Deadline = Time.time + Mathf.Max(0.1f, lifetime) + ONESHOT_RECLAIM_GRACE,
                Type     = type,
                HovlKey  = hovlKey,
            });
        }

        /// <summary>Drop the slot for a normally-returned oneshot (called from ReturnToPool /
        /// ReturnHovlToPool). Removing the slot IS the decrement - the count derives from the set.</summary>
        private void UnregisterOneshot(GameObject go)
        {
            if (go == null) return;
            for (int i = _oneshotSlots.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_oneshotSlots[i].Go, go)) _oneshotSlots.RemoveAt(i);
        }

        /// <summary>Remove slots whose host GameObject was destroyed before its return ran (the leak
        /// itself). Each removal frees a oneshot budget slot. Section 12: FlowTrace when any are freed.</summary>
        private void PruneDeadOneshots()
        {
            int reclaimed = 0;
            for (int i = _oneshotSlots.Count - 1; i >= 0; i--)
            {
                if (_oneshotSlots[i].Go == null)   // Unity-overloaded ==: true once destroyed
                {
                    _oneshotSlots.RemoveAt(i);
                    reclaimed++;
                }
            }
            if (reclaimed > 0)
                FlowTrace.Step("VFXManager",
                    "SweepOneshots: reclaimed " + reclaimed + " leaked oneshot slot(s) whose host was " +
                    "destroyed before return (active now " + _oneshotSlots.Count + "/" + _maxActiveOneshots +
                    ") - the counter can no longer pin at cap.");
        }

        /// <summary>Per-frame sweep: (1) prune destroyed-host oneshots (the leak), (2) force-return any
        /// oneshot past its deadline (defense against a lost return coroutine), (3) reclaim loops whose
        /// host was destroyed before Stop(). Keeps BOTH budget buckets from pinning at their cap.</summary>
        private void SweepOneshots()
        {
            PruneDeadOneshots();

            float now = Time.time;
            for (int i = _oneshotSlots.Count - 1; i >= 0; i--)
            {
                var slot = _oneshotSlots[i];
                if (slot.Go == null) { _oneshotSlots.RemoveAt(i); continue; }
                if (now < slot.Deadline) continue;

                _oneshotSlots.RemoveAt(i);   // remove first so the return's Unregister is a no-op
                FlowTrace.Throttle("VFXManager", "oneshot-reclaim", 1f,
                    "SweepOneshots: force-returning a oneshot past its deadline (Type='" + slot.Type +
                    "', key='" + (slot.HovlKey ?? "-") + "') - its return coroutine never fired. Slot reclaimed.");
                if (!string.IsNullOrEmpty(slot.HovlKey)) ReturnHovlToPool(slot.Go, slot.HovlKey);
                else                                     ReturnToPool(slot.Go, slot.Type);
            }

            ReclaimDestroyedLoops();

            // WO-1473: the release POLICY. Runs BEFORE the audit below on purpose — a loop the
            // policy has just released or suspended must not also be reported as stuck in the
            // same sweep. 0.5 s cadence, never per frame.
            if (Time.realtimeSinceStartup >= _nextLoopPolicy)
            {
                _nextLoopPolicy = Time.realtimeSinceStartup + LOOP_POLICY_INTERVAL;
                TickLoopReleasePolicy();
            }

            // WO-1057: age/orphan audit. Deliberately NOT per-frame — it walks the registries on a
            // 5 s cadence and allocates nothing until it actually has something to report.
            if (Time.realtimeSinceStartup >= _nextLoopAudit)
            {
                _nextLoopAudit = Time.realtimeSinceStartup + LOOP_AUDIT_INTERVAL;
                AuditLoopAges();
            }
        }

        /// <summary>Reclaim loop budget for any aura/trail whose host GameObject was destroyed before
        /// its VFXHandle.Stop() ran (the loop-bucket twin of the oneshot leak). Section 12 FlowTrace on free.</summary>
        private void ReclaimDestroyedLoops()
        {
            int freed = PruneDestroyedFromRegistry(_loopObjects) + PruneDestroyedFromRegistry(_hovlLoopObjects);
            if (freed > 0)
            {
                // WO-1057 — the `_activeLoops = Mathf.Max(0, _activeLoops - freed)` clamp that USED
                // to live on this line is GONE, and its deletion is the point, not a tidy-up. The
                // count is now derived from the registries, so removing the entries above IS the
                // decrement and the value cannot go negative. The old clamp only mattered if
                // decrements could outrun increments — i.e. exactly when the pool was silently
                // UNDER-counting and the cap had stopped protecting anything. A clamp that hides
                // the drift it implies is the bug; do not reintroduce one.
                FlowTrace.Step("VFXManager",
                    "SweepOneshots: reclaimed " + freed + " loop slot(s) whose host was destroyed before " +
                    "Stop() (active loops now " + _activeLoops + "/" + _maxActiveLoops + ").");
            }
        }

        /// <summary>Remove every Unity-destroyed host from <paramref name="registry"/>; returns how
        /// many were dropped. A destroyed GO keeps its managed hash, so the lookup still matches it
        /// and the overloaded == null identifies it. Runs per frame, so it reuses
        /// <see cref="_loopPruneScratch"/> rather than allocating a key list.</summary>
        private int PruneDestroyedFromRegistry(Dictionary<GameObject, LoopRecord> registry)
        {
            if (registry == null || registry.Count == 0) return 0;
            _loopPruneScratch.Clear();
            foreach (var kv in registry)
                if (kv.Key == null) _loopPruneScratch.Add(kv.Key);
            for (int i = 0; i < _loopPruneScratch.Count; i++)
                registry.Remove(_loopPruneScratch[i]);
            int freed = _loopPruneScratch.Count;
            _loopPruneScratch.Clear();
            return freed;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── WO-1057: the loop registry — identity, self-report, capture dump ──
        // ─────────────────────────────────────────────────────────────────────
        //
        // THE ONE-LINE TRUTH THIS SECTION EXISTS FOR: the pool knew HOW MANY loops were live and
        // never WHICH. "Random VFX stuck around" (F8 seq 3583) was structurally unanswerable —
        // the capture had zero [Flow:Vfx] lines because no such line existed anywhere in the tree.
        // These three methods are the instrument. They do NOT fix any leak; they make one nameable.

        /// <summary>Register one live loop under its host. Called on EVERY path that takes a loop
        /// slot (VFXType prefab, procedural fallback, Hovl string key) — the Add IS the increment.</summary>
        private void RegisterLoop(Dictionary<GameObject, LoopRecord> registry, GameObject host,
                                  VFXType type, string hovlKey, Transform owner)
        {
            if (host == null || registry == null) return;

            // Owner name is captured HERE, at start, not read at dump time: a loop that outlives
            // the thing that started it is the single most useful row in the table, and by then
            // owner.name is unreachable. One small string per loop START — never per frame.
            bool   unparented = owner == null;
            string ownerName;
            int    ownerId;
            if (!unparented)
            {
                ownerName = owner.name;
                ownerId   = owner.gameObject.GetInstanceID();
            }
            else
            {
                // Unparented loop (world-positioned aura/marker). The host itself is the only
                // identity there is; say so rather than printing a bare "?" nobody can act on.
                ownerName = "(unparented) " + host.name;
                ownerId   = host.GetInstanceID();
            }

            registry[host] = new LoopRecord
            {
                Type       = type,
                HovlKey    = hovlKey,
                Host       = host,
                Owner      = owner,
                Unparented = unparented,
                OwnerName  = ownerName,
                OwnerId    = ownerId,
                StartedAt  = Time.realtimeSinceStartup,
                StartPos   = host.transform.position,
                Warned     = false,
                // WO-1473: a brand-new loop is HELD and has never been judged off-camera. -1
                // means "no off-camera streak running", which is not the same as "0 seconds".
                Suspended      = false,
                SuspendedAt    = 0f,
                OffscreenSince = -1f,
            };

            // WO-1473 — THE PER-OWNER AMBIENT CAP, asserted at the moment the slot is taken.
            // A single owner accumulating loop records is the counter-leak SHAPE (the same host
            // re-registering, or a driver that starts a second aura without stopping the first):
            // it is invisible in a bare count and obvious here. The ceiling is derived, not
            // picked: the largest legitimate simultaneous hold read at source is THREE on one
            // structure - StructureDamageVisuals' Burn loop (Damage_Smolder/Fire/Ruin) plus its
            // Beacon loop (Damage_CriticalBeacon) plus an ArcaneAura ambient ring - so the cap
            // sits one above that. Anything past it is released OLDEST-KEPT / NEWEST-DROPPED,
            // because the oldest record is the one the owner's own handle field still points at.
            EnforceOwnerLoopCap(registry, ownerId);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── WO-1473: THE RELEASE POLICY — one policy in the pool, not per-effect
        // ─────────────────────────────────────────────────────────────────────
        //
        // ## WHY SUSPEND AND NOT RETURN-TO-POOL
        //
        // The obvious "release" is ReturnToPool(host). It is WRONG here and the reason is not
        // stylistic: every loop's caller still holds a live VFXHandle pointing at that host
        // (ArcaneAura._handle, StructureDamageVisuals rec.Burn/rec.Beacon, ...). Hand the host
        // back to the pool behind the caller's back and its later, entirely correct Stop() is a
        // SECOND return of an instance the pool may already have handed to somebody else - one
        // GameObject enqueued twice and simultaneously owned by two effects. That is a worse bug
        // than the one being fixed, and it is precisely the class of defect WO-955 spent a day on.
        //
        // So the slot is released WITHOUT disturbing ownership: the host's particle systems are
        // stopped-and-cleared, the record is marked Suspended, and _activeLoops (which is DERIVED,
        // see CountHeld) stops counting it. The effect costs no slot, no simulation and no draw
        // call; the handle stays valid; every existing return path (VFXHandle.Stop, ReturnToPool,
        // ReturnHovlToPool, the destroyed-host reclaim) works unchanged because the record is
        // still in the same registry it always was. Resume is PlayAllParticles on the same host.
        //
        // The one HARD release stays where WO-1057 put it: ReclaimDestroyedLoops, for a host Unity
        // has actually destroyed. Nothing else in this class returns a host a caller still owns.
        private void TickLoopReleasePolicy()
        {
            // Cache the camera across ticks; Camera.main is a tagged search. This mirrors
            // VfxAuraProximityCuller.RankingOrigin, which is the established camera seam in
            // this silo - a second way of finding the gameplay camera is a second thing to fix.
            if (_policyCamera == null) _policyCamera = Camera.main;
            bool cameraKnown = _policyCamera != null;
            if (cameraKnown)
                GeometryUtility.CalculateFrustumPlanes(_policyCamera, _loopFrustum);

            _loopPolicyScratch.Clear();
            foreach (var kv in _loopObjects)     if (kv.Value != null) _loopPolicyScratch.Add(kv.Value);
            foreach (var kv in _hovlLoopObjects) if (kv.Value != null) _loopPolicyScratch.Add(kv.Value);
            if (_loopPolicyScratch.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            int cap = _maxActiveLoops;
            int held = _activeLoops;

            for (int i = 0; i < _loopPolicyScratch.Count; i++)
            {
                var rec = _loopPolicyScratch[i];
                if (rec == null || rec.Host == null) continue;   // destroyed host: ReclaimDestroyedLoops owns it

                // The colourblind low-HP tell is unrefusable at the cap (WO-1229) and is likewise
                // never suspended here. One list, one predicate, in VfxLoopBudget - an id written
                // inline at a second check site is the drift this repo keeps paying for.
                bool exempt = VfxLoopBudget.IsAccessibilityLoop(rec.Type);

                bool hadOwner       = !rec.Unparented;
                bool ownerDestroyed = hadOwner && rec.Owner == null;
                bool ownerActive    = !hadOwner || (rec.Owner != null && rec.Owner.gameObject.activeInHierarchy);

                bool visible = true;
                if (cameraKnown && !ownerDestroyed)
                {
                    var bounds = new Bounds(rec.Host.transform.position,
                                            Vector3.one * (LOOP_VISIBILITY_RADIUS * 2f));
                    visible = GeometryUtility.TestPlanesAABB(_loopFrustum, bounds);
                }

                // Off-camera STREAK clock. realtimeSinceStartup, not Time.time: captured sessions
                // run at timeScale 0.28, and a grace measured in scaled seconds would be a
                // different grace on every device.
                if (visible || !cameraKnown) rec.OffscreenSince = -1f;
                else if (rec.OffscreenSince < 0f) rec.OffscreenSince = now;
                float offscreenFor = rec.OffscreenSince < 0f ? 0f : now - rec.OffscreenSince;

                // Resume is budgeted: a town's worth of spires walking back on camera must not
                // re-starve the pool the moment the player turns around.
                bool slotAvailable = held < cap;

                var action = VfxLoopReleasePolicy.Decide(
                    suspended:      rec.Suspended,
                    exempt:         exempt,
                    ownerDestroyed: ownerDestroyed,
                    ownerActive:    ownerActive,
                    cameraKnown:    cameraKnown,
                    visible:        visible,
                    offscreenFor:   offscreenFor,
                    grace:          OFFSCREEN_RELEASE_GRACE,
                    slotAvailable:  slotAvailable);

                switch (action)
                {
                    case VfxLoopReleasePolicy.LoopAction.Suspend:
                        SuspendLoop(rec, now,
                            ownerDestroyed ? "owner destroyed"
                          : !ownerActive   ? "owner disabled"
                          : "off camera " + offscreenFor.ToString("F0") + "s");
                        held--;
                        break;

                    case VfxLoopReleasePolicy.LoopAction.Resume:
                        ResumeLoop(rec, now);
                        held++;
                        break;
                }
            }

            _loopPolicyScratch.Clear();
        }

        /// <summary>Release this loop's SLOT without touching its ownership: stop-and-clear the
        /// host's particles and stop counting it. Permanent FlowTrace (CLAUDE.md §12) — every
        /// release names the key, the owner, the reason and the age, because a suspended effect
        /// is an effect the player stopped seeing and that must never be a silent event.</summary>
        private void SuspendLoop(LoopRecord rec, float now, string reason)
        {
            if (rec == null || rec.Suspended || rec.Host == null) return;
            rec.Suspended   = true;
            rec.SuspendedAt = now;
            foreach (var ps in rec.Host.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            FlowTrace.Step("Vfx",
                "LOOP RELEASED " + rec.Key + " owner='" + rec.OwnerName + "'#" + rec.OwnerId +
                " age=" + (now - rec.StartedAt).ToString("F0") + "s reason=" + reason +
                " — slot freed (now " + _activeLoops + "/" + _maxActiveLoops + "); the host is kept " +
                "checked out so its owner's VFXHandle stays valid and it can resume in place.");
        }

        /// <summary>Put a suspended loop back on screen, on the same host, with the same handle.</summary>
        private void ResumeLoop(LoopRecord rec, float now)
        {
            if (rec == null || !rec.Suspended || rec.Host == null) return;
            rec.Suspended = false;
            rec.OffscreenSince = -1f;
            PlayAllParticles(rec.Host);
            FlowTrace.Step("Vfx",
                "LOOP RESUMED " + rec.Key + " owner='" + rec.OwnerName + "'#" + rec.OwnerId +
                " after " + (now - rec.SuspendedAt).ToString("F0") + "s suspended — back on camera " +
                "and a slot was free (now " + _activeLoops + "/" + _maxActiveLoops + ").");
        }

        /// <summary>WO-1473 — assert the per-owner ambient cap the moment a loop registers.
        /// The ceiling is READ, not picked: the largest legitimate simultaneous hold on one owner
        /// found at source is THREE (StructureDamageVisuals' Burn loop + its CriticalBeacon loop +
        /// an ArcaneAura ambient ring), so <see cref="LOOPS_PER_OWNER_CAP"/> sits one above it.
        /// Beyond that, the NEWEST records are suspended and the oldest kept — the oldest is the
        /// one the owner's own handle field points at, so keeping it is what preserves the
        /// feature while the accumulation stops costing slots.</summary>
        private void EnforceOwnerLoopCap(Dictionary<GameObject, LoopRecord> registry, int ownerId)
        {
            if (registry == null || registry.Count <= LOOPS_PER_OWNER_CAP) return;

            _ownerCapScratch.Clear();
            foreach (var kv in registry)
                if (kv.Value != null && kv.Value.OwnerId == ownerId && !kv.Value.Suspended)
                    _ownerCapScratch.Add(kv.Value);

            if (_ownerCapScratch.Count > LOOPS_PER_OWNER_CAP)
            {
                _ownerCapScratch.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));   // oldest first
                float now = Time.realtimeSinceStartup;
                for (int i = LOOPS_PER_OWNER_CAP; i < _ownerCapScratch.Count; i++)
                    SuspendLoop(_ownerCapScratch[i], now,
                        "per-owner cap (" + _ownerCapScratch.Count + " held by one owner, cap " +
                        LOOPS_PER_OWNER_CAP + ") — an owner accumulating loops is the counter-leak shape");
            }
            _ownerCapScratch.Clear();
        }

        /// <summary>Self-report a loop that has outlived its owner or a sane age — ONCE per handle,
        /// naming the owner. This is what turns the NEXT occurrence from "the owner noticed a stuck
        /// effect and pressed F8" into a line already sitting in the log (CLAUDE.md §14).
        /// <para/>
        /// ⚠ Reports ONLY — still. Releasing is <see cref="TickLoopReleasePolicy"/>'s job (WO-1473),
        /// and keeping the two apart is what lets this method be the ORACLE for that policy: every
        /// line it prints is now a statement that the policy failed to act, not merely that a loop
        /// is old. A detector that the fix silences by construction proves nothing.</summary>
        private void AuditLoopAges()
        {
            AuditRegistryAges(_loopObjects);
            AuditRegistryAges(_hovlLoopObjects);
        }

        private void AuditRegistryAges(Dictionary<GameObject, LoopRecord> registry)
        {
            if (registry == null || registry.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            // Dictionary's enumerator is a struct, so this foreach allocates nothing. LoopRecord is
            // a class precisely so Warned can be latched in place without re-assigning the entry.
            foreach (var kv in registry)
            {
                var rec = kv.Value;
                if (rec == null || rec.Warned) continue;

                // WO-1473 — STUCK now means "HELD WHEN THE POLICY SAYS IT SHOULD HAVE BEEN
                // RELEASED", not merely "old". A suspended loop holds no slot, so it is not
                // stuck; and a five-minute aura the player is LOOKING AT is the feature working,
                // which is exactly what the WO-1057 age-only rule mis-reported 20 times in one
                // session. Age alone was the right oracle while there was no policy - now that
                // there is one, the oracle has to test the policy or it fails on the fix.
                if (rec.Suspended) continue;

                float age = now - rec.StartedAt;
                bool orphaned = !rec.Unparented && rec.Owner == null;
                // Off camera past the grace and STILL held => the policy tick did not run or did
                // not act. That, and an orphan still holding a slot, are the two real defects.
                bool offscreenPastGrace = rec.OffscreenSince >= 0f
                                       && (now - rec.OffscreenSince) >= OFFSCREEN_RELEASE_GRACE;
                if (!orphaned && !offscreenPastGrace) continue;
                // A long-lived ON-CAMERA loop is legitimate; the age threshold survives only as
                // the "this has been true for ages, say so louder" note in the line below.

                rec.Warned = true;   // one per handle, forever — never a per-frame log storm
                FlowTrace.Warn("Vfx",
                    "STUCK LOOP " + rec.Key + " owner='" + rec.OwnerName + "'#" + rec.OwnerId +
                    " age=" + age.ToString("F0") + "s " +
                    (orphaned
                        ? "— ITS OWNER IS DESTROYED and it is STILL HELD: the WO-1473 release policy should " +
                          "have suspended it within " + LOOP_POLICY_INTERVAL + "s. "
                        : "— OFF CAMERA for " + (now - rec.OffscreenSince).ToString("F0") + "s and STILL HELD: " +
                          "past the " + OFFSCREEN_RELEASE_GRACE + "s release grace. ") +
                    (age >= STUCK_LOOP_AGE_SECONDS ? "(Also older than the " + STUCK_LOOP_AGE_SECONDS +
                        "s age threshold.) " : "") +
                    "Holding 1 of " + _maxActiveLoops + " loop slots (now " + _activeLoops + "/" + _maxActiveLoops +
                    "). The policy exists (WO-1473) — if this line appears, the policy is not doing its job.");
            }
        }

        /// <summary>Print an age-sorted table of every live loop, one line per loop, tagged
        /// [Flow:Vfx] so it lands in the F8 harvest. Wired to
        /// BreakCaptureHarness.CaptureSnapshotRequested in Awake, so the owner presses F8 and the
        /// table is simply THERE — it is never a debug menu she has to remember to open, and it is
        /// never fenced behind DEVELOPMENT_BUILD (instrumentation is permanent, CLAUDE.md §12).
        /// <para/>Costs nothing until a capture: no allocation, no string building on the hot path.</summary>
        public void DumpLiveLoops()
        {
            // Reclaim first so the table never lists a loop whose host is already gone.
            ReclaimDestroyedLoops();

            _loopDumpScratch.Clear();
            foreach (var kv in _loopObjects)     if (kv.Value != null) _loopDumpScratch.Add(kv.Value);
            foreach (var kv in _hovlLoopObjects) if (kv.Value != null) _loopDumpScratch.Add(kv.Value);

            // OLDEST FIRST — age is the leak signal, so the suspect is row one.
            _loopDumpScratch.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));

            int shown = _loopDumpScratch.Count < LOOP_DUMP_MAX_ROWS
                      ? _loopDumpScratch.Count : LOOP_DUMP_MAX_ROWS;

            FlowTrace.Step("Vfx",
                "LOOPS " + _activeLoops + "/" + _maxActiveLoops +
                " held, " + _loopDumpScratch.Count + " registered (the difference is WO-1473 " +
                "SUSPENDED loops: released slot, host kept, resumes on camera re-entry)" +
                " (live loop registry, oldest first" +
                (shown < _loopDumpScratch.Count ? "; showing " + shown + " oldest" : "") + ")");

            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < shown; i++)
            {
                var rec = _loopDumpScratch[i];
                // Live host position where we still have one — the row has to tie to the thing the
                // owner can actually see on screen. Falls back to where it started otherwise.
                Vector3 pos = rec.Host != null ? rec.Host.transform.position : rec.StartPos;
                FlowTrace.Step("Vfx",
                    "  " + rec.Key +
                    "  owner='" + rec.OwnerName + "'#" + rec.OwnerId +
                    "  age=" + (now - rec.StartedAt).ToString("F0") + "s" +
                    "  pos=(" + pos.x.ToString("F1") + ", " + pos.y.ToString("F1") + ", " + pos.z.ToString("F1") + ")" +
                    (rec.Suspended ? "  [SUSPENDED " + (now - rec.SuspendedAt).ToString("F0") +
                                     "s, holds no slot]" : "") +
                    (!rec.Unparented && rec.Owner == null ? "  <-- OWNER DESTROYED, nothing can Stop() it" : ""));
            }
            if (shown < _loopDumpScratch.Count)
                FlowTrace.Step("Vfx", "  ... and " + (_loopDumpScratch.Count - shown) +
                    " younger loop(s) not listed (row cap keeps the capture tail readable).");

            _loopDumpScratch.Clear();   // do not hold the records past the capture
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void PlayAllParticles(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear();
                ps.Play();
            }
        }

        private static void StopAllParticles(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// Auto-detect the longest total duration among all particle systems on the
        /// prefab so we know when it's safe to return to the pool.
        /// </summary>
        /// <remarks>
        /// ⚠ THIS READS <c>duration + startLifetime</c> AND HAS NEVER CONSULTED <c>main.loop</c>.
        /// For a LOOPING system that number is not an end at all - the system keeps emitting for
        /// the whole window and the figure is merely when the pool reclaims it. That is safe only
        /// because <see cref="EnforceOneshotEmission"/> now clears the loop flag on every oneshot
        /// instance BEFORE this is measured, so by the time we get here nothing is looping and the
        /// arithmetic is the true end. Do not call this on a host whose systems still loop.
        /// </remarks>
        private static float DetectDuration(GameObject go)
        {
            float max = 0f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                float d = m.duration + m.startLifetime.constantMax;
                if (d > max) max = d;
            }
            return Mathf.Max(max, 0.5f);
        }

        /// <summary>
        /// THE CATALOG'S <c>IsLoop</c> FLAG IS AUTHORITATIVE ON THE INSTANCE. Clears
        /// <c>main.loop</c> on every ParticleSystem of a host checked out through a ONESHOT row,
        /// so a prefab authored as an endless ambient loop cannot emit for its entire pool
        /// lifetime when a oneshot row plays it. Returns the number of systems corrected.
        /// </summary>
        /// <remarks>
        /// <para>WHY THIS EXISTS - owner F8 2026-09-02 seq 4644, verbatim: <i>"the fire spell is
        /// wrong. casts at me and stays at me."</i> The mage's Fireball cast beat resolves to
        /// <see cref="VFXType.Cast_FireCharge"/>, whose catalogued prefab
        /// (Resources/VFX/Projectiles/Casting_Fire.prefab) carries FOUR ParticleSystems that are
        /// all <c>looping: 1</c>, lengthInSec 5, startLifetime up to 5 - while the catalog row
        /// declares <c>IsLoop: 0</c> with no LifetimeOverride. <see cref="DetectDuration"/> then
        /// returned 5 + 5 + 0.3 = 10.3s and, because nothing stopped the emitters, the effect
        /// BURNED CONTINUOUSLY on the caster for those 10.3 seconds. Fireball's authored cooldown
        /// is 0.6s, so up to seventeen of them overlapped on the hero - which is precisely the
        /// "stays at me" the owner reported, and it matches the captured
        /// <c>[Flow:VFX] live systems=35</c> against an idle ability pool in that same session.</para>
        /// <para>The fix is deliberately at the LIFECYCLE OWNER and not in the art: no prefab is
        /// re-authored, no effect is substituted (choosing a different fire effect is the owner's
        /// creative call, never the CLI's). Clearing the flag makes a oneshot behave the way its
        /// row already claims it does. A row that genuinely wants a continuous effect declares
        /// <c>IsLoop: 1</c> and never reaches this path.</para>
        /// <para>Idempotent, and safe across pool cycles: a pooled host is only ever checked out
        /// for the one key/type it was built for, so the cleared flag can never leak onto an
        /// effect that is supposed to loop.</para>
        /// </remarks>
        private static int EnforceOneshotEmission(GameObject go, string what)
        {
            if (go == null) return 0;
            int fixedCount = 0;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                if (!m.loop) continue;
                m.loop = false;
                fixedCount++;
            }
            if (fixedCount > 0)
                FlowTrace.Once("VFXManager", "oneshot-loop-clamp:" + what,
                    $"ONESHOT ROW PLAYING A LOOPING PREFAB: '{what}' has {fixedCount} ParticleSystem(s) " +
                    "authored looping while its catalog row declares IsLoop=0. Loop CLEARED on the " +
                    "instance so it emits one cycle and ends (it would otherwise burn for the whole " +
                    "pooled lifetime - owner F8 seq 4644 'casts at me and stays at me'). Set IsLoop=1 " +
                    "on the row if a continuous effect was intended.");
            return fixedCount;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── WO-1327 — THE ART PACK'S PHYSICS AND LIGHT BILL, CLAMPED HERE ────
        //
        // ⛔ WHY THIS IS CODE AND NOT A PREFAB EDIT. `Assets/Spells Pack/` is
        //    GITIGNORED (.gitignore:430), exactly like the polyperfect and
        //    Quaternius packs. A hand-edit to Spell_Fire_9.prefab therefore
        //    cannot be committed, cannot be reviewed, does not reach another
        //    machine or CI, and is erased by the next pack re-import — while
        //    still silently changing what THIS machine builds. Tuning the
        //    numbers where they are authored is not available to us, so the
        //    clamp goes at the ONE spawn owner instead. That also fixes every
        //    other pack prefab carrying the same misconfiguration rather than
        //    the single instance somebody happened to notice.
        //
        // ⛔ BOTH DIALS ARE FEEL/PRESENTATION VALUES, so per the 2026-09-02
        //    standing rule they ride the PROD-022 tunables rail
        //    (docs/PROD022_TUNABLE_FLAGS.md) and move without a rebuild. The
        //    numbers baked into the PREFAB are NOT reachable by that rail —
        //    only these code-side clamps are, which is the second reason the
        //    fix has to live here.
        //
        // ⛔ NEITHER TOUCHES THE LOOK. No colour, no material, no prefab swap,
        //    no light PROTOTYPE deleted. The owner owns every creative VFX
        //    call and is red/green colourblind; these change only WHETHER a
        //    particle bounces and HOW MANY lights it lights.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Restitution ceiling, 0–100 percent, for world-colliding particles on a spawned VFX
        /// host. Resolved per spawn (never cached) so a flip reaches a running client.
        /// </summary>
        public static int ParticleBouncePct =>
            Mathf.Clamp(RemoteTunables.Int(RemoteTunables.KeyVfxParticleBouncePct), 0, 100);

        /// <summary>
        /// Concurrent particle-driven real-time lights ONE VFX host may carry, summed over its
        /// emitters. Resolved per spawn. 0 = particle lights off.
        /// </summary>
        public static int MaxParticleLights =>
            Mathf.Clamp(RemoteTunables.Int(RemoteTunables.KeyVfxMaxParticleLights), 0, 64);

        /// <summary>
        /// A PERFECTLY ELASTIC PARTICLE INSIDE A WALLED TOWN IS A PROJECTILE IN A BOX.
        /// Clamps world collision on every ParticleSystem of a checked-out host so an impact
        /// TERMINATES the particle instead of returning it to the caster.
        /// </summary>
        /// <remarks>
        /// <para>THE MEASURED SETTINGS (read out of the prefab YAML, not inferred):
        /// <c>Spell_Fire_9</c>'s <c>Fireballs</c> emitter — the prefab the owner tagged to
        /// <c>firespell_Cast</c> — has world collision at <c>quality: 0</c> (High) against
        /// <c>collidesWith = 0xFFFFFFFF</c> (ALL 32 LAYERS), with <c>m_Bounce</c> scalar
        /// <b>1.0</b> (perfectly elastic), <c>m_Dampen</c> <b>0</b>, and <c>minKillSpeed</c>
        /// <b>0</b> (no impact ever kills the particle). Six of its seven emitters have
        /// collision DISABLED; this one does not.</para>
        /// <para>⚠ HONEST ABOUT WHAT IS AND IS NOT PROVEN (CLAUDE.md §12). The above is a
        /// mechanical reading of captured settings, NOT a captured runtime trace, and it is
        /// NOT the proven root of the owner's two reports. Seq 4644 ("casts at me and stays at
        /// me") was independently root-caused the same day to the ONESHOT-ROW-PLAYING-A-LOOPING-
        /// PREFAB defect that <see cref="EnforceOneshotEmission"/> fixes, evidenced by
        /// <c>[Flow:VFX] live systems=35</c>. What corroborates THIS defect is the owner's own
        /// description of the prefab in her VFX Caster, quoted in <see cref="MarqueeSpellVfx"/>:
        /// "a wind up directly into projectiles flying AND BOUNCING". So: a real
        /// misconfiguration, a plausible contributor to the same felt symptom, and not a
        /// substitute for her eyes on the result.</para>
        /// <para>The clamp is DELIBERATELY ONE-WAY. Bounce is only ever lowered toward the cap;
        /// dampen and lifetime-loss are only ever raised toward its complement. It can never make
        /// an effect bouncier or longer-lived than its author made it, so
        /// <c>vfx.particleBouncePct = 100</c> is a true no-op that hands the pack back its
        /// authored behaviour in one flag flip.</para>
        /// <para>Setting <c>bounce</c>/<c>dampen</c>/<c>lifetimeLoss</c> as CONSTANTS also
        /// resolves an ambiguity in the authored YAML, where <c>m_EnergyLossOnCollision</c>
        /// carries scalar 1 over a zero-valued curve. Whichever of those Unity would have
        /// evaluated, after this the particle terminates at the first surface it touches.</para>
        /// <para>Idempotent, and safe across pool cycles: a pooled host is only ever checked out
        /// for the one key/type it was built for.</para>
        /// </remarks>
        private static int TameWorldCollision(GameObject go, string what)
        {
            if (go == null) return 0;

            int pct = ParticleBouncePct;
            if (pct >= 100)
            {
                FlowTrace.Once("VFXManager", "collision-clamp-off:" + what,
                    $"world-collision clamp DISABLED for '{what}' (vfx.particleBouncePct=100) — the " +
                    "art pack's authored collision is playing untouched. This is the escape hatch, " +
                    "not a failure.");
                return 0;
            }

            float bounceCap = pct / 100f;
            float lossFloor = 1f - bounceCap;
            int corrected = 0;

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var col = ps.collision;
                if (!col.enabled) continue;
                if (col.type != ParticleSystemCollisionType.World) continue;

                bool changed = false;
                if (col.bounceMultiplier > bounceCap)       { col.bounce = bounceCap;       changed = true; }
                if (col.dampenMultiplier < lossFloor)       { col.dampen = lossFloor;       changed = true; }
                if (col.lifetimeLossMultiplier < lossFloor) { col.lifetimeLoss = lossFloor; changed = true; }
                if (changed) corrected++;
            }

            if (corrected > 0)
                FlowTrace.Once("VFXManager", "collision-clamp:" + what,
                    $"WORLD-COLLISION CLAMPED on '{what}': {corrected} emitter(s) were authored to " +
                    $"bounce off scene geometry with more than {pct}% restitution. Bounce <= {bounceCap:0.00}, " +
                    $"dampen and lifetime-loss >= {lossFloor:0.00}, so an impact now TERMINATES the particle " +
                    "instead of ricocheting it back at the caster (WO-1327; the authored values live in a " +
                    "gitignored art pack, so the clamp lives here). Flip vfx.particleBouncePct to 100 to " +
                    "hand the pack back its authored collision.");
            return corrected;
        }

        /// <summary>
        /// TWENTY-FIVE REAL-TIME POINT LIGHTS PER CAST IS A FRAME-RATE EVENT ON A PHONE.
        /// Spends a fixed per-host budget of concurrent particle lights evenly across the host's
        /// enabled <c>LightsModule</c>s and scales each module's <c>ratio</c> down with it.
        /// </summary>
        /// <remarks>
        /// <para>THE MEASURED NUMBERS. <c>Spell_Fire_9</c>'s child <c>Point Light</c> has
        /// <c>m_Enabled: 0</c> on its <c>Light</c> component, which LOOKS like an off switch and
        /// is not — it is the PROTOTYPE the modules clone. Two emitters instantiate from it with
        /// <c>ratio: 1</c>: <c>Fireballs</c> at <c>maxLights: 20</c> and the sub-emitter
        /// <c>Explosion</c> at <c>maxLights: 5</c>. <b>25 concurrent real-time point lights per
        /// cast</b>, intensity 5, range 5. At the shipped budget of 4 that becomes <b>2 + 2 = 4</b>.</para>
        /// <para>⛔ THE PROTOTYPE IS NEVER DELETED OR DISABLED. It is what the module clones from;
        /// removing it breaks the effect instead of tuning it. The dials are <c>maxLights</c> and
        /// <c>ratio</c>, and only those are touched.</para>
        /// <para>WHY <c>ratio</c> MOVES TOO. <c>maxLights</c> alone is a hard ceiling: leaving
        /// <c>ratio</c> at 1 would attach the surviving lights to the FIRST few particles and leave
        /// the rest of the effect dark. Scaling ratio by the same factor keeps the lights spread
        /// across the burst, which is the closest the budget can stay to what the author drew. It
        /// is a behaviour/perf change, not a restyle — no colour, intensity or range is touched.</para>
        /// <para>A host whose authored total already fits the budget is left completely alone.</para>
        /// </remarks>
        private static int ClampParticleLights(GameObject go, string what)
        {
            if (go == null) return 0;

            var modules = new List<ParticleSystem>();
            int authored = 0;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var lights = ps.lights;
                if (!lights.enabled) continue;
                if (lights.light == null) continue;   // enabled with no prototype emits nothing
                modules.Add(ps);
                authored += Mathf.Max(0, lights.maxLights);
            }
            if (modules.Count == 0) return 0;

            int budget = MaxParticleLights;
            if (authored <= budget) return 0;         // already inside the budget — untouched

            // Even share, at least one light each while any budget remains, so no single emitter
            // is silently blacked out by integer division.
            int share = budget <= 0 ? 0 : Mathf.Max(1, budget / modules.Count);
            int granted = 0;

            foreach (var ps in modules)
            {
                var lights = ps.lights;
                int was = Mathf.Max(0, lights.maxLights);
                int now = Mathf.Min(was, share);
                if (budget <= 0)
                {
                    lights.enabled = false;           // the module, never the prototype
                    continue;
                }
                if (now < was)
                {
                    // Keep the lights spread across the burst instead of stuck to the first
                    // particles: scale the spawn probability by the same factor.
                    float scale = was > 0 ? (float)now / was : 1f;
                    lights.ratio = Mathf.Clamp(lights.ratio * scale, 0.02f, 1f);
                    lights.maxLights = now;
                }
                granted += now;
            }

            FlowTrace.Once("VFXManager", "light-budget:" + what,
                $"PARTICLE LIGHT BUDGET APPLIED to '{what}': {modules.Count} emitter(s) authored " +
                $"{authored} concurrent real-time point light(s); capped to {granted} " +
                $"(vfx.maxParticleLights={budget}). Prototypes are untouched — only maxLights and " +
                "ratio moved (WO-1327: Spell_Fire_9 alone authored 25 per cast, on a phone).");
            return authored - granted;
        }

        /// <summary>
        /// The one place a checked-out VFX host is made safe to play: world collision clamped so a
        /// particle dies where it lands, and the host's real-time light bill capped. Called from
        /// EVERY spawn path (oneshot, loop, and the Hovl key path) because neither defect has
        /// anything to do with whether a row loops.
        /// </summary>
        private static void NormalizeSpawnedHost(GameObject go, string what)
        {
            TameWorldCollision(go, what);
            ClampParticleLights(go, what);
        }

        /// <summary>
        /// Ceiling on how long a <c>Cast_*</c> WIND-UP beat may live. Cast_* is defined by
        /// <see cref="VFXType"/>'s own contract as a "charge/wind-up on the caster that plays
        /// BEFORE the release" - it is bounded by the cast, not by whatever length an art pack
        /// happened to author into an ambient prefab. DERIVATION, not taste: the longest authored
        /// hero <c>castSeconds</c> in abilities.json is 0.5s (mage.poison / mage.meteor /
        /// mage.cataclysm), and the shortest authored cooldown that re-fires the beat is
        /// mage.fireball's 0.6s - so a wind-up must be finished well inside the following cast or
        /// it stacks on itself. 0.5s of charge + a 0.75s tail for the release to read.
        /// Impacts, deaths, auras and every other category are UNAFFECTED.
        /// </summary>
        private const float CAST_BEAT_MAX_SECONDS = 1.25f;

        // ─────────────────────────────────────────────────────────────────────
        // ── WO-1327 REOPEN — THE BEAT THAT STAYED AT THE CASTER ──────────────
        //
        // ⛔ THIS SET IS DERIVED FROM THE ENUM NAMES. DO NOT REPLACE IT WITH A
        //    HAND-WRITTEN LIST AGAIN — the hand-written list is the defect.
        //
        // WHAT THE DEVICE CAPTURE PROVED (Logs/device/endstate-window-20260904.log,
        // owner's own 2026-09-04 session, ONE mage.fireball cast at 09:35:43.89):
        //
        //   [Flow:VFXManager] PlayOneshot('Cast_FireCharge')  at (5000.19, 1.28, 4994.75)
        //                     parent='<none, world-space>' lifetime=1.25s.
        //   [Flow:VFXManager] PlayOneshot('Cast_MuzzleFlash') at (5000.19, 1.18, 4994.75)
        //                     parent='<none, world-space>' lifetime=20.30s.
        //   [Flow:HeroMana]   cast CHARGED slot=Q 'Fireball' ... cd=0.60s
        //
        // Two beats, the SAME cast, the SAME caster position (the FireSpellOrb origin),
        // both unparented in world space. One was clamped to CAST_BEAT_MAX_SECONDS; the
        // other stood at the caster for 20.30 s while the cooldown that re-spawns it is
        // 0.60 s — and it is fired again by EVERY tower shot (TowerCombat.cs:376) and
        // every ranged release (RangedAttackVFX.PlayReleaseFlash). The same capture shows
        // live systems climbing 12 → 56 (particles~1006).
        //
        // ⛔ THE ROOT IS NOT IN THIS FILE — IT IS A STALE VFXCatalog.asset, AND THE CLAMP
        //    BELOW IS ONLY THE BLAST DOOR. Proven from tree bytes, arithmetic exact:
        //      • 0b18cccc5 (2026-08-20) DELETED two VFXType members (Aura_PetLevel1/3 with
        //        the pet-aura system). VFXCatalog.asset was last regenerated e65b549ff
        //        (2026-08-16), four days EARLIER, and stores rows by ORDINAL.
        //      • So every ordinal past the deletion point is off by two. VFXType.
        //        Cast_MuzzleFlash == 81, and catalog row 81 holds Env_SteamVent.prefab.
        //      • Env_SteamVent.prefab measures lengthInSec 10.0 + startLifetime 10.0 =
        //        20.0 s; DetectDuration + 0.3 = 20.30 s — the log's number, to the digit.
        //    The muzzle flash was never playing a muzzle flash. It was planting a TWENTY-
        //    SECOND STEAM COLUMN on the caster, several times a second. Nothing in code
        //    could catch that: the reference resolves, the prefab loads, particles play.
        //    FIX AT SOURCE: Defenders/VFX/Generate VFX Catalog (Unity, out of this silo).
        //    PINNED so it cannot silently return: VfxLoopFlagRegression's enum-alignment
        //    case, which fails today on this exact row.
        //
        // ⚠ WHAT IS *NOT* PROVEN, stated plainly (CLAUDE.md §11B): that this is the exact
        //    "red glowing orb stayed at me" the owner felt on build 2026.09.04.354315.
        //    Only her eyes close that. The 09-04 log's own build string was not captured;
        //    what IS provable about her build is that IsCastBeat has not changed since
        //    ba5b7fad0 (09-02) and the catalog since e65b549ff (08-16), so ANY 09-04 build
        //    carries both. Also proven: the WO-1327 bounce clamp is not even in evidence on
        //    the path she plays — the capture reads "[Flow:Ranged] FireSpellOrb -> ...
        //    prefab=<pooled-vfx> hovl=<none>" and "[Flow:Projectile] Launch dist=9.5
        //    speed=24.0 ... timeout=2.2s", a pooled mover that self-terminates, with no
        //    collision-clamp / marquee line anywhere in the session.
        //
        /// <summary>
        /// True when <paramref name="type"/> is a <c>Cast_*</c> wind-up beat. Membership is
        /// DERIVED from the <see cref="VFXType"/> naming convention (Category_Descriptor,
        /// VFXType.cs:12), computed once, so a newly added <c>Cast_*</c> member is bounded
        /// from its first cast instead of waiting for someone to remember this file.
        /// </summary>
        public static bool IsCastBeatType(VFXType type) => _castBeatTypes.Contains(type);

        private static bool IsCastBeat(VFXType type) => IsCastBeatType(type);

        private static readonly HashSet<VFXType> _castBeatTypes = BuildCastBeatSet();

        private static HashSet<VFXType> BuildCastBeatSet()
        {
            var set = new HashSet<VFXType>();
            foreach (VFXType t in System.Enum.GetValues(typeof(VFXType)))
            {
                // Ordinal, culture-invariant: this is an identifier prefix, never display text.
                if (t.ToString().StartsWith("Cast_", System.StringComparison.Ordinal))
                    set.Add(t);
            }
            return set;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ── PROCEDURAL FALLBACK ───────────────────────────────────────────────
        // Map VFXType → AbilityVfxKit calls so nothing is ever a silent no-op.
        // ─────────────────────────────────────────────────────────────────────

        private static readonly Color _aetherColor = new Color(0.90f, 0.87f, 1.00f);
        private static readonly Color _flameColor  = new Color(1.00f, 0.27f, 0.00f);
        private static readonly Color _iceColor    = new Color(0.50f, 0.80f, 1.00f);
        private static readonly Color _healColor   = new Color(0.35f, 1.00f, 0.55f);
        private static readonly Color _physColor   = new Color(0.69f, 0.69f, 0.63f);
        private static readonly Color _goldColor   = new Color(0.92f, 0.78f, 0.30f);

        private void ProceduralFallback(VFXType type, Vector3 position, Quaternion rotation)
        {
            // Map VFXType to an AbilityVfxKit call. Types without a mapping get a
            // generic Aoe nova so nothing is ever silently missing.
            switch (type)
            {
                // ── Arcane cast chain (2026-07-02 spell language) ─────────────
                // WINDUP: a gathering glow at the caster's hand, sized to the >=1s
                // enemy-caster telegraph — replaces the old generic Strike tracer
                // (owner F8 flag_15: casts must read as a charge, not a hit).
                case VFXType.Cast_MageCharge:
                case VFXType.Cast_NecromancerSummon:
                case VFXType.Cast_EnemyCaster:
                    AbilityVfxKit.SpawnCastWindup(SpellSchool.Arcane, position + Vector3.up * 1.4f, 1.0f);
                    break;

                // IMPACT: flash + shard fan + grounded ring (flat side down).
                case VFXType.Impact_Aether:
                    AbilityVfxKit.SpawnSchoolImpact(SpellSchool.Arcane, position, 1.5f);
                    break;

                case VFXType.Impact_ExplosionAether:
                    AbilityVfxKit.SpawnSchoolImpact(SpellSchool.Arcane, position, 2.5f);
                    break;

                case VFXType.Aura_Necromancer:
                case VFXType.Aura_EnemyCaster:
                case VFXType.Aura_Healer:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Heal, _aetherColor, position, 1f, position);
                    break;

                // ── Flame impacts ─────────────────────────────────────────────
                // Fire-school impact language (ember core + grounded scorch ring).
                case VFXType.Impact_Flame:
                case VFXType.Juice_GroundDecal_Flame:
                    AbilityVfxKit.SpawnSchoolImpact(SpellSchool.Fire, position, 1.6f);
                    break;

                case VFXType.Impact_ExplosionFire:
                case VFXType.Death_Tiefling:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, _flameColor, position, 2f, position);
                    break;

                case VFXType.Aura_Flame:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Cleave, _flameColor, position, 1f, position);
                    break;

                // ── Ice impacts ───────────────────────────────────────────────
                case VFXType.Impact_Ice:
                case VFXType.Cast_FrostNova:
                case VFXType.Death_Wolf:
                case VFXType.Juice_GroundDecal_Ice:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _iceColor, position, 2f, position);
                    break;

                case VFXType.Aura_Ice:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Snare, _iceColor, position, 1f, position);
                    break;

                // ── Healing ───────────────────────────────────────────────────
                // Nature/heal school: the green-gold rising motes (BuildHeal column) —
                // already the intended language; colour comes from the school body.
                case VFXType.Impact_Heal:
                case VFXType.Cast_Heal:
                    // CLI ④ dedup: Juice_LevelUp / Juice_WaveClear removed here — they
                    // are (correctly) handled in the gold "celebration" block below.
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Heal, _healColor, position, 1.5f, position);
                    break;

                // ── Physical ──────────────────────────────────────────────────
                case VFXType.Impact_Physical:
                case VFXType.Death_Skeleton:
                case VFXType.Death_Generic:
                case VFXType.Impact_ShardsBurst:
                    AbilityVfxKit.SpawnAbilityVfxForClass(AbilityEffect.Strike, _physColor, position, 1f,
                        position + rotation * Vector3.forward, "knight");
                    break;

                case VFXType.Impact_ShockwaveRing:
                case VFXType.Cast_KnightSlam:
                    AbilityVfxKit.SpawnAbilityVfxForClass(AbilityEffect.Cleave, _physColor, position, 2f,
                        position, "knight");
                    break;

                // WO-886: Death_Boss NO LONGER shares this case. It is the legacy alias of
                // Boss_Death and the two must be the same effect at every tier - including
                // HERE, in the fallback nobody looks at. They sat at 3f and 4f respectively,
                // which is exactly the drift the work order calls out; a boss that dies
                // through the alias got a visibly smaller death than one that did not.
                case VFXType.Death_Brute:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, _physColor, position, 2.5f, position);
                    break;

                // ── Projectile / ranger ───────────────────────────────────────
                case VFXType.Projectile_Arrow:
                case VFXType.Cast_RangerDraw:
                    AbilityVfxKit.SpawnAbilityVfxForClass(AbilityEffect.Strike, _healColor, position, 1f,
                        position + rotation * Vector3.forward * 3f, "ranger");
                    break;

                case VFXType.Projectile_FlameArrow:
                    AbilityVfxKit.SpawnAbilityVfxForClass(AbilityEffect.Strike, _flameColor, position, 1f,
                        position + rotation * Vector3.forward * 3f, "ranger");
                    break;

                // ── Juice ─────────────────────────────────────────────────────
                case VFXType.Juice_CriticalHit:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Cleave, _goldColor, position, 1f, position);
                    break;

                case VFXType.Juice_KillStreak:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _goldColor, position, 2f, position);
                    break;

                // ── Smoke / wisps / auras ─────────────────────────────────────
                case VFXType.Impact_SmokeWisps:
                case VFXType.Aura_SmokeReaper:
                case VFXType.Aura_Dust:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Snare, _physColor, position, 0.8f, position);
                    break;

                // ── Portal ──────────────────────────────────────────────────────────
                case VFXType.Portal_Enter:
                case VFXType.Portal_Exit:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _aetherColor, position, 2f, position);
                    break;

                // ── Default ───────────────────────────────────────────────────

                // ── WO-62 celebration / combo / pet types ────────────────────
                case VFXType.WaveClear_Celebration:
                case VFXType.Juice_WaveClear:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _goldColor, position, 2.5f, position);
                    break;

                case VFXType.LevelUp_Celebration:
                case VFXType.Juice_LevelUp:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Heal, _goldColor, position, 1.5f, position);
                    break;

                                case VFXType.Combo_Tier1:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Cleave, _goldColor, position, 1f, position);
                    break;

                case VFXType.Combo_Tier2:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _goldColor, position, 2f, position);
                    break;

                case VFXType.Pet_Aura_Fire:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Cleave, _flameColor, position, 1f, position);
                    break;

                case VFXType.Pet_Aura_Ice:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Snare, _iceColor, position, 1f, position);
                    break;

                case VFXType.Pet_Attack:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Strike, _flameColor, position, 1f, position);
                    break;

                // -- Weather / shooting star (WO-52) -----------------------------------------------
                case VFXType.ShootingStar:
                    // Procedural fallback: bright white flash at spawn position.
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, Color.white, position, 0.5f, position);
                    break;

                // -- WO-59: dungeon enemy death -----------------------------------------------------
                // WO-886: sized to sit BETWEEN brute (2.5) and elite (3.2). It used to be
                // 3.5 - bigger than the elite death it is meant to rank below - so even the
                // fallback ladder ran out of order.
                case VFXType.Death_EnemyExplosion_Dungeon:
                    // Larger, darker explosion than the village default.
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, _physColor, position, 2.8f, position);
                    break;

                // -- WO-66: elite / boss VFX -------------------------------------------------------
                case VFXType.Elite_Spawn:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _aetherColor, position, 1.5f, position);
                    break;

                case VFXType.Elite_Death:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, _aetherColor, position, 3.2f, position);
                    break;

                case VFXType.Boss_Spawn:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _aetherColor, position, 3f, position);
                    break;

                // WO-886: the legacy alias rides the SAME case as the value it aliases, so
                // the two cannot be re-tuned apart by a future edit. The catalog does the
                // same thing by pointing both rows at one prefab.
                case VFXType.Boss_Death:
                case VFXType.Death_Boss:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, _physColor, position, 4f, position);
                    break;

                case VFXType.Boss_AttackImpact:
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Cleave, _physColor, position, 3f, position);
                    break;

                // -- WO-66: boss phase transition / telegraph -----------------------------------
                case VFXType.Boss_PhaseTransition:
                    // Enrage burst — a wide flame nova when the boss crosses a threshold.
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, _flameColor, position, 3.5f, position);
                    break;

                case VFXType.Boss_Telegraph:
                    // Wind-up tell — a tight aether gather just before a special attack.
                    AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Strike, _aetherColor, position, 1.5f, position);
                    break;

                default:
                    // WO-560: unmapped types previously ALL spawned an identical aether nova
                    // (visual sameness). Pick a type-appropriate procedural burst by reading
                    // the enum NAME — element keyword -> colour, category keyword -> effect —
                    // so a new/uncased type still reads roughly right (committed-asset-only,
                    // no pack prefab) until it earns an explicit case above.
                    SpawnHeuristicFallback(type, position, rotation);
                    break;
            }
        }

        /// <summary>
        /// WO-560: name-driven procedural fallback for VFXTypes with no explicit case.
        /// Maps the enum name's element keyword to a colour and its category keyword to an
        /// <see cref="AbilityEffect"/> so unmapped types stop looking identical. Pure
        /// procedural (AbilityVfxKit) — never references a (gitignored) pack prefab.
        /// </summary>
        private static void SpawnHeuristicFallback(VFXType type, Vector3 position, Quaternion rotation)
        {
            string n = type.ToString();

            // Element keyword -> colour.
            Color col =
                (n.Contains("Fire") || n.Contains("Flame")) ? _flameColor :
                (n.Contains("Ice")  || n.Contains("Frost")) ? _iceColor :
                (n.Contains("Heal")) ? _healColor :
                (n.Contains("Physical") || n.Contains("Shard") || n.Contains("Shockwave")) ? _physColor :
                (n.Contains("Celebration") || n.Contains("Juice") || n.Contains("Combo") || n.Contains("LevelUp")) ? _goldColor :
                _aetherColor;

            // Category keyword -> effect + scale.
            if (n.Contains("Death") || n.Contains("Explosion") || n.Contains("Boss"))
                AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Meteor, col, position, 2.5f, position);
            else if (n.Contains("Cast") || n.Contains("Telegraph") || n.Contains("Projectile"))
                AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Strike, col, position, 1.5f, position + rotation * Vector3.forward * 2f);
            else if (n.Contains("Aura"))
                AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Heal, col, position, 1f, position);
            else if (n.Contains("Ring") || n.Contains("Shockwave") || n.Contains("Celebration") || n.Contains("Aoe"))
                AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Aoe, col, position, 2f, position);
            else
                AbilityVfxKit.SpawnAbilityVfx(AbilityEffect.Strike, col, position, 1.5f, position);
        }

        /// <summary>
        /// Procedural fallback for loop effects -- spawns a simple looping procedural
        /// particle child and returns the host GameObject for the VFXHandle to own.
        /// </summary>
        private GameObject ProceduralLoopFallback(VFXType type, Vector3 position, Transform parent)
        {
            // Build a minimal procedural loop (heal-style upward particles) as placeholder.
            Color col = type switch
            {
                VFXType.Aura_Flame          => _flameColor,
                VFXType.Aura_Ice            => _iceColor,
                VFXType.Aura_Healer         => _healColor,
                VFXType.Env_TorchFlame      => _flameColor,
                // WO-66 boss phase auras — calm -> enraged -> seething.
                VFXType.Boss_Aura_Phase1    => _aetherColor,
                VFXType.Boss_Aura_Phase2    => _goldColor,
                VFXType.Boss_Aura_Phase3    => _flameColor,
                _                           => _aetherColor,
            };

            var host = new GameObject($"[ProceduralLoop_{type}]");
            host.transform.position = position;
            if (parent != null) host.transform.SetParent(parent, true);

            var ps = host.AddComponent<ParticleSystem>();

            // MAGENTA FIX (founding-Echo aura + Heart of Elarion aura, both route here):
            // a runtime AddComponent<ParticleSystem> leaves its ParticleSystemRenderer on
            // Unity's BUILT-IN default particle material (a LEGACY built-in particle shader).
            // URP has no subshader for that, so it is drawn with Hidden/InternalErrorShader =
            // opaque MAGENTA billboard quads -- the broken pink squares trailing the pale Echo.
            // This is the same class as the runtime-equipped weapon: MagentaGuard's scene-load
            // sweep already ran, and this loop is spawned AT RUNTIME (after PlayAura), so it is
            // never caught. Assign a URP-valid ADDITIVE particle material so the aura reads as a
            // soft glow -- in the editor AND in a built player: AbilityVfxKit.ResolveParticleShader
            // (WO-420) is build-strip-safe and, critically, never falls through to the magenta
            // default silently (it widens the fallback chain and FlowTrace.Warns on a miss).
            var psr = host.GetComponent<ParticleSystemRenderer>();
            if (psr != null)
            {
                var glowShader = AbilityVfxKit.ResolveParticleShader();
                if (glowShader != null)
                {
                    var glowMat = new Material(glowShader) { name = $"EchoAura_{type}_URP" };
                    AbilityVfxKit.ConfigureUrpParticleTransparency(glowMat, additive: true);
                    // SQUARE-BILLBOARD FIX: a bare additive material with NO texture draws each
                    // particle as a hard WHITE/tinted SQUARE quad (the reported "ugly white squares"),
                    // not a glow. Feed the shared soft round glow sprite so the fallback reads as a
                    // soft radial glow. Mirror ApplyParticleMaterial's _BaseMap handling (URP
                    // Particles/Unlit samples _BaseMap and has no _MainTex -> writing the alias spams
                    // the console). Primary fix is the Hovl bridge above; this only runs if that misses.
                    var softDot = AbilityVfxKit.SoftDotTexture;
                    if (softDot != null)
                    {
                        if (glowMat.HasProperty("_BaseMap")) glowMat.SetTexture("_BaseMap", softDot);
                        else glowMat.mainTexture = softDot;
                    }
                    psr.material = glowMat;
                }
                else
                {
                    FlowTrace.Warn("VFXManager",
                        $"ProceduralLoopFallback('{type}'): no URP particle shader resolved -- " +
                        "renderer left unassigned (skipped) rather than rendered magenta.");
                }
            }

            var m  = ps.main;
            m.loop           = true;
            m.duration       = 1f;
            m.startLifetime  = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            m.startSpeed     = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            m.startSize      = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            m.startColor     = new ParticleSystem.MinMaxGradient(col);
            m.gravityModifier = -0.1f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 8f;
            var sh = ps.shape;   sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            ps.Play();

            // WO-1057: registering IS the increment. The procedural fallback holds a real loop
            // slot exactly like an authored prefab does, so it must be named in the dump too.
            RegisterLoop(_loopObjects, host, type, null, parent);
            return host;
        }
    }

    // =========================================================================
    //  WO-1473 — THE LOOP RELEASE DECISION, as pure arithmetic
    // =========================================================================
    //
    // Deliberately split out of VFXManager for the same reason VfxLoopBudget.WouldRefuseLoop is:
    // a decision buried inside a MonoBehaviour tick can only be proven by playing the game, so it
    // never gets an oracle and the next seat re-derives it from the symptom. This class owns no
    // state, touches no Unity object and starts nothing — so VfxLoopFlagRegression can assert every
    // branch of it headless, in the editor, in microseconds.
    //
    // The manager's job is to MEASURE (is the owner gone, is the host in the frustum, how long has
    // the streak run, is a slot free) and then to ACT. Choosing is this class's job, and there is
    // exactly one copy of the choice.
    public static class VfxLoopReleasePolicy
    {
        /// <summary>What the pool should do with one live loop this tick.</summary>
        public enum LoopAction
        {
            /// <summary>Leave it exactly as it is.</summary>
            Keep,
            /// <summary>Release its slot: stop-and-clear the particles, stop counting it, keep the
            /// host checked out so the owner's VFXHandle stays valid.</summary>
            Suspend,
            /// <summary>Put a suspended loop back on screen, on the same host.</summary>
            Resume,
        }

        /// <summary>
        /// THE decision. Pure: same inputs, same answer, no Unity, no clock.
        /// <para/>Order matters and is the specification:
        /// <list type="number">
        /// <item>an accessibility loop is never suspended and is always resumed (WO-1229 —
        /// the colourblind low-HP tell is the one loop a player reads state from);</item>
        /// <item>a loop whose owner is DESTROYED or DISABLED releases immediately — no grace,
        /// because there is nothing left on screen for it to belong to;</item>
        /// <item>otherwise a loop releases only after it has been off camera for the whole
        /// grace, and comes back the moment it is visible again AND a slot is free;</item>
        /// <item>with no camera to judge by (headless, boot, a scene between cameras) nothing
        /// is ever suspended for visibility — an unjudgeable loop is kept, never guessed at.</item>
        /// </list>
        /// </summary>
        /// <param name="suspended">Is this loop currently suspended (holding no slot)?</param>
        /// <param name="exempt">Is it on VfxLoopBudget.AccessibilityLoops?</param>
        /// <param name="ownerDestroyed">Had an owner, and that owner is now destroyed.</param>
        /// <param name="ownerActive">Owner is active in the hierarchy (true when it never had one).</param>
        /// <param name="cameraKnown">Was a camera available to judge visibility?</param>
        /// <param name="visible">Host is inside the camera frustum (meaningless when !cameraKnown).</param>
        /// <param name="offscreenFor">Seconds the off-camera streak has run (0 when on camera).</param>
        /// <param name="grace">Seconds off camera before a release (VFXManager's OFFSCREEN_RELEASE_GRACE).</param>
        /// <param name="slotAvailable">Is there loop-budget headroom for a resume right now?</param>
        public static LoopAction Decide(bool suspended, bool exempt,
                                        bool ownerDestroyed, bool ownerActive,
                                        bool cameraKnown, bool visible,
                                        float offscreenFor, float grace,
                                        bool slotAvailable)
        {
            // 1. Accessibility: unsuspendable, and it jumps the resume queue (it is why
            //    AccessibilityReserve exists — a slot is held open for exactly this).
            if (exempt) return suspended ? LoopAction.Resume : LoopAction.Keep;

            // 2. Nothing to belong to. A destroyed owner can never re-enable, so this is the
            //    permanent case; a disabled one can, and Resume below picks it back up.
            bool orphanedOrHidden = ownerDestroyed || !ownerActive;
            if (orphanedOrHidden) return suspended ? LoopAction.Keep : LoopAction.Suspend;

            // 3. Unjudgeable visibility is never a reason to release. (Headless AutoPilot runs
            //    have no Camera.main, and a policy that suspended everything there would silently
            //    change what every headless capture measures.)
            if (!cameraKnown) return suspended && slotAvailable ? LoopAction.Resume : LoopAction.Keep;

            // 4. Back on camera -> resume, budget permitting. Held at Keep when the pool is full,
            //    so a town of returning spires cannot re-starve combat feedback the moment the
            //    player turns around; the next tick with headroom picks it up.
            if (visible) return suspended ? (slotAvailable ? LoopAction.Resume : LoopAction.Keep)
                                          : LoopAction.Keep;

            // 5. Off camera: release only after the FULL grace. The grace is the hysteresis —
            //    a loop clipping the edge of the frustum must not flicker its slot every tick.
            if (!suspended && offscreenFor >= grace) return LoopAction.Suspend;
            return LoopAction.Keep;
        }
    }
}

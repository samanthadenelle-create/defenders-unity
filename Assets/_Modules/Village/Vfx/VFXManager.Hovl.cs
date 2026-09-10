// =============================================================================
// VFXManager.Hovl — string-key Hovl-prefab VFX path (WO-VFX-002).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village   (partial of VFXManager)
//
// WHY A PARTIAL, NOT A SECOND SYSTEM:
//   The existing VFXManager already owns the singleton, the DontDestroyOnLoad
//   _poolRoot, the oneshot/loop caps, the FlowTrace instrumentation, and the
//   ParticleSystem play/stop/duration helpers. WO-VFX-002 adds a *second key
//   space* (arbitrary string keys -> Hovl Studio prefabs) that reuses ALL of that.
//   So this is a partial of the SAME class, not a duplicate manager. The VFXType
//   enum path (VFXManager.cs) is untouched and keeps working exactly as before.
//
// WHAT THIS ADDS:
//   • PlayKey("Fireball_Projectile", pos, parent, color, scale, lifetime, follow)
//     — spawn any Hovl prefab by string key, routed through the shared pool.
//   • Object pooling keyed by string (mobile-critical): instantiate once, return
//     on lifetime end — no per-call Instantiate. Reuses _poolRoot + the caps.
//   • Override knobs: world-space position + rotation, PARENT to a transform,
//     HDR COLOR (recolour via ParticleSystem StartColor across all children —
//     the Hovl HS_Blend_CG effects tint at runtime), SCALE, LIFETIME, and a
//     FOLLOW target (HovlVfxFollower keeps the effect on a moving transform).
//   • Designer-friendly registration WITHOUT code: HovlVfxCatalog ScriptableObject
//     (Resources/VFX/HovlVfxCatalog.asset) holds { Key, Prefab, PoolSize,
//     DefaultScale, DefaultLifetime, Recolorable, IsLoop } rows. Authored in-editor
//     (Hovl prefabs are NOT under Resources/, so the catalog holds serialized prefab
//     refs; the .asset itself lives in Resources/ and is the only new Resources item).
//
// AUTHORING (copy-paste, 8-10 example rows) — exact Hovl paths from the shortlist
// in Docs/VFX/HovlStudio_Inventory.md §5. Run Defenders/VFX/Generate Hovl VFX Catalog
// (HovlVfxCatalogGenerator) to author the .asset from this table, or wire by hand:
//
//   KEY                    PREFAB (under Assets/Hovl Studio/)                                              LOOP  SCALE
//   Fireball_Projectile    AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 16 fire.prefab      Y    1
//   Fireball_Cast          AAA Projectiles Vol 1/Prefabs/Flash and hits/Flash 16 fire.prefab                N    1
//   Fireball_Impact        AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 16 fire.prefab                  N    1
//   Thunderbolt_Projectile AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 2 electro.prefab    Y    1
//   Thunderbolt_Impact     AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 2 electro.prefab                N    1
//   Arcane_Projectile      AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 17 nova violet.prefab Y  1
//   Arcane_Cast            AAA Projectiles Vol 1/Prefabs/Flash and hits/Flash 17 nova violet.prefab          N    1
//   Arcane_Impact          AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 17 nova violet.prefab            N    1
//   Frost_Projectile       AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 26 blue diamond.prefab Y 1
//   Frost_Impact           AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 26 blue crystal.prefab           N    1
//   Collector_Full         RPG VFX Bundle/Random effect prefabs/Gold dot.prefab                             Y    1
//   Raid_Explosion         AOE Magic spells Vol.1/Prefabs/Meteor hit.prefab                                 N    1.5
//   LevelUp_Burst          RPG VFX Bundle/Random effect prefabs/Lvl up.prefab                               N    1
//
//   — WO-VFX-003 Knight skill-tree actives (13 new keys; map = Docs/VFX/SkillTree_VFX_Mapping.md) —
//   Thunderbolt_Cast       AAA Projectiles Vol 1/Prefabs/Flash and hits/Flash 2 electro.prefab              N    1
//   Spear_Projectile       AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 11 orange arrow.prefab Y  1
//   Spear_Impact           AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 11 orange arrow.prefab          N    1
//   Melee_Slash            AOE Magic spells Vol.1/Prefabs/Flower slash.prefab                               N    1
//   Melee_Impact           RPG VFX Bundle/Random effect prefabs/Punch Hit.prefab                            N    1
//   Cleave_Impact          AOE Magic spells Vol.1/Prefabs/Energy explosion.prefab                           N    1.3
//   Heal_Cast              Magic circles/Prefabs/Magic circle sun.prefab                                    N    1
//   Heal_Aura              RPG VFX Bundle/Random effect prefabs/Buff heal.prefab                            Y    1  (loop)
//   Taunt_Roar             AOE Magic spells Vol.1/Prefabs/Energy explosion.prefab                           N    1
//   Taunt_Aura             Magic circles/Prefabs/Loop version/Magic circle blood loop.prefab               Y    1  (loop)
//   Aegis_Cast             Magic circles/Prefabs/Magic shield holy.prefab                                   N    1
//   Aegis_Shield           Magic circles/Prefabs/Loop version/Magic shield holy loop.prefab                Y    1  (loop)
//   Ember_Burn             RPG VFX Bundle/Random effect prefabs/Debuff 1.prefab                             Y    1  (loop)
//   Dash_Blink             RPG VFX Bundle/Random effect prefabs/Buff white twist.prefab                     N    1
//
// USAGE:
//   // oneshot cast flash on the hero's hand, tinted blue, 1.3x scale:
//   VFXManager.PlayKey("Fireball_Cast", hand.position, hand.rotation, hand, new Color(0.4f,0.6f,1f,1f), 1.3f);
//   // projectile trail that follows a moving target transform, returns a handle:
//   var h = VFXManager.PlayKey("Fireball_Projectile", spawn, default, null, null, 0f, 0f, targetTf);
//   h?.Stop();                                  // call on impact
//   // collector FULL glow (loop) — keep the handle:
//   var glow = VFXManager.PlayKey("Collector_Full", stackTop);
//   glow?.Stop();                               // when the collector is emptied
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    public sealed partial class VFXManager
    {
        // ── Hovl catalog + pools (string-keyed, parallel to the VFXType path) ────

        [Header("Hovl VFX (WO-VFX-002)")]
        [Tooltip("String-key catalog of Hovl Studio prefabs. Auto-loaded from " +
                 "Resources/VFX/HovlVfxCatalog when null. Author via " +
                 "Defenders/VFX/Generate Hovl VFX Catalog.")]
        [SerializeField] private HovlVfxCatalog _hovlCatalog;

        [Tooltip("DIAGNOSTIC / ROLLBACK ONLY. ON = pre-warm EVERY catalog key's pool at boot, " +
                 "the pre-WO-1113 behaviour. Leave OFF: warming is demand-driven (a key's pool " +
                 "is built the first time that key is actually played).")]
        [SerializeField] private bool _eagerWarmAllVfxKeys = false;

        // WO-1113 (mobile perf): keys whose pool has already been demand-warmed, so the
        // one-time warm happens exactly once per key even for keys that never fill up.
        private readonly HashSet<string> _hovlWarmedKeys = new HashSet<string>();

        // Instrumentation for the demand-warm (§12): how many pooled bodies this session
        // actually cost us, and across how many keys. Reported in the warm trace so a
        // capture can compare against the catalog's full pre-warm bill.
        private int _hovlWarmedInstances;

        // Per-key queue of dormant instances ready to reuse (mirrors _pools).
        private readonly Dictionary<string, Queue<GameObject>> _hovlPools
            = new Dictionary<string, Queue<GameObject>>();

        // Which key a live pooled object belongs to (so a VFXHandle can return it).
        private readonly Dictionary<GameObject, string> _hovlKeyOf
            = new Dictionary<GameObject, string>();

        // Loop objects, so the shared _activeLoops count covers the right bucket on
        // return (mirrors _loopObjects for the VFXType path).
        // WO-1057: keyed registry, not a bare set — the value names the loop (key, owner, start
        // time, position) so an F8 capture can print WHICH loops are live, not just how many. The
        // shared count is DERIVED from Count on both registries; nothing increments an int.
        private readonly Dictionary<GameObject, LoopRecord> _hovlLoopObjects
            = new Dictionary<GameObject, LoopRecord>();

        // WO-VFX #2 (hue-shift tint): the AUTHORED startColor of each child ParticleSystem,
        // cached the first time that PS is recolored. Every pooled reuse then hue-shifts from
        // the ORIGINAL saturation / value (brightness) / alpha instead of a previously written
        // (already hue-shifted) value - so an HDR round-trip can't drag the S/V off the authored
        // bright core across reuses, and a later acquire with a DIFFERENT hue still starts clean.
        // Keyed by the PS; pooled instances live for the app lifetime, so the entry stays valid.
        private readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> _hovlAuthoredStartColor
            = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();

        // ── VFXType -> Hovl string-key bridge (aura wiring) ──────────────────────
        // A few VFXType LOOPS have no VFXType-catalog (VFXCatalog.asset) prefab but a
        // curated Hovl loop wired by string key in the HovlVfxCatalog. PlayLoop consults
        // this map BEFORE the procedural fallback, so the real pooled Hovl glow plays
        // instead of the textureless additive billboard SQUARES the procedural system
        // draws. Aura_HeartPulse is HeartAuraController's (the Heart-of-Elarion tree
        // nucleus) -- plus ArcaneAura's combat-spire key. THE ROW STAYS.
        // WO-993 (2026-08-16): the SECOND consumer named here, EchoSpiritPresentation (the
        // founding-Echo floating spirit), is RETIRED -- the guide is a grounded wolf that
        // walks, not a hovering glow. Only the ECHO's use of this key went; the Heart's did
        // NOT. Do not delete this bridge row on the strength of that retirement: removing it
        // sends the Heart's aura back to the textureless procedural billboard SQUARES this
        // bridge exists to fix.
        // Add a row here (+ the matching key in HovlVfxCatalogGenerator.Map + a catalog
        // regen) to route any other unwired VFXType loop to a Hovl prefab.
        private static readonly Dictionary<VFXType, string> _hovlKeyForType
            = new Dictionary<VFXType, string>
            {
                { VFXType.Aura_HeartPulse, "Aura_HeartPulse" },
            };

        /// <summary>True + the Hovl catalog key when <paramref name="type"/> should
        /// resolve through the string-keyed Hovl path instead of the VFXType pool.</summary>
        private static bool TryGetHovlKeyForType(VFXType type, out string key)
            => _hovlKeyForType.TryGetValue(type, out key);

        // ── Catalog load / pool pre-warm ─────────────────────────────────────────

        private void EnsureHovlCatalog()
        {
            if (_hovlCatalog != null) return;
            // Addressables-first / Resources-fallback seam (VfxAssetLoader). The key is the
            // FULL Resources-relative path, used verbatim as BOTH the Addressable address and
            // the Resources.Load key — see VfxAssetLoader's KEY CONVENTION header.
            _hovlCatalog = DeNelle.Core.VfxAssetLoader.LoadVfxAsset<HovlVfxCatalog>("VFX/HovlVfxCatalog");
            if (_hovlCatalog == null)
                FlowTrace.Warn("VFXManager",
                    "EnsureHovlCatalog: no _hovlCatalog assigned and 'VFX/HovlVfxCatalog' resolved via NEITHER " +
                    "Addressables NOR Resources (VfxAssetLoader tried both) — PlayKey('...') calls will no-op " +
                    "until the catalog is authored (Defenders/VFX/Generate Hovl VFX Catalog); if the VFX content " +
                    "has been migrated out of Resources, confirm DeNelle.Editor.VfxAddressablesGrouper marked it.");
            else
                FlowTrace.Step("VFXManager",
                    $"EnsureHovlCatalog: loaded HovlVfxCatalog via VfxAssetLoader key 'VFX/HovlVfxCatalog' ({_hovlCatalog.Rows?.Length ?? 0} rows).");
        }

        /// <summary>
        /// WO-1113 (mobile perf, Seeker): the pre-warm is now DEMAND-DRIVEN. This method only
        /// builds the key lookup; NOT ONE GameObject is instantiated at boot.
        ///
        /// THE DEFECT it closes: this used to instantiate <c>PoolSize</c> bodies for EVERY row in
        /// the catalog at Awake — 887 pooled GameObjects for the 152 baked rows. The VFX catalog
        /// audit (docs/reference/VFX_CATALOG.md, 2026-08-16) found 76 of those keys have NO
        /// consumer anywhere in the tree, 45 of them the PP_* palette, so roughly a third of that
        /// boot cost was memory + boot time spent on effects nothing in the game can ever play.
        /// On a phone that is real RAM and real time before the first frame.
        ///
        /// It is a WARM change, NOT a content change: no key is removed, no prefab is touched, no
        /// row is edited (an untagged key today may be owner-tagged tomorrow — deleting art is
        /// never a CLI decision). A key that IS played warms its authored PoolSize on its FIRST
        /// play, so from the second play onward the pool behaves exactly as before.
        /// Set <see cref="_eagerWarmAllVfxKeys"/> to restore the old boot-warm for A/B.
        /// </summary>
        private void InitialiseHovlPools()
        {
            if (_hovlCatalog == null) return;
            _hovlCatalog.BuildLookup();

            if (!_eagerWarmAllVfxKeys)
            {
                int rows = 0, wouldWarm = 0;
                var all = _hovlCatalog.Rows;
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var r = all[i];
                        if (string.IsNullOrEmpty(r.Key) || r.Prefab == null || r.PoolSize <= 0) continue;
                        rows++;
                        wouldWarm += r.PoolSize;
                    }
                }
                FlowTrace.Step("VFXManager",
                    $"InitialiseHovlPools: DEMAND-WARM (WO-1113) — 0 instances built at boot " +
                    $"(eager warm would have built {wouldWarm} across {rows} keys). Each key warms " +
                    "its PoolSize on its first play; unplayed keys cost nothing.");
                return;
            }

            int warmed = 0;
            foreach (var row in _hovlCatalog.Rows)
            {
                if (string.IsNullOrEmpty(row.Key) || row.Prefab == null || row.PoolSize <= 0) continue;
                if (!_hovlPools.ContainsKey(row.Key))
                    _hovlPools[row.Key] = new Queue<GameObject>();

                for (int i = 0; i < row.PoolSize; i++)
                {
                    var go = CreateHovlInstance(row.Prefab, row.Key);
                    if (go != null) { _hovlPools[row.Key].Enqueue(go); warmed++; }
                }
                _hovlWarmedKeys.Add(row.Key);
            }
            _hovlWarmedInstances = warmed;
            FlowTrace.Warn("VFXManager",
                $"InitialiseHovlPools: EAGER warm is ON (_eagerWarmAllVfxKeys) — built {warmed} pooled " +
                "instances at boot, including keys with no consumer. This is the diagnostic/rollback " +
                "path; the shipping default is demand-warm.");
        }

        /// <summary>
        /// WO-1113: builds a key's pool the FIRST time that key is actually played, so the
        /// authored PoolSize depth still exists for every consumed key — it is just paid for on
        /// use instead of at boot, and never paid at all for a key with no consumer.
        /// Guarded (§12): a bad row degrades to acquire-on-demand, it never throws into the
        /// caller's play path.
        /// </summary>
        private void EnsureHovlKeyWarm(string key, in HovlVfxCatalog.Row row)
        {
            if (string.IsNullOrEmpty(key) || row.Prefab == null || row.PoolSize <= 0) return;
            if (!_hovlWarmedKeys.Add(key)) return;   // already warmed (or eager-warmed) — one time only

            GameObject prefab = row.Prefab;
            int size = row.PoolSize;
            Guard.Try("VFXManager", $"demand-warm hovl key '{key}'", () =>
            {
                if (!_hovlPools.TryGetValue(key, out var q))
                {
                    q = new Queue<GameObject>();
                    _hovlPools[key] = q;
                }
                int built = 0;
                for (int i = q.Count; i < size; i++)
                {
                    var go = CreateHovlInstance(prefab, key);
                    if (go == null) break;
                    q.Enqueue(go);
                    built++;
                }
                _hovlWarmedInstances += built;
                FlowTrace.Step("VFXManager",
                    $"demand-warm '{key}': built {built} pooled instance(s) on first play " +
                    $"(session total {_hovlWarmedInstances} across {_hovlWarmedKeys.Count} key(s)).");
            });
        }

        /// <summary>
        /// WO-1348: the Command Center re-pointed one or more VFX keys, and the new picks are
        /// standing as of this town load. Drop the IDLE pooled bodies for exactly those keys so
        /// the next play instantiates from the NEW prefab.
        /// <para>
        /// ⛔ THIS IS NOT LIVE HOT-SWAPPING, which the work order forbids by name: nothing that is
        /// currently PLAYING is touched, re-parented or stopped. Only bodies sitting idle in the
        /// free queue are destroyed, and only for keys whose answer actually changed.
        /// </para>
        /// <para>
        /// Without it the change would silently not take: this manager is DontDestroyOnLoad and
        /// its pools are keyed by VFX key, so a key played before the re-pick would keep handing
        /// out bodies built from the OLD prefab for the rest of the session - the exact
        /// "I picked it and nothing happened" the owner cannot debug from a phone.
        /// </para>
        /// Guarded (§12): a flush failure degrades to "the old bodies stay pooled", never a throw
        /// into a scene-load path.
        /// </summary>
        private void OnVfxPickSnapshotChanged(IReadOnlyList<string> changedKeys)
        {
            if (changedKeys == null || changedKeys.Count == 0) return;

            Guard.Try("VFXManager", "flush idle hovl pools for re-picked VFX keys", () =>
            {
                int destroyed = 0, keysTouched = 0;
                for (int i = 0; i < changedKeys.Count; i++)
                {
                    string key = changedKeys[i];
                    if (string.IsNullOrEmpty(key)) continue;

                    _hovlWarmedKeys.Remove(key);   // let the next play re-warm from the NEW prefab
                    if (!_hovlPools.TryGetValue(key, out var q) || q == null) continue;

                    keysTouched++;
                    while (q.Count > 0)
                    {
                        var go = q.Dequeue();
                        if (go == null) continue;
                        _hovlKeyOf.Remove(go);
                        Destroy(go);
                        destroyed++;
                    }
                }

                _hovlWarmedInstances -= destroyed;
                if (_hovlWarmedInstances < 0) _hovlWarmedInstances = 0;

                FlowTrace.Warn("VFXManager",
                    $"VFX PICK FLUSH: {changedKeys.Count} key(s) were re-pointed from the Command Center " +
                    $"({string.Join(",", changedKeys)}); destroyed {destroyed} IDLE pooled body(ies) across " +
                    $"{keysTouched} warmed key(s) so the next play builds from the NEW prefab. Nothing " +
                    "currently playing was touched - this is a pool flush, not a hot-swap.");
            });
        }

        /// <summary>Pooled Hovl instances built so far this session (WO-1113 regression hook).
        /// 0 immediately after boot when the warm is demand-driven.</summary>
        public int HovlWarmedInstanceCount => _hovlWarmedInstances;

        /// <summary>Distinct Hovl keys warmed so far this session (WO-1113 regression hook).</summary>
        public int HovlWarmedKeyCount => _hovlWarmedKeys.Count;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// WO-1305: can this key actually PLAY? True only when the catalog is loaded, carries a
        /// row for <paramref name="key"/>, and that row has a non-null Prefab — i.e. the same
        /// three conditions <see cref="PlayKeyInternal"/> checks before it spawns anything.
        /// <para>READ-ONLY: it resolves nothing, warms nothing and spawns nothing — there is
        /// still exactly ONE spawn owner (<see cref="PlayKey"/>). It exists so a caller that
        /// changes its BEHAVIOUR based on a key (the marquee projectile suppression in
        /// HeroAbilities) can refuse to do so when that key could never have drawn: suppressing
        /// a projectile for an effect that then fails to resolve is an invisible spell, and a
        /// throttled miss-log inside PlayKey cannot undo a decision already taken (§12).</para>
        /// </summary>
        public static bool CanPlayKey(string key) => Instance != null && Instance.CanPlayKeyInternal(key);

        private bool CanPlayKeyInternal(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureHovlCatalog();
            // Row is a struct (HovlVfxCatalog.Row) — the only nullable half is its Prefab.
            return _hovlCatalog != null && _hovlCatalog.TryGet(key, out var row) && row.Prefab != null;
        }

        /// <summary>
        /// Spawn a Hovl prefab by string key, routed through the shared pool. Null-safe —
        /// no-ops (returns null) if VFXManager or the catalog is not ready, or the key is
        /// unknown. Returns a <see cref="VFXHandle"/> for LOOP effects (call Stop() when
        /// done); oneshots auto-return and yield null.
        /// </summary>
        /// <param name="key">Catalog key, e.g. "Fireball_Projectile" / "Collector_Full".</param>
        /// <param name="position">World-space spawn position.</param>
        /// <param name="rotation">World rotation (default = identity).</param>
        /// <param name="parent">Optional transform to parent to (effect moves with it).</param>
        /// <param name="color">Optional HDR tint — applied as ParticleSystem StartColor across
        /// all children when the row is Recolorable.</param>
        /// <param name="scale">Uniform scale override (&gt;0). 0 = row DefaultScale, else 1.</param>
        /// <param name="lifetime">Lifetime override in seconds (&gt;0). 0 = row DefaultLifetime,
        /// else auto-detected from the particle systems. Ignored for loops.</param>
        /// <param name="follow">Optional target the effect keeps its position on each frame
        /// (a small mover, for projectiles/trails on a moving transform).</param>
        public static VFXHandle PlayKey(string key, Vector3 position,
                                        Quaternion rotation = default, Transform parent = null,
                                        Color? color = null, float scale = 0f, float lifetime = 0f,
                                        Transform follow = null)
            => Instance?.PlayKeyInternal(key, position, rotation, parent, color, scale, lifetime, follow);

        // ── Core spawn ──────────────────────────────────────────────────────────

        private VFXHandle PlayKeyInternal(string key, Vector3 position, Quaternion rotation,
                                          Transform parent, Color? color, float scale, float lifetime,
                                          Transform follow)
        {
            if (string.IsNullOrEmpty(key)) return null;

            EnsureHovlCatalog();
            if (_hovlCatalog == null || !_hovlCatalog.TryGet(key, out var row))
            {
                FlowTrace.Throttle("VFXManager", $"hovl-nokey:{key}", 2f,
                    $"PlayKey('{key}'): no HovlVfxCatalog row for this key — nothing spawned. " +
                    "Add a row (Defenders/VFX/Generate Hovl VFX Catalog) or check the key spelling.");
                return null;
            }
            if (row.Prefab == null)
            {
                FlowTrace.Throttle("VFXManager", $"hovl-noprefab:{key}", 2f,
                    $"PlayKey('{key}'): catalog row has a null Prefab (pack not imported?) — nothing spawned.");
                return null;
            }

            // Success trace (owner F8 class "which VFX drew THAT?" — e.g. rocks on a basic
            // swing, 2026-08-02): every successful play names key -> prefab, throttled per
            // key so hot loops stay ~1/sec. Failures were already loud above; successes
            // were invisible, which made mis-tagged keys undiagnosable from a log.
            FlowTrace.Throttle("VFXManager", $"hovl-play:{key}", 1f,
                $"PlayKey('{key}') -> prefab '{row.Prefab.name}'");

            // Shared caps with the VFXType path (mobile budget). Loop vs oneshot bucket.
            if (row.IsLoop)
            {
                if (_activeLoops >= _maxActiveLoops)
                {
                    FlowTrace.Throttle("VFXManager", "hovl-loop-cap", 1f,
                        $"PlayKey('{key}') SKIPPED — active loops {_activeLoops}/{_maxActiveLoops} (cap hit).");
                    return null;
                }
            }
            else
            {
                // Leak-proof: derived + prune-on-read count, shared with the VFXType path
                // (VFXManager.ActiveOneshotCount). A missed return can no longer pin it at cap.
                int activeOneshots = ActiveOneshotCount();
                if (activeOneshots >= _maxActiveOneshots)
                {
                    FlowTrace.Throttle("VFXManager", "hovl-oneshot-cap", 1f,
                        $"PlayKey('{key}') SKIPPED — active oneshots {activeOneshots}/{_maxActiveOneshots} (cap hit).");
                    return null;
                }
            }

            // WO-1113: this key has a real consumer (we are in its play path RIGHT NOW), so it
            // earns its pool depth here — once — instead of at boot alongside 76 keys that never
            // play. Cheap no-op on every play after the first.
            EnsureHovlKeyWarm(key, row);

            var go = AcquireHovl(key, row);
            if (go == null)
            {
                FlowTrace.Warn("VFXManager", $"PlayKey('{key}'): Acquire returned null — nothing spawned.");
                return null;
            }

            // Position / rotation / scale.
            go.transform.SetParent(null, false);
            go.transform.position = position;
            bool rotValid = !(rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f);
            go.transform.rotation = rotValid ? rotation : Quaternion.identity;

            float s = scale > 0f ? scale
                    : row.DefaultScale > 0f ? row.DefaultScale
                    : 1f;
            go.transform.localScale = Vector3.one * s;

            if (parent != null) go.transform.SetParent(parent, true);
            go.SetActive(true);

            // HDR colour override — recolour the Hovl effect via ParticleSystem StartColor.
            if (color.HasValue && row.Recolorable)
                ApplyStartColor(go, color.Value);

            VerifyHovlHasParticles(go, key);
            // A row that is NOT a true endless loop must not emit like one. Clear main.loop
            // BEFORE the systems start, so a pack prefab authored as a continuous effect under an
            // isLoop:false row cannot burn for its whole pooled lifetime (owner F8 seq 4644 - the
            // VFXType twin of this defect put a 10.3s fire on the caster after every Fireball).
            // A genuine IsLoop row keeps looping: it is excluded below.
            if (!row.IsLoop) EnforceOneshotEmission(go, "hovl:" + key);
            // WO-1327 — THIS is the path the owner's fire spell takes. `firespell_Cast` resolves
            // to Spell_Fire_9, whose Fireballs emitter is authored perfectly elastic (bounce 1.0,
            // dampen 0, minKillSpeed 0) against all 32 layers, and whose two enabled LightsModules
            // drive 25 concurrent real-time point lights per cast. Both numbers live in a
            // GITIGNORED pack prefab, so they are clamped here instead. Unconditional: neither
            // defect has anything to do with whether the row loops.
            NormalizeSpawnedHost(go, "hovl:" + key);
            PlayAllParticles(go);

            // Follow a moving transform (projectile/trail) without parenting.
            if (follow != null)
            {
                var f = go.GetComponent<HovlVfxFollower>();
                if (f == null) f = go.AddComponent<HovlVfxFollower>();
                f.Begin(follow);
            }

            // A LOOP WITH A DECLARED FINITE LIFETIME IS A TIMED EFFECT, NOT AN ENDLESS ONE.
            //
            // WHY THIS EXISTS (2026-08-05): the loop branch below does a bare _activeLoops++
            // and hands back a handle with NO deadline. The ONLY thing that ever releases that
            // slot is the caller calling VFXHandle.Stop() - PruneDestroyedFromSet frees
            // destroyed hosts, and pooled objects are never destroyed. So a loop row played
            // fire-and-forget (return value discarded) leaks one of the 20 global loop slots
            // PERMANENTLY, for the whole session. Six captured F8 sessions show that cap
            // saturated, starving tower projectiles, the Tree of Life aura and POI markers.
            //
            // Deriving IsLoop from the prefab fixed the rows that were never loops. But it
            // also correctly promoted some genuinely-continuous prefabs TO loops - and at
            // least one of those (UpgradeStructureComplete_Aura, the upgrade fireworks) is
            // played fire-and-forget. Truthful flag, same leak. A celebration is FINITE; the
            // catalog just had no way to say so.
            //
            // So: if a finite lifetime is declared - by the caller or by the row - route the
            // effect through the oneshot path, which is already leak-proof (RegisterOneshot +
            // deadline + SweepOneshots reclaim). No handle is surfaced, exactly as the oneshot
            // path does, because a caller that also Stop()'d it would double-return.
            // A row with NO lifetime is still a true endless loop: it keeps its handle and its
            // caller still owns stopping it. At the time of writing all 44 loop rows declare
            // no lifetime, so this changes nothing today - it is the guard that stops the next
            // fire-and-forget loop from silently re-opening the same P0.
            bool loopIsTimed = row.IsLoop && (lifetime > 0f || row.DefaultLifetime > 0f);

            if (row.IsLoop && !loopIsTimed)
            {
                // WO-1057: registering IS the increment. `key` is the OWNER-AUTHORED catalog key
                // and it is stored + printed VERBATIM — never resolved, prettified or substituted.
                RegisterLoop(_hovlLoopObjects, go, VFXType.None, key, parent);
                return new VFXHandle(go, key);
            }

            float life = lifetime > 0f ? lifetime
                       : row.DefaultLifetime > 0f ? row.DefaultLifetime
                       : DetectDuration(go) + 0.3f;
            // §12: name the resolved world position of a Hovl oneshot + how long it holds it.
            FlowTrace.Throttle("VFXManager", $"hovl-at:{key}", 1f,
                $"PlayKey('{key}') oneshot at {position} parent=" +
                $"'{(parent != null ? parent.name : "<none, world-space>")}' follow=" +
                $"'{(follow != null ? follow.name : "<none, static>")}' lifetime={life:0.00}s.");
            // Leak-proof: register the checked-out Hovl oneshot in the shared live set + a deadline
            // (instead of a raw ++), so a host destroyed on enemy-death / scene-change before
            // ReturnHovlToPool runs cannot pin _activeOneshots at cap. SweepOneshots reclaims it.
            RegisterOneshot(go, VFXType.None, key, life);
            StartCoroutine(ReturnHovlAfterSeconds(go, key, life));
            // Oneshot auto-returns; a handle is intentionally not surfaced (would risk a
            // double-return if the caller also Stop()'d it).
            return null;
        }

        // ── Pool management (string-keyed; reuses _poolRoot + the helpers) ─────────

        private GameObject AcquireHovl(string key, in HovlVfxCatalog.Row row)
        {
            if (_hovlPools.TryGetValue(key, out var q))
            {
                // WO-955: this drain used to drop destroyed entries SILENTLY ("Drop destroyed
                // entries and keep looking"), which is the §12 no-silent-failure violation that
                // let the VFXType twin of the same corruption reach the owner as an NRE before
                // anyone knew hosts were dying in a free list at all. Same seam, same rule, one
                // implementation: a destroyed dormant host is evidence of a bad RETURN and now
                // says so once per drain.
                var reused = VfxPoolGuard.DrainToLiveHost(q, "hovl:" + key, out _);
                if (reused != null)
                {
                    reused.transform.SetParent(null, false);
                    return reused;
                }
            }
            // Pool empty (or all-null) — instantiate a fresh one (pooled after use).
            return CreateHovlInstance(row.Prefab, key);
        }

        private GameObject CreateHovlInstance(GameObject prefab, string key)
        {
            if (prefab == null)
            {
                FlowTrace.Warn("VFXManager",
                    $"CreateHovlInstance('{key}'): null prefab — no pooled instance built.");
                return null;
            }
            var go = Instantiate(prefab, _poolRoot);
            go.name = $"[Hovl_{key}]";
            NormalizeVendorContainerRenderers(go, key);
            // NOTE: no URP-proof pass here — Hovl packs ship URP-clean HS_* shader graphs
            // (Docs/VFX/HovlStudio_Inventory.md §2.2 GREEN, no magenta). The VFXType path's
            // ProofUrpParticleShaders is only for the legacy-built Lana/Spells prefabs.
            _hovlKeyOf[go] = key;
            go.SetActive(false);
            return go;
        }

        // =====================================================================
        // WO-1100 - vendor "container" renderer normalization (MagentaProbe M2
        // false-positive killer).
        // ---------------------------------------------------------------------
        // PROVEN AT SOURCE (2026-08-16): the Mirza Beig Ultimate VFX prefab behind
        // 'Portal_Threshold_Aura' (pf_vfx-ult_demo_psys_loop_portalBlue) carries TWO
        // ParticleSystemRenderers - the prefab ROOT (fileID 1641099969701414) and one
        // child (1590112034912844) - that are authored m_Enabled: 0 with m_Materials
        // = [null, null]. That is the vendor CONTAINER pattern: the system exists only
        // to parent/drive its children and its renderer is switched off, so nothing is
        // ever drawn from it and NO art is missing (every material the ENABLED
        // renderers reference was verified present on disk). A byte-scan of the packs
        // found 339 renderers of this exact shape - it is pervasive vendor authoring,
        // not a defect.
        //
        // MagentaGuard.SweepRenderers, however, treats an ALL-null-slot
        // ParticleSystemRenderer as a genuine "nothing to draw" defect and probes it
        // at FAIL severity (class M2) - correctly refusing to repaint it (URP/Lit into
        // a particle slot is the 2026-08-05 white-blob regression). Result: 12
        // identical owner F8 captures per session, one per spawned portal
        // ([Flow:MagentaProbe] FAIL ... obj='...[Hovl_Portal_Threshold_Aura]' slot=0
        // material='NULL' class=M2), for an effect that renders perfectly.
        //
        // THE FIX LIVES HERE, NOT IN MAGENTAGUARD (WO-1100 section 4 forbids touching
        // the guard - it is the net, never strip it): at instance creation, fill slot 0
        // of every DISABLED all-null ParticleSystemRenderer with a donor material
        // borrowed from elsewhere in the same instance. The renderer STAYS DISABLED,
        // so the donor is never drawn - the slot is simply no longer NULL when the
        // guard sweeps the subtree, and the remaining null slot(s) then match the
        // guard's own vendor trail-slot whitelist (IsVendorParticleNullSlot: any
        // non-null sibling slot legitimises the empty ones).
        //
        // DELIBERATELY NARROW: an ENABLED renderer with all-null slots is left alone.
        // That IS a real defect (it draws the engine-default magenta) and must stay
        // visible to MagentaGuard - see the [vfx-null-slot] regression, which ratchets
        // the five known pack offenders (PP_Goop*/PP_EarthShatter/
        // PP_LightnigStormCloud) and fails on any new one.
        // =====================================================================
        private static void NormalizeVendorContainerRenderers(GameObject go, string key)
        {
            if (go == null) return;
            var renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            // Donor: the first real material anywhere in this instance. It is only ever
            // assigned into DISABLED renderers, so it is never drawn - it exists purely
            // so the slot is not NULL when MagentaGuard probes the subtree.
            Material donor = null;
            for (int i = 0; i < renderers.Length && donor == null; i++)
            {
                var candidate = renderers[i] != null ? renderers[i].sharedMaterials : null;
                if (candidate == null) continue;
                for (int m = 0; m < candidate.Length; m++)
                {
                    if (candidate[m] != null) { donor = candidate[m]; break; }
                }
            }

            int normalized = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                // ENABLED all-null is a REAL defect - leave it for MagentaGuard to report.
                if (r == null || r.enabled) continue;

                var mats = r.sharedMaterials;   // copy - assigned back below
                if (mats == null || mats.Length == 0) continue;
                bool allNull = true;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null) { allNull = false; break; }
                }
                if (!allNull) continue;

                if (donor == null)
                {
                    // A whole instance with zero materials anywhere has nothing to lend.
                    // Say so once and let the guard's probe stand - that case IS reportable.
                    FlowTrace.Once("VFXManager", $"hovl-container-nodonor:{key}",
                        $"CreateHovlInstance('{key}'): disabled all-null-slot renderer '{r.gameObject.name}' " +
                        "has NO donor material anywhere in the instance - left as-is, MagentaProbe will " +
                        "report it (WO-1100).");
                    continue;
                }

                mats[0] = donor;
                r.sharedMaterials = mats;   // assignment is what sticks on the instance
                normalized++;
            }

            if (normalized > 0)
                FlowTrace.Once("VFXManager", $"hovl-container-normalized:{key}",
                    $"CreateHovlInstance('{key}'): filled slot 0 on {normalized} DISABLED all-null vendor " +
                    $"container renderer(s) with donor material '{donor.name}' - never drawn (renderer stays " +
                    "disabled); silences the MagentaProbe M2 false positive on this pooled instance (WO-1100).");
        }

        /// <summary>
        /// Return a Hovl-keyed instance to its pool (called by the lifetime coroutine or
        /// via VFXHandle.Stop). Stops particles, resets scale, decrements the right cap
        /// bucket, and re-parents under the pool root. Public so VFXHandle can call it.
        /// </summary>
        public void ReturnHovlToPool(GameObject go, string key)
        {
            if (go == null) return;

            StopAllParticles(go);

            var follower = go.GetComponent<HovlVfxFollower>();
            if (follower != null) follower.EndFollow();

            // Reparent the pooled loop back under the pool root so it tracks nothing while dormant.
            // Unity LOGS AN ERROR (it does NOT throw) from Transform.SetParent when the current parent
            // is mid-(de)activation — e.g. an Arcane Spire being deactivated by a tier reskin, or a
            // scene unload. Because it never throws, the old Guard.Try wrapper caught nothing and the
            // LogError still reached the F8 recorder: proven by data — the guard was in place at
            // 06:03 yet the error fired 55 min later at 06:58, across 22 captures (owner F8 2026-07-17).
            //
            // The tell for "parent is mid-(de)activation" is exactly `!activeInHierarchy` — during the
            // deactivation propagation the child reads inactive-in-hierarchy while OnDisable runs. So we
            // reparent ONLY when the object is still active (the normal loop-stop, where returning to the
            // pool root keeps things tidy) and DEACTIVATE IN PLACE otherwise. This never issues the
            // illegal call, so nothing to log. AcquireHovl re-seats the parent on next use regardless
            // (SetParent(null) then SetParent(newParent), lines ~242/252).
            //
            // ⚠ THE REST OF THIS PARAGRAPH IS RETIRED BY WO-955 (2026-08-10). It used to conclude
            // that AcquireHovl "tolerates the object being destroyed with its tower (drops null
            // entries, instantiates fresh) — so leaving a dormant loop parented under a torn-down
            // tower is harmless." It is NOT harmless: the drop was SILENT, so nobody learned that
            // dormant hosts were dying, and the identical shape in the VFXType pool reached the
            // owner as an NRE (VFXManager.cs:876, twice on 2026-08-10). Deactivating in place is
            // still right — issuing the illegal reparent is not the answer. ENQUEUING the result
            // is what was wrong, and that is what the guard below now refuses.
            if (go.activeInHierarchy && go.transform.parent != _poolRoot)
                go.transform.SetParent(_poolRoot, false);
            go.transform.localScale = Vector3.one;   // clear any scale override for reuse
            go.SetActive(false);

            // The budget bookkeeping settles NOW, whatever happens to the pooling below: the
            // loop has stopped, so its slot is free this instant. Only the ENQUEUE is allowed
            // to wait. (Moved above the pooling branch by WO-955 — leaving it after a branch
            // that can defer or drop would have turned a pooling decision into a budget leak.)
            // WO-1057: the registry Remove IS the decrement — the shared loop count is derived
            // from the two registries' Count, so there is no int that can drift from the set.
            bool wasLoop = _hovlLoopObjects.Remove(go);
            if (!wasLoop) UnregisterOneshot(go);   // removing the live-set slot IS the decrement

            // WO-955 — the write side. The branch above deliberately SKIPS the reparent while
            // the host is mid-(de)activation (that non-throwing Unity refusal is what the
            // 2026-07-17 comment above documents), and this method then enqueued the host
            // ANYWAY, with a note that it "tolerates the object being destroyed with its tower
            // (drops null entries)". That toleration IS the defect: a queue entry parented under
            // a scene object is not covered by the DontDestroyOnLoad pool root, so the tower's
            // teardown leaves a corpse in the free list — and the poisoned list outlives the
            // scene, which is why the captured NRE surfaced in dg_ember_deep from a caller that
            // had nothing to do with the tower. Only a host genuinely parked under the pool root
            // may be enqueued; anything else waits one frame for the cascade to end.
            if (VfxPoolGuard.IsPoolSafe(go, _poolRoot))
            {
                EnqueueHovl(go, key);
                return;
            }

            for (int i = 0; i < _pendingHovlReturns.Count; i++)
                if (_pendingHovlReturns[i].go == go) return;   // already waiting

            _pendingHovlReturns.Add((go, key));
            FlowTrace.Throttle("VFXManager", "hovl-deferred-pool", 5f,
                $"ReturnHovlToPool('{key}'): host could not be reparented to the pool root yet (still under " +
                $"{VfxPoolGuard.DescribeParent(go)}) — pooling DEFERRED one frame rather than enqueuing an " +
                "unprotected slot (WO-955). Budget already reclaimed.");
        }

        // The enqueue tail, so the direct and deferred paths cannot drift apart.
        private void EnqueueHovl(GameObject go, string key)
        {
            if (!_hovlPools.ContainsKey(key))
                _hovlPools[key] = new Queue<GameObject>();
            _hovlPools[key].Enqueue(go);
            _hovlKeyOf[go] = key;
        }

        // WO-955: returns whose reparent has to wait out a host (de)activation/teardown window.
        // Mirrors the VFXType path's _pendingReturns (WO-929) rather than inventing a second
        // shape. Swept once, from Update; there is no second chance, because a host that is
        // STILL not under the pool root a frame later is not a timing problem.
        private readonly List<(GameObject go, string key)> _pendingHovlReturns
            = new List<(GameObject, string)>();

        /// <summary>
        /// One-frame-later completion of a deferred Hovl pool return. Called from
        /// <c>VFXManager.Update</c> alongside the VFXType sweep.
        /// </summary>
        private void SweepPendingHovlReturns()
        {
            if (_pendingHovlReturns.Count == 0) return;

            for (int i = 0; i < _pendingHovlReturns.Count; i++)
            {
                var (go, key) = _pendingHovlReturns[i];

                // Destroyed with its owner in the meantime — the expected outcome for a
                // teardown-time return, and now a HARMLESS one: the host never entered the
                // free list, so there is no corpse to hand back. Budget was settled at return.
                if (go == null) continue;

                if (go.transform.parent != _poolRoot && _poolRoot != null)
                    go.transform.SetParent(_poolRoot, false);

                if (VfxPoolGuard.IsPoolSafe(go, _poolRoot))
                {
                    EnqueueHovl(go, key);
                    continue;
                }

                // Refused twice: not a (de)activation-window timing issue. Drop the slot rather
                // than pool an unprotected one — capacity self-heals on the next AcquireHovl.
                FlowTrace.Warn("VFXManager",
                    $"SweepPendingHovlReturns('{key}'): host is STILL not under the pool root a frame later " +
                    $"(parent={VfxPoolGuard.DescribeParent(go)}) — NOT pooled (WO-955). This is no longer a " +
                    "timing window; the named parent is holding a VFX host it does not own, and its teardown " +
                    "is what would have poisoned the free list.");
            }

            _pendingHovlReturns.Clear();
        }

        /// <summary>Defer a Hovl pool return by <paramref name="delay"/> seconds (graceful loop stop).</summary>
        public void ReturnHovlAfterDelay(GameObject go, string key, float delay)
        {
            if (go == null) return;
            StartCoroutine(ReturnHovlAfterSeconds(go, key, delay));
        }

        private IEnumerator ReturnHovlAfterSeconds(GameObject go, string key, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnHovlToPool(go, key);
        }

        // ── Overrides / helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Recolour a Hovl effect toward <paramref name="color"/> the way the VENDOR's own
        /// recolor tool does (HS_CameraHolder.Counter/OnGUI, docs/HOVL_STUDIO_SME.md sec 4d):
        /// cache each particle system's AUTHORED startColor as HSV on first acquire, then
        /// shift only the HUE to the target hue while PRESERVING its cached saturation / value
        /// (brightness) / alpha - so the bright-core / soft-halo layering (and the HDR luminance
        /// bloom feeds on) survives. The old flat MinMaxGradient(color) flood-fill stamped every
        /// layer one identical color, flattening the authored art ("not like the demo"). Near-
        /// white hot cores (saturation &lt; 0.05) are left untouched - hue means nothing on them
        /// and pushing the tint in is exactly the flood-fill artifact. Idempotent under pooling:
        /// the S/V/A always come from the cached ORIGINAL, never from an already-shifted value,
        /// so repeated reuses (and later hue changes) never drift the authored brightness.
        /// Gradient startColor modes are hue-rotated key-by-key (never thrown on).
        /// </summary>
        private void ApplyStartColor(GameObject go, Color color)
        {
            Color.RGBToHSV(color, out float targetHue, out _, out _);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var main = ps.main;
                // Cache the AUTHORED startColor the first time this PS is recolored, so every
                // pooled reuse hue-shifts from the original S/V/A rather than a prior result.
                if (!_hovlAuthoredStartColor.TryGetValue(ps, out var authored))
                {
                    authored = main.startColor;
                    _hovlAuthoredStartColor[ps] = authored;
                }
                main.startColor = ShiftHue(authored, targetHue);
            }
        }

        /// <summary>Return a hue-shifted copy of a ParticleSystem startColor
        /// <see cref="ParticleSystem.MinMaxGradient"/>, preserving each source colour's authored
        /// saturation / value / alpha across every gradient mode (Color / TwoColors / Gradient /
        /// TwoGradients). Never mutates <paramref name="sc"/>; safe on the cached authored value.</summary>
        private static ParticleSystem.MinMaxGradient ShiftHue(ParticleSystem.MinMaxGradient sc, float targetHue)
        {
            switch (sc.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(ShiftHue(sc.color, targetHue));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(ShiftHue(sc.colorMin, targetHue),
                                                             ShiftHue(sc.colorMax, targetHue));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(ShiftHue(sc.gradient, targetHue));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(ShiftHue(sc.gradientMin, targetHue),
                                                             ShiftHue(sc.gradientMax, targetHue));
                default:
                    return sc;
            }
        }

        /// <summary>Move <paramref name="src"/>'s hue to <paramref name="targetHue"/>,
        /// keeping its authored saturation/value (HDR-safe) and alpha.</summary>
        private static Color ShiftHue(Color src, float targetHue)
        {
            Color.RGBToHSV(src, out _, out float s, out float v);
            if (s < 0.05f) return src;   // white-hot core layer — leave it white
            var c = Color.HSVToRGB(targetHue, s, v, hdr: true);
            c.a = src.a;
            return c;
        }

        private static Gradient ShiftHue(Gradient g, float targetHue)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                keys[i].color = ShiftHue(keys[i].color, targetHue);
            var ng = new Gradient { mode = g.mode };
            ng.SetKeys(keys, g.alphaKeys);
            return ng;
        }

        // =====================================================================
        // FIT-TO-SIZE (WO-870) - measure the authored art, then normalize it.
        // ---------------------------------------------------------------------
        // WHY: every owner-tagged projectile row in Assets/Editor/VfxManualPicks.json
        // carries scale 1.0 - the owner tags the KEY, not a per-key size. So a caller
        // that needs a key to read at a specific WORLD size (a tower projectile that
        // must look the same at range 14 and at range 36) cannot read that size off
        // the picks; it has to MEASURE the prefab and derive the scale. Same shape as
        // the DEF-208 / WO-751 repo.visualHeight fit-to-height pass on catalog items:
        // measure -> normalize -> scale by the gameplay number, never trust authored scale.
        //
        // Measurement is per-PREFAB (never a playing instance) and cached per key, so
        // it costs one reflection-free component walk once per process and is a plain
        // dictionary hit from then on - safe to call from a fire hot-loop.
        // =====================================================================

        // key -> measured authored visual size in world units (0 = unknown/unmeasurable).
        private static readonly Dictionary<string, float> _hovlMeasuredSize
            = new Dictionary<string, float>();

        // Keys already warned about as unmeasurable, so the warn path stays allocation-free
        // after the first call (string interpolation only happens once per bad key).
        private static readonly HashSet<string> _hovlFitWarned = new HashSet<string>();

        /// <summary>
        /// Measured authored visual size (world units) of a catalogued key's PREFAB, or 0
        /// when the key is unknown / the catalog is not ready / nothing measurable was found.
        /// Cached per key (measured ONCE per process) so this is safe on a hot loop. Never
        /// throws - a failed measure is logged by Guard and reported as 0.
        /// </summary>
        public static float MeasureKeyVisualSize(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0f;
            if (_hovlMeasuredSize.TryGetValue(key, out float cached)) return cached;

            float measured = 0f;
            string prefabName = "<none>";
            Guard.Try("VFXManager", "measure hovl key '" + key + "'", () =>
            {
                var mgr = Instance;
                if (mgr == null) return;                       // manager not booted yet - retry next call
                mgr.EnsureHovlCatalog();
                if (mgr._hovlCatalog == null) return;
                if (!mgr._hovlCatalog.TryGet(key, out var row)) return;
                if (row.Prefab == null) return;
                prefabName = row.Prefab.name;
                measured = MeasurePrefabVisualSize(row.Prefab);
            });

            // Only cache a real measurement: a 0 from "manager not booted yet" must not be
            // frozen in for the rest of the process (the first fire can precede VFXManager).
            if (measured > 0f)
            {
                _hovlMeasuredSize[key] = measured;
                FlowTrace.Once("VFXManager", "hovl-measure:" + key,
                    $"MeasureKeyVisualSize('{key}') -> prefab '{prefabName}' authored visual size {measured:0.###} m.");
            }
            return measured;
        }

        /// <summary>
        /// Uniform scale that makes <paramref name="key"/>'s prefab read at roughly
        /// <paramref name="targetWorldSize"/> world units, clamped into
        /// [<paramref name="minScale"/>, <paramref name="maxScale"/>]. Returns 1f (the
        /// authored size, unchanged) when the prefab cannot be measured - and says so once
        /// via FlowTrace, so an unmeasurable prefab SELF-REPORTS instead of silently
        /// shipping at 1.0.
        /// </summary>
        public static float ResolveFitScale(string key, float targetWorldSize, float minScale, float maxScale)
        {
            if (targetWorldSize <= 0f) return 1f;

            float measured = MeasureKeyVisualSize(key);
            if (measured <= 0f)
            {
                if (!string.IsNullOrEmpty(key) && _hovlFitWarned.Add(key))
                    FlowTrace.Once("VFXManager", "hovl-fit-nomeasure:" + key,
                        $"ResolveFitScale('{key}'): prefab could not be measured (no key / no prefab / no " +
                        $"ParticleSystem or mesh bounds) - falling back to scale 1.0 for a requested " +
                        $"{targetWorldSize:0.###} m. Check the catalog row for this key.");
                return 1f;
            }

            float lo = Mathf.Min(minScale, maxScale);
            float hi = Mathf.Max(minScale, maxScale);
            if (hi <= 0f) return 1f;
            return Mathf.Clamp(targetWorldSize / measured, lo, hi);
        }

        // instanceID -> measured authored visual size, for prefabs that have NO catalog row
        // (the WO-1035 portal-circle mirror is a plain tracked Resources prefab). Same
        // measure-once-per-process contract as _hovlMeasuredSize above.
        private static readonly Dictionary<int, float> _prefabMeasuredSize
            = new Dictionary<int, float>();

        /// <summary>
        /// Measured authored visual size (world units) of an UNCATALOGUED prefab — the same
        /// measurement <see cref="MeasureKeyVisualSize"/> performs, for callers that hold a
        /// plain prefab reference instead of a catalog key (e.g. a tracked Resources mirror).
        /// Cached per prefab instance id. 0 = unmeasurable.
        /// <para/>
        /// Exposed rather than re-implemented at the call site: a second copy of the
        /// startSize/mesh-bounds walk is exactly the duplicated-state drift CLAUDE.md §5 keeps
        /// having to un-rot, and the two copies would answer differently the first time a
        /// vendor prefab used a curve-mode start size.
        /// </summary>
        public static float MeasureVisualSize(GameObject prefab)
        {
            if (prefab == null) return 0f;
            int id = prefab.GetInstanceID();
            if (_prefabMeasuredSize.TryGetValue(id, out float cached)) return cached;

            float measured = 0f;
            Guard.Try("VFXManager", "measure prefab '" + prefab.name + "'",
                      () => { measured = MeasurePrefabVisualSize(prefab); });
            if (measured > 0f)
            {
                _prefabMeasuredSize[id] = measured;
                FlowTrace.Once("VFXManager", "prefab-measure:" + id,
                    $"MeasureVisualSize('{prefab.name}') -> authored visual size {measured:0.###} m.");
            }
            return measured;
        }

        /// <summary>
        /// <see cref="ResolveFitScale(string,float,float,float)"/> for an uncatalogued prefab.
        /// Returns 1f (authored size, unchanged) and says so once when the prefab cannot be
        /// measured, so an unmeasurable prefab SELF-REPORTS instead of silently shipping at 1.0.
        /// </summary>
        public static float ResolveFitScale(GameObject prefab, float targetWorldSize,
                                            float minScale, float maxScale)
        {
            if (prefab == null || targetWorldSize <= 0f) return 1f;

            float measured = MeasureVisualSize(prefab);
            if (measured <= 0f)
            {
                FlowTrace.Once("VFXManager", "prefab-fit-nomeasure:" + prefab.GetInstanceID(),
                    $"ResolveFitScale('{prefab.name}'): prefab could not be measured (no ParticleSystem " +
                    $"or mesh bounds) - falling back to scale 1.0 for a requested {targetWorldSize:0.###} m.");
                return 1f;
            }

            float lo = Mathf.Min(minScale, maxScale);
            float hi = Mathf.Max(minScale, maxScale);
            if (hi <= 0f) return 1f;
            return Mathf.Clamp(targetWorldSize / measured, lo, hi);
        }

        /// <summary>
        /// Max authored visual extent of a prefab: the largest particle start size across
        /// every child ParticleSystem, and the largest mesh bounds diagonal across every
        /// child MeshRenderer / mesh-mode ParticleSystemRenderer - times the prefab root's
        /// own localScale (largest axis). Editor-safe, allocation-heavy but called ONCE per key.
        /// </summary>
        private static float MeasurePrefabVisualSize(GameObject prefab)
        {
            if (prefab == null) return 0f;
            float size = 0f;

            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var main = ps.main;
                float start;
                switch (main.startSize.mode)
                {
                    case ParticleSystemCurveMode.Constant:
                    case ParticleSystemCurveMode.TwoConstants:
                        // NOTE: in constant mode the MinMaxCurve constant ALREADY carries the
                        // curve multiplier (startSizeMultiplier is an alias of it) - multiplying
                        // the two would square the size, so we take the constant alone.
                        start = main.startSize.constantMax;
                        if (start <= 0f) start = main.startSize.constant;
                        break;
                    default:
                        // Curve modes: the constant is meaningless; the multiplier IS the world size.
                        start = main.startSizeMultiplier;
                        break;
                }
                if (start > size) size = start;
            }

            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float d = mf.sharedMesh.bounds.size.magnitude;
                if (d > size) size = d;
            }

            foreach (var pr in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (pr == null) continue;
                var mesh = pr.mesh;                       // null unless the renderer is in Mesh mode
                if (mesh == null) continue;
                float d = mesh.bounds.size.magnitude;
                if (d > size) size = d;
            }

            var ls = prefab.transform.localScale;
            float axis = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            if (axis <= 0f) axis = 1f;
            return size * axis;
        }

        // A Hovl VFX object must carry at least one ParticleSystem (or visible Renderer) to be
        // seen. Traced Once per key so a bad catalog prefab self-reports in the break-log.
        private static void VerifyHovlHasParticles(GameObject go, string key)
        {
            if (go == null) return;
            int particles = go.GetComponentsInChildren<ParticleSystem>(true).Length;
            int renderers = go.GetComponentsInChildren<Renderer>(true).Length;
            if (particles == 0 && renderers == 0)
                FlowTrace.Once("VFXManager", $"hovl-novisual:{key}",
                    $"PlayKey('{key}'): prefab has NO ParticleSystem and NO Renderer — plays but " +
                    "renders nothing (invisible VFX). Check the catalog prefab for this key.");
        }
    }
}

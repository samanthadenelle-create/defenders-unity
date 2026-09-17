// =============================================================================
// BattlePlansPickup -- WO-1804: the walk-over prop for BOTH new plans kinds, and
// the one place either kind's HELD flag is written.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Mirrors CastleDefensePlansPickup's grammar exactly (trigger sphere +
// GetComponentInParent<HeroHealth> hero check + one-shot _taken latch + a static
// TryCollect the regression can call headless) and is a SEPARATE class rather than a
// generalisation of it -- see BattlePlans.cs' header for why the shipping wave-3
// marquee is not refactored three days before the video.
//
// MECHANICS vs PRESENTATION, the WO-1013 split, kept: TryCollect IS the mechanics
// (persist the HELD flag, raise the tutorial signal). The reveal screen is
// PRESENTATION and hangs off the signal seam; failing to build it can never cost the
// player the unlock, because the flag is already committed when the signal is raised.
//
// ⛔ NO FUNDING GRANT. The castle plans hand over a Spire's worth of resources because
// they unlock a STRUCTURE the player then has to pay for. These plans unlock a RAID,
// which costs no resources -- the starter army is already free (WO-1803's lane) and
// the Heartfire charge is a clock, not a price. Granting a basket here would be an
// invented economy change nobody ruled on.
//
// -----------------------------------------------------------------------------
// THE VISUAL, AND WHY IT IS NOT A NEW VFX SYSTEM.
// -----------------------------------------------------------------------------
// Primitive satchel + point-light glint + PoiBeacon landmark pillar: the identical
// grammar CastleDefensePlansService.BuildVisual uses, which itself mirrors
// DungeonTreasureCache. On top of it, ONE owner-tagged key:
//
//     Treasure_Aura  ->  Lana Studio/Casual RPG VFX/Prefabs/Loot/Loot_iddle.prefab
//                        (Assets/Editor/VfxManualPicks.json; her words: "treasure chest")
//
// ⚠ THE CASTLE PLANS DROP CARRIES NO VFX KEY OF ITS OWN -- read at source 2026-09-16,
// CastleDefensePlansService.BuildVisual spawns primitives, a Light, CastlePlansGlint
// and a PoiBeacon, and never calls VFXManager. So there was no "castle plans key" to
// reuse. Grepped the owner's tag file for Plans / Reveal / Drop / Unlock / Reward
// keys: NONE exist. Treasure_Aura is the nearest OWNER-TAGGED key whose authored
// meaning is exactly this ("loot sitting on the ground, unclaimed") and it is already
// bound to the prop family this one copies -- DungeonTreasureCache, the very visual
// grammar the castle plans header names. Mapped VERBATIM through the existing
// VFXManager (no prefab path is read here, nothing is rescaled, no tint is imposed --
// memory vfx-map-owner-tags-no-creative-pick), with NO new spawner and NO new pool
// (docs/ARCHITECTURE_PRINCIPLES.md §2b.1, the two-VFX-stack scar). If the owner wants
// a distinct key for plans, she tags one and this const changes.
//
// The one-shot/loop handling is copied from DungeonTreasureCache.StartShimmer for the
// same measured reason recorded there: her isLoop flag reads false while the prefab's
// systems are authored looping, so the LIFETIME is owned by the prop's unclaimed
// state and a one-shot row is re-fired. No code change either way if she retags.
//
// ASCII only. FlowTrace tag BattlePlans.Sys. Instrumentation is permanent (section 12).
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Tutorial;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// Walk-over pickup for an Enemy Battle Plans / Bastion Plans drop (WO-1804).
    /// Collection persists that kind's HELD flag and raises the reveal signal. Once, ever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePlansPickup : MonoBehaviour
    {
        /// <summary>Her tag, verbatim. The catalog owns the key -&gt; prefab mapping.</summary>
        public const string ShimmerKey = "Treasure_Aura";

        /// <summary>Metres above the prop origin the shimmer is seated -- just over the
        /// rolled plans on top of the satchel (0.66 in <see cref="BuildVisual"/>).</summary>
        private const float ShimmerHeight = 0.95f;

        /// <summary>Seconds between one-shot re-fires. MEASURED by the WO-1347 lane from the
        /// prefab YAML (longest lengthInSec = 4), so this re-arms just before the previous
        /// burst ends with no double-density overlap. Not re-derived, not re-chosen.</summary>
        private const float RefireSeconds = 3.9f;

        /// <summary>
        /// Raised ONCE per collection, AFTER the HELD flag is committed. The reveal screen
        /// subscribes here. Mirrors <c>CastleDefensePlansPickup.PlansCollected</c>; carries
        /// the kind so one subscriber serves both drops.
        /// </summary>
        public static event Action<BattlePlansKind> PlansCollected;

        /// <summary>Neutral high-luminance pale gold, shared with the castle plans drop's
        /// glint. NOT a semantic hue (the owner is red/green colourblind).</summary>
        private static readonly Color GlintTint = new Color(1f, 0.86f, 0.5f, 1f);

        private BattlePlansKind _kind;
        private bool _taken;
        private VFXHandle _shimmer;
        private bool _shimmerStarted;
        private float _nextShimmerRefire;

        /// <summary>Which kind this prop is (read by the trace lines and the pickup).</summary>
        public BattlePlansKind Kind => _kind;

        // =====================================================================
        //  SPAWN
        // =====================================================================

        /// <summary>
        /// Build the prop at <paramref name="position"/>. SCENE-OWNED on purpose (never
        /// parented to a DDOL service): the prop dies with the scene and the owning service's
        /// scan deterministically re-spawns it from persisted state until it is collected --
        /// the CastleDefensePlansService.SpawnDrop contract, verbatim.
        /// </summary>
        public static BattlePlansPickup Spawn(BattlePlansKind kind, Vector3 position, string why)
        {
            var go = new GameObject("BattlePlans_Drop_" + kind);
            go.transform.position = position;

            var pickup = go.AddComponent<BattlePlansPickup>();
            pickup._kind = kind;
            pickup.BuildVisual();

            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 1.6f;
            // Kinematic body so the trigger fires regardless of how the hero rig is composed
            // (CharacterController vs collider+rigidbody) -- the castle plans' own reason.
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            FlowTrace.Step(BattlePlans.Sys,
                "plans-drop-SPAWNED kind=" + kind + " title='" + BattlePlans.TitleFor(kind) +
                "' @ " + position + " (" + why + ") -- persists until collected; WO-1804");
            return pickup;
        }

        // =====================================================================
        //  COLLECTION
        // =====================================================================

        private void OnTriggerEnter(Collider other)
        {
            if (_taken) return;
            if (other == null || other.GetComponentInParent<HeroHealth>() == null) return;

            if (!TryCollect(_kind, transform.position))
            {
                // Already held (a stale prop from a race) -- retire it quietly.
                if (ProgressionUnlocks.IsUnlocked(BattlePlans.PlansIdFor(_kind)))
                    Retire("already held");
                return;
            }
            Retire("collected");
        }

        private void Retire(string why)
        {
            _taken = true;
            StopShimmer(why);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        /// <summary>
        /// The collection mechanics, callable headless (the regression oracle): idempotence
        /// gate -&gt; persisted HELD flag -&gt; reveal signal. Returns TRUE only on the one
        /// real collection; every later call returns false and writes nothing.
        /// </summary>
        public static bool TryCollect(BattlePlansKind kind) => TryCollect(kind, null);

        /// <summary>See <see cref="TryCollect(BattlePlansKind)"/>. <paramref name="at"/> is
        /// the prop position for the funnel trace (null when driven headless).</summary>
        public static bool TryCollect(BattlePlansKind kind, Vector3? at)
        {
            string plansId = BattlePlans.PlansIdFor(kind);

            // ONE collection, ever -- the persisted flag is the gate (WO-1013 SS3 idiom).
            if (ProgressionUnlocks.IsUnlocked(plansId))
            {
                FlowTrace.Step(BattlePlans.Sys,
                    "plans-collect IGNORED kind=" + kind + ": '" + plansId +
                    "' already held (once-ever gate held)");
                return false;
            }

            // Flag FIRST: once-ever beats a lost presentation. If the reveal below never
            // opens the player still holds the plans, and the Bastion still unlocks.
            if (!ProgressionUnlocks.Unlock(plansId))
            {
                FlowTrace.Fail(BattlePlans.Sys,
                    "plans-collect ABORTED kind=" + kind + ": HELD flag write refused (no " +
                    "GameStateService?) -- nothing persisted, the prop stays for a retry");
                return false;
            }

            FlowTrace.Step(BattlePlans.Sys,
                "plans-PICKED-UP kind=" + kind + " flag='" + plansId + "' " +
                (at.HasValue ? "@ " + at.Value + " (walk-over)" : "(headless/direct TryCollect)") +
                " -- WO-1804");

            // ONE hand-off, named in the WO so WO-1802's helper-chain lane can meet it here
            // without either lane editing the other's files.
            Guard.Try(BattlePlans.Sys, "plans revealed signal", () =>
                TutorialSignals.Raise(SignalFor(kind)));

            Guard.Try(BattlePlans.Sys, "plans collected seam", () => PlansCollected?.Invoke(kind));
            return true;
        }

        /// <summary>
        /// THE HAND-OFF, and the ONE call WO-1802's helper chain listens for:
        /// <c>plans.revealed:battle</c> / <c>plans.revealed:bastion</c>. One prefix, two ids,
        /// following the bus' own grammar (TutorialSignals.StructurePlacedPrefix,
        /// PetBondedPrefix, ManageTroopSelectedPrefix all read this way) rather than a flat
        /// one-off id -- so a step can await EITHER kind or the family.
        /// </summary>
        public static string SignalFor(BattlePlansKind kind)
            => kind == BattlePlansKind.Bastion
                ? TutorialSignals.BastionPlansRevealed
                : TutorialSignals.BattlePlansRevealed;

        // =====================================================================
        //  VISUAL -- primitives + glint + landmark pillar + her tagged aura
        // =====================================================================

        private void BuildVisual()
        {
            var t = transform;
            // A satchel of plans: leather body, gilt strap, rolled plans on top. The same
            // three primitives CastleDefensePlansService.BuildVisual draws, so the two drops
            // read as the same KIND of thing in the world.
            AddDecor(t, PrimitiveType.Cube, new Vector3(0f, 0.30f, 0f),
                new Vector3(0.85f, 0.50f, 0.55f), new Color(0.42f, 0.29f, 0.13f));
            AddDecor(t, PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f),
                new Vector3(0.90f, 0.12f, 0.60f), new Color(0.86f, 0.71f, 0.30f));
            AddDecor(t, PrimitiveType.Cylinder, new Vector3(0f, 0.66f, 0f),
                new Vector3(0.14f, 0.42f, 0.14f), new Color(0.92f, 0.87f, 0.72f),
                euler: new Vector3(0f, 0f, 90f));

            var lightGo = new GameObject("BattlePlans_Glint");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = GlintTint;
            light.range = 14f;     // WO-1105 measured this: 8 m did not reach past the gate arch
            light.intensity = 2.4f;

            // Findability at hub scale, the WO-1105 lesson: an 0.85 m satchel ~37 m from a
            // town-centred camera is not findable. Same far-field landmark pillar the castle
            // plans use, same neutral tint (verticality / motion / luminance, never hue).
            Guard.Try(BattlePlans.Sys, "plans landmark beacon", () =>
            {
                var beacon = PoiBeacon.Attach(gameObject, PoiBeacon.PoiTier.Landmark,
                    calloutRadius: 500f, handoffRadius: 4f, tint: GlintTint);
                FlowTrace.Step(BattlePlans.Sys,
                    "plans-drop landmark beacon " + (beacon != null ? "attached" : "NOT attached") +
                    " (PoiCallouts flag=" + DeNelle.Core.FeatureFlags.PoiCallouts + ")");
            });

            StartShimmer("prop built");
        }

        private static void AddDecor(Transform parent, PrimitiveType type, Vector3 localPos,
            Vector3 scale, Color color, Vector3 euler = default)
        {
            Guard.Try(BattlePlans.Sys, "build plans decor", () =>
            {
                var go = GameObject.CreatePrimitive(type);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.transform.localScale = scale;
                if (euler != Vector3.zero) go.transform.localEulerAngles = euler;
                // Strip the primitive's solid collider: the pickup's own sphere TRIGGER is the
                // one collider this prop owns (a solid box at a gate mouth would snag paths).
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    if (sh != null) rend.material = new Material(sh) { color = color };
                    else rend.material.color = color;
                }
            });
        }

        /// <summary>
        /// Spawn her tagged idle aura over the UNCOLLECTED prop. Idempotent, a hard no-op once
        /// taken, and null-safe end to end: VFXManager.PlayKey returns null when the manager or
        /// the catalog row is not ready, so an un-regenerated catalog costs the shimmer and
        /// nothing else. The trace states whether the prefab RESOLVED, because a missing effect
        /// and a deliberately subtle one are otherwise indistinguishable (section 12).
        /// </summary>
        private void StartShimmer(string why)
        {
            if (_taken) return;
            if (_shimmer != null && _shimmer.IsAlive) return;

            bool resolves = VFXManager.CanPlayKey(ShimmerKey);
            Vector3 at = transform.position + Vector3.up * ShimmerHeight;

            Guard.Try(BattlePlans.Sys, "spawn plans idle aura", () =>
            {
                // Her scale is 1.0, passed as 0 = "use the catalog row's DefaultScale", i.e.
                // nothing here rescales her effect. Parented so it tracks the prop; no tint
                // is passed, so no hue is imposed on a colourblind-safe read.
                _shimmer = VFXManager.PlayKey(ShimmerKey, at, Quaternion.identity, transform);
            });

            _shimmerStarted = true;
            _nextShimmerRefire = Time.unscaledTime + RefireSeconds;

            FlowTrace.Step(BattlePlans.Sys,
                "plans idle aura '" + ShimmerKey + "' (" + why + "): prefabResolved=" + resolves +
                " handle=" + (_shimmer != null ? "LOOP (held)" : "null -> ONE-SHOT (re-fired while unclaimed)") +
                " space=WORLD pos=" + at + " kind=" + _kind + ". " +
                (resolves
                    ? "Owner-tagged key mapped verbatim; the prop's UNCLAIMED state owns the lifetime."
                    : "Key not in the runtime catalog yet - regenerate it (Defenders/VFX/Generate Hovl " +
                      "VFX Catalog) or the aura stays absent. The glint and the beacon are unaffected."));
        }

        private void StopShimmer(string why)
        {
            bool had = _shimmerStarted;
            if (_shimmer != null)
            {
                _shimmer.Stop(immediate: true);
                _shimmer = null;
            }
            _shimmerStarted = false;
            if (had)
                FlowTrace.Step(BattlePlans.Sys,
                    "plans idle aura '" + ShimmerKey + "' STOPPED (" + why + ") kind=" + _kind +
                    " -- nothing is left playing over a collected drop.");
        }

        // =====================================================================
        //  GLINT + one-shot re-fire (unscaled: the town can be paused behind a modal)
        // =====================================================================

        private float _baseY;
        private bool _baseYTaken;

        private void Update()
        {
            if (_taken) return;

            if (!_baseYTaken) { _baseY = transform.position.y; _baseYTaken = true; }

            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            var p = transform.position;
            p.y = _baseY + Mathf.Lerp(0f, 0.14f, k);
            transform.position = p;
            transform.Rotate(0f, 30f * Time.unscaledDeltaTime, 0f, Space.World);

            // One-shot row: re-fire while the prop is still unclaimed (the measured cadence).
            if (_shimmerStarted && _shimmer == null && Time.unscaledTime >= _nextShimmerRefire)
            {
                _nextShimmerRefire = Time.unscaledTime + RefireSeconds;
                Guard.Try(BattlePlans.Sys, "refire plans idle aura", () =>
                    VFXManager.PlayKey(ShimmerKey, transform.position + Vector3.up * ShimmerHeight,
                        Quaternion.identity, transform));
            }
        }

        private void OnDisable() => StopShimmer("prop disabled");
        private void OnDestroy() => StopShimmer("prop destroyed");
    }
}

// =============================================================================
// StructureHitReaction - the PER-HIT flinch on anything that can be damaged.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// ## THE GAP THIS CLOSES (verified at source before a line was written)
//
// The game had a damage-STATE ladder and no damage-EVENT at all.
//
//   * Building.ApplyDamage (Building.cs) subtracts HP, raises HpChanged, and returns.
//     Nothing anywhere subscribes to HpChanged for a visual. The ONLY thing that ever
//     reacts is StructureDamageVisuals, whose Evaluate runs on a 0.3 s poll (and whose
//     Scan runs on a 2.0 s poll) and which only reacts when the HP crosses a data
//     THRESHOLD - so a hit that does not cross 0.5 or 0.25 produces literally nothing,
//     and one that does produces it up to a third of a second late. Being hit and being
//     BADLY hurt were the same channel, and the first one was silent.
//
//   * HeartController.SetHp is worse: it fires OnHealthChanged and derives a HeartState,
//     and StructureDamageVisuals never scans the Heart at all (deliberately - it has a
//     bespoke tell). HeartAuraController DOES read Hp continuously and drives a genuinely
//     colour-free tell off it (aura SIZE, glow LUMINANCE, pulse RATE) - so the claim
//     "the Heart has zero visual response" is not quite right and is worth stating
//     precisely: it has a good STATE read and NO EVENT read. That state read is lerped
//     across the whole 0-100 range, so a single contact hit moves the aura by a percent
//     or two - invisible. The thing the game is named for did not flinch when struck.
//
// This component is the missing event read, and it is deliberately ONE component rather
// than a per-type edit: it observes an HP fraction through a delegate - exactly the
// surface StructureDamageVisuals already builds for every structure it tracks - so
// walls, buildings, gates, towers, collectors, harvest sites and the Heart are all
// covered without a new damage model, a new event, or a line in any gameplay class.
//
// ## WHY POLLING IS CORRECT HERE AND WRONG THERE
//
// StructureDamageVisuals polls at 0.3 s because its work (a FindObjectsByType scan, a
// worst-first burn-loop re-assignment) is expensive. This polls EVERY FRAME because its
// work is one float compare against a cached value. A hit is an instant, so the read
// has to be too; a third of a second late reads as unrelated to the blow that caused it.
//
// ## COST
//
// ZERO loop slots. The flinch is a Family B one-shot - fired and forgotten, no handle,
// no stop path, and it cannot leak one of VFXManager's 20 global slots no matter how
// many enemies chew on how many walls. It is additionally rate-limited per structure
// (MinBurstInterval) and gated on a minimum drop, because a wave contact-attacking a
// wall would otherwise fire one burst per damage tick per attacker.
//
// COLOURBLIND (owner is red/green): the read is MOTION and TIMING - a puff bursts off
// the structure AT THE INSTANT of contact. There is no tint in it at all, and it is
// redundant with the existing state ladder (smoke density -> flame presence -> break)
// rather than replacing it.
//
// LANDSCAPE PHONE (2670x1200): seated at the structure's bounds CENTRE, not above it -
// a burst that grows upward off a tall wall leaves the frame.
//
// =============================================================================
// WO-1717 section 6C - THE NUMBER AND THE SOUND (added 2026-09-14)
// -----------------------------------------------------------------------------
// The owner, mid-raid: "when troops attack you should see the damage numbers so you
// can tell something is happening". The RCA proved the pool already exists and was
// simply never called from a structure: DamageNumberSpawner.Spawn had exactly TWO
// call sites in the whole tree, both inside Enemy.cs. Killing a unit numbered; hitting
// a wall did not. This component is the one place every damageable structure in the
// game already passes through, so ONE seam closes it for walls, buildings, gates,
// towers, collectors and harvest sites with no edit to any gameplay class.
//
// THREE CHANNELS, DELIBERATELY REDUNDANT (owner is colourblind - memory
// `owner-colorblind-delegate-visual-creative`): MOTION (the dust burst that was
// already here), SOUND (SfxId.StructureImpact, new) and the NUMBER. No tint carries
// any meaning; remove any one channel and the blow still reads.
//
// THE NUMBER IS THE DAMAGE ACTUALLY APPLIED, NOT THE REQUEST. This component never
// sees the incoming damage request - it sees the HP FRACTION move, which is the state
// AFTER WallSegment.ApplyDamage's tier divide (WallSegment.cs:334) and after the
// Faction-gated BULWARK reduction (:343-344). Multiplied by the structure's own max HP
// that is exactly the points that landed. A steel wall therefore shows the small number
// it really took, and the read is post-divide BY CONSTRUCTION rather than by a value
// someone remembered to forward. (Wall: RepairTarget.DamageFraction is
// `_wall.Damage / 100f` and WallSegment.MaxHp is 100 - so drop x maxHp == `effective`
// exactly. Gate/Building wrap their own HpFraction, same identity.)
//
// ACCUMULATE, NEVER DISCARD. The cadence gates are throttles on the TELL, not on the
// damage: a drop landing inside a cooldown window is added to a pending total and
// flushed on the next eligible frame. Six troops chewing one panel therefore produce a
// readable series of honest sums instead of either a wall of overlapping text or a
// number that under-reports what the panel actually lost.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Watches one structure's HP fraction and fires a one-shot impact burst the frame
    /// it drops. Presentation only: it never reads or writes gameplay state beyond the
    /// read-only fraction delegate it is handed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureHitReaction : MonoBehaviour
    {
        /// <summary>
        /// Minimum seconds between bursts on ONE structure. A wave contact-attacking a
        /// wall lands hits far faster than the eye separates them, so without this the
        /// tell would be a continuous smear instead of a series of blows.
        /// </summary>
        private const float MinBurstInterval = 0.15f;

        /// <summary>
        /// Minimum HP-fraction drop that counts as a hit. Guards against float jitter and
        /// against a max-HP recalculation (ApplyStructureHpMultiplier, tier bonuses) being
        /// mistaken for damage.
        /// </summary>
        private const float MinDropFraction = 0.004f;

        /// <summary>
        /// A drop larger than this in one frame is a re-scale or a save-restore, not a
        /// blow - fire the flinch but do not treat the number as meaningful. Kept so a
        /// reload that restores a half-damaged structure does not read as a huge hit.
        /// </summary>
        private const float MaxCredibleDrop = 0.75f;

        /// <summary>
        /// Minimum seconds between floating NUMBERS on one structure. Deliberately
        /// LONGER than <see cref="MinBurstInterval"/>: DamageNumberSpawner.Lifetime is
        /// 0.55 s, so 0.35 s keeps at most two numbers alive over a structure at once -
        /// legible - while the dust keeps its existing 0.15 s cadence so the MOTION
        /// channel still reads one puff per blow. The damage that lands between numbers
        /// is not thrown away: it accumulates into the next one (see _pendingNumberDrop),
        /// so a six-troop warband shows fewer, larger, still-honest numbers.
        /// </summary>
        private const float MinNumberInterval = 0.35f;

        /// <summary>
        /// A number below this many points rounds to "0" in DamageNumberSpawner and
        /// would read as a miss. Below it we hold the damage back into the next number
        /// rather than print a zero.
        /// </summary>
        private const float MinShownDamage = 0.5f;

        /// <summary>One sample's verdict, returned by <see cref="TrySample"/>.</summary>
        public struct HitSample
        {
            /// <summary>Accumulated 0..1 HP fraction this blow (or burst of blows) removed.</summary>
            public float DropFraction;
            /// <summary>
            /// Points actually applied, ready to show. ZERO means "do not print one this
            /// frame" - either the structure's max HP is unknown, the number is not due
            /// yet, or the sum still rounds to zero. The damage is never lost, only held.
            /// </summary>
            public float Damage;
            /// <summary>Renderer-bounds centre - where the tell belongs.</summary>
            public Vector3 At;
            /// <summary>
            /// False when the drop exceeds <see cref="MaxCredibleDrop"/>: a re-scale or a
            /// save-restore, not a blow. The flinch still plays; the number and the sound
            /// are withheld, because a "72" popping on scene load is a lie.
            /// </summary>
            public bool Credible;
        }

        private Func<float> _hpFraction;
        private Func<float> _maxHp;          // structure's own max HP, for the number
        private Action      _onHit;          // optional host-owned flinch (see the Heart)
        private string      _label = "structure";
        private float       _last  = -1f;    // -1 = not yet sampled
        private float       _nextBurstAt;
        private float       _nextNumberAt;
        private float       _pendingDrop;        // held for the flinch cadence
        private float       _pendingNumberDrop;  // held for the number cadence

        /// <summary>
        /// Attach (or re-point) the hit reaction on <paramref name="host"/>. Idempotent -
        /// a second call re-points the SAME component, so two bootstraps wiring the same
        /// structure cannot produce two bursts per hit.
        /// </summary>
        /// <param name="host">The structure GameObject.</param>
        /// <param name="hpFraction">Read-only 0..1 HP fraction. Must be null-safe.</param>
        /// <param name="label">Trace label.</param>
        /// <param name="onHit">
        /// Optional callback the HOST owns, invoked on the same frame as the burst. This is
        /// how the Heart adds its own flinch (a kick in its existing colour-free pulse)
        /// without this component knowing anything about the Heart.
        /// </param>
        /// <remarks>
        /// There is deliberately NO scale parameter. VFXManager.Play(VFXType, ...) takes no
        /// scale (only the Hovl string-key path does), so a scale argument here would be a
        /// knob that silently did nothing - the exact shape of defect this codebase keeps
        /// paying for. If per-structure burst size is wanted, it belongs in the catalog row
        /// or in a scaled recipe, not in a parameter that cannot reach the manager.
        /// </remarks>
        /// <param name="maxHp">
        /// WO-1717: the structure's own maximum HP, used ONLY to turn the observed
        /// fraction drop into the points the player sees. Optional and null-safe - a
        /// structure that cannot name its max HP still gets the dust and the sound and
        /// simply prints no number, which is strictly better than printing a wrong one.
        /// LAST parameter on purpose: HeartAuraController.cs:346 calls this positionally.
        /// </param>
        public static StructureHitReaction Attach(GameObject host, Func<float> hpFraction,
                                                  string label, Action onHit = null,
                                                  Func<float> maxHp = null)
        {
            if (host == null || hpFraction == null) return null;
            var r = host.GetComponent<StructureHitReaction>();
            if (r == null) r = host.AddComponent<StructureHitReaction>();
            r._hpFraction = hpFraction;
            r._maxHp      = maxHp;
            r._onHit      = onHit;
            r._label      = string.IsNullOrEmpty(label) ? "structure" : label;
            r._last       = -1f;   // re-baseline: never fire a burst for the attach itself
            r._pendingDrop = 0f;
            r._pendingNumberDrop = 0f;
            return r;
        }

        /// <summary>
        /// THE DECISION HALF - pure of Unity's frame clock, of the camera, of the VFX
        /// pool and of the audio device, so the headless oracle
        /// (StructureFeedbackRegression) can drive it directly and assert the value, the
        /// accumulation and the cadence without a PlayMode session. Update supplies
        /// Time.time; nothing else calls this in the shipping game.
        ///
        /// <para>Returns true on the frame a blow should be told. Mutates the baseline and
        /// the cadence exactly once per call, and invokes the host's own flinch
        /// (<c>onHit</c>) here rather than in the presentation half so that the HP-bar
        /// attach WO-1717 hangs off it is provable in the same breath.</para>
        /// </summary>
        /// <param name="hpNow">Current 0..1 HP fraction.</param>
        /// <param name="time">Seconds on the caller's clock (Update passes Time.time).</param>
        /// <param name="hit">The verdict; meaningless when this returns false.</param>
        public bool TrySample(float hpNow, float time, out HitSample hit)
        {
            hit = default;
            hpNow = Mathf.Clamp01(hpNow);

            if (_last < 0f) { _last = hpNow; return false; }   // first sample is a baseline, never a hit

            float delta = _last - hpNow;
            _last = hpNow;

            // Healed / unchanged / float noise contributes nothing, but it must NOT clear
            // what is already pending - a repair tick between two blows would otherwise
            // swallow the blows.
            if (delta > 0f)
            {
                _pendingDrop       += delta;
                _pendingNumberDrop += delta;
            }

            if (_pendingDrop < MinDropFraction) return false;   // nothing worth telling yet
            if (time < _nextBurstAt) return false;              // still inside this structure's cadence
            _nextBurstAt = time + MinBurstInterval;

            hit.DropFraction = _pendingDrop;
            _pendingDrop     = 0f;
            hit.Credible     = hit.DropFraction <= MaxCredibleDrop;
            hit.At           = BoundsCentre();

            // The NUMBER runs on its own, slower cadence and its own accumulator. An
            // incredible drop (a re-scale, a save-restore) is DISCARDED from the number
            // rather than held, because carrying it forward would poison the next honest
            // number with a value that never was damage.
            if (!hit.Credible)
            {
                _pendingNumberDrop = 0f;
            }
            else if (time >= _nextNumberAt)
            {
                float max = 0f;
                if (_maxHp != null)
                    max = Guard.Try("DamageVis", $"max-hp source '{_label}'", _maxHp, fallback: 0f);

                if (max > 0f)
                {
                    float points = _pendingNumberDrop * max;
                    if (points >= MinShownDamage)
                    {
                        hit.Damage         = points;
                        _pendingNumberDrop = 0f;
                        _nextNumberAt      = time + MinNumberInterval;
                    }
                    // Below the floor: hold it. The next blow adds to it and the number
                    // that eventually prints is the true sum, never a "0".
                }
            }

            if (_onHit != null) Guard.Try("DamageVis", $"host hit-flinch '{_label}'", _onHit);
            return true;
        }

        /// <summary>
        /// Bounds CENTRE, not the pivot and not above the mesh: a wall's pivot is at its
        /// foot, and a burst on the ground under a wall does not read as the wall being
        /// struck. Recomputed per burst because a structure can swap its tier model.
        /// </summary>
        private Vector3 BoundsCentre()
        {
            Vector3 at = transform.position + Vector3.up * 0.5f;
            var rends = GetComponentsInChildren<Renderer>(false);
            bool have = false;
            Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                var rr = rends[i];
                if (rr == null || rr is ParticleSystemRenderer) continue;   // never the effect's own particles
                if (!have) { b = rr.bounds; have = true; }
                else b.Encapsulate(rr.bounds);
            }
            return have ? b.center : at;
        }

        private void Update()
        {
            if (_hpFraction == null) return;

            float now;
            try
            {
                now = Mathf.Clamp01(_hpFraction());
            }
            catch (Exception e)
            {
                // No silent failures (CLAUDE.md section 12): a source that throws is
                // dropped loudly, and this component goes quiet rather than spamming.
                FlowTrace.Fail("DamageVis",
                    $"StructureHitReaction '{_label}': HP source threw ({e.Message}) - hit flinch disabled " +
                    "on this structure. The damage-state ladder is unaffected.");
                _hpFraction = null;
                return;
            }

            // THE DECISION HALF. Everything below this line is presentation - and it is
            // below the line precisely so the oracle can prove the decision without any
            // of it (no camera, no VFX pool, no audio device, no frame clock).
            if (!TrySample(now, Time.time, out HitSample hit)) return;

            // MOTION. Family B one-shot: no handle, no loop slot, nothing to stop.
            // Env_DestructionDust is the LANDED enum value whose own doc names this exact
            // moment - "Destroyable object impact dust (barrel, crate, wall section)" - so
            // this is transcription, not a new creative pick.
            VFXManager.Play(VFXType.Env_DestructionDust, hit.At);

            if (hit.Credible)
            {
                // NUMBER. The ONE pool (WO-953 one-pool ruling): DamageNumberSpawner is the
                // project's only floating-text pool and this is a new CALLER of it, never a
                // second pool. Zero means "not due / not knowable this frame", never "no damage".
                if (hit.Damage > 0f)
                    DamageNumberSpawner.Spawn(hit.Damage, hit.At);

                // SOUND. Null-safe end to end: no AudioService yet is a no-op, and an
                // SfxId with no authored clip falls through to ProceduralSfx's synth
                // (AudioService.cs:663-676) rather than going silent - so the third channel
                // is audible on a fresh clone with zero asset wiring.
                DeNelle.Audio.AudioService.Instance?.PlaySfxAtPosition(
                    DeNelle.Audio.SfxId.StructureImpact, hit.At);
            }

            if (!hit.Credible)
                FlowTrace.Throttle("DamageVis", "hit-implausible", 10f,
                    $"StructureHitReaction '{_label}': a {hit.DropFraction:P0} drop in one frame is a re-scale " +
                    "or a save-restore rather than a blow - the flinch played, but the NUMBER and the SOUND were " +
                    "withheld so the tell cannot lie about damage that never happened.");
            else
                FlowTrace.Throttle("DamageVis", "hit:" + _label, 2f,
                    $"HIT '{_label}': hp {(_last + hit.DropFraction):0.00} -> {_last:0.00} " +
                    $"(-{hit.DropFraction:0.000}) - flinch + sound fired, number={hit.Damage:0.#} " +
                    "(0 = held for the next number, never lost). Before WO-1717 a struck structure " +
                    "produced no number and no sound at all.");
        }
    }
}

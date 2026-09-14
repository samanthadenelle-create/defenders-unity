// =============================================================================
// HeartAuraController -- the persistent sacred aura around the Heart of Elarion.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// PRESENTATION (read-only): a gentle, always-on aura that makes the Heart read as
// "the sacred thing you defend". This is the "add an aura effect to the tree" half
// of the owner request. It ONLY READS HeartController.Hp -- it never heals or
// mutates the Heart (that is HeartRegen's job). Presentation stays a separate layer.
//
// REUSE: plays the canonical VFXType.Aura_HeartPulse loop ("Heart of Elarion
// ambient pulse -- nucleus loop") through the existing VFXManager pool -- the enum
// value already existed for exactly this and was previously unwired. No new art is
// authored; if no prefab is catalogued the manager's procedural aura loop fills in.
//
// COLOR-FREE HEALTH TELL (the owner is colorblind -- never encode meaning by color
// alone; use motion / shape / luminance):
//   * SHAPE/SIZE   -- the aura swells when the Heart is healthy, shrinks when hurt.
//   * LUMINANCE    -- a soft light glows bright when healthy, dim when hurt.
//   * MOTION       -- a slow calm "breath" when healthy accelerates into a fast
//                     anxious flicker as HP falls.
// The light hue is a fixed neutral warm-white and NEVER changes with health, so no
// information is carried by color. A player who sees no color at all still reads the
// Heart's condition from size + brightness + pulse rate.
//
// ATTACH: self-bootstraps onto the HeartController GameObject at runtime (the
// canonical reactive-bridge pattern used by HeartwoodAmbientController) so no
// curated .unity scene is hand-edited.
//
// INSTRUMENTATION (CLAUDE.md section 12): FlowTrace.Step on aura start + on each
// health-tier transition (Healthy / Strained / Critical); FlowTrace.Once/Warn when
// the VFX loop cannot start so a missing manager/catalog self-reports instead of
// the Heart just looking auraless.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Drives the Heart's persistent sacred aura -- a pooled Aura_HeartPulse loop plus
    /// a soft glow light, whose size / brightness / pulse-rate read the Heart's health
    /// without relying on color. Read-only: it never mutates <see cref="HeartController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeartController))]
    public sealed class HeartAuraController : MonoBehaviour
    {
        // Health tier, for Step-tracing a color-free readout transition (not for color).
        private enum AuraTier { Unknown, Healthy, Strained, Critical }

        [Header("Heart (auto-wired to the HeartController on this GameObject)")]
        [SerializeField] private HeartController _heart;

        [Header("Aura placement")]
        [Tooltip("Local offset from the Heart pivot where the aura + glow sit (mid-trunk).")]
        [SerializeField] private Vector3 _auraOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Color-free health tell (size)")]
        [Tooltip("Aura scale when the Heart is critically hurt (HP 0).")]
        [SerializeField, Min(0.1f)] private float _scaleHurt = 0.7f;
        [Tooltip("Aura scale when the Heart is fully healthy (HP 100).")]
        [SerializeField, Min(0.1f)] private float _scaleHealthy = 1.15f;

        [Header("Color-free health tell (luminance)")]
        [Tooltip("Glow light intensity when the Heart is critically hurt.")]
        [SerializeField, Min(0f)] private float _lightHurt = 0.6f;
        [Tooltip("Glow light intensity when the Heart is fully healthy.")]
        [SerializeField, Min(0f)] private float _lightHealthy = 2.2f;
        [Tooltip("Glow light range (world units). Constant -- unaffected by the size pulse.")]
        [SerializeField, Min(0f)] private float _lightRange = 9f;

        [Header("Color-free health tell (motion)")]
        [Tooltip("Pulse frequency (Hz) when healthy -- a slow, calm breath.")]
        [SerializeField, Min(0f)] private float _pulseHzHealthy = 0.5f;
        [Tooltip("Pulse frequency (Hz) when hurt -- a fast, anxious flicker.")]
        [SerializeField, Min(0f)] private float _pulseHzHurt = 2.2f;
        [Tooltip("Pulse depth (fraction) when healthy -- barely breathing.")]
        [SerializeField, Range(0f, 1f)] private float _pulseDepthHealthy = 0.12f;
        [Tooltip("Pulse depth (fraction) when hurt -- a hard flicker.")]
        [SerializeField, Range(0f, 1f)] private float _pulseDepthHurt = 0.5f;

        // Fixed neutral warm-white -- the ONE thing that never changes with health, so no
        // meaning is ever carried by color. (Colorblind-safe: hue is a constant.)
        private static readonly Color GlowColor = new Color(1f, 0.96f, 0.9f);

        // Owner VfxManualPick: the Heart of Elarion IS the world tree -- a persistent ambient
        // Tree-of-Life aura loop sits at the trunk, held through the pooled VFXManager (the same
        // PlayKey/VFXHandle held-loop pattern as HealingFountain's HealingFountain_Aura) and
        // Stop()'d on destroy. It is ADDITIVE to the Aura_HeartPulse health-tell nucleus and is
        // parented to the Heart ROOT (not the health-size pivot) so the ambient glow reads
        // constant, unaffected by the color-free size pulse above.
        // WO-1002 / owner F8 seq 2306: the key itself is UNCHANGED (owner owns the tag) -- it now
        // comes from AmbientAuraPolicy so the hub tree and the harvest nodes read ONE definition of
        // "the rejected yellow loop", and a retag can never re-ship it past only one of the gates.
        private const string TreeAuraKey = AmbientAuraPolicy.WithheldAmbientAuraKey;

        // HP tier cut points for the Step-traced readout (mirrors HeartwoodAmbient tiers).
        private const float HealthyMin  = 75f;
        private const float StrainedMin = 40f;
        private const float FullHp      = 100f;

        private Transform _pivot;
        private Light _light;
        private VFXHandle _handle;
        private VFXHandle _treeHandle;   // persistent Tree-of-Life ambient loop (TreeofLifeAura_Aura)
        // WO-1343 Ask 1: the SECOND, ADDITIVE aura at the tree's FOOT, BOUND 2026-09-03 to
        // HeldVfxKeys.TreeOfLifeFootAura (her re-tagged Aura_Nature loop). It is ADDITIVE to the
        // FireFlies crown loop above - both play. Declared and torn down alongside _treeHandle, so
        // the two ambient loops share one lifecycle and there is no second spawner or pool.
        private VFXHandle _footHandle;
        private AuraTier _tier = AuraTier.Unknown;
        private float _pulsePhase;   // accumulated so a frequency change never snaps the wave

        // ── CROWN TETHER + STRAY-HEAL FIX (owner felt-test 2026-07-24, on device) ─────────────────
        // Symptom: two auras float in the OPEN FIELD, offset from the visible Tree of Life; the WHITE
        // Aura_HeartPulse swirl reads as the stray "heal VFX" the owner has repeatedly asked be gone
        // from the static town. (The tree DOES render -- it is NOT missing.)
        // RCA (position offset): HeartAuraController lives on the CLEAN scale-1 anchor 'HeartOfElarion';
        // the visible tree is a CHILD 'TreeOfLife_Visual' authored at localScale 7 + localRotation
        // Euler(-90,0,0) (CastleHubBuilder.WireCastleHeart ~:2468-2470). The Tripo fbx geometry is
        // OFFSET from its pivot, so at scale 7 through a -90 X-rotation the rendered trunk/canopy lands
        // METRES from the anchor origin in world XZ. SeatOnGroundOnStart only shifts Y (never XZ), so
        // the tree renders far from the anchor while the auras -- seated at anchor + _auraOffset --
        // sit at the anchor: hence "auras float in the field, offset from the tree".
        // FIX: seat both auras on the tree's LIVE renderer bounds (crown), re-seated each throttle tick
        // so the LATE ground-snap can't strand them; and DO NOT spawn the white swirl on a hub Heart
        // (one that has a visible tree centerpiece). Parenting stays on the scale-1 ANCHOR (never the
        // scale-7 tree, which would inflate the pooled effect 7x) so the health-tell pulse/glow math
        // is unpolluted.
        private const float CrownTrackInterval = 0.5f;   // throttle for the live-crown re-seat
        private bool  _hasTreeBody;                       // a visible non-particle tree renderer exists (hub centerpiece)
        private bool  _suppressWhiteSwirl;                // hub static-town Heart: white Aura_HeartPulse withheld
        private bool  _suppressTreeAura;                  // hub static-town Heart: TreeofLifeAura_Aura withheld (WO-1002)
        private float _crownTrackTimer;
        private bool  _crownReported;                     // §12 Once-trace guard

        // -- WO-1025 STEP 1: PRESENTATION AUDIT (INSTRUMENT, do not fix) --------------------------
        // The owner's screenshot shows a yellow cone + white starburst at the hub tree while BOTH
        // authored loops are traced as WITHHELD (crown-tether line) -- so an UNIDENTIFIED emitter is
        // drawing. Per CLAUDE.md sec 12 no edit is earned until a captured trace NAMES it. This audit
        // dumps, as MEASURED outcomes (INSTRUMENTATION_STANDARD sec 1.4b: resolved world transforms / live
        // materials / renderer states, never authored intent):
        //   1. every ParticleSystem / Renderer / Projector / Light under the Heart root (children of
        //      HeartOfElarion incl. TreeOfLife_Visual -- WO-1025 sec 2b says the emitter is
        //      scene-attached or runtime-spawned there),
        //   2. every non-child ParticleSystem / Projector within AuditNearRadius of the anchor
        //      (another system spawning AT the Heart's transform, e.g. alongside HeartRegen), and
        //   3. every 'TreeOfLife_Visual' instance scene-wide (a stale duplicate that escaped the
        //      CastleHubBuilder dedup pass is itself a live suspect -- WO-1025 sec 2b),
        // each with its VFX catalog identity (pool names carry the key: '[VFX_<type>]' /
        // '[ProceduralLoop_<type>]') or 'not catalogued', plus world position and lossy scale.
        // Runs TWICE: EARLY (end of BuildAura) and SETTLED (+4s, after the late ground-snap and any
        // late spawner), so a component destroyed/spawned between the two is visible as a diff.
        private const float AuditSettleDelay = 4f;    // > the ~2.5s late ground-snap window
        private const float AuditNearRadius  = 25f;   // world metres around the anchor, XZ
        private const int   AuditMaxLines    = 40;    // per-section cap so a runaway pool can't firehose
        private float _auditSettleTimer = AuditSettleDelay;
        private bool  _auditSettledDone;

        // -- Self-bootstrap (attach onto the Heart at runtime; no scene edit) -----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedStatic;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;
            AttachToHearts();
        }

        private static void OnSceneLoadedStatic(
            UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode mode)
            => AttachToHearts();

        private static void AttachToHearts()
        {
            var hearts = Object.FindObjectsByType<HeartController>();
            foreach (var heart in hearts)
            {
                if (heart == null) continue;
                if (heart.GetComponent<HeartAuraController>() == null)
                    heart.gameObject.AddComponent<HeartAuraController>();
            }
        }

        private void Reset() => _heart = GetComponent<HeartController>();

        private void Awake()
        {
            if (_heart == null) _heart = GetComponent<HeartController>();
        }

        private void Start()
        {
            BuildAura();
        }

        private void OnDestroy()
        {
            _handle?.Stop(immediate: true);
            _handle = null;
            _treeHandle?.Stop(immediate: true);
            _treeHandle = null;
            // WO-1343: the foot seat's teardown ships with the seat, not later. It is null today
            // (the key is held) and this line is a no-op - which is exactly the point: binding the
            // key becomes a data change with no lifecycle work left to remember.
            _footHandle?.Stop(immediate: true);
            _footHandle = null;
        }

        // -- Aura construction ---------------------------------------------------

        private void BuildAura()
        {
            using var _ = FlowTrace.Enter("Heart", $"HeartAura BuildAura on '{name}'");

            // Is this the hub static-town Heart (a visible Tree-of-Life centerpiece under the
            // anchor)? Compute the crown FIRST -- before any pivot/particle is created -- so the
            // renderer scan sees only the tree. When a tree exists the white swirl is withheld and
            // both auras seat on the crown; otherwise the legacy anchor+offset seat is kept.
            _hasTreeBody = TryComputeCrown(out Vector3 crown, out Vector3 canopy, out Vector3 foot);
            _suppressWhiteSwirl = _hasTreeBody;

            // WO-1002 + owner F8 seq 2306 ("i do not want that vfx used at all"): the GREEN/gold
            // TreeofLifeAura_Aura FireFlies loop is the yellow plume at the hub world tree's base.
            // It rides the SAME hub-detection gate as the white swirl above -- _hasTreeBody, the one
            // predicate that means "this Heart is the static-town centerpiece with a visible tree".
            // NO second detection is invented: a bare combat/raid Heart has no tree body, so it keeps
            // its aura exactly as before (the withhold is HUB-ONLY, per WO-1002 section 1).
            // WO-1025 (owner 2026-08-16, "use the butterflies or fireflies" / "that was already
            // there"): AmbientAuraPolicy.HeartTreeFirefliesExempt now EXEMPTS this one key at this
            // one site, so the predicate below returns FALSE at the hub and the FireFlies loop
            // plays at the crown again. Both branches stay wired; the withhold trace still fires
            // if the exemption is ever turned off.
            _suppressTreeAura = ShouldWithholdTreeAura(_hasTreeBody);

            // A pivot child hosts the glow + owns the size pulse, so scaling the aura never pollutes
            // the pooled VFX GameObject's own localScale. Parent to the ANCHOR (scale 1, unrotated)
            // so the pulse/glow math is clean; when a tree exists the pivot sits on the canopy (world)
            // instead of anchor+offset, so the glow reads ON the tree, not out in the field.
            var pivotGo = new GameObject("[HeartAuraPivot]");
            _pivot = pivotGo.transform;
            _pivot.SetParent(transform, false);
            if (_hasTreeBody) _pivot.position = canopy;
            else              _pivot.localPosition = _auraOffset;

            // Soft neutral glow -- the luminance channel of the health tell.
            _light = pivotGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = GlowColor;
            _light.range = _lightRange;
            _light.intensity = _lightHealthy;
            _light.shadows = LightShadows.None;

            // (A) WHITE pulse nucleus (Aura_HeartPulse -- CLI-assigned "Buff white twist"). Owner
            // 2026-07-24: in the static town it reads as a stray heal VFX -> DO NOT spawn it on a hub
            // Heart. No key swap -- the emitter is simply withheld (VFX no-pick rule intact).
            if (_suppressWhiteSwirl)
                FlowTrace.Step("Heart",
                    $"HeartAura '{name}': hub centerpiece Heart -> WHITE Aura_HeartPulse swirl WITHHELD " +
                    "(owner: stray heal VFX removed from static town). Key unchanged; emitter not spawned.");
            else
                StartWhiteNucleus();

            // (B) GREEN Tree-of-Life ambient loop (TreeofLifeAura_Aura -- OWNER-TAGGED FireFlies; key
            // verbatim, NEVER swapped). Seated tightly on the tree CROWN (live renderer bounds) when a
            // tree exists, else the legacy anchor+offset. Crown-tracked each tick in Update.
            //
            // WO-1002 / F8 2306: on the HUB centerpiece it is now WITHHELD -- and the withhold is
            // TRACED, never a silent early-return, because "nobody noticed for three days" is exactly
            // what a silent withhold buys. Combat/raid Hearts fall through and still start it.
            if (_suppressTreeAura)
                FlowTrace.Step("Heart",
                    $"HeartAura '{name}': hub centerpiece Heart -> " +
                    AmbientAuraPolicy.WithholdReason("tree-base ambient loop") +
                    $" (treeBody={_hasTreeBody}, anchorPos={transform.position:F2}) -- combat/raid Hearts unaffected.");
            else
                StartGreenTreeAura(_hasTreeBody ? crown : transform.position + _auraOffset);

            // (B2) WO-1343 Ask 1 - THE FOOT OF THE TREE, and it is ADDITIVE.
            //
            // Owner ask, verbatim: "one for the foot of the tree of life, TO GO WITH THE OTHER ONE".
            // "To go with" means BOTH PLAY. The FireFlies loop above is untouched - not removed, not
            // re-pointed, not rescaled - and this is a SECOND, independent seat at the base.
            //
            // (S) THE KEY IS NOW BOUND (2026-09-03). The seat shipped HELD because the VFX Caster
            // had silently overwritten her Aura row with Elite_Death.prefab while she was tagging a
            // BOSS DEATH, and had invented an '_Impact' sibling she never authored. She has since
            // re-pointed the Aura row to Aura_Nature.prefab (isLoop=true) and DELETED the invented
            // sibling, so the row is hers again. The key still lives in exactly ONE place -
            // HeldVfxKeys.TreeOfLifeFootAura - and is never written as a bare literal here; that is
            // pinned by NightStoreAuraSelectionRegression Case 6.
            //
            // Nothing here chooses or rescales a prefab: scale 0 means the catalog row's own
            // DefaultScale. If the catalog has not been regenerated for this key the hook traces the
            // unresolved seat by name and the remedy (Defenders/VFX/Generate Hovl VFX Catalog)
            // rather than drawing nothing quietly.
            //
            // It rides the SAME lifecycle as the FireFlies seat above - built once in BuildAura,
            // seated off the same renderer-bounds scan - so there is no second spawner and no second
            // pool (CLAUDE.md s7).
            // WO-1476 (owner 2026-09-07, verbatim): "there is a VFX exiting about town along Y and
            // it needs removed or turned off". The riser is THIS seat, proven from the prefab bytes
            // rather than picked between the WO's two candidates: Aura_Nature's "Energy" sub-emitter
            // carries VelocityModule enabled with y = 0.3-0.5 u/s over a 2-4 s lifetime, while the
            // FireFlies loop at (B) has VelocityModule/ForceModule disabled and gravity 0 and so
            // cannot rise at all. See AmbientAuraPolicy's WO-1476 block for the full field values.
            //
            // Withheld the SAME way WO-1002 withheld the FireFlies loop -- the owner's
            // VfxManualPicks row is byte-intact (it is hers, and NightStoreAuraSelectionRegression
            // pins it), the seat and its position survive, and the absence is TRACED so a capture
            // says why rather than showing a hole. One readonly flag restores it.
            if (AmbientAuraPolicy.ShouldWithholdTreeFootAura(HeldVfxKeys.TreeOfLifeFootAura))
                FlowTrace.Step("Heart",
                    $"HeartAura '{name}': " +
                    AmbientAuraPolicy.TreeFootWithholdReason("tree-of-life foot seat") +
                    $" (treeBody={_hasTreeBody}, footPos={(_hasTreeBody ? foot : transform.position):F2}).");
            else
                _footHandle = HeldVfxHook.Play(
                    "tree-of-life foot",
                    HeldVfxKeys.TreeOfLifeFootAura,
                    _hasTreeBody ? foot : transform.position,
                    transform,
                    0f,                 // catalog DefaultScale - nothing here rescales her prefab
                    "WO-1343 Ask 1: this seat WAS bound on 2026-09-03 to her re-tagged Aura row " +
                    "(Aura_Nature.prefab). An EMPTY key here means HeldVfxKeys.TreeOfLifeFootAura was " +
                    "blanked by a later edit - restore the constant; do not re-type the key at this " +
                    "call site.");

            // (C) WO-891 (adjacent, reported): THE HEART DID NOT FLINCH WHEN STRUCK.
            //
            // Precisely: it has a good STATE read and had no EVENT read. Everything above
            // is lerped continuously off Hp - size, luminance and pulse rate all track
            // health, and all of it is colour-free, which is right. But it is lerped across
            // the WHOLE 0-100 range, so one contact hit moves the aura by a percent or two:
            // invisible. And StructureDamageVisuals deliberately never scans HeartController
            // (bespoke tell, StructureDamageVisuals.cs), so nothing else reacted either.
            // HeartController.SetHp raised OnHealthChanged into a room with no VFX listener.
            //
            // StructureHitReaction supplies the per-hit dust burst; NotifyHit below adds the
            // Heart's OWN recoil in the channels this controller already owns (a deeper,
            // faster flicker and a momentary shrink that recovers). Both are shape + rhythm,
            // no hue, and the burst is a Family B one-shot so it cannot cost a loop slot.
            StructureHitReaction.Attach(gameObject,
                () => _heart != null ? Mathf.Clamp01(_heart.Hp / FullHp) : 1f,
                "HeartOfElarion",
                NotifyHit,
                // WO-1717: the Heart's own max HP, so a blow on the thing the game is named
                // for prints the points it cost - the same number channel every other
                // structure now gets. Its fraction above is Hp / FullHp, so the product is
                // the HP actually lost, exactly.
                () => FullHp);

            // Seed the readout so tier one is traced from the first frame.
            ApplyHealthTell(force: true);

            // WO-1025 STEP 1: name the unidentified emitter. Hub centerpiece only -- the artifact
            // is at the hub tree; combat/raid Hearts are not the subject and would only add noise.
            if (_hasTreeBody) AuditHeartPresentation("EARLY");
        }

        // -- The recoil (WO-891 adjacent) -----------------------------------------

        /// <summary>How fast the flinch decays back to rest. ~0.4 s of visible recoil.</summary>
        private const float FlinchDecayPerSecond = 2.5f;

        /// <summary>Extra pulse depth at full flinch - the tree flickers harder for a beat.</summary>
        private const float FlinchDepthBoost = 0.45f;

        /// <summary>How far the aura SHRINKS at full flinch. A recoil, then a recovery.</summary>
        private const float FlinchShrink = 0.22f;

        /// <summary>Extra pulse rate (Hz) at full flinch - the beat quickens on the blow.</summary>
        private const float FlinchHzBoost = 3.0f;

        private float _flinch;   // 1 on the frame of a hit, decaying to 0

        /// <summary>
        /// The Heart was struck. Kicks the recoil that <see cref="ApplyHealthTell"/> reads.
        /// Driven by <see cref="StructureHitReaction"/>, which watches the HP fraction, so
        /// EVERY damage source reaches it - contact attacks, the dragon, and anything future
        /// that routes through <c>SetHp</c> - without a new event or a gameplay edit.
        /// </summary>
        private void NotifyHit() => _flinch = 1f;

        // -- VFX loop lifecycle ---------------------------------------------------

        /// <summary>Spawn the WHITE Aura_HeartPulse pulse nucleus (CLI-assigned "Buff white twist"),
        /// parented to the health-size pivot. Only called on a NON-hub Heart (a hub centerpiece
        /// withholds it -- owner: stray heal VFX). Key is verbatim; never substituted.</summary>
        private void StartWhiteNucleus()
        {
            if (VFXManager.Instance == null)
            {
                FlowTrace.Once("Heart", $"aura-nomanager:{name}",
                    $"HeartAura '{name}': VFXManager.Instance is null -- the pooled Aura_HeartPulse loop " +
                    "will not appear (the glow light still reads the health tell).");
                return;
            }
            _handle = VFXManager.Instance.PlayAura(VFXType.Aura_HeartPulse, transform);
            if (_handle != null)
            {
                _handle.SetParent(_pivot, worldPositionStays: false);
                FlowTrace.Step("Heart",
                    $"HeartAura '{name}': Aura_HeartPulse nucleus started + parented to pivot (offset {_auraOffset}).");
            }
            else
            {
                FlowTrace.Warn("Heart",
                    $"HeartAura '{name}': PlayAura(Aura_HeartPulse) returned a NULL handle -- loop did not start " +
                    "(loop-cap hit or missing catalog prefab + failed procedural fallback); glow light still active.");
            }
        }

        /// <summary>
        /// THE WO-1002 DECISION, as a pure predicate so it is testable headlessly (a live BuildAura
        /// needs a VFXManager + a play session, which would green-tick over a null).
        /// WO-1025 (owner rulings 2026-08-16, "For the tree of life use the butterflies or
        /// fireflies" + "that was already there"): the predicate now routes through
        /// <see cref="AmbientAuraPolicy.ShouldWithholdAtHeartTree"/>, whose fireflies exemption
        /// makes this FALSE at the hub -- the EXISTING FireFlies loop plays at the crown again.
        /// Everything else is unchanged: a combat/raid Heart (no tree body) is always FALSE and
        /// keeps its aura; harvest nodes still consult the un-exempted ShouldWithhold; and turning
        /// <see cref="AmbientAuraPolicy.HeartTreeFirefliesExempt"/> off restores the WO-1002
        /// withhold verbatim. Flipping <see cref="AmbientAuraPolicy.ShrinkInsteadOfWithhold"/>
        /// also makes it FALSE -- the hub then plays the loop small (0.2) instead of not at all.
        /// </summary>
        public static bool ShouldWithholdTreeAura(bool hasTreeBody)
            => hasTreeBody && AmbientAuraPolicy.ShouldWithholdAtHeartTree(TreeAuraKey);

        /// <summary>Spawn the OWNER-TAGGED GREEN Tree-of-Life ambient loop (<see cref="TreeAuraKey"/>
        /// FireFlies) at <paramref name="worldSeat"/> (the tree crown when a centerpiece exists).
        /// Parented to the scale-1 ANCHOR -- NOT the scale-7 tree, which would inflate the pooled
        /// effect 7x -- and crown-tracked in Update via SetPosition. Key is verbatim; never swapped.</summary>
        private void StartGreenTreeAura(Vector3 worldSeat)
        {
            if (VFXManager.Instance == null)
            {
                FlowTrace.Once("Heart", $"tree-aura-nomanager:{name}",
                    $"HeartAura '{name}': VFXManager.Instance is null -- the pooled {TreeAuraKey} loop will not appear.");
                return;
            }
            // WO-1002 fallback path: normally 1. If the owner flips
            // AmbientAuraPolicy.ShrinkInsteadOfWithhold to TRUE ("or set height to .2 so its small"),
            // the HUB tree plays the loop at 0.2 instead of not at all. A bare combat/raid Heart
            // (no tree body) is never resized -- the alternative is hub-scoped like the withhold.
            float scaleMul = _hasTreeBody ? AmbientAuraPolicy.ScaleFor(TreeAuraKey) : 1f;
            _treeHandle = VFXManager.PlayKey(
                TreeAuraKey,
                worldSeat,
                Quaternion.identity,
                transform,          // anchor (scale 1) -- clean world scale; crown-tracked via SetPosition
                null,               // keep the authored aura color (no tint)
                scaleMul);
            if (_treeHandle != null)
                FlowTrace.Step("Heart",
                    $"HeartAura '{name}': {TreeAuraKey} FireFlies ambient started at crown {worldSeat:F2} " +
                    $"(treeBody={_hasTreeBody}, anchorPos={transform.position:F2}, scaleMul={scaleMul:F2}) -- " +
                    "parented to anchor, crown-tracked.");
            else
                FlowTrace.Warn("Heart",
                    $"HeartAura '{name}': PlayKey('{TreeAuraKey}') returned a NULL handle -- ambient tree loop " +
                    "did not start (loop-cap hit or missing catalog row); the glow still reads.");
        }

        /// <summary>Compute the visible tree's CROWN + CANOPY-centre (world) from its LIVE non-particle
        /// renderer bounds. ParticleSystemRenderers are excluded so the aura's OWN FireFlies never
        /// count. Returns false when the Heart has no visible tree centerpiece (a bare combat/raid
        /// Heart) -> callers fall back to the legacy anchor+offset seat.</summary>
        private bool TryComputeCrown(out Vector3 crown, out Vector3 canopy, out Vector3 foot)
        {
            crown = default; canopy = default; foot = default;
            var rends = GetComponentsInChildren<Renderer>(false);
            bool have = false; Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!have) { b = r.bounds; have = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!have) return false;
            // Canopy centre = the enveloping glow seat; crown = high in the canopy for the FireFlies.
            canopy = new Vector3(b.center.x, Mathf.Lerp(b.center.y, b.max.y, 0.5f), b.center.z);
            crown  = new Vector3(b.center.x, Mathf.Lerp(b.center.y, b.max.y, 0.8f), b.center.z);
            // WO-1343 Ask 1: the FOOT / base footprint. Her key is literally "atfootprintoftree" -
            // GROUND LEVEL at the base radius, not the canopy and not the trunk mid-point. Taken
            // from the SAME live renderer bounds the crown comes from (b.min.y is the lowest drawn
            // point of the tree body), so the foot cannot drift away from the crown as the art
            // changes. Derived here rather than at the call site so there is one bounds scan.
            foot   = new Vector3(b.center.x, b.min.y, b.center.z);
            return true;
        }

        // -- WO-1025 STEP 1: presentation audit (read-only; changes NOTHING) ------

        /// <summary>Dump every candidate emitter/renderer at the hub tree as MEASURED runtime state
        /// (WO-1025 sec 3): children of the Heart root, near-field non-child emitters, and every
        /// TreeOfLife_Visual instance scene-wide. Each line carries the VFX catalog identity (or
        /// "not catalogued"), resolved world position/scale, and live material/shader/texture --
        /// never authored intent (INSTRUMENTATION_STANDARD §1.4b). Read-only.</summary>
        private void AuditHeartPresentation(string phase)
        {
            Guard.Try("Heart", $"WO-1025 presentation audit ({phase})", () =>
            {
                Vector3 anchor = transform.position;
                FlowTrace.Step("Heart",
                    $"WO-1025 AUDIT[{phase}] '{name}': anchorPos={anchor:F2}, " +
                    $"whiteSwirlSuppressed={_suppressWhiteSwirl}, treeAuraSuppressed={_suppressTreeAura} -- " +
                    $"dumping Heart-child hierarchy + non-child emitters within {AuditNearRadius:F0}m.");

                // 1) CHILDREN of the Heart root (incl. inactive -- a disabled-but-present emitter is
                //    still evidence). ParticleSystems first (the prime suspects), then non-particle
                //    renderers (tree body: proves whether the DEF-267 material fixer applied), then
                //    projectors and lights.
                int lines = 0;
                foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null) continue;
                    if (++lines > AuditMaxLines) { TruncNote(phase, "CHILD ParticleSystems"); break; }
                    EmitLine(phase, "CHILD", "ParticleSystem", ps.transform, DescribeParticle(ps));
                }
                lines = 0;
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer) continue;   // PS covered above
                    if (++lines > AuditMaxLines) { TruncNote(phase, "CHILD Renderers"); break; }
                    EmitLine(phase, "CHILD", r.GetType().Name, r.transform, DescribeRenderer(r));
                }
                lines = 0;
                foreach (var p in GetComponentsInChildren<Projector>(true))
                {
                    if (p == null) continue;
                    if (++lines > AuditMaxLines) { TruncNote(phase, "CHILD Projectors"); break; }
                    EmitLine(phase, "CHILD", "Projector", p.transform,
                        $"enabled={p.enabled}, mat='{(p.material != null ? p.material.name : "none")}'");
                }
                lines = 0;
                foreach (var l in GetComponentsInChildren<Light>(true))
                {
                    if (l == null) continue;
                    if (++lines > AuditMaxLines) { TruncNote(phase, "CHILD Lights"); break; }
                    EmitLine(phase, "CHILD", "Light", l.transform,
                        $"enabled={l.enabled}, type={l.type}, intensity={l.intensity:F2}, range={l.range:F2}");
                }

                // 2) NEAR-FIELD non-child emitters: another system spawning at the Heart's transform
                //    (WO-1025 sec 3 suspect #2 -- HeartRegen is live in the same capture) renders here
                //    without being a child, so a child-only dump would miss it entirely.
                int near = 0; lines = 0;
                foreach (var ps in FindObjectsByType<ParticleSystem>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (ps == null || ps.transform.IsChildOf(transform)) continue;
                    if (!WithinNearRadius(ps.transform.position, anchor)) continue;
                    near++;
                    if (++lines > AuditMaxLines) { TruncNote(phase, "NEAR ParticleSystems"); break; }
                    EmitLine(phase, "NEAR", "ParticleSystem", ps.transform, DescribeParticle(ps));
                }
                lines = 0;
                foreach (var p in FindObjectsByType<Projector>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (p == null || p.transform.IsChildOf(transform)) continue;
                    if (!WithinNearRadius(p.transform.position, anchor)) continue;
                    near++;
                    if (++lines > AuditMaxLines) { TruncNote(phase, "NEAR Projectors"); break; }
                    EmitLine(phase, "NEAR", "Projector", p.transform,
                        $"enabled={p.enabled}, mat='{(p.material != null ? p.material.name : "none")}'");
                }

                // 3) TreeOfLife_Visual instances SCENE-WIDE. CastleHubBuilder dedups extras
                //    (:1731-1758); a stale duplicate that escaped that pass is a live suspect
                //    (WO-1025 sec 2b). count!=1 at the hub is the anomaly this line exists to catch.
                var treeRoots = new HashSet<Transform>();
                foreach (var r in FindObjectsByType<Renderer>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r == null) continue;
                    for (var t = r.transform; t != null; t = t.parent)
                        if (t.name == "TreeOfLife_Visual") { treeRoots.Add(t); break; }
                }
                foreach (var t in treeRoots)
                    EmitLine(phase, "TREE", "TreeOfLife_Visual", t,
                        $"activeInHierarchy={t.gameObject.activeInHierarchy}, isHeartChild={t.IsChildOf(transform)}");
                if (treeRoots.Count != 1)
                    FlowTrace.Warn("Heart",
                        $"WO-1025 AUDIT[{phase}] '{name}': TreeOfLife_Visual count is {treeRoots.Count} " +
                        "(expected exactly 1 at the hub) -- a stale duplicate/missing tree is a live suspect.");

                FlowTrace.Step("Heart",
                    $"WO-1025 AUDIT[{phase}] '{name}': audit complete -- nearFieldEmitters={near}, " +
                    $"treeOfLifeVisualCount={treeRoots.Count}.");
            });
        }

        private static bool WithinNearRadius(Vector3 pos, Vector3 anchor)
        {
            float dx = pos.x - anchor.x, dz = pos.z - anchor.z;
            return (dx * dx + dz * dz) <= AuditNearRadius * AuditNearRadius;
        }

        private void EmitLine(string phase, string where, string kind, Transform t, string detail)
        {
            FlowTrace.Step("Heart",
                $"WO-1025 AUDIT[{phase}] {where} {kind} '{PathOf(t)}': key={CatalogIdentity(t)}, " +
                $"worldPos={t.position:F2}, lossyScale={t.lossyScale:F2}, " +
                $"activeInHierarchy={t.gameObject.activeInHierarchy}, {detail}");
        }

        private void TruncNote(string phase, string section)
        {
            FlowTrace.Warn("Heart",
                $"WO-1025 AUDIT[{phase}] '{name}': {section} truncated at {AuditMaxLines} lines -- " +
                "more exist than fit the cap (itself an anomaly worth reading).");
        }

        /// <summary>Shader property id for the guarded mainTexture reads below.</summary>
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        /// <summary>Measured particle state: playing/count/emission plus the LIVE renderer material,
        /// shader and main texture -- what is actually on screen, not what was authored.</summary>
        private static string DescribeParticle(ParticleSystem ps)
        {
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            var mat = psr != null ? psr.sharedMaterial : null;
            // HasProperty guard (F8 seq 2428-2431, 2026-08-16): Material.mainTexture LOGS AN ERROR
            // on any shader without _MainTex - Shader Graph materials (HS_Distortion) have none. An
            // AUDIT that spams the break-log with its own errors poisons the channel it exists to
            // read, so the probe reports the absence instead of provoking it.
            var tex = mat != null && mat.HasProperty(MainTexId) ? mat.mainTexture : null;
            return $"playing={ps.isPlaying}, particles={ps.particleCount}, " +
                   $"emissionEnabled={ps.emission.enabled}, rendererEnabled={(psr != null && psr.enabled)}, " +
                   $"mat='{(mat != null ? mat.name : "none")}', " +
                   $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "none")}', " +
                   $"mainTex='{(tex != null ? tex.name : "none")}'";
        }

        /// <summary>Measured renderer state: enabled, live material/shader/main texture (proves at
        /// runtime whether the DEF-267 TreeOfLifeMaterialFixer applied, and whether normal-less
        /// basecolor is all the tree has), and the resolved world bounds size.</summary>
        private static string DescribeRenderer(Renderer r)
        {
            var mat = r.sharedMaterial;
            var tex = mat != null && mat.HasProperty(MainTexId) ? mat.mainTexture : null;   // see DescribeParticle
            return $"enabled={r.enabled}, mat='{(mat != null ? mat.name : "none")}', " +
                   $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "none")}', " +
                   $"mainTex='{(tex != null ? tex.name : "none")}', boundsSize={r.bounds.size:F2}";
        }

        /// <summary>The pooled-VFX catalog identity, resolved from the LIVE hierarchy: VFXManager
        /// names pool instances '[VFX_&lt;type&gt;]' and procedural loops '[ProceduralLoop_&lt;type&gt;]',
        /// so an ancestor carrying either prefix IS the catalog key. A '(Clone)' with neither prefix
        /// was Instantiate'd OUTSIDE the pool (a second spawner -- itself a finding). Anything else
        /// is 'not catalogued' (scene-attached or baked art).</summary>
        private static string CatalogIdentity(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (cur.name.StartsWith("[VFX_") || cur.name.StartsWith("[ProceduralLoop_"))
                    return cur.name;
            }
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (cur.name.EndsWith("(Clone)"))
                    return $"NOT-POOLED clone '{cur.name}' (instantiated outside VFXManager)";
            }
            return "not catalogued";
        }

        /// <summary>Hierarchy path from the scene root (capped at 10 segments), so a trace line
        /// names WHERE an emitter lives without needing the editor open.</summary>
        private static string PathOf(Transform t)
        {
            string path = t.name;
            int depth = 0;
            for (var cur = t.parent; cur != null && depth < 10; cur = cur.parent, depth++)
                path = cur.name + "/" + path;
            return path;
        }

        // -- Per-frame color-free health tell ------------------------------------

        private void Update()
        {
            if (_heart == null) return;
            UpdateCrownTrack();
            ApplyHealthTell(force: false);

            // WO-1025 STEP 1: the SETTLED audit pass -- after the late ground-snap (~2.5s) and any
            // late runtime spawner, so what it dumps is what the player's screenshot actually shows.
            if (_hasTreeBody && !_auditSettledDone)
            {
                _auditSettleTimer -= Time.deltaTime;
                if (_auditSettleTimer <= 0f)
                {
                    _auditSettledDone = true;
                    AuditHeartPresentation("SETTLED");
                }
            }
        }

        // Keep the tree-seated auras locked on the LIVE crown so the LATE ground-snap (SeatOnGround
        // OnStart re-seats the tree's Y up to ~2.5s after load) can never strand them back in the
        // field. Throttled; no-op on a bare (treeless) Heart. Parenting stays on the scale-1 anchor,
        // so SetPosition writes a clean world position (no scale/rotation pollution from the tree).
        private void UpdateCrownTrack()
        {
            if (!_hasTreeBody) return;
            _crownTrackTimer -= Time.deltaTime;
            if (_crownTrackTimer > 0f) return;
            _crownTrackTimer = CrownTrackInterval;

            if (!TryComputeCrown(out Vector3 crown, out Vector3 canopy, out Vector3 foot)) return;

            if (_pivot != null) _pivot.position = canopy;   // glow follows the canopy centre
            _treeHandle?.SetPosition(crown);                // FireFlies follow the crown
            _footHandle?.SetPosition(foot);                 // WO-1343: foot seat tracks the base

            // §12: prove the tether -- anchor vs crown, and that the white swirl is suppressed.
            if (!_crownReported)
            {
                _crownReported = true;
                FlowTrace.Once("Heart", $"aura-crown:{name}",
                    $"HeartAura '{name}': crown-tether LIVE -> anchorPos={transform.position:F2}, crown={crown:F2}, " +
                    $"canopy={canopy:F2}, whiteSwirlSuppressed={_suppressWhiteSwirl}, " +
                    $"treeAuraSuppressed={_suppressTreeAura}, treeHandle={(_treeHandle != null ? "live" : "none")}. " +
                    "Auras now seated on the tree renderer bounds (the offset from the anchor is the RCA " +
                    "and is now corrected).");
            }
        }

        private void ApplyHealthTell(bool force)
        {
            float hp = _heart != null ? _heart.Hp : 0f;
            float hpFrac = Mathf.Clamp01(hp / FullHp);

            // The recoil decays every frame regardless of state, so a flinch is always a
            // short spike and never a latched mode (a latched flinch would fight the health
            // tell and misreport the Heart's condition).
            if (_flinch > 0f) _flinch = Mathf.Max(0f, _flinch - Time.deltaTime * FlinchDecayPerSecond);

            // Motion: pulse rate + depth accelerate/deepen as HP falls. Advance the phase
            // by the CURRENT frequency so a rate change is smooth, not a snap. The flinch
            // rides ON TOP of both, so a hit reads as a quickened, harder beat for ~0.4 s -
            // rhythm, which survives greyscale, rather than a colour flash, which does not.
            float hz    = Mathf.Lerp(_pulseHzHurt,    _pulseHzHealthy,    hpFrac) + _flinch * FlinchHzBoost;
            float depth = Mathf.Lerp(_pulseDepthHurt, _pulseDepthHealthy, hpFrac) + _flinch * FlinchDepthBoost;
            _pulsePhase += Time.deltaTime * hz;
            float pulse = 1f + Mathf.Sin(_pulsePhase * 2f * Mathf.PI) * depth;

            // Luminance: baseline brightness scales with HP, then the pulse rides on top.
            if (_light != null)
            {
                float baseIntensity = Mathf.Lerp(_lightHurt, _lightHealthy, hpFrac);
                _light.intensity = Mathf.Max(0f, baseIntensity * pulse);
            }

            // Shape/size: the aura swells with health; a gentle share of the pulse "breathes".
            if (_pivot != null)
            {
                float baseScale = Mathf.Lerp(_scaleHurt, _scaleHealthy, hpFrac);
                float breath = 1f + (pulse - 1f) * 0.15f;
                // The recoil: the aura SHRINKS on the blow and swells back as _flinch
                // decays. A visible "took that one" in shape, with no hue involved.
                float recoil = 1f - _flinch * FlinchShrink;
                _pivot.localScale = Vector3.one * (baseScale * breath * recoil);
            }

            // Step-trace only on a tier transition (color-free readout), not per frame.
            AuraTier next = TierForHp(hp);
            if (force || next != _tier)
            {
                _tier = next;
                FlowTrace.Step("Heart",
                    $"HeartAura readout -> {next} at HP {hp:F1}/100 " +
                    $"(size x{Mathf.Lerp(_scaleHurt, _scaleHealthy, hpFrac):F2}, " +
                    $"glow {Mathf.Lerp(_lightHurt, _lightHealthy, hpFrac):F2}, pulse {hz:F2}Hz) -- color-free.");
            }
        }

        private static AuraTier TierForHp(float hp)
        {
            if (hp >= HealthyMin)  return AuraTier.Healthy;
            if (hp >= StrainedMin) return AuraTier.Strained;
            return AuraTier.Critical;
        }
    }
}

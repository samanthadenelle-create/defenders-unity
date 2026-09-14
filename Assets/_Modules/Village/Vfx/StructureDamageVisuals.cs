// =============================================================================
// StructureDamageVisuals — WO-672 Slices B+D: the ONE presentation observer for
// structure damage state (F8-50, owner 2026-07-11: "is there a way to visually
// tell what is damaged? health bar or any notification, damaged maybe on fire?").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WO-892 (2026-08-06) RE-SKINNED THE RECIPES AND ADDED THE CRITICAL-SAVE BEACON.
// The observer logic below is UNCHANGED by design — the scan, the tracked set, the
// threshold reads, the worst-first cap and the read-only contract are all WO-672's.
// What changed is WHICH recipe each state plays, and that a fourth state exists.
//
// LAW (WO-672 / ARCHITECTURE §2): presentation NEVER touches the objects. This
// system only READS each structure's damage surface (HpFraction / IsBroken /
// RepairTarget.DamageFraction) and drives world tells from data thresholds:
//
//   hp <= scuffOnset    : WO-1352 SCUFF — the surface itself darkens and goes matte
//                         in discrete steps, through a MaterialPropertyBlock. NO
//                         particle, no GameObject, no pooled loop. This rung exists
//                         because the repair predicate is `DamageFraction > 0.0001`
//                         while the first visible tell used to be the smolder at 0.5,
//                         so 50%..99.99% HP was PRISTINE to the player and DAMAGED to
//                         the code — and Repair-All charged for it ("Repaired 1
//                         structures for Wood 35, Iron 7" on a building with no visible
//                         damage at all). The visual moved to meet the logic; the
//                         predicate, the pricing and Repair-All membership are UNTOUCHED.
//   hp <= smolder (0.5) : Damage_Smolder — a thin, slow smoke wisp. No flame.
//   hp <= fire    (0.25): Damage_Fire — MediumFlames + a merged SmokeEffect layer,
//                         so the step up is a FLAME APPEARING plus roughly double
//                         the smoke. The bar's own critical pulse lands here too.
//   hp <= criticalBeacon: Damage_CriticalBeacon — an ADDITIONAL held loop, strobed
//                         at a fixed fast alarm cadence, plus a billboarded "!"
//                         glyph. THIS IS THE GAP WO-892 EXISTS TO CLOSE: fire alone
//                         says "damaged", it never says "act NOW or lose it".
//   broken (hp == 0)    : one-shot Damage_BreakBurst at the BREAK TRANSITION + a
//                         persistent Damage_Ruin column over the shell — and the
//                         beacon STOPS, because a destroyed structure cannot be
//                         saved (WO-753: destroyed is gone; only a full-cost
//                         rebuild returns it, so an alarm would be a lie).
//   any damage          : FloatingHealthBar (hideAtFull — bar only when damaged)
//
// GREYSCALE IS THE ACCEPTANCE CHANNEL (the owner is red/green colourblind). With
// all colour removed the four states are still four different things:
//   smolder  1 layer  · thin slow smoke     · no flame · steady
//   fire     2 layers · dense smoke volume  · FLAME    · steady
//   critical + a 4-layer spark loop with NO smoke, blinking hard at a fixed fast
//            rate, under a "!" glyph — RHYTHM and a GLYPH, not a tint
//   broken   one 5-layer grounded debris scatter, then a low wide 3-layer gutter
//            over something that is no longer standing
// The escalation smolder -> fire is DENSITY, fire -> critical is RHYTHM, and
// critical -> broken is a SHAPE change. None of the three is a hue.
//
// LANDSCAPE PHONE (2670x1200): the vertical axis is the scarce one and an upward
// column is what crops. Every recipe here is tuned low and close to the structure;
// only the "!" glyph is placed high, and it sits just above the health bar the
// player is already looking at.
//
// WHAT THE RECIPES REPLACE, and why this was a defect and not a re-paint:
//   * "Ember_Burn" is declared in HovlVfxCatalogGenerator.Map as a Hovl path that
//     DOES NOT EXIST ("Debuff 1.prefab"; the pack ships "Debuff chain" and "Debuff
//     scythe"). The generator skips a row whose prefab will not load, so the key
//     never reached HovlVfxCatalog.asset, and PlayKey on an absent key is a
//     throttled no-op. THE SMOLDER AND FIRE LOOPS HAD NEVER RENDERED.
//   * "Raid_Explosion" does resolve, but into /Assets/Hovl Studio/, which is
//     gitignored with zero files tracked — so the break burst only existed on a
//     machine carrying the 236 MB pack.
//   Both now point at TRACKED Particle Pack mirrors under Resources/VFX/Damage/.
//
// All VFX go through VFXManager.PlayKey (POOLED). TWO SEPARATE worst-first caps
// apply on top of VFXManager's own global 20-loop cap: maxBurnLoops for the
// smolder/fire/ruin loop and maxCriticalBeacons for the alarm. A structure holds
// AT MOST one of each, so the worst case this whole system can hold is
// maxBurnLoops + maxCriticalBeacons (8 + 3 = 11 by default). Every loop is held by
// a field on its Tracked record and released on every exit path — tier change,
// break, repair, cap eviction, host destroyed, OnDisable, OnDestroy.
//
// COVERAGE (each through its EXISTING read-only surface — no new damage model):
//   • WallSegment / Building — wrapped via RepairTarget (uniform 0..1 fraction)
//   • Gate                   — data OPT-OUT (bespoke force-field collapse tell)
//   • HeartController        — never scanned (bespoke 7-state crystal tell)
//   • ResourceCollector      — HpFraction / IsBroken (registry)
//   • Tower / DefenseTower / ArcaneTower / HarvestSite — HpFraction / IsBroken
//     (WO-672 Slice A members, added by the lifecycle lane)
//
// SELF-INSTALLING (mirrors WaveFeedbackDirector): [RuntimeInitializeOnLoadMethod]
// + sceneLoaded hook spawn one instance per scene; no scene edit, no drag-drop.
// Thresholds come from Data/Canonical/damage-states.json via CanonicalJson
// (dual-copy, WebGL-safe) — "data only always"; per-type overrides + optOut.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Village
{
    /// <summary>
    /// Typed loader for damage-states.json (WO-672 Slice D): global thresholds
    /// (smolder / fire / barOffset / maxBurnLoops) + per-type overrides with an
    /// optOut flag for structures that carry their own bespoke damage tell
    /// (gate force-field, heart crystal). Lazy, cached, WebGL-safe.
    /// </summary>
    public static class DamageStatesCatalog
    {
        /// <summary>StreamingAssets-relative path (CanonicalJson resolves Resources first).</summary>
        public const string StreamingRelativePath = "Data/Canonical/damage-states.json";

        [Serializable]
        public sealed class DefaultsDef
        {
            public float smolder = 0.5f;
            public float fire = 0.25f;
            // WO-892: the HP fraction at/below which the "repair me NOW" alarm arms.
            // Defaults to the fire threshold (the registry places the beacon AT the fire
            // threshold) but is a separate dial so the owner can felt-tune "how early does
            // the game start shouting" without moving where flames appear.
            public float criticalBeacon = 0.25f;
            public float barOffset = 2.2f;
            public int maxBurnLoops = 8;
            // WO-892: a SEPARATE worst-first cap from maxBurnLoops. The beacon is an alarm,
            // and an alarm on eight things at once is noise rather than a call to action -
            // it should point at the two or three most urgent. Keeping it separate also
            // bounds this system's total loop footprint at maxBurnLoops + this, which
            // matters against VFXManager's global 20-loop cap.
            public int maxCriticalBeacons = 3;

            // ── WO-1352 SCUFF BAND (owner ruling 2026-09-02) ─────────────────────
            // THE BUG THIS CLOSES: RepairTarget.NeedsRepair is `DamageFraction > 0.0001`,
            // so a structure at 99.99% HP is REPAIR-ELIGIBLE and Repair-All CHARGES for it -
            // while the first visible tell in this whole file was the smolder at 0.5. The
            // owner's device toast read "Repaired 1 structures for Wood 35, Iron 7" against
            // a building she could see nothing wrong with. Her ruling: move the VISUAL to
            // meet the logic (a tell from the FIRST point of damage), never the reverse -
            // suppressing the affordance would make a 60%-HP structure unrepairable.
            //
            // scuffOnset is the HP at/below which the surface tell starts. It MUST stay at
            // or above 1 - (the repair predicate's epsilon) or the silent band re-opens;
            // StructureScuffOracle in StructureBurnRegression pins exactly that.
            public float scuffOnset = 0.9999f;
            // Discrete steps across (smolder .. scuffOnset]. STEPS, not a continuous ramp,
            // on purpose: a linear fade from 0 at 100% HP is a 4% change at 95% HP, which
            // the eye adapts straight past. A step lands as an event - "this took a hit".
            //
            // WO-1352 FOLLOW-UP (owner ruling 2026-09-02, measured on device: an Archer Tower
            // at hp 0.960 took step 1 and she could not see it): 3 -> 4. With the band
            // spanning (0.5 .. 0.9999], three steps make step 1 cover a SEVENTEEN-point HP
            // range on its own, so that one rung has to be both "something happened here"
            // AND the whole top third's resolution. Four rungs cut each to ~12.5 points,
            // which is what lets step 1 be authored as a LOUD jump (below) while steps 2..4
            // still approach the smolder in even, gentler increments - a front-loaded ladder
            // instead of a linear one. Cost is unchanged: the step is an int and the write
            // happens only when it CHANGES, so a full drain now costs 4 property-block
            // writes per structure instead of 3.
            public int scuffSteps = 4;
            // How far the surface is darkened at step 1 and at the last step, as a fraction
            // of its own albedo. Step 1 is deliberately a visible-but-calm scuff; the last
            // step hands off to the smolder already dirty, so smoke ARRIVES on a battered
            // building rather than popping onto a pristine one.
            //
            // ⚠ THESE ARE FRONT-LOADED ON PURPOSE, AND THE OLD 0.12/0.34 PAIR WAS THE BUG.
            // 0.12 spread linearly over 3 steps gave jumps of 0.12 / 0.11 / 0.11 - an almost
            // perfectly EVEN ladder, which spends its budget discriminating 83% HP from 67%
            // (a distinction no player ever reads off a wall) and starves the only transition
            // that carries meaning: pristine -> touched. 0.20 -> 0.38 over 4 steps gives
            // 0.200 / 0.060 / 0.060 / 0.060: step 1 is 3.3x the jump of any rung after it.
            // That is deliberate and it is the whole fix. The FIRST rung has no neighbour to
            // be compared against (there is no tell above it at all), while every later rung
            // is read against the rung before it AND is converging on the smolder, which
            // brings an entirely new channel (motion + smoke) of its own.
            // The CEILING is a real constraint, not a formality: most structures in a town
            // sit near full HP most of the time, so step 1 is what a healthy town looks like
            // after a raid grazes it. It must read on INSPECTION, never at a glance. 0.20 is
            // chosen against that ceiling - the extra visibility comes from the SECOND
            // channel arriving at step 1 (scuffGlossStep1), not from a bigger colour hit.
            // StructureBurnRegression's scuff oracle section E pins both the floor and the
            // ceiling, so neither end can drift back.
            public float scuffMinDarken = 0.20f;
            public float scuffMaxDarken = 0.38f;
            // Smoothness multiplier at step 1 and at the last step - the second, non-colour
            // channel: the surface goes matte/dulled as well as darker.
            //
            // ⛔ scuffGlossStep1 EXISTS BECAUSE THE RAMP MATHEMATICALLY COULD NOT MOVE GLOSS
            // AT STEP 1. The ramp interpolates on t = (step-1)/(steps-1), so t is 0 at step 1
            // by construction and the old single-ended gloss knob was pinned to x1.00 there -
            // no authored value could ever have changed it. Her device line said so in as
            // many words: "applied albedo x0.88 ... + smoothness x1.00". So step 1 was a
            // ONE-channel tell of 12%, and this is the field that makes it two.
            // Killing the specular sheen is the strongest cue available here for the price:
            // it removes the sun's highlight off the roof/timber, it is a texture read rather
            // than a colour read (so it is entirely outside the colourblind risk surface),
            // and it costs the same single float in the same property block that was already
            // being written.
            public float scuffGlossStep1 = 0.55f;
            public float scuffGlossFloor = 0.25f;
        }

        [Serializable]
        public sealed class TypeOverrideDef
        {
            public bool optOut;
            public float? smolder;
            public float? fire;
            public float? criticalBeacon;
            public float? barOffset;
            public float? scuffOnset;
        }

        [Serializable]
        private sealed class FileDef
        {
            public int version;
            public DefaultsDef defaults;
            public Dictionary<string, TypeOverrideDef> perType;
        }

        private static DefaultsDef _defaults;
        private static Dictionary<string, TypeOverrideDef> _perType;

        // WO-1352: one-shot probe of whether the scuff-onset knob exists in the static
        // RemoteTunables.Registry (see ScuffOnset for why it is memoised).
        private static bool _scuffKnobProbed;
        private static bool _scuffKnobRegistered;

        /// <summary>Force a fresh reload on next access (e.g. after an editor JSON edit).</summary>
        public static void Invalidate()
        {
            _defaults = null;
            _perType = null;
        }

        /// <summary>True when <paramref name="typeKey"/> opts out of the shared damage tells.</summary>
        public static bool OptOut(string typeKey)
        {
            EnsureLoaded();
            return _perType.TryGetValue(typeKey ?? string.Empty, out var o) && o != null && o.optOut;
        }

        /// <summary>HP fraction at/below which the reduced-scale smolder loop shows.</summary>
        public static float Smolder(string typeKey) => Resolve(typeKey, o => o.smolder, _d().smolder);

        /// <summary>HP fraction at/below which the full-scale fire loop shows.</summary>
        public static float Fire(string typeKey) => Resolve(typeKey, o => o.fire, _d().fire);

        /// <summary>
        /// WO-892: HP fraction at/below which the critical-save alarm arms (an ADDITIONAL
        /// loop on top of the fire tell, plus the "!" glyph). Separate dial from
        /// <see cref="Fire"/> so "when does it start shouting" is tunable independently of
        /// "when does it catch fire".
        /// </summary>
        public static float CriticalBeacon(string typeKey) =>
            Resolve(typeKey, o => o.criticalBeacon, _d().criticalBeacon);

        /// <summary>Minimum world-space health-bar height above the structure pivot.</summary>
        public static float BarOffset(string typeKey) => Resolve(typeKey, o => o.barOffset, _d().barOffset);

        /// <summary>Cap on simultaneous burn loops (worst-first) across all structures.</summary>
        public static int MaxBurnLoops { get { EnsureLoaded(); return Mathf.Max(1, _defaults.maxBurnLoops); } }

        /// <summary>WO-892: cap on simultaneous critical-save beacons (worst-first).</summary>
        public static int MaxCriticalBeacons
        {
            get { EnsureLoaded(); return Mathf.Max(1, _defaults.maxCriticalBeacons); }
        }

        // ── WO-1352: the scuff band's dials ─────────────────────────────────────

        /// <summary>
        /// WO-1352: HP fraction at/below which the surface scuff tell begins. This is the
        /// number that closes the silent band, so it is also the one most likely to want a
        /// felt-tune, which is why it carries a remote-tunable seam (see
        /// <see cref="ScuffOnsetTunableKey"/>).
        /// </summary>
        public static float ScuffOnset(string typeKey)
        {
            float authored = Resolve(typeKey, o => o.scuffOnset, _d().scuffOnset);

            // ⭐ TUNABLE SEAM, owner standing ruling ("make it tweakable from a db call").
            // ASK, do not assume: RemoteTunables.Int on an UNREGISTERED key answers 0 and
            // logs a caller bug - and an onset of 0 would mean the tell never shows at all,
            // i.e. exactly the defect this band exists to fix. So the spec probe is the
            // guard, not a nicety, and the key deliberately is NOT yet added to
            // RemoteTunables.Registry (that file is another WO's single-owner edit and the
            // six sources move together). NO ROW / NO NETWORK / BAD PARSE => the authored
            // damage-states.json value, exactly. Same idiom as ArcaneTowerAuraTuning.
            try
            {
                // The Registry is a static compile-time array, so whether the key EXISTS can
                // never change within a session - probe it once. (The VALUE can still change
                // when a payload lands, so the Int read below stays live.) This matters
                // because ScuffOnset is called once per tracked structure per 0.3 s eval,
                // i.e. for the whole town, and a per-structure string scan of the Registry
                // is exactly the kind of quiet cost this band must not introduce.
                if (!_scuffKnobProbed)
                {
                    _scuffKnobProbed = true;
                    _scuffKnobRegistered = DeNelle.Core.Ops.RemoteTunables.SpecFor(ScuffOnsetTunableKey) != null;
                }
                if (_scuffKnobRegistered)
                {
                    int pct = DeNelle.Core.Ops.RemoteTunables.Int(ScuffOnsetTunableKey);
                    // Clamped to a band that can never re-open the silent gap (a value below
                    // the smolder threshold would) and can never exceed full HP.
                    float v = Mathf.Clamp(pct / 100f, 0.5f, 1f);
                    if (!Mathf.Approximately(v, pct / 100f))
                        FlowTrace.Warn("DamageVis",
                            $"scuffOnset row {pct}% is outside the safe band 50..100 - clamped to {v:0.###}. " +
                            "Below the smolder threshold would re-open the silent repair-eligible band.");
                    return v;
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("DamageVis",
                    $"scuffOnset tunable read threw ({ex.Message}) - authored value {authored:0.####} in effect.");
            }
            return authored;
        }

        /// <summary>
        /// Wire key for the scuff onset, INT PERCENT of full HP (100 = "from the first
        /// point of damage"). NOT yet registered in RemoteTunables.Registry - see
        /// <see cref="ScuffOnset"/> for why, and the no-row invariant it preserves.
        /// </summary>
        public const string ScuffOnsetTunableKey = "vfx.structureScuffOnsetPct";

        /// <summary>WO-1352: how many discrete scuff steps span the band (>= 1).</summary>
        public static int ScuffSteps { get { EnsureLoaded(); return Mathf.Clamp(_defaults.scuffSteps, 1, 8); } }

        /// <summary>WO-1352: albedo darkening at scuff step 1 (0..1).</summary>
        public static float ScuffMinDarken
        {
            get { EnsureLoaded(); return Mathf.Clamp(_defaults.scuffMinDarken, 0f, 0.9f); }
        }

        /// <summary>WO-1352: albedo darkening at the last scuff step (0..1).</summary>
        public static float ScuffMaxDarken
        {
            get { EnsureLoaded(); return Mathf.Clamp(_defaults.scuffMaxDarken, ScuffMinDarken, 0.9f); }
        }

        /// <summary>
        /// WO-1352 follow-up: smoothness multiplier at scuff step 1. The ramp's t is 0 at
        /// step 1 by construction, so without a step-1 endpoint of its own the gloss channel
        /// was pinned at x1.00 there and step 1 was a one-channel tell. Clamped at or below
        /// 1 (a damaged surface may never become SHINIER) and at or above the floor.
        /// </summary>
        public static float ScuffGlossStep1
        {
            get { EnsureLoaded(); return Mathf.Clamp(_defaults.scuffGlossStep1, ScuffGlossFloor, 1f); }
        }

        /// <summary>WO-1352: smoothness multiplier at the last scuff step (matte-off).</summary>
        public static float ScuffGlossFloor
        {
            get { EnsureLoaded(); return Mathf.Clamp01(_defaults.scuffGlossFloor); }
        }

        private static DefaultsDef _d() { EnsureLoaded(); return _defaults; }

        private static float Resolve(string typeKey, Func<TypeOverrideDef, float?> pick, float fallback)
        {
            EnsureLoaded();
            if (_perType.TryGetValue(typeKey ?? string.Empty, out var o) && o != null)
            {
                float? v = pick(o);
                if (v.HasValue) return v.Value;
            }
            return fallback;
        }

        private static void EnsureLoaded()
        {
            if (_defaults != null) return;
            _defaults = new DefaultsDef();
            _perType = new Dictionary<string, TypeOverrideDef>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string text = CanonicalJson.Read(StreamingRelativePath);
                if (string.IsNullOrEmpty(text))
                {
                    FlowTrace.Warn("DamageVis",
                        $"DamageStatesCatalog: {StreamingRelativePath} not found — code defaults in effect " +
                        $"(smolder {_defaults.smolder}, fire {_defaults.fire}).");
                    return;
                }
                var file = JsonConvert.DeserializeObject<FileDef>(text);
                if (file == null)
                {
                    FlowTrace.Warn("DamageVis", "DamageStatesCatalog: damage-states.json parsed null — code defaults in effect.");
                    return;
                }
                if (file.defaults != null) _defaults = file.defaults;
                if (file.perType != null)
                    foreach (var kv in file.perType)
                        if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                            _perType[kv.Key] = kv.Value;
                FlowTrace.Step("DamageVis",
                    $"DamageStatesCatalog loaded v{file.version}: smolder={_defaults.smolder} fire={_defaults.fire} " +
                    $"barOffset={_defaults.barOffset} maxBurnLoops={_defaults.maxBurnLoops} perType={_perType.Count}.");
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("DamageVis",
                    $"DamageStatesCatalog: failed to parse damage-states.json ({ex.Message}) — code defaults in effect.");
            }
        }
    }

    /// <summary>
    /// The one pooled damage-presentation observer (WO-672 Slice B): scans for
    /// damageable structures on a throttled timer, attaches a FloatingHealthBar
    /// on first damage, and drives capped smolder / fire / critical-beacon / ruin
    /// tells from the damage-states thresholds. Read-only over the structures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureDamageVisuals : MonoBehaviour
    {
        // Scan (FindObjectsByType — the expensive part) is throttled hard; the
        // per-record evaluation (cheap delegate reads over the tracked set) runs
        // faster so a break burst lands near the actual transition, not seconds late.
        private const float ScanInterval = 2.0f;
        private const float EvalInterval = 0.3f;

        // ── WO-892 RECIPE KEYS (the re-skin; data, not logic) ────────────────────
        // Every one is a TRACKED Particle Pack mirror under Resources/VFX/Damage/,
        // authored by ParticlePackVfxBatchBuilder and wired in HovlVfxCatalogGenerator.
        // They replace "Ember_Burn" (declared against a Hovl path that does not exist,
        // so the key never reached the catalog and the smolder + fire loops had never
        // rendered on any machine) and "Raid_Explosion" (real, but gitignored Hovl art).
        private const string SmolderKey = "Damage_Smolder";        // A loop — thin smoke, no flame
        private const string FireKey    = "Damage_Fire";           // A loop — flame + smoke volume
        private const string RuinKey    = "Damage_Ruin";           // A loop — low wide gutter over a shell
        private const string BreakKey   = "Damage_BreakBurst";     // B one-shot — grounded collapse
        private const string BeaconKey  = "Damage_CriticalBeacon"; // A loop — the alarm

        // Burn-loop scale buckets (shape/motion tell, WO-672): the smolder recipe is
        // already thinned in the prefab, so this is a size step on top of a density step.
        private const float SmolderScale = 0.55f;
        private const float FireScale = 1.0f;

        // ── WO-892 alarm cadence (the colour-free "act NOW" channel) ────────────
        // FIXED rate, deliberately. HeroHpStateAura's breath ACCELERATES with severity
        // because it answers "how close am I to dying"; this answers a yes/no question -
        // "is this building about to be lost" - so it must read as a steady alarm, not a
        // gauge. A constant fast blink is also what the eye picks out of a busy raid.
        private const float BeaconHz = 2.6f;
        // The wave is SHARPENED (raised to a power) so most of each cycle is dark and the
        // bright part is a snap. A sine would read as breathing; this reads as blinking,
        // and blinking is what an alarm does.
        private const float BeaconWaveSharpness = 4f;
        private const float BeaconTrough = 0.12f;   // very nearly out between blinks
        private const float BeaconCrest  = 2.6f;    // hard over-drive on the blink
        private const float BeaconSimSpeed = 1.35f; // snappier pops, not a lazy sparkle

        // The "!" glyph. Placed just above the health bar the player is already reading,
        // and pulsed on the SAME wave as the sparks so glyph and effect are one signal.
        private const string BeaconGlyph = "!";
        private const float BeaconGlyphRise = 0.55f;   // above the bar
        private const float BeaconGlyphScaleMin = 0.85f;
        private const float BeaconGlyphScaleMax = 1.35f;

        // ── WO-1352: THE SCUFF TELL — shader parameters, not particles ───────────
        // Owner ruling 2026-09-02: show a visible damage tell from the FIRST point of
        // damage, escalating INTO the existing smolder at 0.5 rather than popping in at it.
        //
        // WHY A MATERIAL PARAMETER AND NOT AN EFFECT. Smolder/fire/beacon only ever ran on
        // the handful of structures below 50%, capped at 8 + 3 loops. THIS band covers
        // EVERY structure in a player-built town at once, and the device already reports
        // [Flow:VfxPerfGate] hitches against a 16.7 ms budget on a 29.5 ms baseline. A
        // per-structure particle system here would cost more frames than the bug costs
        // trust. This tell allocates NO GameObject, NO particle, NO pooled loop and NO
        // per-frame work: it is a MaterialPropertyBlock write that happens ONLY on a step
        // change, and an undamaged structure never receives a block at all.
        //
        // ⛔ IT IS NOT CARRIED BY HUE (the owner is red/green colourblind). The albedo is
        // multiplied by a SCALAR - R, G and B by the identical factor - so the hue and the
        // saturation ratio are mathematically unchanged and only the VALUE moves. Any
        // greyscale conversion is a weighted sum of R,G,B, so it scales by that same
        // factor: the tell survives desaturation exactly intact, which is the strongest
        // form of the colourblind guarantee available. The second channel is SMOOTHNESS
        // (a matte, dulled surface), which is a texture read and not a colour read at all.
        //
        // ⚠ IT IS DELIBERATELY QUIETER THAN EVERY EXISTING RUNG, so the ladder still
        // escalates: scuff is a STATIC SURFACE change (no motion, no new element), smolder
        // adds MOTION and a new element (smoke), fire adds a FLAME, critical adds RHYTHM
        // plus a glyph, broken changes the SHAPE. At 95% HP a building reads "this took a
        // hit"; nothing about it reads as burning.
        private const string ScuffLabel = "scuff";
        // Both albedo property names, because this project mixes URP/Lit (_BaseColor) with
        // legacy/unlit materials (_Color). Setting a property a shader does not declare is
        // a harmless no-op in a MaterialPropertyBlock, so writing both is cheaper and far
        // more robust than branching per shader family.
        private static readonly int ScuffBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ScuffLegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int ScuffSmoothnessId = Shader.PropertyToID("_Smoothness");
        // Two submesh materials whose base colours differ by more than this cannot share one
        // property block without recolouring one of them wrongly - such a renderer is SKIPPED
        // and said so, never silently mis-tinted.
        private const float ScuffMultiMatEpsilon = 0.02f;

        /// <summary>
        /// WO-1352 — THE PURE BAND FUNCTION, and the one the oracle binds to. Returns the
        /// scuff step for an HP fraction: 0 = no surface tell, 1..<see cref="DamageStatesCatalog.ScuffSteps"/>
        /// = progressively dirtier. Held at the last step below the smolder threshold, so a
        /// burning building never visually cleans itself up.
        /// </summary>
        /// <remarks>
        /// This is the shipping code path (Evaluate calls it), NOT a copy of it - an oracle
        /// that binds to a duplicated threshold proves only that the duplicate is intact.
        /// </remarks>
        public static int ScuffStepFor(float hp, string typeKey)
        {
            hp = Mathf.Clamp01(hp);
            float onset = DamageStatesCatalog.ScuffOnset(typeKey);
            if (hp > onset) return 0;                       // pristine: no tell, and none is owed

            int steps = DamageStatesCatalog.ScuffSteps;
            float smolder = DamageStatesCatalog.Smolder(typeKey);
            if (hp <= smolder) return steps;                // held dirty under the smoke

            // Progress across the band, 0 at the onset -> 1 at the smolder handoff. The
            // FIRST point of damage already lands in step 1 (ceil), which is the whole point.
            float span = Mathf.Max(0.0001f, onset - smolder);
            float t = Mathf.Clamp01((onset - hp) / span);
            return Mathf.Clamp(Mathf.CeilToInt(t * steps), 1, steps);
        }

        /// <summary>
        /// WO-1352 — the burn tier for an HP fraction, lifted out of Evaluate verbatim so
        /// the oracle can read the WHOLE ladder from one place. 0 none / 1 smolder /
        /// 2 fire-or-ruin.
        /// </summary>
        public static int BurnTierFor(float hp, bool broken, string typeKey)
        {
            if (broken) return 2;
            hp = Mathf.Clamp01(hp);
            if (hp <= DamageStatesCatalog.Fire(typeKey)) return 2;
            if (hp <= DamageStatesCatalog.Smolder(typeKey)) return 1;
            return 0;
        }

        /// <summary>
        /// WO-1352 — the whole visible ladder as one monotonic ordinal, for the oracle and
        /// the trace. 0 = VISUALLY SILENT (and nothing above 0 may ever be silent while the
        /// structure is repair-eligible); 1..N = scuff steps; N+1 = smolder; N+2 = fire/ruin.
        /// </summary>
        public static int TellOrdinalFor(float hp, bool broken, string typeKey)
        {
            int steps = DamageStatesCatalog.ScuffSteps;
            int tier = BurnTierFor(hp, broken, typeKey);
            if (tier > 0) return steps + tier;
            return ScuffStepFor(hp, typeKey);
        }

        /// <summary>
        /// WO-1352 follow-up — THE STRENGTH RAMP, lifted out of <see cref="ApplyScuff"/> so
        /// the oracle can pin HOW HARD the tell hits, not merely THAT it exists. Returns the
        /// albedo darkening for a scuff step as a fraction of the surface's own colour
        /// (0 = untouched, 0.20 = 20% darker). Step &lt;= 0 is pristine and darkens nothing.
        /// </summary>
        /// <remarks>
        /// Extracted, not duplicated: ApplyScuff calls THIS. The original oracle proved the
        /// band was non-silent and the tell still could not be seen on a real device, because
        /// "non-silent" and "visible" are different claims and only the first one was pinned.
        /// </remarks>
        public static float ScuffDarkenFor(int step)
        {
            if (step <= 0) return 0f;
            int steps = Mathf.Max(1, DamageStatesCatalog.ScuffSteps);
            step = Mathf.Clamp(step, 1, steps);
            float t = steps <= 1 ? 1f : (step - 1) / (float)(steps - 1);
            return Mathf.Lerp(DamageStatesCatalog.ScuffMinDarken, DamageStatesCatalog.ScuffMaxDarken, t);
        }

        /// <summary>
        /// WO-1352 follow-up — the SECOND channel's ramp: the smoothness multiplier for a
        /// scuff step. 1 = the surface's own gloss, lower = duller/matte. Interpolates from
        /// <see cref="DamageStatesCatalog.ScuffGlossStep1"/> to
        /// <see cref="DamageStatesCatalog.ScuffGlossFloor"/>, so step 1 already drops the
        /// specular sheen instead of sitting at x1.00 as it did before this follow-up.
        /// </summary>
        public static float ScuffGlossMulFor(int step)
        {
            if (step <= 0) return 1f;
            int steps = Mathf.Max(1, DamageStatesCatalog.ScuffSteps);
            step = Mathf.Clamp(step, 1, steps);
            float t = steps <= 1 ? 1f : (step - 1) / (float)(steps - 1);
            return Mathf.Lerp(DamageStatesCatalog.ScuffGlossStep1, DamageStatesCatalog.ScuffGlossFloor, t);
        }

        /// <summary>One observed structure (read-only view; presentation never mutates it).</summary>
        private sealed class Tracked
        {
            public GameObject Host;
            public string TypeKey;      // damage-states perType key ("wall"/"tower"/...)
            public string Name;         // trace label
            public Func<float> Hp;      // 0..1 HP fraction (1 = pristine)
            public Func<bool> Broken;   // true once an inoperable shell
            public Vector3 VfxAnchor;   // bounds centre — where the ember/burst sits
            public float BarOffset;     // world height for the floating bar
            public bool BarAttached;
            public VFXHandle Burn;      // live smolder/fire/ruin loop (null = none)
            public int BurnTier;        // 0 none · 1 smolder · 2 fire/broken
            // WO-892: WHICH recipe the live burn loop is playing. Tier alone is no longer
            // enough to decide whether the loop needs restarting: a standing structure on
            // fire and a broken ruin are BOTH tier 2 but play different recipes, so
            // without this a building that broke while burning would keep its flame loop
            // and never show the ruin.
            public string BurnKey;
            public int PendingTier;     // this eval's desired tier (pre-cap)
            public bool WasBroken;
            public bool Observed;       // first eval done (no burst for arrived-broken shells)
            public bool CleanedUpOnBreak; // structure-death cleanup done (bar torn down + aura stopped)

            // -- WO-892 critical-save beacon ------------------------------------
            public VFXHandle Beacon;    // the live alarm loop (null = none)
            public bool WantsBeacon;    // this eval's desired state (pre-cap)
            public float BeaconPhase;   // blink accumulator (radians)
            public Transform BeaconTag; // the billboarded "!" glyph, built on demand

            // -- WO-1352 scuff band ---------------------------------------------
            public int ScuffStep;             // 0 = clean surface, 1..N = progressively dirtier
            public Renderer[] ScuffRenderers; // the VISIBLE, single-albedo renderers we drive
            public Color[] ScuffBaseColors;   // captured baseline albedo, for an exact restore
            public float[] ScuffBaseGloss;    // captured baseline smoothness
            public int ScuffChildCount;       // host childCount when the list was resolved
            public bool ScuffUnreachable;     // no eligible renderer - warned once, never spammed
        }

        private readonly Dictionary<GameObject, Tracked> _tracked =
            new Dictionary<GameObject, Tracked>();
        private readonly List<GameObject> _dead = new List<GameObject>();      // scratch
        private readonly List<Tracked> _burnWants = new List<Tracked>();        // scratch
        private readonly List<Tracked> _beaconWants = new List<Tracked>();      // scratch
        // Records with a LIVE beacon, so the per-frame blink walks three entries rather
        // than the whole tracked set. Rebuilt by Evaluate; never the source of truth.
        private readonly List<Tracked> _beaconLive = new List<Tracked>();
        private float _scanTimer;
        private float _evalTimer;

        // ── Runtime install (no scene edit — mirrors WaveFeedbackDirector) ──────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn();   // the first scene is already loaded when this runs
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode) => TrySpawn();

        private static void TrySpawn()
        {
            if (UnityEngine.Object.FindAnyObjectByType<StructureDamageVisuals>() != null) return;
            var go = new GameObject("StructureDamageVisuals");
            go.AddComponent<StructureDamageVisuals>();
            FlowTrace.Step("DamageVis",
                $"installed (scene='{SceneManager.GetActiveScene().name}') — structure damage tells active.");
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _scanTimer = 0f;    // scan immediately on install
            _evalTimer = 0f;
        }

        /// <summary>
        /// WO-892: this component being switched off is an EXIT PATH like any other. It
        /// was not one before — only OnDestroy released loops — so a disabled observer
        /// (a pooled/parked host, an editor toggle) would have stranded every held loop
        /// against VFXManager's global 20-loop cap with nothing able to release them.
        /// Releasing here is safe because the state is fully re-derivable: the next
        /// Evaluate re-acquires whatever the thresholds still call for.
        /// </summary>
        private void OnDisable() => ReleaseAllHeld("OnDisable");

        private void OnDestroy()
        {
            ReleaseAllHeld("OnDestroy");
            _tracked.Clear();
        }

        /// <summary>
        /// Return EVERY loop this observer holds — burn/ruin and beacon — and tear down
        /// the "!" glyphs. Idempotent, and safe to call with nothing held. A scene swap
        /// or a disable must never strand a loop; a stranded loop is invisible, permanent,
        /// and silently starves every later effect in the session.
        /// </summary>
        private void ReleaseAllHeld(string reason)
        {
            int loops = 0;
            foreach (var rec in _tracked.Values)
            {
                if (rec.Burn != null) { rec.Burn.Stop(immediate: true); rec.Burn = null; loops++; }
                rec.BurnTier = 0;
                rec.BurnKey = null;
                if (rec.Beacon != null) { rec.Beacon.Stop(immediate: true); rec.Beacon = null; loops++; }
                DestroyBeaconTag(rec);
                // WO-1352: the scuff holds no pooled loop, but it DOES hold a property block
                // on somebody else's renderer - which is exactly the kind of thing that
                // survives a scene swap and reads as "the art is broken". Restore it here
                // for the same reason the loops are released here.
                RestoreScuffBaselines(rec);
            }
            _beaconLive.Clear();
            if (loops > 0)
                FlowTrace.Step("DamageVis",
                    $"released {loops} held loop(s) ({reason}) - burn + beacon slots returned to the pool.");
        }

        private void Update()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Guard.Try("DamageVis", "structure scan", Scan);
            }

            _evalTimer -= Time.deltaTime;
            if (_evalTimer <= 0f)
            {
                _evalTimer = EvalInterval;
                Guard.Try("DamageVis", "damage-state eval", Evaluate);
            }

            // WO-892: the alarm blink runs EVERY FRAME, not on the 0.3 s eval tick — a
            // 2.6 Hz blink sampled at 3.3 Hz would alias into a random flicker, which
            // reads as a broken effect rather than an alarm. Walks _beaconLive (at most
            // maxCriticalBeacons entries), so this costs nothing when nothing is critical.
            if (_beaconLive.Count > 0)
                Guard.Try("DamageVis", "critical beacon pulse", DriveBeacons);
        }

        // ── SCAN — register every damageable structure (throttled) ──────────────

        private void Scan()
        {
            // Wall / Building through the uniform RepairTarget view (Gate is a data
            // opt-out: its force-field collapse is the already-good bespoke tell; the
            // Heart's crystal states likewise — HeartController is never scanned).
            // WO-1717: the second argument is the structure's own MAX HP, and it exists
            // only so StructureHitReaction can turn the observed fraction drop into the
            // points the player sees. WallSegment.MaxHp is a const 100 and the wall's
            // fraction is exactly 1 - Damage/100 (RepairTarget.cs:141), so drop x maxHp is
            // the POST-tier-divide `effective` from WallSegment.cs:334 by construction.
            RegisterRepairables<WallSegment>("wall", _ => WallSegment.MaxHp);
            RegisterRepairables<Building>("building", b => b.MaxHp);
            if (!DamageStatesCatalog.OptOut("gate"))
                RegisterRepairables<Gate>("gate", g => g.MaxHp);

            // Resource collectors — HpFraction / IsBroken via the registry.
            if (!DamageStatesCatalog.OptOut("collector"))
            {
                foreach (var c in ResourceCollectorRegistry.All)
                {
                    if (c == null || _tracked.ContainsKey(c.gameObject)) continue;
                    var cc = c;   // capture the loop variable, not the iterator
                    // WO-1717: NO maxHp argument on purpose. ResourceCollector exposes
                    // HpFraction but no public max HP, and its runtime callers all take the
                    // component's own DefaultMaxHp (120) rather than the catalog row - so
                    // reading BuildingCatalog here would print a number that does not match
                    // the HP that actually moved. A collector therefore gets the dust and the
                    // sound and no number, which beats a number that lies. The fix is one
                    // read-only getter on ResourceCollector, which WO-1717 6C is forbidden
                    // from adding (no gameplay-class edits); it is flagged in the RESULT.
                    Register(c.gameObject, "collector", c.BuildingId,
                        () => cc != null ? cc.HpFraction : 1f,
                        () => cc != null && cc.IsBroken);
                }
            }

            // Towers + harvest sites — the WO-672 Slice A surface (HpFraction /
            // IsBroken, added by the lifecycle lane; broken shells persist).
            if (!DamageStatesCatalog.OptOut("tower"))
                foreach (var t in UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsSortMode.None))
                {
                    if (t == null || _tracked.ContainsKey(t.gameObject)) continue;
                    var tt = t;
                    Register(t.gameObject, "tower", t.gameObject.name,
                        () => tt != null ? tt.HpFraction : 1f, () => tt != null && tt.IsBroken,
                        () => tt != null ? tt.MaxHp : 0f);
                }
            if (!DamageStatesCatalog.OptOut("defensetower"))
                foreach (var t in UnityEngine.Object.FindObjectsByType<DefenseTower>(FindObjectsSortMode.None))
                {
                    if (t == null || _tracked.ContainsKey(t.gameObject)) continue;
                    var tt = t;
                    Register(t.gameObject, "defensetower", t.gameObject.name,
                        () => tt != null ? tt.HpFraction : 1f, () => tt != null && tt.IsBroken,
                        () => tt != null ? tt.MaxHp : 0f);
                }
            if (!DamageStatesCatalog.OptOut("arcanetower"))
                foreach (var t in UnityEngine.Object.FindObjectsByType<ArcaneTower>(FindObjectsSortMode.None))
                {
                    if (t == null || _tracked.ContainsKey(t.gameObject)) continue;
                    var tt = t;
                    Register(t.gameObject, "arcanetower", t.gameObject.name,
                        () => tt != null ? tt.HpFraction : 1f, () => tt != null && tt.IsBroken,
                        () => tt != null ? tt.MaxHp : 0f);
                }
            if (!DamageStatesCatalog.OptOut("harvestsite"))
                foreach (var t in UnityEngine.Object.FindObjectsByType<DeNelle.Village.World.HarvestSite>(FindObjectsSortMode.None))
                {
                    if (t == null || _tracked.ContainsKey(t.gameObject)) continue;
                    var tt = t;
                    Register(t.gameObject, "harvestsite", t.gameObject.name,
                        () => tt != null ? tt.HpFraction : 1f, () => tt != null && tt.IsBroken,
                        () => tt != null ? tt.MaxHp : 0f);
                }
        }

        /// <summary>Register damaged-or-not structures of type <typeparamref name="T"/>
        /// through the uniform RepairTarget wrapping (never re-branching per type).</summary>
        /// <param name="maxHpOf">
        /// WO-1717: reads the concrete structure's own max HP, so the per-hit tell can
        /// print the POINTS that landed instead of a fraction. Kept as a selector on the
        /// concrete type rather than a new property on RepairTarget, because WO-1717 §7
        /// puts RepairTarget off-limits (its faction-blindness is load-bearing here).
        /// </param>
        private void RegisterRepairables<T>(string typeKey, Func<T, float> maxHpOf) where T : Component
        {
            foreach (var s in UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (s == null || _tracked.ContainsKey(s.gameObject)) continue;
                var target = RepairTarget.TryWrap(s);
                if (target == null || !target.IsValid) continue;
                var ss = s;   // capture the component, not the iterator
                Register(s.gameObject, typeKey, target.DisplayName,
                    () => target.IsValid ? 1f - target.DamageFraction : 1f,
                    () => target.IsValid && target.DamageFraction >= 0.999f,
                    maxHpOf == null ? (Func<float>)null : () => ss != null ? maxHpOf(ss) : 0f);
            }
        }

        private void Register(GameObject host, string typeKey, string name,
            Func<float> hp, Func<bool> broken, Func<float> maxHp = null)
        {
            // VFX anchor + bar offset from the renderer bounds (structures do not
            // move); the data barOffset is the floor so a flat foundation still
            // floats its bar clear of the mesh. FloatingHealthBar clamps the top end.
            Vector3 anchor = host.transform.position + Vector3.up * 0.5f;
            float offset = DamageStatesCatalog.BarOffset(typeKey);
            var renderers = host.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    if (renderers[i] != null) b.Encapsulate(renderers[i].bounds);
                anchor = b.center;
                offset = Mathf.Max(offset, b.max.y - host.transform.position.y + 0.4f);
            }

            var rec = new Tracked
            {
                Host = host,
                TypeKey = typeKey,
                Name = string.IsNullOrEmpty(name) ? typeKey : name,
                Hp = hp,
                Broken = broken,
                VfxAnchor = anchor,
                BarOffset = offset,
            };
            _tracked[host] = rec;

            // WO-1024: THIS is the moment a repairable provably exists in the scene. This class
            // installs unconditionally while HubRepairAffordance gated on a scene-load-time scan,
            // and the town is restored from the save AFTER that scan - so a structure could burn
            // here, with fire rendering from this very tracker, while no repair surface existed at
            // all. Raising the install from the tracker closes the asymmetry at its source: the
            // repair surface now follows the town instead of racing it. Guarded by a static bool
            // on the other side, so this costs a bool test once a scene is served.
            HubRepairAffordance.NotifyRepairableAppeared();

            // WO-891 (adjacent, reported): the PER-HIT flinch. Everything above this line
            // is a damage-STATE ladder driven by the 0.3 s Evaluate poll against DATA
            // THRESHOLDS - so a hit that does not cross 0.5 or 0.25 HP produced no reaction
            // at all, and one that did produced it up to a third of a second after the blow.
            // Being hit and being badly hurt were the same channel, and the first was silent.
            //
            // This is the ONE place every damageable structure in the game already passes
            // through, and it already holds the read-only HP delegate the flinch needs - so
            // walls, buildings, gates, towers, collectors and harvest sites are all covered
            // by one line, with no new damage model and no edit to any gameplay class.
            // Family B one-shot: it cannot consume a loop slot, and it is rate-limited
            // per-structure inside the component. The state ladder is untouched.
            //
            // WO-1717 (6C): the hit callback now ALSO attaches the floating HP bar. Before
            // this, the bar was attached only by Evaluate, which runs on a 0.3 s poll behind
            // a 2.0 s Scan poll - so a structure could be struck and stay visibly inert for
            // up to 2.3 s, which is exactly why the owner read a wall under attack as doing
            // nothing. Hanging it off the flinch is the SMALLEST fix that actually closes the
            // latency: pre-registering at scene load would only remove the 2.0 s half (Scan
            // already runs on the install frame - OnEnable sets _scanTimer = 0), would not
            // touch the 0.3 s half at all, and would attach bars to a whole pristine town.
            // The flinch already fires the frame the HP moves, so the bar now appears on the
            // FIRST hit, in the same frame, on structures registered and yet-to-be-registered
            // alike. Evaluate keeps its own attach as the belt-and-braces path for damage that
            // arrives below the flinch's MinDropFraction - both go through AttachBar, so the
            // FloatingHealthBar argument list exists exactly once.
            StructureHitReaction.Attach(host, hp, string.IsNullOrEmpty(name) ? typeKey : name,
                () => AttachBar(rec, "first-hit"), maxHp);
        }

        /// <summary>
        /// Attach the floating HP bar to a tracked structure, once. The ONLY place
        /// FloatingHealthBar.Attach is called from this class - both the WO-1717 first-hit
        /// path and the Evaluate poll route through here so the two can never drift.
        /// </summary>
        private void AttachBar(Tracked rec, string via)
        {
            if (rec == null || rec.BarAttached || rec.Host == null) return;

            // Never bond a bar to a corpse. The collapsing blow drops the fraction to 0 and
            // would otherwise attach a bar to a ruin that the very next Evaluate tears down
            // again (the CleanedUpOnBreak path) - a bar that blinks into existence on death
            // is worse than no bar.
            if (rec.Broken != null && Guard.Try("DamageVis", "broken probe", rec.Broken, fallback: false)) return;

            float hp = Mathf.Clamp01(rec.Hp != null ? rec.Hp() : 1f);
            if (hp >= 0.999f) return;   // hideAtFull would hide it anyway

            FloatingHealthBar.Attach(rec.Host, rec.Hp, () => false,
                heightOffset: rec.BarOffset, hideAtFull: true, destroyOnDead: false);
            rec.BarAttached = true;
            FlowTrace.Step("DamageVis",
                $"bar attached: '{rec.Name}' ({rec.TypeKey}) hp={hp:0.00} via={via}");
        }

        // ── EVALUATE — drive the tells from the observed state (fast, cheap) ────

        private void Evaluate()
        {
            _dead.Clear();
            _burnWants.Clear();
            _beaconWants.Clear();

            foreach (var kv in _tracked)
            {
                var rec = kv.Value;
                if (rec.Host == null)
                {
                    // Host destroyed under us — BOTH held loops go back, and the "!" glyph
                    // with them (it is parented to the host, but a Destroy on the host is
                    // deferred to end-of-frame and the glyph is torn down explicitly so the
                    // teardown is not order-dependent).
                    rec.Burn?.Stop(immediate: true);
                    rec.Burn = null;
                    rec.BurnTier = 0;
                    rec.BurnKey = null;
                    StopBeacon(rec, "host destroyed");
                    // The renderers went with the host; drop our references so the arrays
                    // cannot keep a destroyed Renderer alive in the tracked record.
                    rec.ScuffRenderers = null;
                    rec.ScuffBaseColors = null;
                    rec.ScuffBaseGloss = null;
                    rec.ScuffStep = 0;
                    _dead.Add(kv.Key);
                    continue;
                }

                float hp = Mathf.Clamp01(rec.Hp != null ? rec.Hp() : 1f);
                bool broken = rec.Broken != null && rec.Broken();
                if (broken) hp = 0f;   // a broken shell always reads as empty

                // Structure-death cleanup (owner felt-test 2026-07-15: "tower was
                // destroyed ... but the vfx and 0 health bar still exist"). A DESTROYED
                // (broken) shell tears its HP bar down and STOPS any arcane aura loop -
                // this reverses WO-672's "pin the empty bar on the shell", because the
                // root is NOT destroyed on break (Tower/ArcaneTower go to a broken shell,
                // so ArcaneAura.OnDisable/OnDestroy never fire and the aura keeps looping).
                // A still-standing damaged structure gets the lazy first-damage bar as
                // before; a repaired structure restores both.
                if (broken)
                {
                    if (!rec.CleanedUpOnBreak)
                    {
                        rec.CleanedUpOnBreak = true;

                        var bar = rec.Host.GetComponent<FloatingHealthBar>();
                        bool hadBar = bar != null;
                        if (hadBar) bar.Teardown();
                        rec.BarAttached = false;

                        var aura = rec.Host.GetComponentInChildren<ArcaneAura>(true);
                        bool hadAura = aura != null;
                        if (hadAura) aura.StopAndDisable();

                        FlowTrace.Step("StructureDeath",
                            $"cleanup '{rec.Name}' ({rec.TypeKey}): HP bar {(hadBar ? "TORN-DOWN" : "none")} + " +
                            $"arcane aura {(hadAura ? "STOPPED" : "n/a")} - dead shell shows no empty 0-bar, no aura loop.");
                    }
                }
                else
                {
                    if (rec.CleanedUpOnBreak)
                    {
                        // Repaired / standing again - restore the aura we stopped on break
                        // (symmetric cleanup so a repaired spire does not lose its aura).
                        var aura = rec.Host.GetComponentInChildren<ArcaneAura>(true);
                        if (aura != null && !aura.enabled) aura.enabled = true;
                        rec.CleanedUpOnBreak = false;
                        FlowTrace.Step("StructureDeath",
                            $"restore '{rec.Name}' ({rec.TypeKey}): repaired - aura {(aura != null ? "re-enabled" : "n/a")}.");
                    }

                    // Health bar - attach lazily on first damage (hideAtFull keeps it
                    // invisible again at full HP).
                    // WO-1717: the flinch (StructureHitReaction's onHit -> AttachBar) now
                    // wins this race in the common case, same-frame. This stays as the
                    // catch-all for damage too small to trip the flinch's MinDropFraction,
                    // and is a no-op once the bar is on.
                    if (!rec.BarAttached && hp < 0.999f) AttachBar(rec, "eval-poll");
                }

                // Break transition — one-shot burst at the moment it broke. A shell
                // that was ALREADY broken when first observed (scene load / save
                // restore) gets the persistent ember only, never a phantom burst.
                if (broken && !rec.WasBroken && rec.Observed)
                {
                    VFXManager.PlayKey(BreakKey, rec.VfxAnchor);
                    FlowTrace.Step("DamageVis", $"BREAK burst: '{rec.Name}' ({rec.TypeKey})");
                }
                rec.WasBroken = broken;
                rec.Observed = true;

                // ── WO-1352: the SCUFF band — the rung below the smolder ─────────
                // Driven every eval for every tracked structure, because this band covers
                // the whole town rather than the damaged few. The cost of that breadth is
                // exactly one float compare here: ApplyScuff returns immediately unless the
                // STEP changed, so an undamaged town does no work and holds no property
                // block at all.
                int scuffStep = ScuffStepFor(hp, rec.TypeKey);
                // Gated so a pristine structure allocates NOTHING - not even the Guard
                // closure. In a full untouched town this whole band is one int compare each.
                if (scuffStep > 0 || rec.ScuffStep > 0)
                    Guard.Try("DamageVis", "scuff band", () => ApplyScuff(rec, scuffStep));

                // Desired burn tier from the data thresholds.
                int wantTier = BurnTierFor(hp, broken, rec.TypeKey);
                rec.PendingTier = wantTier;
                if (wantTier > 0)
                {
                    _burnWants.Add(rec);   // capped assignment happens below
                }
                else if (rec.BurnTier != 0 || rec.Burn != null)
                {
                    rec.Burn?.Stop();
                    rec.Burn = null;
                    rec.BurnTier = 0;
                    rec.BurnKey = null;
                }

                // ── WO-892: the CRITICAL-SAVE alarm ──────────────────────────────
                // Armed only on a structure that is BOTH critically damaged AND still
                // standing. A broken shell is deliberately excluded: WO-753 rules that a
                // destroyed structure is gone and returns only through a full-cost
                // rebuild, so an alarm on a ruin would be telling the player to do
                // something the game will not let them do.
                rec.WantsBeacon = !broken && hp <= DamageStatesCatalog.CriticalBeacon(rec.TypeKey);
                if (rec.WantsBeacon) _beaconWants.Add(rec);
                else if (rec.Beacon != null) StopBeacon(rec, "no longer critical");
            }

            foreach (var key in _dead) _tracked.Remove(key);

            // ── Capped burn assignment: worst-first (lowest HP) keeps its loop ────
            int cap = DamageStatesCatalog.MaxBurnLoops;
            _burnWants.Sort((a, b) =>
                Mathf.Clamp01(a.Hp != null ? a.Hp() : 1f)
                    .CompareTo(Mathf.Clamp01(b.Hp != null ? b.Hp() : 1f)));
            if (_burnWants.Count > cap)
                FlowTrace.Throttle("DamageVis", "burn-cap", 5f,
                    $"burn loops capped: {_burnWants.Count} structures want fire, cap={cap} (worst-first kept).");

            for (int i = 0; i < _burnWants.Count; i++)
            {
                var rec = _burnWants[i];
                int tier = i < cap ? rec.PendingTier : 0;

                if (tier == 0)
                {
                    if (rec.Burn != null) { rec.Burn.Stop(); rec.Burn = null; }
                    rec.BurnTier = 0;
                    rec.BurnKey = null;
                    continue;
                }

                // WO-892: WHICH recipe this tier wants. Tier 1 is the smoke wisp; tier 2
                // splits by whether the thing is still standing - a burning building and a
                // smoking ruin are different pictures, and before this they were the same
                // loop at the same scale.
                string wantKey = tier == 1 ? SmolderKey : (rec.WasBroken ? RuinKey : FireKey);

                // Restart when the TIER changed, when the RECIPE changed (the broken
                // transition, which keeps tier 2), or when the loop was lost / cap-skipped.
                if (rec.BurnTier == tier && rec.BurnKey == wantKey &&
                    rec.Burn != null && rec.Burn.IsAlive) continue;

                // Pooled; PlayKey returns null on the global loop cap or an absent key -
                // leave tier at 0 so the next eval retries rather than latching dark.
                rec.Burn?.Stop(immediate: true);
                rec.Burn = VFXManager.PlayKey(wantKey, rec.VfxAnchor,
                    scale: tier == 1 ? SmolderScale : FireScale);
                rec.BurnTier = rec.Burn != null ? tier : 0;
                rec.BurnKey  = rec.Burn != null ? wantKey : null;
                if (rec.Burn != null)
                    FlowTrace.Step("DamageVis",
                        $"burn {(tier == 1 ? "SMOLDER" : (rec.WasBroken ? "RUIN" : "FIRE"))} " +
                        $"('{wantKey}'): '{rec.Name}' ({rec.TypeKey})");
                else
                    FlowTrace.Throttle("DamageVis", "burn-refused", 5f,
                        $"burn loop REFUSED for '{rec.Name}' ({rec.TypeKey}) key='{wantKey}' - " +
                        "global loop cap hit, or the key is absent from HovlVfxCatalog. " +
                        "Retrying next eval; the structure shows no damage state meanwhile.");
            }

            // ── WO-892: capped critical-beacon assignment, worst-first ────────────
            // Deliberately a SECOND, SMALLER cap rather than sharing maxBurnLoops. An
            // alarm is only useful if it points somewhere: eight simultaneous alarms are
            // wallpaper, three are a decision. It also bounds this component's total loop
            // footprint at maxBurnLoops + maxCriticalBeacons against the global cap of 20.
            int beaconCap = DamageStatesCatalog.MaxCriticalBeacons;
            _beaconWants.Sort((a, b) =>
                Mathf.Clamp01(a.Hp != null ? a.Hp() : 1f)
                    .CompareTo(Mathf.Clamp01(b.Hp != null ? b.Hp() : 1f)));
            if (_beaconWants.Count > beaconCap)
                FlowTrace.Throttle("DamageVis", "beacon-cap", 5f,
                    $"critical beacons capped: {_beaconWants.Count} structures are critical, " +
                    $"cap={beaconCap} (worst-first kept - the alarm points at the ones closest to lost).");

            _beaconLive.Clear();
            for (int i = 0; i < _beaconWants.Count; i++)
            {
                var rec = _beaconWants[i];
                if (i >= beaconCap) { StopBeacon(rec, "cap eviction"); continue; }
                if (rec.Beacon != null && !rec.Beacon.IsAlive) StopBeacon(rec, "handle went dead");
                if (rec.Beacon == null) StartBeacon(rec);
                if (rec.Beacon != null) _beaconLive.Add(rec);
            }
        }

        // ── WO-892: the critical-save beacon ─────────────────────────────────────

        /// <summary>
        /// Arm the alarm on one structure: a held pooled loop plus the "!" glyph. A refused
        /// start (global loop cap / absent key) leaves the record with no beacon so the next
        /// Evaluate retries - a refusal must never latch, or a structure that became
        /// critical during a busy moment would stay silent for the rest of the raid.
        /// </summary>
        private void StartBeacon(Tracked rec)
        {
            if (rec == null || rec.Host == null) return;

            // Seated ON the structure body (the bounds centre), not above it: the phone is
            // landscape at 2670x1200 and the vertical axis is the one that crops. Parented
            // to the host so a moved/rebuilt structure carries its alarm with it and the
            // VFXManager destroyed-host sweep can reclaim the instance if the host dies
            // between evals.
            rec.Beacon = VFXManager.PlayKey(BeaconKey, rec.VfxAnchor,
                Quaternion.identity, rec.Host.transform);
            if (rec.Beacon == null)
            {
                FlowTrace.Throttle("DamageVis", "beacon-refused", 2f,
                    $"CRITICAL beacon REFUSED for '{rec.Name}' ({rec.TypeKey}) - global loop cap hit " +
                    "or key absent. This is the 'save it NOW' read; retrying next eval.");
                return;
            }

            rec.BeaconPhase = 0f;   // every alarm starts ON the beat, so it reads immediately

            // Clear any modulation left by this pooled instance's previous owner before
            // driving it (VfxLoopModulator restores on return, but capturing a clean
            // baseline here costs nothing and makes the first blink correct).
            var mod = rec.Beacon.Modulator;
            if (mod != null)
            {
                mod.SetSimulationSpeed(BeaconSimSpeed);
                mod.SetEmissionScale(BeaconCrest);
            }

            EnsureBeaconTag(rec);

            FlowTrace.Step("DamageVis",
                $"CRITICAL beacon ARMED: '{rec.Name}' ({rec.TypeKey}) hp={(rec.Hp != null ? rec.Hp() : 1f):0.00} " +
                $"- {BeaconHz:0.0} Hz blink + '{BeaconGlyph}' glyph (rhythm + glyph, not colour).");
        }

        /// <summary>
        /// Disarm the alarm and return its loop slot. Idempotent and safe with nothing
        /// held. Called from EVERY exit path: no longer critical, broken (a ruin cannot be
        /// saved), cap eviction, a dead handle, host destroyed, OnDisable and OnDestroy.
        /// </summary>
        private void StopBeacon(Tracked rec, string reason)
        {
            if (rec == null) return;
            bool had = rec.Beacon != null;
            if (had)
            {
                rec.Beacon.Stop();   // Stop restores the instance's modulation before pooling
                rec.Beacon = null;
            }
            DestroyBeaconTag(rec);
            if (had)
                FlowTrace.Step("DamageVis",
                    $"CRITICAL beacon released: '{rec.Name}' ({rec.TypeKey}) reason={reason} - loop slot returned.");
        }

        /// <summary>
        /// The per-frame blink. Runs over <c>_beaconLive</c> only (at most
        /// maxCriticalBeacons entries), and drives BOTH the spark emission and the glyph
        /// scale off ONE wave so the two are visibly the same signal rather than two
        /// effects that happen to be near each other.
        /// </summary>
        private void DriveBeacons()
        {
            float step = Time.deltaTime * BeaconHz * Mathf.PI * 2f;

            for (int i = _beaconLive.Count - 1; i >= 0; i--)
            {
                var rec = _beaconLive[i];
                if (rec == null || rec.Beacon == null || !rec.Beacon.IsAlive)
                {
                    // The pool reclaimed it under us (host destroyed, scene teardown).
                    // Drop it from the live list; Evaluate owns re-arming.
                    if (rec != null && rec.Beacon != null) StopBeacon(rec, "handle died mid-blink");
                    _beaconLive.RemoveAt(i);
                    continue;
                }

                rec.BeaconPhase += step;
                if (rec.BeaconPhase > Mathf.PI * 2f) rec.BeaconPhase -= Mathf.PI * 2f;

                // 0..1 sine, then SHARPENED: most of the cycle sits near zero and the peak
                // is a snap. A plain sine reads as breathing (which is what the hero's
                // wounded aura does, on purpose); an alarm has to read as blinking.
                float wave01 = 0.5f + 0.5f * Mathf.Sin(rec.BeaconPhase);
                float blink  = Mathf.Pow(wave01, BeaconWaveSharpness);

                var mod = rec.Beacon.Modulator;
                if (mod != null) mod.SetEmissionScale(Mathf.Lerp(BeaconTrough, BeaconCrest, blink));

                if (rec.BeaconTag != null)
                {
                    float s = Mathf.Lerp(BeaconGlyphScaleMin, BeaconGlyphScaleMax, blink);
                    rec.BeaconTag.localScale = new Vector3(s, s, s);

                    // Billboard the glyph so "!" is never read edge-on. Flat look-at, no
                    // roll - the same idiom DungeonExitBeacon uses for its EXIT label.
                    var cam = Camera.main;
                    if (cam != null)
                        rec.BeaconTag.rotation =
                            Quaternion.LookRotation(rec.BeaconTag.position - cam.transform.position);
                }
            }
        }

        /// <summary>
        /// Build the "!" glyph once per armed beacon. A legacy TextMesh, because code-built
        /// UI is the law here (UXML does not work in builds) and TextMesh is the idiom the
        /// project already uses for world labels (DungeonExitBeacon, BuildingSign). If no
        /// built-in font resolves, the beacon ships without the glyph and says so - the
        /// spark blink still carries the read on its own.
        /// </summary>
        private void EnsureBeaconTag(Tracked rec)
        {
            if (rec == null || rec.Host == null || rec.BeaconTag != null) return;

            var font = Guard.Try("DamageVis", "resolve builtin font",
                () => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
            if (font == null)
                font = Guard.Try("DamageVis", "resolve builtin font (Arial fallback)",
                    () => Resources.GetBuiltinResource<Font>("Arial.ttf"), null);
            if (font == null)
            {
                FlowTrace.Warn("DamageVis",
                    $"no builtin font resolved - '{rec.Name}' gets the blink without the '{BeaconGlyph}' glyph.");
                return;
            }

            var go = new GameObject("CriticalSaveTag");
            go.transform.SetParent(rec.Host.transform, false);
            // Just above the health bar the player is already reading, so the glyph and the
            // empty bar are one glance rather than two.
            go.transform.localPosition = Vector3.up * (rec.BarOffset + BeaconGlyphRise);

            var tm = go.AddComponent<TextMesh>();
            tm.text = BeaconGlyph;
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = 0.16f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            // Neutral white ON PURPOSE. A red "!" would put the single most urgent signal
            // in this system on the one channel the owner cannot see - the exact defect
            // WO-888 fixed for low HP. The urgency is the GLYPH and the BLINK.
            tm.color = Color.white;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (font.material != null) mr.sharedMaterial = font.material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            rec.BeaconTag = go.transform;
        }

        // ── WO-1352: the scuff band's apply / restore ────────────────────────────

        /// <summary>
        /// Drive one structure's surface to <paramref name="step"/> (0 = pristine). A no-op
        /// unless the step actually changed or the host's visible renderer set changed, so
        /// the steady-state cost of this whole band across a full town is one integer
        /// compare per structure per 0.3 s eval.
        /// </summary>
        private void ApplyScuff(Tracked rec, int step)
        {
            if (rec == null || rec.Host == null) return;

            // ⛔ THE PERFORMANCE GUARD, AND IT IS THE FIRST LINE ON PURPOSE. A pristine
            // structure that has never been damaged must never be resolved, never be
            // written to, and never receive a MaterialPropertyBlock at all - a renderer
            // carrying an MPB drops OUT of the SRP batcher, so touching every building in a
            // full town "just to set it back to its own colour" would cost draw calls
            // permanently, on the exact device that already reports [Flow:VfxPerfGate]
            // hitches against a 16.7 ms budget. Everything below this line therefore runs
            // only for a structure that is damaged now or was damaged earlier in the
            // session; for the whole rest of the town this method is one integer compare.
            if (step <= 0 && rec.ScuffStep <= 0) return;

            // ⚠ THE BAKED-TWIN CASE (proven 2026-09-02): hub structures are BAKED TWINS
            // re-skinned by HubStructureVisualInjector and never route through
            // StructureFactory. SkinStorefront hides the baked model with `r.enabled = false`
            // and PARENTS a LightSkin_ child under the same host - and that child can arrive
            // AFTER we first resolved. So the resolve is (a) filtered on `enabled`, so the
            // hidden baked mesh is never the thing we tint, and (b) re-run whenever the
            // host's childCount moves, so a late skin picks the tell up on the next eval.
            // Without both halves the tell would work everywhere except the town she is
            // looking at.
            bool structureChanged = rec.ScuffRenderers == null || rec.Host.transform.childCount != rec.ScuffChildCount;
            if (!structureChanged && rec.ScuffRenderers != null)
                for (int i = 0; i < rec.ScuffRenderers.Length; i++)
                    if (rec.ScuffRenderers[i] == null) { structureChanged = true; break; }

            if (step == rec.ScuffStep && !structureChanged) return;

            if (structureChanged)
            {
                // Restore whatever the OLD list still holds before dropping it, so a swapped
                // skin never strands a darkened renderer we no longer track.
                RestoreScuffBaselines(rec);
                ResolveScuffRenderers(rec);
            }

            rec.ScuffStep = step;

            if (rec.ScuffRenderers == null || rec.ScuffRenderers.Length == 0)
            {
                if (step > 0 && !rec.ScuffUnreachable)
                {
                    rec.ScuffUnreachable = true;
                    // NO SILENT FAILURE (CLAUDE.md section 12): a structure that is
                    // repair-eligible but cannot show the tell names ITSELF here, rather
                    // than the owner discovering it as another surprise charge.
                    FlowTrace.Warn("DamageVis",
                        $"scuff UNREACHABLE: '{rec.Name}' ({rec.TypeKey}) step={step} - no drivable " +
                        "single-albedo body renderer under this host (every one is hidden, an effect " +
                        "renderer, mesh-less, or the all-or-nothing gate forfeited the structure because " +
                        "one visible body mesh could not be driven - see the ABSTAINS line above for " +
                        "which). This structure is " +
                        "repair-eligible with NO surface tell; the health bar and the burn ladder " +
                        "are unaffected.");
                }
                return;
            }

            if (step <= 0)
            {
                RestoreScuffBaselines(rec);
                FlowTrace.Step("DamageVis",
                    $"scuff CLEARED: '{rec.Name}' ({rec.TypeKey}) - repaired to pristine, " +
                    $"{rec.ScuffRenderers.Length} renderer(s) restored to their captured albedo.");
                return;
            }

            int steps = Mathf.Max(1, DamageStatesCatalog.ScuffSteps);
            // The two channels' ramps live in ScuffDarkenFor / ScuffGlossMulFor so the
            // regression oracle can pin the STRENGTH against the shipping code rather than
            // against a copy of the arithmetic. Do not re-inline them here.
            float mul = Mathf.Clamp01(1f - ScuffDarkenFor(step));
            float glossMul = ScuffGlossMulFor(step);

            var block = new MaterialPropertyBlock();
            int applied = 0;
            for (int i = 0; i < rec.ScuffRenderers.Length; i++)
            {
                var r = rec.ScuffRenderers[i];
                if (r == null) continue;
                Color b = rec.ScuffBaseColors[i];
                // SCALAR multiply on R, G and B alike - the hue and the saturation ratio are
                // untouched and only the VALUE moves, so the tell reads identically in
                // greyscale. Alpha is preserved verbatim (a transparent material must not
                // become opaque because it took a hit).
                var c = new Color(b.r * mul, b.g * mul, b.b * mul, b.a);
                r.GetPropertyBlock(block);
                block.SetColor(ScuffBaseColorId, c);
                block.SetColor(ScuffLegacyColorId, c);
                block.SetFloat(ScuffSmoothnessId, rec.ScuffBaseGloss[i] * glossMul);
                r.SetPropertyBlock(block);
                applied++;
            }

            FlowTrace.Step("DamageVis",
                $"scuff step {step}/{steps}: '{rec.Name}' ({rec.TypeKey}) hp={(rec.Hp != null ? rec.Hp() : 1f):0.000} " +
                $"damageFraction={(1f - (rec.Hp != null ? rec.Hp() : 1f)):0.000} band=SCUFF " +
                $"applied albedo x{mul:0.00} (VALUE only, no hue shift) + smoothness x{glossMul:0.00} " +
                $"to {applied} renderer(s). Smolder hands over at hp<={DamageStatesCatalog.Smolder(rec.TypeKey):0.00}.");
        }

        /// <summary>
        /// Collect the renderers this structure's surface tell may drive, and capture their
        /// baseline albedo + smoothness for an exact restore.
        /// </summary>
        /// <remarks>
        /// WO-1352 FOLLOW-UP — THIS METHOD NOW ANSWERS TWO SEPARATE QUESTIONS, and conflating
        /// them is what made a correct result look like a defect on the owner's device. Her
        /// trace read "1 eligible of 2" on an Archer Tower, which reads as "half the building
        /// is being tinted". It was not: the second renderer was
        /// <see cref="DefenseTower"/>'s AimBeam LineRenderer - the lock-on laser, a child of
        /// the tower host, created with `enabled = false` until a target is locked. It is not
        /// part of the building at all, and dropping it was right. The line just could not
        /// say so, because every skip reason shared one counter.
        ///
        /// So the two questions are now asked separately:
        ///   1. IS THIS RENDERER THE BUILDING'S BODY? Only a MeshRenderer or a
        ///      SkinnedMeshRenderer with an actual mesh is. A LineRenderer, TrailRenderer,
        ///      SpriteRenderer or ParticleSystemRenderer under the host is an EFFECT the
        ///      structure owns, never its surface - it is ignored outright and is NOT a
        ///      coverage hole. This is also a real bug fix and not only a clearer log: the
        ///      old `enabled` filter dropped the aim beam only WHILE IT WAS OFF, so the first
        ///      resolve of a tower that had already locked a target - i.e. any tower damaged
        ///      during a raid, which is when towers get damaged - would have captured the
        ///      laser and darkened it as though it were masonry.
        ///   2. AMONG THE BODY RENDERERS THAT ARE ACTUALLY VISIBLE, CAN WE DRIVE THEM ALL?
        ///      ⛔ ALL-OR-NOTHING (owner-delegated ruling 2026-09-02). A half-tinted building
        ///      reads as PATCHY - a rendering artefact - while an untinted one merely reads
        ///      as undamaged, and only one of those two costs the player trust in the whole
        ///      surface. So if even one visible body renderer cannot be driven correctly
        ///      (submesh albedos that differ by more than <see cref="ScuffMultiMatEpsilon"/>,
        ///      so one property block would recolour one of them wrongly; or no material at
        ///      all), the WHOLE structure abstains and says so through the existing
        ///      "scuff UNREACHABLE" warning. Partial coverage is never shipped.
        ///
        /// ⚠ `enabled` STAYS THE VISIBILITY TEST AND MUST NOT BECOME A COVERAGE HOLE.
        /// HubStructureVisualInjector.SkinStorefront hides the BAKED TWIN with
        /// `r.enabled = false` (never SetActive), so a hub structure legitimately carries a
        /// disabled body mesh that draws nothing. Counting that as a hole would make every
        /// hub structure in her town abstain - the exact town she is looking at. It is
        /// ignored, like an effect renderer, and only VISIBLE body geometry can trip the
        /// all-or-nothing abort. The childCount re-resolve in ApplyScuff stays untouched for
        /// the same reason: a late-arriving LightSkin_ child must still pick the tell up.
        /// </remarks>
        private void ResolveScuffRenderers(Tracked rec)
        {
            rec.ScuffRenderers = null;
            rec.ScuffBaseColors = null;
            rec.ScuffBaseGloss = null;
            rec.ScuffChildCount = rec.Host != null ? rec.Host.transform.childCount : 0;
            if (rec.Host == null) return;

            var all = rec.Host.GetComponentsInChildren<Renderer>(false);
            var keep = new List<Renderer>();
            var cols = new List<Color>();
            var gloss = new List<float>();
            int notBody = 0;        // effects + mesh-less: never the surface, never a hole
            int hiddenBody = 0;     // baked twins and the like: invisible, never a hole
            int undrivable = 0;     // VISIBLE body we cannot drive - this is what aborts
            string undrivableWhy = null;

            foreach (var r in all)
            {
                if (r == null) continue;

                // (1) Body or effect? Whitelist, not blacklist - a blacklist has to be
                // extended every time a new Renderer subclass appears under a structure,
                // and the cost of missing one is tinting an effect as though it were stone.
                // This subsumes the old explicit ParticleSystemRenderer skip.
                bool isBody = r is MeshRenderer || r is SkinnedMeshRenderer;
                if (isBody)
                {
                    if (r is SkinnedMeshRenderer smr) isBody = smr.sharedMesh != null;
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        isBody = mf != null && mf.sharedMesh != null;
                    }
                }
                if (!isBody) { notBody++; continue; }

                // (2) Visible? A disabled body mesh draws nothing (the hidden baked twin).
                if (!r.enabled) { hiddenBody++; continue; }

                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0 || mats[0] == null)
                {
                    undrivable++;
                    undrivableWhy = undrivableWhy ?? $"'{r.name}' has no material to read a base colour from";
                    continue;
                }

                Color baseCol;
                if (mats[0].HasProperty(ScuffBaseColorId)) baseCol = mats[0].GetColor(ScuffBaseColorId);
                else if (mats[0].HasProperty(ScuffLegacyColorId)) baseCol = mats[0].GetColor(ScuffLegacyColorId);
                else baseCol = Color.white;

                bool uniform = true;
                for (int m = 1; m < mats.Length && uniform; m++)
                {
                    var mm = mats[m];
                    if (mm == null) continue;
                    Color c2 = mm.HasProperty(ScuffBaseColorId) ? mm.GetColor(ScuffBaseColorId)
                             : mm.HasProperty(ScuffLegacyColorId) ? mm.GetColor(ScuffLegacyColorId)
                             : Color.white;
                    uniform = Mathf.Abs(c2.r - baseCol.r) <= ScuffMultiMatEpsilon
                           && Mathf.Abs(c2.g - baseCol.g) <= ScuffMultiMatEpsilon
                           && Mathf.Abs(c2.b - baseCol.b) <= ScuffMultiMatEpsilon;
                }
                if (!uniform)
                {
                    undrivable++;
                    undrivableWhy = undrivableWhy ??
                        $"'{r.name}' is multi-material with submesh base colours further apart than " +
                        $"{ScuffMultiMatEpsilon:0.###}, so one property block cannot recolour them both";
                    continue;
                }

                float g = mats[0].HasProperty(ScuffSmoothnessId) ? mats[0].GetFloat(ScuffSmoothnessId) : 0.5f;

                keep.Add(r);
                cols.Add(baseCol);
                gloss.Add(g);
            }

            // ⛔ THE ALL-OR-NOTHING GATE. One undrivable VISIBLE body renderer forfeits the
            // whole structure's tell rather than tinting the rest of it. ApplyScuff's
            // existing empty-list path then emits the "scuff UNREACHABLE" warning, so the
            // abstention is loud (CLAUDE.md section 12: no silent failures).
            int drivable = keep.Count;
            if (undrivable > 0 && drivable > 0)
            {
                keep.Clear();
                cols.Clear();
                gloss.Clear();
                FlowTrace.Warn("DamageVis",
                    $"scuff ABSTAINS on '{rec.Name}' ({rec.TypeKey}): {undrivable} of {drivable + undrivable} " +
                    $"VISIBLE body renderer(s) cannot be driven ({undrivableWhy}). Tinting the other " +
                    $"{drivable} would leave the building visibly PATCHY, which reads as a rendering fault " +
                    "rather than as wear - so the whole structure keeps its pristine surface. The health " +
                    "bar and the smolder/fire/beacon ladder are unaffected.");
            }

            rec.ScuffRenderers = keep.ToArray();
            rec.ScuffBaseColors = cols.ToArray();
            rec.ScuffBaseGloss = gloss.ToArray();
            if (keep.Count > 0) rec.ScuffUnreachable = false;

            FlowTrace.Throttle("DamageVis", "scuff-resolve:" + rec.TypeKey, 5f,
                $"scuff renderers resolved for '{rec.Name}' ({rec.TypeKey}): driving {keep.Count} of " +
                $"{all.Length} renderer(s) under the host - {notBody} are effects or mesh-less (line/trail/" +
                $"sprite/particle: NOT the building, not a coverage hole), {hiddenBody} are hidden body " +
                $"meshes (baked twins behind an injected skin: invisible, not a coverage hole), " +
                $"{undrivable} are visible body meshes we cannot drive (ALL-OR-NOTHING: any one of these " +
                "forfeits the whole structure's tell).");
        }

        /// <summary>
        /// Put every driven renderer back exactly as it was found. Idempotent, safe with
        /// nothing captured, and called from every exit path - repaired to pristine, a
        /// re-skin, OnDisable and OnDestroy - so this component can never leave a town
        /// permanently darkened.
        /// </summary>
        private void RestoreScuffBaselines(Tracked rec)
        {
            if (rec == null || rec.ScuffRenderers == null) return;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < rec.ScuffRenderers.Length; i++)
            {
                var r = rec.ScuffRenderers[i];
                if (r == null) continue;
                Color b = rec.ScuffBaseColors[i];
                r.GetPropertyBlock(block);
                block.SetColor(ScuffBaseColorId, b);
                block.SetColor(ScuffLegacyColorId, b);
                block.SetFloat(ScuffSmoothnessId, rec.ScuffBaseGloss[i]);
                r.SetPropertyBlock(block);
            }
            rec.ScuffStep = 0;
        }

        /// <summary>Tear the "!" glyph down. Safe when there is none, and when the host is
        /// already gone (the glyph is its child, but the teardown is explicit so it is not
        /// order-dependent).</summary>
        private void DestroyBeaconTag(Tracked rec)
        {
            if (rec == null || rec.BeaconTag == null) return;
            var go = rec.BeaconTag.gameObject;
            rec.BeaconTag = null;
            if (go != null) Destroy(go);
        }
    }
}

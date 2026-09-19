// =============================================================================
// VfxLoopFlagRegression [vfx-loop-flag] -- the oracle that stops a BURST prefab
// from being catalogued as a LOOP, and the single home of the loop-vs-burst
// derivation the whole project shares.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
//
// WHY THIS EXISTS (the leak it guards, proven from captured data):
//   VFXManager.Hovl.cs ~283-288 -- a row with IsLoop TRUE increments _activeLoops,
//   hands back a VFXHandle and registers NO reclaim deadline. Only the oneshot
//   branch (~290-297) calls RegisterOneshot + ReturnHovlAfterSeconds. The ONLY
//   loop reclaim in the system is PruneDestroyedFromSet (VFXManager.cs ~973),
//   which frees loops whose HOST was destroyed -- and pooled hosts are never
//   destroyed. So any fire-and-forget play of a loop-flagged row leaks one of the
//   _maxActiveLoops = 20 slots (VFXManager.cs:142) for the rest of the session.
//   Six separate F8 captures show that cap saturated and starving a live effect:
//     capture-20260730-175552.md:55  PlayKey('ArcherTower_Projectile') SKIPPED -
//                                    active loops 20/20 (cap hit)
//     capture-20260730-175447.md:21  ARcaneTower_Projectile
//     capture-20260730-175729.md:54  ArcaneTower-Baselevel_Projectile
//     capture-20260716-205819.md:99  Poi_NodeAura
//     capture-20260716-210343.md:97  Poi_Landmark
//   The flags got there because IsLoop was a STICKY MANUAL UI TOGGLE in
//   VfxCasterWindow (a checkbox, force-set true for the Projectile/Aura roles),
//   never a read of the prefab. 95 of 135 HovlVfxCatalog rows carried IsLoop:1,
//   including PP_BigExplosion / PP_MuzzleFlash / PP_EarthShatter and friends --
//   rate-0 + burst-at-t0 prefabs that self-terminate in under a second.
//
// WHAT IT ASSERTS -- TWO FACTS ABOUT EVERY ROW, IN ONE PASS:
//
//   (A) THE MIRROR JOIN (added 2026-08-22 -- see CheckRowPointsAtMirror below for the
//       full RCA). A row must not point at a GITIGNORED PACK PREFAB for which a
//       committed, repaired MIRROR exists on disk. This is the assertion that was
//       missing when the five PP_*Impacts rows shipped pointing at the unrepaired
//       pack copies: THIS suite compared each row's flag against the prefab the row
//       pointed at (both the pack copy, both loop -> green) and
//       SurfaceImpactVfxRegression asserted the MIRROR was one-shot (it was ->
//       green). Neither asserted the row POINTS AT the mirror, and the leak lived in
//       the gap between the two correct assertions.
//
//   (B) THE FLAG (the original charter):
//   For every catalog row (HovlVfxCatalog.asset AND VFXCatalog.asset) whose
//   prefab RESOLVES and carries a ParticleSystem, the STORED IsLoop equals the
//   value DERIVED from that prefab's emission. Rows whose prefab is missing (the
//   art packs are gitignored; a fresh clone has none of them) or that carry no
//   ParticleSystem at all are SKIPPED AND COUNTED -- never failed. A suite that
//   goes red on a clean clone is a suite the next person deletes.
//
// SHARED DERIVATION (deliberate): TryDerive below is the ONE implementation.
//   VfxLoopFlagAudit (the fixer) and VfxCasterWindow (the authoring UI) both call
//   it, because DeNelle.Editor references DeNelle.EditorRegression. The oracle is
//   not "testing its own algorithm": it asserts a fact about STORED DATA (the
//   .asset bytes) against the prefabs on disk. One derivation means the tool that
//   writes the flag and the gate that judges it can never disagree.
//
// POSITIVE CONTROL (prove it can go red): open
//   Assets/Resources/VFX/HovlVfxCatalog.asset, flip any one row's `IsLoop: 0` to
//   `IsLoop: 1` (e.g. PP_TinyExplosion), re-run the suite -- it must fail naming
//   that exact key with stored=True derived=False. Flip it back.
//
// Editor-only asset reads. No scene, no play mode, no runtime singletons.
//
// Registered in DataRegression.RunAll as [vfx-loop-flag].
// =============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class VfxLoopFlagRegression
    {
        public const string HovlCatalogPath = "Assets/Resources/VFX/HovlVfxCatalog.asset";
        public const string TypedCatalogPath = "Assets/Resources/VFX/VFXCatalog.asset";

        // How many curve samples to take when an emission rate is authored as a curve
        // rather than a constant. Keys alone can miss a hump between them.
        private const int CurveSamples = 12;

        // =====================================================================
        //  THE DERIVATION -- the single authority on "is this prefab a loop?"
        // =====================================================================
        //
        // COMBINING RULE (explicit, and it is an AND, not an OR):
        //
        //   derivedIsLoop = root.main.loop  AND  root emits by RATE
        //
        //   where "emits by RATE" means the emission module is ENABLED and
        //   max(rateOverTime) > 0 OR max(rateOverDistance) > 0.
        //
        // Both halves are load-bearing:
        //   * main.loop == false  -> the system stops on its own after main.duration.
        //     It is NOT a sustained stream no matter how high the rate is, so it must
        //     be a oneshot or the pool slot is never reclaimed.
        //   * rate == 0 with only BURSTS -> the classic explosion/impact shape:
        //     everything is spat out at t=0 and the system idles. Even when someone
        //     left main.loop ticked on such a prefab (looping an empty stream), it
        //     self-terminates visually and must never hold a loop slot. This is the
        //     exact shape of all eleven PP_* explosion/impact rows the audit flips.
        //
        // rateOverDistance counts as a rate on purpose: a projectile trail that emits
        // per metre travelled is a genuinely sustained emitter whose lifetime is the
        // FLIGHT, not a duration -- calling it a oneshot would reclaim it mid-flight.
        // The audit reports how many rows qualify by distance alone so that widening
        // is auditable rather than silent.
        //
        // AUTHORITY IS THE ROOT SYSTEM: the prefab root's own ParticleSystem, or the
        // first one in the hierarchy when the root is a bare transform (the Hovl
        // packs' usual shape). Sub-emitters and secondary smoke layers are decoration;
        // the root is what "this effect" means. When a child disagrees with the root
        // the detail string says so -- reported, never allowed to change the verdict.
        //
        // ONE EXCEPTION, and it is measured, not assumed: a root whose EMISSION MODULE
        // IS DISABLED emits nothing at all, so it is a container, not the effect. The
        // authority then falls through to the first system that can actually emit.
        // Assets/Lana Studio/Casual RPG VFX/Prefabs/Fire/Fire_medium.prefab is exactly
        // this shape -- root ParticleSystem with `enabled: 0` on its emission over a
        // child emitting 15/sec on loop. Reading the disabled root would call the
        // burning-structure / torch / fog auras one-shots and cut them off mid-burn.
        // The fallback skips systems on inactive GameObjects for the same reason.
        //
        // Returns FALSE when the truth is UNDETERMINABLE (null prefab, or no
        // ParticleSystem anywhere in it). An undeterminable prefab must never flip a
        // stored flag and must never fail a gate -- callers skip and name it.
        // ── Standing owner rulings outrank the derivation ────────────────────────
        //
        // The prefab is the authority on what the ART DOES. It is NOT the authority on
        // what the GAME SHOULD DO with it. Where the owner has already ruled on a row
        // after seeing it in play, that ruling wins and is pinned here with its reason.
        //
        // A pin is not a workaround for an inconvenient derivation - it is the record of
        // a decision that came from felt play, which no amount of reading emission
        // modules can reproduce. Adding one requires an owner ruling to point at.
        //
        // Keyed by catalog key; value = the flag the row MUST carry.
        private static readonly System.Collections.Generic.Dictionary<string, bool> OwnerPinned =
            new System.Collections.Generic.Dictionary<string, bool>
        {
            // The upgrade fireworks. The prefab genuinely emits continuously (rate 5 on
            // loop), so the derivation promotes it to a loop - and that is exactly the
            // "perma-fireworks" bug the owner reported and had fixed: the celebration
            // never ended. It is played fire-and-forget at BuildModeController's upgrade
            // completion with its handle discarded, so as a loop it would also leak one of
            // the 20 global loop slots forever. A celebration is FINITE. The existing
            // vfx-aura-diff oracle already encodes this ruling and correctly failed the
            // first run of the derivation - that failure is why this table exists.
            { "UpgradeStructureComplete_Aura", false },

            // The Realm Store landmark ring (WO-1052; owner tagged this key to
            // Assets/Resources/VFX/Markers/Marker8_SafeZoneLoop.prefab on 2026-08-21 as the store's
            // persistent near-field signature -- see HovlVfxCatalogGenerator's Map entry).
            //
            // WHAT THE ART ACTUALLY DOES, read from the prefab: root Marker8_SafeZoneLoop is
            // looping:1, lengthInSec:1, emission ENABLED, rateOverTime Constant 0, and ONE burst of
            // 1 at t=0.1. So `rootLoop && byRate` derives FALSE purely on the zero rate.
            // (The `minScalar: 10` sitting next to `scalar: 0` is Unity's vestigial default in the
            // unused TwoConstants slot -- every prefab in Resources/VFX carries it. The derivation
            // reads `scalar`, which is correct; this is NOT a mis-read field.)
            //
            // WHY THE DERIVATION'S PREMISE DOES NOT HOLD HERE: the "rate 0 + bursts only" clause is
            // justified above as the explosion shape that "spits everything out at t=0 and idles".
            // That is true of a system whose main.loop is FALSE. This one loops, and a LOOPING
            // system re-fires its bursts every duration cycle -- the ring genuinely repeats forever
            // and never self-terminates. It is a persistent landmark exactly as the owner intended.
            //
            // WHY A PIN AND NOT A WIDER DERIVATION: seven other prefabs on disk share this exact
            // shape (Buff_Light, PortalCircleDarkStar, Casting_Fire, Casting_Fire_2, BigExplosion,
            // TalentNodePointer, Cast_MuzzleFlash) and EVERY one is stored IsLoop:0 and passes.
            // Widening the rule to (loop && bursts) would flip all seven to expected-loop and
            // re-open the pool leak for the muzzle-flash/explosion rows -- the precise bug this
            // oracle exists to stop. Emission alone cannot separate the two cases; only the CALL
            // SITE can, which is what a pin is for.
            //
            // WHY True IS SAFE HERE (the leak cannot occur): RealmStoreBeacon RETAINS the handle in
            // _nearAura and releases it on four paths -- leaving the 20m proximity ring, scene
            // unload, OnDisable, OnDestroy. It is not fire-and-forget (which is where the documented
            // leak comes from), poolSize is 1, and it only runs while the hero stands at the store.
            // Stamping False instead would send it down the oneshot branch, whose reclaim deadline
            // would return the ring to the pool while the player is still standing there: the
            // landmark vanishes and _nearAura is left holding a stale handle.
            //
            // NOTE for the owner: the ruling pointed at here is her 2026-08-21 pick of this key as a
            // PERSISTENT landmark, not a felt-play ruling on the flag itself. The pin is also the
            // only durable home for it -- HovlVfxCatalogGenerator overrides its own Map literal with
            // TryResolveExpected, so without this entry the next regenerate silently flips the row
            // back to 0 and the beacon starts despawning under the player.
            { "store.beacon.near", true },
        };

        /// <summary>
        /// The flag a row must carry, honouring any standing owner ruling over the
        /// derived value. Returns false when there is no pin for this key.
        /// </summary>
        public static bool TryOwnerPin(string key, out bool pinnedIsLoop, out string why)
        {
            pinnedIsLoop = false;
            why = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!OwnerPinned.TryGetValue(key, out pinnedIsLoop)) return false;
            why = "PINNED by a standing owner ruling - the prefab's own emission is overridden here on purpose";
            return true;
        }

        /// <summary>
        /// THE flag a catalog row must carry: a standing owner ruling if one exists,
        /// otherwise the prefab's derived truth. Every consumer - the audit, this suite,
        /// and both catalog generators - goes through here, so a pin cannot be honoured in
        /// one place and forgotten in another. That divergence is how the original bug
        /// survived: one surface believed a checkbox, another believed the art.
        /// Returns false only when the truth is UNDETERMINABLE (skip and name it).
        /// </summary>
        public static bool TryResolveExpected(string key, GameObject prefab,
                                              out bool expected, out string detail)
        {
            string why;
            if (TryOwnerPin(key, out expected, out why))
            {
                // Still derive, purely so the report can say what the art actually does
                // and how far the ruling departs from it. A pin that silently matched the
                // derivation would be dead weight nobody could audit.
                bool derived; string dd;
                detail = TryDerive(prefab, out derived, out dd)
                    ? why + " (art derives " + derived + "; " + dd + ")"
                    : why + " (art underivable: " + dd + ")";
                return true;
            }
            return TryDerive(prefab, out expected, out detail);
        }

        public static bool TryDerive(GameObject prefab, out bool derivedIsLoop, out string detail)
        {
            derivedIsLoop = false;
            detail = "no prefab";
            if (prefab == null) return false;

            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0)
            {
                detail = "no ParticleSystem in prefab";
                return false;
            }

            bool viaFallback;
            var root = PickAuthority(prefab, systems, out viaFallback);
            if (root == null)
            {
                detail = "no ParticleSystem in prefab";
                return false;
            }

            bool rootLoop = root.main.loop;
            var em = root.emission;
            bool emissionOn = em.enabled;
            float rateTime = emissionOn ? MaxOf(em.rateOverTime) : 0f;
            float rateDist = emissionOn ? MaxOf(em.rateOverDistance) : 0f;
            int bursts = emissionOn ? em.burstCount : 0;

            bool byRate = rateTime > 0f || rateDist > 0f;
            derivedIsLoop = rootLoop && byRate;

            // Report-only: does any OTHER system in the prefab derive differently?
            bool childDisagrees = false;
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null || ps == root) continue;
                var cem = ps.emission;
                bool cByRate = cem.enabled && (MaxOf(cem.rateOverTime) > 0f || MaxOf(cem.rateOverDistance) > 0f);
                if ((ps.main.loop && cByRate) != derivedIsLoop) { childDisagrees = true; break; }
            }

            var sb = new StringBuilder();
            sb.Append("root='").Append(root.gameObject.name).Append("'");
            if (viaFallback) sb.Append(" (prefab root emits nothing -- authority fell through to this system)");
            sb.Append(", loop=").Append(rootLoop ? "True" : "False");
            sb.Append(", emission=").Append(emissionOn ? "on" : "OFF");
            sb.Append(", rateOverTime=").Append(rateTime.ToString("0.###"));
            sb.Append(", rateOverDistance=").Append(rateDist.ToString("0.###"));
            sb.Append(", bursts=").Append(bursts);
            sb.Append(", systems=").Append(systems.Length);
            if (childDisagrees) sb.Append(", NOTE: a child system derives differently (root is authority)");
            detail = sb.ToString();
            return true;
        }

        /// <summary>
        /// The ONE system whose emission decides the verdict: the prefab root's own
        /// ParticleSystem when it can actually emit, otherwise the first system in
        /// hierarchy order that can (emission module enabled, GameObject active). A
        /// disabled/inactive shell emits nothing and cannot speak for the effect. If
        /// nothing can emit, the root (or first) system is returned anyway so the caller
        /// still gets a reading rather than a silent skip.
        /// </summary>
        public static ParticleSystem PickAuthority(GameObject prefab, ParticleSystem[] systems, out bool viaFallback)
        {
            viaFallback = false;
            if (prefab == null) return null;
            if (systems == null || systems.Length == 0)
                systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0) return null;

            var own = prefab.GetComponent<ParticleSystem>();
            if (own != null && CanEmit(own)) return own;

            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null || ps == own) continue;
                if (!CanEmit(ps)) continue;
                viaFallback = true;
                return ps;
            }

            if (own != null) return own;
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) return systems[i];
            return null;
        }

        private static bool CanEmit(ParticleSystem ps)
        {
            if (ps == null) return false;
            if (!ps.gameObject.activeSelf) return false;
            return ps.emission.enabled;
        }

        /// <summary>True when the derivation qualifies ONLY through rateOverDistance
        /// (rateOverTime is zero). Lets the audit report how much the distance clause
        /// widens the rule instead of hiding it.</summary>
        public static bool QualifiesByDistanceOnly(GameObject prefab)
        {
            if (prefab == null) return false;
            bool viaFallback;
            var root = PickAuthority(prefab, null, out viaFallback);
            if (root == null) return false;
            var em = root.emission;
            if (!em.enabled) return false;
            return MaxOf(em.rateOverTime) <= 0f && MaxOf(em.rateOverDistance) > 0f;
        }

        /// <summary>Largest value a MinMaxCurve can take, across every authoring mode.
        /// Curve modes are sampled (keys alone can miss a hump) and scaled by the
        /// curve multiplier, which is where Unity actually stores the magnitude.</summary>
        public static float MaxOf(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return c.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(c.constantMin, c.constantMax);
                case ParticleSystemCurveMode.Curve:
                    return Mathf.Abs(c.curveMultiplier) * MaxOfCurve(c.curve);
                case ParticleSystemCurveMode.TwoCurves:
                    return Mathf.Abs(c.curveMultiplier) *
                           Mathf.Max(MaxOfCurve(c.curveMin), MaxOfCurve(c.curveMax));
                default:
                    return c.constantMax;
            }
        }

        private static float MaxOfCurve(AnimationCurve curve)
        {
            if (curve == null) return 0f;
            float max = 0f;
            var keys = curve.keys;
            if (keys != null)
                for (int i = 0; i < keys.Length; i++)
                    max = Mathf.Max(max, keys[i].value);
            for (int i = 0; i <= CurveSamples; i++)
                max = Mathf.Max(max, curve.Evaluate(i / (float)CurveSamples));
            return max;
        }

        // =====================================================================
        //  THE MIRROR JOIN -- the assertion neither oracle was making
        // =====================================================================
        //
        // WHAT WENT WRONG, and why the fix is a THIRD assertion rather than a
        // tightening of either existing one.
        //
        // On 2026-08-21 the five PP_*Impacts rows in HovlVfxCatalog pointed at the
        // UNREPAIRED gitignored PACK prefabs while their repaired, committed mirrors sat
        // on disk beside them (the mirror builder had run; HovlVfxCatalogGenerator.Generate
        // had not, so VfxMirrorRedirect never got to redirect the rows). Every pack copy is
        // IsLoop, and HitSurface.cs:221 plays that key fire-and-forget with the returned
        // VFXHandle DISCARDED -- which per VFXManager.Hovl.cs:399-422 burns one of the 20
        // global loop slots PERMANENTLY, for the whole session. The owner's live capture:
        // "active loops 24/24 (cap hit)" 21 times, never recovering, with ~50 orphaned
        // ParticleSystems still playing after the battle. That is her "random vfx stuck
        // around".
        //
        // ⚠ BOTH ORACLES PASSED GREEN THROUGHOUT, AND NEITHER WAS WRONG:
        //   * THIS suite compares a row's stored IsLoop against THE PREFAB THAT ROW POINTS
        //     AT. Row and prefab were both the pack copy; both said loop. Agreement. Green.
        //   * SurfaceImpactVfxRegression asserts THE MIRROR is one-shot. It was. Green.
        //   NEITHER ASSERTED THAT THE ROW POINTS AT THE MIRROR. The defect lived exactly in
        //   the gap between two correct assertions, so no amount of sharpening either one
        //   reaches it -- only an assertion over the JOIN does, which is this.
        //
        // WHY IT LIVES HERE AND NOT IN SurfaceImpactVfxRegression:
        //   1. The two suites already have a written division of labour, and this is on
        //      this side of it. That suite's own header says: "This suite asserts the
        //      PREFAB is one-shot; that suite asserts the ROW agrees." The ROW is this
        //      suite's charter. A row pointing at the wrong prefab is a fact about the row.
        //   2. This suite ALREADY walks every row of BOTH catalogs and resolves every
        //      prefab reference. The join is one extra question asked of an object already
        //      in hand, in the same pass -- so the two answers about a given row cannot
        //      drift apart or be run against different catalog states.
        //   3. The defect class is not surface-specific. Portal, talent-pointer and status
        //      mirrors are wired the identical way through VfxMirrorRedirect and can rot
        //      identically. SurfaceImpactVfxRegression is scoped to the five surfaces by
        //      name and by its layer-count contract; widening it would make its name lie.
        //
        // WHAT IT MEASURES (and it is a MEASUREMENT, not a restatement):
        //   It loads the catalog .asset FROM DISK, takes each row's serialized prefab
        //   REFERENCE, resolves that reference to an ASSET PATH, and compares that path
        //   against VfxMirrorPairSet -- the pair table the mirror BUILDERS themselves read.
        //   It never recomputes the row's GUID from the generator's own path table, which
        //   would only prove the generator agrees with itself.
        //
        //   A row whose prefab path IS a declared mirror SOURCE = FAIL, by name, with both
        //   paths printed. The one exception is the exception VfxMirrorRedirect itself
        //   makes: if the declared mirror does not actually LOAD, no redirect was possible,
        //   and failing would demand a fix that cannot be performed -- so it is reported as
        //   an unbuilt mirror instead.
        //
        // VACUITY IS REPORTED, NOT ASSUMED. On a clean clone the packs are gitignored, so a
        // source-pointing row resolves to NULL and has no path to test. The summary
        // therefore always prints how many declared mirrors are loadable here and how many
        // rows were joined, so a run that COULD NOT have failed says so out loud instead of
        // reading as a pass. (On the machine that matters -- the one with the packs
        // imported, where the catalog is generated -- the join is live.)
        //
        // POSITIVE CONTROL (prove it can go red): open HovlVfxCatalog.asset and change the
        // PP_WoodImpacts row's Prefab guid from 05e9acc051f2f52438573b60d3930524 (the mirror
        // at Assets/Resources/VFX/Impact/WoodImpacts.prefab) to 3c0b5f221ea995442b9d11dc526de1c0
        // (the pack source) -- i.e. put the catalog back in the exact state that shipped.
        // Re-run: it must fail naming PP_WoodImpacts and both paths. Change it back.

        private sealed class MirrorJoinStats
        {
            public int Joined;          // rows whose prefab resolved to a path we could test
            public int AtMirror;        // rows already pointing at a declared mirror
            public int Unresolvable;    // rows whose prefab is null (pack absent) - untestable
            public int UnbuiltMirror;   // row is at a source whose declared mirror will not load
        }

        /// <summary>
        /// One row, one question: does this row point at a declared mirror SOURCE when a
        /// built mirror exists for it? Appends a failure naming the key and both paths if so.
        /// Called BEFORE the loop-flag skip on purpose -- a row pointing at a source that
        /// carries no ParticleSystem is skipped by the flag check but is still a wrong row.
        /// </summary>
        private static void CheckRowPointsAtMirror(string catalog, string key, GameObject prefab,
                                                   List<string> failures, MirrorJoinStats stats)
        {
            if (prefab == null) { stats.Unresolvable++; return; }

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path)) { stats.Unresolvable++; return; }

            stats.Joined++;

            string mirror;
            if (!VfxMirrorPairSet.TryMirrorForSource(path, out mirror))
            {
                // Not a declared source. Count the ones that are already AT a mirror so the
                // summary can show the join is doing real work rather than matching nothing.
                foreach (var (_, dst) in VfxMirrorPairSet.AllPairs())
                {
                    if (!string.IsNullOrEmpty(dst) &&
                        string.Equals(dst, path, System.StringComparison.OrdinalIgnoreCase))
                    {
                        stats.AtMirror++;
                        break;
                    }
                }
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(mirror) == null)
            {
                // Same stance VfxMirrorRedirect takes at its third test: a mirror that is
                // declared but not on disk cannot be redirected onto, and pointing the row
                // at a missing asset would trade a fresh-clone break for a break everywhere.
                stats.UnbuiltMirror++;
                return;
            }

            failures.Add(catalog + " '" + key + "': the row points at the UNREPAIRED PACK SOURCE '" +
                         path + "' while its committed mirror '" + mirror + "' IS on disk. " +
                         "Nothing redirected it, so the shipped row is the raw pack prefab -- " +
                         "gitignored (it resolves to NOTHING on a fresh clone) and unrepaired " +
                         "(demo geometry, colliders, and LOOPING particle systems the mirror strips). " +
                         "A loop-flagged row played fire-and-forget -- HitSurface.cs:221 discards the " +
                         "VFXHandle -- permanently burns one of the 20 global loop slots " +
                         "(VFXManager.Hovl.cs:399-422). FIX: re-run Defenders/VFX/Generate Hovl VFX " +
                         "Catalog, which applies VfxMirrorRedirect; do NOT hand-edit the .asset, the " +
                         "next regenerate would undo it.");
        }

        // =====================================================================
        //  THE ORACLE
        // =====================================================================

        /// <summary>
        /// Asserts every resolvable catalog row's stored IsLoop equals the value derived
        /// from its prefab's emission. Missing prefabs / prefabs with no ParticleSystem
        /// are skipped and counted (gitignored art packs must not turn this red).
        /// </summary>
        /// <summary>
        /// The largest UPWARD (+Y) velocity-over-lifetime any ParticleSystem under
        /// <paramref name="prefab"/> can impart, in units per second; 0 when no system enables the
        /// module or every authored y is zero or negative. <paramref name="detail"/> names the worst
        /// sub-emitter and its simulation space, so a red case says WHICH emitter climbs rather than
        /// only that one does. Measured from the prefab, never from a table (CLAUDE.md s11B).
        /// </summary>
        public static float MaxUpwardVelocityOverLifetime(GameObject prefab, out string detail)
        {
            detail = "no ParticleSystem enables velocity-over-lifetime";
            if (prefab == null) return 0f;

            float worst = 0f;
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null) continue;
                var vol = ps.velocityOverLifetime;
                if (!vol.enabled) continue;

                float y = MaxOf(vol.y);
                if (y <= worst) continue;
                worst = y;
                detail = "sub-emitter '" + ps.name + "' velocity-over-lifetime y peaks at " +
                         y.ToString("0.###") + " u/s in " + vol.space + " space, over a lifetime of up to " +
                         MaxOf(ps.main.startLifetime).ToString("0.##") + " s";
            }
            return worst;
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            int checkedRows = 0, skipped = 0, catalogsFound = 0;
            var notes = new List<string>();
            var join = new MirrorJoinStats();

            // --- HovlVfxCatalog: string-keyed rows (the 135-row catalog) -------------
            var hovl = AssetDatabase.LoadAssetAtPath<HovlVfxCatalog>(HovlCatalogPath);
            if (hovl == null)
            {
                failures.Add("HovlVfxCatalog.asset did not load from " + HovlCatalogPath +
                             " -- VFXManager.PlayKey resolves nothing and every keyed effect no-ops.");
            }
            else
            {
                catalogsFound++;
                var rows = hovl.Rows ?? new HovlVfxCatalog.Row[0];
                int hSkipped = 0;
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    string key = string.IsNullOrEmpty(row.Key) ? ("<row " + i + ">") : row.Key;

                    // THE MIRROR JOIN, before the flag skip: a row pointing at an unrepaired
                    // pack source is wrong even when its prefab carries no ParticleSystem and
                    // the flag check below would skip it silently.
                    CheckRowPointsAtMirror("HovlVfxCatalog", key, row.Prefab, failures, join);

                    bool derived;
                    string detail;
                    if (!TryResolveExpected(key, row.Prefab, out derived, out detail))
                    {
                        skipped++; hSkipped++;
                        continue;
                    }
                    checkedRows++;
                    if (row.IsLoop != derived)
                        failures.Add("HovlVfxCatalog '" + key + "': stored IsLoop=" + row.IsLoop +
                                     " but the prefab derives " + derived + " (" + detail +
                                     "). A burst prefab flagged as a loop never returns its pool slot " +
                                     "and permanently burns one of the 20 loop slots.");
                }
                notes.Add("hovl rows=" + rows.Length + " skipped=" + hSkipped);
            }

            // --- VFXCatalog: VFXType-keyed rows --------------------------------------
            // Types are reported via ToString() only -- naming an enum MEMBER here would
            // fail-compile the whole editor assembly the day that member is renamed.
            var typed = AssetDatabase.LoadAssetAtPath<VFXCatalog>(TypedCatalogPath);
            if (typed == null)
            {
                failures.Add("VFXCatalog.asset did not load from " + TypedCatalogPath +
                             " -- every VFXType falls through to the procedural fallback.");
            }
            else
            {
                catalogsFound++;
                var entries = typed.Entries ?? new VFXCatalog.Entry[0];
                int tSkipped = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    string name = e.Type.ToString();

                    CheckRowPointsAtMirror("VFXCatalog", name, e.Prefab, failures, join);

                    bool derived;
                    string detail;
                    if (!TryResolveExpected(name, e.Prefab, out derived, out detail))
                    {
                        skipped++; tSkipped++;
                        continue;
                    }
                    checkedRows++;
                    if (e.IsLoop != derived)
                        failures.Add("VFXCatalog '" + name + "': stored IsLoop=" + e.IsLoop +
                                     " but the prefab derives " + derived + " (" + detail +
                                     "). Fix it with Defenders/VFX/Audit Loop Flags, and correct the " +
                                     "isLoop argument in VFXCatalogGenerator.Map or the next Generate undoes it.");
                }
                notes.Add("typed rows=" + entries.Length + " skipped=" + tSkipped);
            }

            if (catalogsFound == 0)
            {
                reason = "vfx-loop-flag: NEITHER VFX catalog asset loaded -- nothing could be verified.";
                return false;
            }

            // --- WO-1327 REOPEN: every Cast_* type must be bounded by the cast beat cap ----
            // THE LEAK THIS PINS, from the owner's own device capture
            // (Logs/device/endstate-window-20260904.log, one mage.fireball cast at 09:35:43.89):
            //   PlayOneshot('Cast_FireCharge')  ... lifetime=1.25s     <- on the whitelist
            //   PlayOneshot('Cast_MuzzleFlash') ... lifetime=20.30s    <- NOT on the whitelist
            // Both spawned unparented at the SAME caster position on the SAME cast, at a 0.60s
            // cooldown. VFXManager.IsCastBeat was a hand-written 8-member list; Cast_MuzzleFlash
            // is the NINTH Cast_* member (VFXType.cs:233, committed 0011b8ba4 2026-08-05 -- it
            // PREDATES the list, which landed ba5b7fad0 2026-09-02) and it sits in the "Combat
            // release" region rather than the Cast_ block, so the list was born omitting an
            // existing member. The one beat that anchors to the caster was the one beat nothing
            // bounded. The fix derives membership from the enum names; this case is what goes red
            // the day somebody writes the list back by hand.
            // POSITIVE CONTROL: make VFXManager.BuildCastBeatSet skip one Cast_* member and
            // re-run -- this must fail naming that member.
            {
                int castTypes = 0;
                foreach (VFXType t in System.Enum.GetValues(typeof(VFXType)))
                {
                    if (!t.ToString().StartsWith("Cast_", System.StringComparison.Ordinal)) continue;
                    castTypes++;
                    if (!VFXManager.IsCastBeatType(t))
                        failures.Add("VFXType '" + t + "' is named Cast_* (a caster-anchored wind-up " +
                                     "beat) but VFXManager.IsCastBeatType says it is NOT a cast beat, so " +
                                     "CAST_BEAT_MAX_SECONDS never bounds it. That is exactly how " +
                                     "Cast_MuzzleFlash reached lifetime=20.30s at the caster while its " +
                                     "sibling on the same cast read 1.25s (owner capture 2026-09-04). " +
                                     "FIX: keep the set DERIVED from the enum names -- never re-hardcode it.");
                }
                if (castTypes == 0)
                    failures.Add("VfxLoopFlagRegression cast-beat case is VACUOUS: no VFXType is named " +
                                 "Cast_*, so this check cannot fail. Either the naming convention " +
                                 "(VFXType.cs:12) changed or the enum did -- re-point this case.");
                notes.Add("cast-beat coverage=" + castTypes + " Cast_* type(s)");
            }

            // --- WO-1327 REOPEN: VFXCatalog rows must still line up with the ENUM ----------
            // ⛔ THE ROOT THE DEVICE CAPTURE LED TO, and it is a data defect, not a code one.
            // VFXCatalog.asset stores rows keyed by the VFXType ORDINAL. Insert a member into
            // the middle of the enum without regenerating, and every row from that point on
            // silently re-points at ANOTHER TYPE'S PREFAB. Measured on this tree 2026-09-06:
            // ordinals 76-95 are shifted by +2, and rows exist at ordinals 94/95 which the
            // enum no longer defines at all.
            //   VFXType.Cast_MuzzleFlash == 81, and row 81 holds Env_SteamVent.prefab.
            //   Env_SteamVent measures lengthInSec 10.0 + startLifetime 10.0 = 20.0s;
            //   VFXManager.DetectDuration + 0.3 = 20.30s -- EXACTLY the number in the owner's
            //   device log (Logs/device/endstate-window-20260904.log):
            //     PlayOneshot('Cast_MuzzleFlash') at (5000.19, 1.18, 4994.75)
            //                                     parent='<none, world-space>' lifetime=20.30s.
            // So every ranged release (RangedAttackVFX.PlayReleaseFlash) and every tower shot
            // (TowerCombat.cs:376) planted a TWENTY-SECOND STEAM COLUMN, unparented, at the
            // caster -- at a 0.60s cooldown. Nothing in the code could have caught it: the
            // reference resolves, the prefab loads, particles play. Only the ordinal is wrong.
            // FIX when this goes red: Defenders/VFX/Generate VFX Catalog (regenerates the .asset).
            // POSITIVE CONTROL: edit any correct row's guid in VFXCatalog.asset -- must fail it.
            if (typed != null)
            {
                // Name -> committed prefab under Assets/Resources/VFX. VERIFIED 2026-09-06 against
                // VFXCatalogGenerator.Map: of the 32 VFXType names that have a same-named prefab
                // there, the generator picks that exact file for ALL 32 -- zero false positives.
                // Cross-named picks (Impact_Flame -> BigExplosion.prefab, Cast_FireCharge ->
                // Casting_Fire.prefab, ...) have no same-named file and are correctly untouched.
                var mirrorByName = new Dictionary<string, string>();
                var dupes = new HashSet<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/VFX" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string nm = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (mirrorByName.ContainsKey(nm)) { dupes.Add(nm); continue; }
                    mirrorByName[nm] = path;
                }
                foreach (var d in dupes) mirrorByName.Remove(d);   // ambiguous: never judge it

                int aligned = 0, unjudged = 0;
                var seenOrdinals = new HashSet<int>();
                foreach (var e in (typed.Entries ?? new VFXCatalog.Entry[0]))
                {
                    int ordinal = (int)e.Type;
                    if (!System.Enum.IsDefined(typeof(VFXType), e.Type))
                    {
                        failures.Add("VFXCatalog carries a row at ordinal " + ordinal + " which the " +
                                     "VFXType enum no longer defines -- the asset was generated against " +
                                     "an OLDER enum and every row at or past the insertion point now " +
                                     "resolves to the wrong prefab. Re-run Defenders/VFX/Generate VFX Catalog.");
                        continue;
                    }
                    if (!seenOrdinals.Add(ordinal))
                        failures.Add("VFXCatalog has TWO rows at ordinal " + ordinal + " ('" + e.Type +
                                     "') -- VFXCatalog.BuildLookup keeps one and the other is dead.");

                    string name = e.Type.ToString();
                    if (!mirrorByName.TryGetValue(name, out string expected)) { unjudged++; continue; }
                    string actual = e.Prefab != null ? AssetDatabase.GetAssetPath(e.Prefab) : null;
                    if (string.Equals(actual, expected, System.StringComparison.OrdinalIgnoreCase)) { aligned++; continue; }
                    failures.Add("VFXCatalog row '" + name + "' (ordinal " + ordinal + ") points at '" +
                                 (string.IsNullOrEmpty(actual) ? "<null>" : actual) + "' but a committed " +
                                 "prefab of its own name exists at '" + expected + "'. This is the " +
                                 "ordinal-shift class: Cast_MuzzleFlash resolved to Env_SteamVent and " +
                                 "played a 20.30s steam column at the caster on every shot (owner device " +
                                 "capture 2026-09-04). Re-run Defenders/VFX/Generate VFX Catalog.");
                }
                if (aligned + unjudged == 0)
                    failures.Add("VFXCatalog enum-alignment case is VACUOUS: no row could be judged. " +
                                 "Either VFXCatalog.asset is empty or Assets/Resources/VFX holds no " +
                                 "committed prefabs -- re-point this case rather than trusting it.");
                notes.Add("enum-alignment aligned=" + aligned + " unjudged=" + unjudged +
                          " (no same-named committed prefab)");
            }

            // =================================================================
            //  WO-1473 — the loop RELEASE POLICY decision table
            // =================================================================
            //
            // WHY IT IS HERE. This suite's subject is "a loop slot that is taken and never given
            // back": every case above asks whether a row's IsLoop flag is honest, because a
            // mis-flagged burst holds a slot forever. WO-1473 is the same defect from the other
            // end - 14 of 24 slots held by correctly-flagged, correctly-behaved ambient tower
            // auras (device log 2026-09-06, "STUCK LOOP ArcaneTower_Aura owner='Arcane Spire'
            // ... 14/24", 20 occurrences), after which the raid pinned at 24/24 and dropped
            // Damage_Ruin 31 times. Same suite, same question, so the policy is pinned here
            // rather than in a second file nobody runs.
            //
            // ⚠ CORRECTION 2026-09-17 (WO-1786) — READ THIS BEFORE CITING A "STUCK LOOP" LINE.
            // The saturation evidence above stands on its own (14/24 held, 24/24 in the raid,
            // Damage_Ruin dropped 31x). What no longer stands is the inference that a STUCK LOOP
            // line BY ITSELF proves the release policy failed. Until WO-1786 the audit derived
            // "stuck" from its own copy of the 6 s grace on a 5 s cadence while the policy acted
            // on a 0.5 s one, so an audit landing in the gap accused a policy that was about to
            // act -- and the Warned latch made it permanent. In
            // Logs/device/pull-20260916-143101-bastion-owner-run all fourteen STUCK LOOP lines
            // read exactly "OFF CAMERA for 6s" and every one is followed inside 0.25 s by its own
            // "LOOP RELEASED ... reason=off camera 6s". The oracle now reports the POLICY'S OWN
            // consecutive tick count instead; see the WO-1786 case block below.
            //
            // ⚠ WHAT THIS CASE CANNOT DO, stated plainly rather than implied: the WO's own
            // acceptance test - spawn N aura owners, destroy them, assert the pool drains to
            // zero - is a PLAYMODE assertion against a live VFXManager, and this is a static
            // EditMode suite with no scene. It cannot make that assertion and does not pretend
            // to. What it CAN pin is the decision itself, which is why VfxLoopReleasePolicy.Decide
            // was written as pure arithmetic with no Unity object in its signature.
            //
            // RED PROOF: with Decide hardwired to return Keep, cases 1, 2, 4 and 6 below fail
            // (4 failures). With it hardwired to Suspend, cases 3, 5, 7 and 8 fail. Neither
            // degenerate implementation is green, so this case cannot pass vacuously.
            {
                const float Grace = 6f;
                int policyChecked = 0;
                void Policy(string what, VfxLoopReleasePolicy.LoopAction expect,
                            VfxLoopReleasePolicy.LoopAction actual)
                {
                    policyChecked++;
                    if (actual != expect)
                        failures.Add("WO-1473 release policy: " + what + " -- expected " + expect +
                                     " but Decide returned " + actual + ". The loop pool's release " +
                                     "policy is the only thing standing between a town's ambient " +
                                     "auras and a pool pinned at its cap (14/24 held by one effect, " +
                                     "device log 2026-09-06).");
                }

                // 1. Owner destroyed -> release NOW, no grace. The WO's first clause.
                Policy("owner destroyed, on camera",
                    VfxLoopReleasePolicy.LoopAction.Suspend,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: true, ownerActive: false, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: Grace, slotAvailable: true));

                // 2. Owner merely disabled -> release NOW as well (it is off screen by definition).
                Policy("owner disabled, on camera",
                    VfxLoopReleasePolicy.LoopAction.Suspend,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: false, ownerActive: false, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: Grace, slotAvailable: true));

                // 3. Off camera but INSIDE the grace -> keep. The grace is the anti-flicker
                //    hysteresis; releasing on the first tick off-frustum would strobe every
                //    aura at the screen edge.
                Policy("off camera for less than the grace",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: Grace - 0.5f, grace: Grace, slotAvailable: true));

                // 4. Off camera PAST the grace -> release. This is the Arcane Spire case: the
                //    aura is correct, its owner is alive, and the player cannot see it.
                Policy("off camera past the grace (the ArcaneTower_Aura case)",
                    VfxLoopReleasePolicy.LoopAction.Suspend,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: Grace + 0.1f, grace: Grace, slotAvailable: true));

                // 5. Already suspended and still off camera -> stay released (no churn).
                Policy("suspended and still off camera",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: true, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: Grace * 10f, grace: Grace, slotAvailable: true));

                // 6. Back on camera with headroom -> resume. A release policy with no resume is
                //    a deletion policy, and the owner would see her tower auras vanish for good.
                Policy("suspended, back on camera, slot free",
                    VfxLoopReleasePolicy.LoopAction.Resume,
                    VfxLoopReleasePolicy.Decide(suspended: true, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: Grace, slotAvailable: true));

                // 7. Back on camera with the pool FULL -> wait. Resume is budgeted, or a town
                //    of returning spires re-starves combat feedback the instant the player turns.
                Policy("suspended, back on camera, pool full",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: true, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: Grace, slotAvailable: false));

                // 8. NO CAMERA (headless AutoPilot, boot, a scene between cameras) -> never
                //    suspend for visibility. An unjudgeable loop is kept, never guessed at;
                //    otherwise every headless capture would measure a different pool.
                Policy("no camera to judge by",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: false,
                        visible: false, offscreenFor: Grace * 10f, grace: Grace, slotAvailable: true));

                // 9. The accessibility allowlist (WO-1229) outranks all of it: the colourblind
                //    low-HP tell is a LOOP, so a policy that could suspend it would take away the
                //    hero's only non-colour danger signal.
                Policy("accessibility loop, off camera past the grace",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: true,
                        ownerDestroyed: true, ownerActive: false, cameraKnown: true,
                        visible: false, offscreenFor: Grace * 10f, grace: Grace, slotAvailable: false));

                // 10. WO-1868 visibilityExempt: off-camera past grace must KEEP (raid fog/storm).
                //     Distinct from accessibility — owner destroy still suspends (case 11).
                Policy("visibilityExempt off camera past the grace (raid atmosphere)",
                    VfxLoopReleasePolicy.LoopAction.Keep,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: Grace + 0.1f, grace: Grace, slotAvailable: true,
                        visibilityExempt: true));

                // 11. WO-1868 visibilityExempt does NOT protect a destroyed owner.
                Policy("visibilityExempt + owner destroyed still suspends",
                    VfxLoopReleasePolicy.LoopAction.Suspend,
                    VfxLoopReleasePolicy.Decide(suspended: false, exempt: false,
                        ownerDestroyed: true, ownerActive: false, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: Grace, slotAvailable: true,
                        visibilityExempt: true));

                notes.Add("wo1473 release-policy cases=" + policyChecked);
            }

            // =================================================================
            //  WO-1786 — THE ORACLE MUST NOT RACE THE POLICY IT WATCHES
            // =================================================================
            //
            // WHY. AuditRegistryAges used to re-derive "stuck" from its own copy of the 6 s grace
            // on a 5 s cadence, while the policy acts on a 0.5 s one. Two clocks at one threshold
            // is a race, and the slower one accused the faster one of never running. Proof, from
            // Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt: all fourteen
            // STUCK LOOP lines read exactly "OFF CAMERA for 6s" - never 7, never 12 - and each is
            // followed within 52-218 ms by its own "LOOP RELEASED ... reason=off camera 6s"
            // (13:17:56.723 -> .775, 13:20:02.207 -> .285, 13:24:06.394 -> .612,
            // 13:25:56.756 -> .892). That capture carries 189 RELEASED and 146 RESUMED lines. The
            // policy fired every single time; the detector looked inside the gap before it acted,
            // and its Warned latch made each false accusation permanent. WO-1786 was raised off
            // those lines, so the false positive cost a ticket as well as a morning.
            //
            // RED PROOF: revert IsStuck to `>= 1` and case 2 below fails (the single acting tick
            // reads as a defect again, which is precisely the 6 s race). Hardwire it to false and
            // cases 3 and 4 fail, so a detector that reports nothing cannot pass either. Delete
            // the accessibility guard from ShouldHaveReleased and case 7 fails.
            {
                int oracleChecked = 0;
                void Oracle(string what, bool expect, bool actual)
                {
                    oracleChecked++;
                    if (actual != expect)
                        failures.Add("WO-1786 loop oracle: " + what + " -- expected " + expect +
                                     " but got " + actual + ". The stuck-loop detector must judge " +
                                     "the POLICY'S OWN consecutive should-have-released tick count, " +
                                     "never a second clock of its own: the 2026-09-16 capture shows " +
                                     "14 false STUCK LOOP accusations, every one contradicted by a " +
                                     "LOOP RELEASED line under 0.25 s later.");
                }

                const float G = 6f;

                // ── IsStuck: the cadence race, pinned ──
                // 1. Nothing measured yet is never stuck.
                Oracle("zero should-have-released ticks", false, VfxLoopReleasePolicy.IsStuck(0));

                // 2. THE RACE ITSELF. One tick is the tick that ACTS. Reporting here is exactly
                //    the 6 s false positive; the count must survive a further tick first.
                Oracle("one tick (the acting tick) is not yet a defect",
                    false, VfxLoopReleasePolicy.IsStuck(1));

                // 3. A whole further tick elapsed with the release not in effect -> real defect.
                Oracle("two consecutive ticks still held IS a defect",
                    true, VfxLoopReleasePolicy.IsStuck(VfxLoopReleasePolicy.StuckAfterPolicyTicks));

                // 4. And it stays reported as the count climbs (no upper window).
                Oracle("many consecutive ticks still held IS a defect",
                    true, VfxLoopReleasePolicy.IsStuck(40));

                if (VfxLoopReleasePolicy.StuckAfterPolicyTicks < 2)
                    failures.Add("WO-1786: StuckAfterPolicyTicks is " +
                                 VfxLoopReleasePolicy.StuckAfterPolicyTicks + ", below 2. At 1 the " +
                                 "oracle fires on the very tick the policy is acting on, which is " +
                                 "the 2026-09-16 false-positive race this case exists to keep shut.");

                // ── ShouldHaveReleased: the measurement, complement of Decide's release branches ──
                // 5. Off camera INSIDE the grace: the anti-flicker hysteresis, not a defect.
                Oracle("off camera inside the grace is not releasable",
                    false, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: G - 0.5f, grace: G));

                // 6. Off camera PAST the grace: releasable.
                Oracle("off camera past the grace is releasable",
                    true, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: false, offscreenFor: G + 0.1f, grace: G));

                // 7. An accessibility loop is CORRECTLY held off camera (WO-1229). The old audit
                //    had no such guard and would have reported the ruling working as a bug.
                Oracle("accessibility loop off camera is never releasable",
                    false, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: true,
                        ownerDestroyed: true, ownerActive: false, cameraKnown: true,
                        visible: false, offscreenFor: G * 10f, grace: G));

                // 8. Destroyed owner: releasable immediately, no grace.
                Oracle("destroyed owner is releasable with no grace",
                    true, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: false,
                        ownerDestroyed: true, ownerActive: false, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: G));

                // 9. NO CAMERA: unjudgeable is never a defect, or every headless AutoPilot run
                //    would report its whole registry stuck.
                Oracle("no camera to judge by is never releasable",
                    false, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: false,
                        visible: false, offscreenFor: G * 10f, grace: G));

                // 10. On camera: never releasable.
                Oracle("on camera is never releasable",
                    false, VfxLoopReleasePolicy.ShouldHaveReleased(exempt: false,
                        ownerDestroyed: false, ownerActive: true, cameraKnown: true,
                        visible: true, offscreenFor: 0f, grace: G));

                // ── The two halves must agree. A measurement that says "release" while Decide
                //    says Keep (or the reverse) would put the oracle and the action back out of
                //    step, which is the whole class of bug WO-1786 closes. Swept, not sampled.
                int agreementChecked = 0;
                bool agreementBroken = false;   // report the FIRST disagreement only; a broken
                                                // invariant would otherwise add 128 identical rows
                bool[] bools = { false, true };
                float[] streaks = { 0f, G - 0.5f, G, G + 5f };
                foreach (bool exempt in bools)
                foreach (bool ownerDestroyed in bools)
                foreach (bool ownerActive in bools)
                foreach (bool cameraKnown in bools)
                foreach (bool visible in bools)
                foreach (float streak in streaks)
                {
                    bool should = VfxLoopReleasePolicy.ShouldHaveReleased(
                        exempt, ownerDestroyed, ownerActive, cameraKnown, visible, streak, G);
                    var decided = VfxLoopReleasePolicy.Decide(
                        suspended: false, exempt: exempt,
                        ownerDestroyed: ownerDestroyed, ownerActive: ownerActive,
                        cameraKnown: cameraKnown, visible: visible,
                        offscreenFor: streak, grace: G, slotAvailable: true);
                    agreementChecked++;
                    bool wouldSuspend = decided == VfxLoopReleasePolicy.LoopAction.Suspend;
                    if (should != wouldSuspend && !agreementBroken)
                    {
                        agreementBroken = true;
                        failures.Add("WO-1786: ShouldHaveReleased and Decide DISAGREE for " +
                            "exempt=" + exempt + " ownerDestroyed=" + ownerDestroyed +
                            " ownerActive=" + ownerActive + " cameraKnown=" + cameraKnown +
                            " visible=" + visible + " offscreenFor=" + streak +
                            " -- measurement says " + should + ", Decide says " + decided +
                            ". The oracle and the action must read the SAME release condition, or " +
                            "the detector is back to keeping a second opinion and the 2026-09-16 " +
                            "false-positive class is reopened.");
                    }
                }
                if (agreementChecked == 0)
                    failures.Add("WO-1786 agreement sweep is VACUOUS: no combination was judged.");

                notes.Add("wo1786 oracle cases=" + oracleChecked +
                          " agreement-sweep=" + agreementChecked);
            }

            // =================================================================
            //  WO-1476 — NO TOWN AMBIENT SEAT MAY DRIVE PARTICLES UP +Y
            // =================================================================
            //
            // Owner, 2026-09-07: "there is a VFX exiting about town along Y and it needs removed
            // or turned off". The town's ambient seats are permanent loops parented to the Heart
            // of Elarion world tree, so an upward velocity term does not disperse -- it climbs
            // until the particle's lifetime runs out, every second the player is in town.
            //
            // WHY IT IS PINNED HERE RATHER THAN BY DELETING A PICK: the rows in
            // VfxManualPicks.json are the OWNER's tags (memory vfx-map-owner-tags-no-creative-pick)
            // and this very pair is pinned byte-for-byte by NightStoreAuraSelectionRegression. The
            // ONLY correct lever is whether the seat SPAWNS, which is AmbientAuraPolicy. So this
            // case reads her row, measures the prefab, and fails only when a RISING prefab is also
            // ALLOWED to spawn -- a re-point to another riser and a flip of the withhold flag are
            // both caught, and re-tagging a non-riser stays green with no code change.
            //
            // RED PROOF (it can fail): flip AmbientAuraPolicy.WithholdTreeFootAura to false with
            // the Spells Pack imported and this case names 'atfootprintoftree_Aura' with
            // maxUpY 0.5. It is measured, never asserted from a table.
            {
                int townChecked = 0, townSkipped = 0;
                string picksJson = null;
                string picksAbs = System.IO.Path.Combine(Application.dataPath, "Editor/VfxManualPicks.json");
                try
                {
                    if (System.IO.File.Exists(picksAbs)) picksJson = System.IO.File.ReadAllText(picksAbs);
                    else failures.Add("WO-1476: VfxManualPicks.json is absent at " + picksAbs +
                                      " -- the owner's VFX tag file is the input this case measures.");
                }
                catch (System.Exception e)
                {
                    failures.Add("WO-1476: could not read VfxManualPicks.json: " + e.Message);
                }

                // The town's ambient aura seats, and whether AmbientAuraPolicy lets each SPAWN
                // today. Both keys come from the constants the game itself plays, never re-typed,
                // so a rename cannot leave this case quietly measuring nothing.
                var townSeats = new List<KeyValuePair<string, bool>>
                {
                    new KeyValuePair<string, bool>(
                        AmbientAuraPolicy.WithheldAmbientAuraKey,
                        !AmbientAuraPolicy.ShouldWithholdAtHeartTree(AmbientAuraPolicy.WithheldAmbientAuraKey)),
                    new KeyValuePair<string, bool>(
                        HeldVfxKeys.TreeOfLifeFootAura,
                        !AmbientAuraPolicy.ShouldWithholdTreeFootAura(HeldVfxKeys.TreeOfLifeFootAura)),
                };

                for (int i = 0; i < townSeats.Count && picksJson != null; i++)
                {
                    string seatKey = townSeats[i].Key;
                    bool allowed = townSeats[i].Value;

                    var pick = System.Text.RegularExpressions.Regex.Match(
                        picksJson,
                        "\"key\"\\s*:\\s*\"" +
                        System.Text.RegularExpressions.Regex.Escape(seatKey) +
                        "\"\\s*,\\s*\"prefabPath\"\\s*:\\s*\"([^\"]*)\"");
                    if (!pick.Success)
                    {
                        failures.Add("WO-1476: VfxManualPicks.json has NO row for the town ambient key '" +
                                     seatKey + "'. The seat is wired in code and the owner's tag is gone, " +
                                     "so nothing here can judge what it would spawn. Restore her row; do " +
                                     "not re-type the key at the call site.");
                        continue;
                    }

                    string prefabPath = pick.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/");
                    var seatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (seatPrefab == null)
                    {
                        townSkipped++;
                        notes.Add("wo1476 " + seatKey + " SKIPPED (prefab '" + prefabPath +
                                  "' not on disk -- gitignored art pack, not a failure)");
                        continue;
                    }

                    townChecked++;
                    float upY = MaxUpwardVelocityOverLifetime(seatPrefab, out string upDetail);
                    notes.Add("wo1476 " + seatKey + " allowed=" + allowed + " maxUpY=" +
                              upY.ToString("0.###") + " u/s (" + upDetail + ")");

                    if (allowed && upY > 0f)
                        failures.Add("WO-1476: the town ambient seat '" + seatKey + "' is ALLOWED to " +
                                     "spawn and its prefab '" + prefabPath + "' drives particles UP +Y at " +
                                     upY.ToString("0.###") + " u/s -- " + upDetail + ". That is the column " +
                                     "the owner reported rising over the town on 2026-09-07. Withhold the " +
                                     "seat in AmbientAuraPolicy, or ASK HER for a non-rising tag; do NOT " +
                                     "re-point VfxManualPicks.json from this seat.");
                }

                if (townChecked == 0)
                    notes.Add("wo1476 ⚠ VACUOUS ON THIS MACHINE: " + townSkipped + " town ambient seat(s) " +
                              "skipped because their owner-tagged prefabs are gitignored art-pack assets " +
                              "that are absent here. The case is live on a machine with the packs imported, " +
                              "which is the machine the catalog is generated on");
            }

            // Mirror-join summary. Printed on PASS as well as FAIL, and deliberately spelling
            // out how much of the join was live: this suite shipped green for a defect that a
            // vacuous check would also have been green for, so a run that could not have
            // failed has to say so rather than look identical to one that could.
            int pairsTotal, pairsWithSource, mirrorsOnDisk = 0;
            VfxMirrorPairSet.Count(out pairsTotal, out pairsWithSource);
            foreach (var (src, dst) in VfxMirrorPairSet.AllPairs())
            {
                if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) continue;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) mirrorsOnDisk++;
            }

            var joinNote = new StringBuilder();
            joinNote.Append("mirror-join: ").Append(pairsWithSource).Append(" redirectable pair(s) of ")
                    .Append(pairsTotal).Append(" declared, ").Append(mirrorsOnDisk)
                    .Append(" mirror(s) loadable here; ").Append(join.Joined)
                    .Append(" row(s) joined, ").Append(join.AtMirror)
                    .Append(" already at a mirror, ").Append(join.Unresolvable)
                    .Append(" unresolvable (prefab null -- gitignored pack absent), ")
                    .Append(join.UnbuiltMirror).Append(" at a source whose mirror is not built");
            if (mirrorsOnDisk == 0 || join.Joined == 0)
                joinNote.Append(" -- ⚠ VACUOUS ON THIS MACHINE: with no loadable mirrors or no " +
                                "resolvable rows the join had nothing to compare and CANNOT have " +
                                "failed. It is live on a machine with the art packs imported, " +
                                "which is the machine the catalog is generated on");
            notes.Add(joinNote.ToString());

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("vfx-loop-flag FAILED (").Append(failures.Count)
                  .Append(" row(s) disagree with their prefab, or point at an unrepaired mirror source; ")
                  .Append(checkedRows).Append(" checked, ").Append(skipped).Append(" skipped): ");
                sb.Append(string.Join(" | ", failures.ToArray()));
                sb.Append(" [").Append(string.Join("; ", notes.ToArray())).Append(']');
                reason = sb.ToString();
                return false;
            }

            reason = "vfx-loop-flag OK: " + checkedRows + " catalog row(s) match their prefab emission (" +
                     string.Join(", ", notes.ToArray()) + "); " + skipped +
                     " skipped (prefab absent or no ParticleSystem -- gitignored art packs are not a failure).";
            return failures.Count == 0;
        }
    }
}

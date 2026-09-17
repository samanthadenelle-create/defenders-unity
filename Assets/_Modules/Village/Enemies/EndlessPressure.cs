// =============================================================================
// EndlessPressure — the post-wave-20 escalation surface (WO-1835)
// -----------------------------------------------------------------------------
// OWNER RULING (verbatim, 2026-09-17):
//   "after level 20, i want it to get difficult. THey need a real challenge so maybe 2
//    dragons spawn and troops breaking through walls, or healing caravans or mages using
//    AoE large heal spells, rage spells"
//   "catapults blasting the walls from the sides at the same time"
//
// This class is the ONE place that answers "how much extra pressure does true wave N
// carry, and of what kind". Every predicate here is a PURE STATIC FUNCTION of the true
// wave number (plus authored tunables), so the oracle suite
// Assets/Editor/Regression/EndlessEscalationRegression.cs can assert the escalation
// curve with NO scene, NO prefab and NO Unity play loop.
//
// ⛔ ENDLESS ONLY. Every count function returns ZERO at or below
// <see cref="LastAuthoredWave"/>. The authored waves 1-20 schedule and its pacing are
// explicitly out of scope for WO-1835, and a single "extra" unit leaking into wave 7
// would be a balance regression on the tutorial arc. The gate is one predicate,
// <see cref="IsEndlessWave"/>, called by every count below — not a repeated
// `> 20` test at each site, which is how a gate drifts.
//
// ⚠ WHY THERE ARE NO NEW enemies.json DEFS AND NO NEW ART ADDRESSES (CLAUDE.md §16).
// Enemy and structure ART IS SERVED FROM R2 AND A MISSING BUNDLE FAILS SILENTLY — the
// build installs, launches and plays with tinted CAPSULES and no error on screen. A new
// enemy def with a new modelKey is therefore a new content-hashed bundle that must be
// pushed before anyone can see it, and a lane that forgets leaves the owner looking at
// capsules. So all four new pressure units are BEHAVIOUR COMPONENTS bolted onto enemy
// defs that ALREADY ship art: the caravan rides hollow-acolyte, the support mage rides
// orc-shaman, the catapult rides ogre, and the wall-breacher is simply a share of the
// wave's own already-spawned bodies. FIRST-PASS ART MAPPING — the owner may commission
// distinct bodies later; when she does, that is an enemies.json + R2 push ticket, not a
// change to this file's logic.
//
// ⚠ WHY THIS DOES NOT REUSE TroopController.SharedBreachFocus. That method is the right
// SHAPE (scene-wide most-damaged wall, throttled, shared so every attacker stacks one
// breach) but it is `private static` inside the PLAYER's raid-assault brain, it is
// faction-inverted (it hunts the walls of a base the player is raiding), and
// TroopController is carrying two other in-flight tickets this session (WO-1764 / WO-1830).
// Widening its access to serve the enemy side would put a second concern inside a file
// mid-change. <see cref="NearestBreachTarget"/> below is the enemy-side equivalent, with
// its own shared 0.5 s cache — ONE scan per half-second for ALL breachers, which is
// strictly cheaper than the per-troop cadence it mirrors.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
// NOTE: WallSegment is in the DeNelle.Village namespace itself (Walls/WallSegment.cs:50), not in
// DeNelle.Village.Walls (which holds only WallTierData). This file is already in DeNelle.Village.

namespace DeNelle.Village
{
    /// <summary>
    /// Which endless-mode pressure behaviour a released body is carrying. Exactly one per
    /// body — <see cref="EndlessPressure.Attach"/> enforces that by disabling the other
    /// three, which is what makes the behaviour pool-safe (see that method's remarks).
    /// </summary>
    public enum EndlessPressureRole
    {
        /// <summary>Ordinary wave enemy — marches the authored Heart-siege path.</summary>
        None = 0,
        /// <summary>Melee that beelines the nearest wall panel instead of the Heart.</summary>
        WallBreacher = 1,
        /// <summary>Non-combatant that heals the squad around it.</summary>
        SupportCaravan = 2,
        /// <summary>Telegraphed AoE heal + rage caster.</summary>
        SupportMage = 3,
        /// <summary>Flanking siege engine that bombards walls from the side.</summary>
        SiegeCatapult = 4,
    }

    /// <summary>
    /// Owner-tunable endless escalation knobs, deserialized from the waves.json
    /// <c>endless</c> block (see <c>WaveData.EndlessDef</c>). Every field is a
    /// FIRST-PASS ESTIMATE for the owner to felt-correct, never a final balance number —
    /// WO-1835 explicitly flags the tuning as hers to judge.
    /// </summary>
    public sealed class EndlessPressureTuning
    {
        /// <summary>Share of an endless wave's melee bodies re-tasked to break walls.</summary>
        public float BreacherFraction = 0.35f;
        /// <summary>Extra HP each twin dragon keeps, as a share of the solo dragon's HP.</summary>
        public float TwinDragonHpShare = 0.7f;
        /// <summary>Seconds between synchronized catapult volleys.</summary>
        public float CatapultVolleySeconds = 6f;
        /// <summary>Structure damage one catapult stone deals to one wall panel.</summary>
        public float CatapultDamagePerHit = 9f;
        /// <summary>Seconds between support-mage casts (telegraph runs inside this).</summary>
        public float MageCastSeconds = 9f;

        /// <summary>The compiled-in defaults, used when waves.json declares no endless block.</summary>
        public static EndlessPressureTuning Default => new EndlessPressureTuning();
    }

    /// <summary>
    /// Pure escalation math + the one attach seam for the WO-1835 endless pressure units.
    /// </summary>
    public static class EndlessPressure
    {
        /// <summary>FlowTrace system tag for every line this feature emits.</summary>
        public const string Sys = "EndlessPressure";

        /// <summary>
        /// The last AUTHORED wave. Endless mode begins at 21. Kept as a named constant so
        /// the gate reads as one idea rather than a literal 20 sprinkled through five files.
        /// Matches <c>WaveManager.FirstDragonWave</c> by design: wave 20 IS the apex wave and
        /// the boundary at once.
        /// </summary>
        public const int LastAuthoredWave = 20;

        /// <summary>
        /// THE GATE. True only STRICTLY past the authored schedule. Every count function
        /// below funnels through this, so there is exactly one place that decides whether a
        /// wave is allowed to carry endless pressure at all.
        /// </summary>
        public static bool IsEndlessWave(int trueWave) => trueWave > LastAuthoredWave;

        /// <summary>
        /// How many tiers past the boundary this wave sits (0 at wave 20 and below, 1 at 21,
        /// 17 at 37...). The escalation ramps off this, never off the raw wave number, so the
        /// curves all start at zero on the boundary.
        /// </summary>
        public static int EndlessTier(int trueWave)
            => trueWave <= LastAuthoredWave ? 0 : trueWave - LastAuthoredWave;

        // ── 1. TWIN-DRAGON APEX ───────────────────────────────────────────────────────────

        /// <summary>
        /// How many apex dragons a wave fields. ZERO when this is not a dragon-cadence wave,
        /// ONE on authored wave 20, and TWO on every recurring dragon wave past it (25, 30,
        /// 35, ...). <paramref name="isRecurringDragonWave"/> is supplied by
        /// <c>WaveManager.IsRecurringDragonWave</c> — the cadence constants live there and are
        /// deliberately NOT copied here (CLAUDE.md's duplicated-state rule; a second copy of
        /// the 5-wave interval is a second thing to drift).
        /// </summary>
        public static int ApexDragonCount(int trueWave, bool isRecurringDragonWave)
        {
            if (!isRecurringDragonWave) return 0;
            return IsEndlessWave(trueWave) ? 2 : 1;
        }

        /// <summary>
        /// Per-dragon max HP when a wave fields more than one. Two dragons at full HP is a
        /// flat doubling of the apex health pool, which WO-1835 flags as likely unfair on the
        /// first felt-test; each twin keeps <see cref="EndlessPressureTuning.TwinDragonHpShare"/>
        /// of the solo figure, so the pair reads as ~1.4x the solo fight rather than 2x.
        /// FIRST-PASS, OWNER FELT-TUNES. A count of 1 is returned UNSCALED so authored wave 20
        /// is bit-identical to what shipped.
        /// </summary>
        public static float PerDragonHp(float soloHp, int dragonCount, EndlessPressureTuning tuning)
        {
            if (dragonCount <= 1) return soloHp;
            float share = tuning != null ? tuning.TwinDragonHpShare : 0.7f;
            share = Mathf.Clamp(share, 0.25f, 1f);
            return Mathf.Max(1f, soloHp * share);
        }

        /// <summary>
        /// Spawn offset for dragon <paramref name="index"/> of <paramref name="count"/> around
        /// the Heart, so twins enter from OPPOSITE sides instead of occupying the same point
        /// above the tree (they own their own kinematic flight and would otherwise overlap).
        /// Deterministic — no Random — so a capture is reproducible.
        /// </summary>
        public static Vector3 ApexSpawnOffset(int index, int count, float radius = 14f)
        {
            if (count <= 1) return Vector3.zero;
            float step = 360f / Mathf.Max(2, count);
            float deg = step * index;
            float rad = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
        }

        // ── 2. WALL-BREACHER MELEE ────────────────────────────────────────────────────────

        /// <summary>
        /// How many of an endless wave's <paramref name="squadSize"/> bodies are re-tasked to
        /// break walls. Zero inside the authored schedule (the 1-20 arc keeps its
        /// everyone-marches-the-Heart behaviour untouched). A share, never all of them: the
        /// wave must still read as a push on the town, with a visible cohort peeling onto the
        /// wall — "troops breaking through walls", not "the wave forgot the Heart".
        /// </summary>
        public static int BreacherCount(int trueWave, int squadSize, EndlessPressureTuning tuning)
        {
            if (!IsEndlessWave(trueWave) || squadSize <= 0) return 0;
            float fraction = tuning != null ? tuning.BreacherFraction : 0.35f;
            fraction = Mathf.Clamp01(fraction);
            int n = Mathf.FloorToInt(squadSize * fraction);
            // A squad big enough to spare a body always sends at least one, otherwise the
            // feature is invisible on the smaller endless rosters.
            if (n <= 0 && squadSize >= 3) n = 1;
            return Mathf.Clamp(n, 0, squadSize);
        }

        /// <summary>
        /// True when the body at <paramref name="spawnIndex"/> of this wave is a breacher.
        /// Deterministic (index-based, not Random) so the same wave releases the same shape
        /// every run and a trace is reproducible.
        /// </summary>
        public static bool IsBreacherIndex(int spawnIndex, int breacherCount)
            => breacherCount > 0 && spawnIndex >= 0 && spawnIndex < breacherCount;

        // ── 3 + 4. SUPPORT UNITS ──────────────────────────────────────────────────────────

        /// <summary>
        /// Healing caravans per endless wave: one from wave 21, a second from the eighth
        /// endless tier, hard-capped at two so killing them stays a readable tactical
        /// priority rather than a mob-clearing chore. FIRST-PASS, OWNER FELT-TUNES.
        /// </summary>
        public static int SupportCaravanCount(int trueWave)
        {
            if (!IsEndlessWave(trueWave)) return 0;
            return Mathf.Clamp(1 + EndlessTier(trueWave) / 8, 1, 2);
        }

        /// <summary>
        /// Support mages per endless wave: one from 21, climbing one per five tiers, capped at
        /// three. FIRST-PASS, OWNER FELT-TUNES.
        /// </summary>
        public static int SupportMageCount(int trueWave)
        {
            if (!IsEndlessWave(trueWave)) return 0;
            return Mathf.Clamp(1 + EndlessTier(trueWave) / 5, 1, 3);
        }

        // ── 5. FLANKING CATAPULTS ─────────────────────────────────────────────────────────

        /// <summary>
        /// Catapults per endless wave. The floor is TWO, not one: the owner's ruling is
        /// "catapults blasting the walls from the sides AT THE SAME TIME", and a single engine
        /// on one face cannot satisfy it however hard it hits. Climbs to four so the late
        /// endless waves press every face, and is capped there because each engine counts
        /// against the DEF-48 simultaneous-enemy cap and would otherwise starve the roster.
        /// FIRST-PASS, OWNER FELT-TUNES.
        /// </summary>
        public static int CatapultCount(int trueWave)
        {
            if (!IsEndlessWave(trueWave)) return 0;
            return Mathf.Clamp(2 + EndlessTier(trueWave) / 9, 2, 4);
        }

        /// <summary>
        /// The shared volley index for <paramref name="time"/>. Every catapult in the scene
        /// quantizes its own fire to this, so they release TOGETHER with no static clock, no
        /// coordinator component and no second owner of the cadence — the synchronization is a
        /// property of the arithmetic rather than of an object that could fail to exist.
        /// </summary>
        public static int VolleyIndex(float time, float intervalSeconds)
        {
            float interval = Mathf.Max(0.25f, intervalSeconds);
            return Mathf.FloorToInt(Mathf.Max(0f, time) / interval);
        }

        // ── Shared breach-target cache ─────────────────────────────────────────────────────

        private static readonly List<WallSegment> s_wallScratch = new List<WallSegment>(64);
        private static float s_wallCacheStamp = -999f;
        private const float WallCacheSeconds = 0.5f;

        /// <summary>
        /// The nearest live, attackable wall panel to <paramref name="from"/>, or null when the
        /// town has none standing. Backed by ONE scene scan per <see cref="WallCacheSeconds"/>
        /// shared by every breacher and catapult in the scene — the scan is the expensive part
        /// and there is no wall registry to read instead (checked: WallSegment has no static
        /// list; every consumer in the tree calls FindObjectsByType, which is why this caches
        /// rather than adding a fifth uncached caller).
        /// <para>
        /// Faction-filtered through <see cref="CombatFactionRules.MayAttack"/> — the ONE
        /// predicate (WO-1439). Never re-implement the friend-or-foe comparison here: an
        /// enemy-owned raid base also has WallSegments, and a breacher must not smash the wall
        /// of the camp it spawned in.
        /// </para>
        /// </summary>
        public static WallSegment NearestBreachTarget(Vector3 from, CombatFaction attacker)
        {
            RefreshWallCache();

            WallSegment best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < s_wallScratch.Count; i++)
            {
                WallSegment wall = s_wallScratch[i];
                // ⚠ THE CAST IS LOAD-BEARING, NOT COSMETIC. WallSegment implements BOTH
                // IDamageable and IDamageableStructure (deliberately — the two contracts share the
                // Faction member, see IDamageableStructure.cs), and CombatFactionRules.MayAttack is
                // overloaded on BOTH (CombatFactionRules.cs:53 and :95). Passing a WallSegment raw
                // is therefore an ambiguous call, not a working line. Naming the structure contract
                // explicitly also says which seam this is: enemy -> structure contact damage.
                IDamageableStructure asStructure = wall;
                if (!CombatFactionRules.MayAttack(attacker, asStructure)) continue;
                float sqr = (wall.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = wall; }
            }
            return best;
        }

        /// <summary>
        /// How "off-axis" a wall panel is relative to an attacker's approach heading: 0 when the
        /// panel sits dead ahead on the approach lane, 1 when it is square to the side. Pure and
        /// allocation-free so the flank selection is assertable without a scene.
        /// <para>
        /// This is what makes the catapults FLANKING rather than just another front rank — the
        /// owner's ruling is "blasting the walls from the sides", and a siege engine that queues
        /// up behind the melee push on the same face does not read as a flank however hard it hits.
        /// </para>
        /// </summary>
        public static float FlankScore(Vector3 toWall, Vector3 approachHeading)
        {
            Vector3 a = new Vector3(toWall.x, 0f, toWall.z);
            Vector3 b = new Vector3(approachHeading.x, 0f, approachHeading.z);
            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) return 0f;
            return 1f - Mathf.Abs(Vector3.Dot(a.normalized, b.normalized));
        }

        /// <summary>
        /// A live wall panel off to the SIDE of <paramref name="approachHeading"/> for siege
        /// engine number <paramref name="flankIndex"/>. Engines with different indices are handed
        /// panels on OPPOSING lateral sides (even index = one side, odd = the other), so a pair
        /// bombards two faces AT THE SAME TIME — the explicit "at the same time" half of the
        /// ruling, which one engine cannot satisfy alone. Falls back to the nearest attackable
        /// panel when the town has no wall to either side (a partly-razed town late in endless).
        /// </summary>
        public static WallSegment FlankBreachTarget(Vector3 from, Vector3 approachHeading,
                                                    CombatFaction attacker, int flankIndex)
        {
            RefreshWallCache();

            bool wantRightSide = (flankIndex & 1) == 0;
            WallSegment best = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < s_wallScratch.Count; i++)
            {
                WallSegment wall = s_wallScratch[i];
                // See NearestBreachTarget: WallSegment dual-implements, MayAttack is overloaded on
                // both contracts, so the cast is what makes this an unambiguous call.
                IDamageableStructure asStructure = wall;
                if (!CombatFactionRules.MayAttack(attacker, asStructure)) continue;

                Vector3 toWall = wall.transform.position - from;
                float lateral = Vector3.Dot(Vector3.Cross(Vector3.up, approachHeading).normalized, toWall);
                bool onWantedSide = wantRightSide ? lateral >= 0f : lateral < 0f;

                // Score: strongly prefer a genuinely lateral panel, prefer the assigned side, and
                // mildly prefer a nearer one so an engine does not trek across the whole map.
                float score = FlankScore(toWall, approachHeading) * 2f
                            + (onWantedSide ? 1f : 0f)
                            - Mathf.Clamp01(toWall.magnitude / 120f);

                if (score > bestScore) { bestScore = score; best = wall; }
            }

            return best != null ? best : NearestBreachTarget(from, attacker);
        }

        /// <summary>
        /// Re-scans the scene's wall panels at most once per <see cref="WallCacheSeconds"/>.
        /// Uses unscaled time so a paused/slowed game cannot wedge the cache stale forever.
        /// </summary>
        private static void RefreshWallCache()
        {
            float now = Time.unscaledTime;
            if (now - s_wallCacheStamp < WallCacheSeconds && s_wallScratch.Count > 0) return;
            s_wallCacheStamp = now;

            s_wallScratch.Clear();
            WallSegment[] found = Object.FindObjectsByType<WallSegment>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (found == null) return;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].IsAlive) s_wallScratch.Add(found[i]);
            }
        }

        /// <summary>
        /// Drops the shared wall cache. Called on a scene/town change so a breacher released
        /// into a new scene cannot steer at a destroyed panel from the previous one (the list
        /// holds component references; Unity fake-null makes a stale entry look alive to a
        /// bare != null test, which is why the refresh re-tests IsAlive on every rebuild).
        /// </summary>
        public static void InvalidateWallCache()
        {
            s_wallScratch.Clear();
            s_wallCacheStamp = -999f;
        }

        // ── The one attach seam ───────────────────────────────────────────────────────────

        /// <summary>
        /// Stamps <paramref name="role"/> onto a just-released <paramref name="enemy"/>.
        /// <para>
        /// ⛔ POOL SAFETY IS THE WHOLE REASON THIS IS ONE METHOD. Wave bodies come from
        /// <c>EnemyPool</c>, which REUSES the GameObject: a component added to a body in one
        /// life is still bolted on — and still enabled — when that body is leased again as a
        /// completely unrelated enemy. <c>Enemy.ResetForPool</c> clears its own latches and
        /// the brain's, but it cannot know about components a spawner added. So this method
        /// DISABLES ALL FOUR pressure behaviours on the body first and then enables exactly
        /// the one asked for, every single time. That makes a stale component from a previous
        /// life impossible by construction rather than by remembering to clean up, which is
        /// the same doctrine <c>ClearPooledLatches</c> is written to (one method, both sides,
        /// nothing for a future edit to forget).
        /// </para>
        /// Guarded + traced per CLAUDE.md §12: a brand-new system that silently failed to
        /// attach would present as "the owner sees no caravans" with nothing in the capture.
        /// </summary>
        public static void Attach(Enemy enemy, EndlessPressureRole role, int trueWave,
                                 EndlessPressureTuning tuning)
        {
            if (enemy == null)
            {
                FlowTrace.Fail(Sys,
                    $"Attach: null enemy for role {role} on wave {trueWave} — no pressure unit released.");
                return;
            }

            Guard.Try(Sys, "attach endless pressure role", () =>
            {
                // Clear every role first (see the pool-safety remarks above).
                var breacher = enemy.GetComponent<EnemyWallBreacher>();
                var caravan  = enemy.GetComponent<EnemySupportCaravan>();
                var mage     = enemy.GetComponent<EnemySupportMage>();
                var catapult = enemy.GetComponent<EnemySiegeCatapult>();
                if (breacher != null) breacher.enabled = false;
                if (caravan  != null) caravan.enabled  = false;
                if (mage     != null) mage.enabled     = false;
                if (catapult != null) catapult.enabled = false;

                switch (role)
                {
                    case EndlessPressureRole.WallBreacher:
                        if (breacher == null) breacher = enemy.gameObject.AddComponent<EnemyWallBreacher>();
                        breacher.enabled = true;
                        breacher.Configure(enemy, trueWave);
                        break;

                    case EndlessPressureRole.SupportCaravan:
                        if (caravan == null) caravan = enemy.gameObject.AddComponent<EnemySupportCaravan>();
                        caravan.enabled = true;
                        caravan.Configure(enemy, trueWave);
                        break;

                    case EndlessPressureRole.SupportMage:
                        if (mage == null) mage = enemy.gameObject.AddComponent<EnemySupportMage>();
                        mage.enabled = true;
                        mage.Configure(enemy, trueWave, tuning);
                        break;

                    case EndlessPressureRole.SiegeCatapult:
                        if (catapult == null) catapult = enemy.gameObject.AddComponent<EnemySiegeCatapult>();
                        catapult.enabled = true;
                        catapult.Configure(enemy, trueWave, tuning);
                        break;

                    case EndlessPressureRole.None:
                    default:
                        // All four already disabled above — an ordinary body, deliberately.
                        break;
                }

                if (role != EndlessPressureRole.None)
                {
                    FlowTrace.Step(Sys,
                        $"wave {trueWave}: released {role} on '{enemy.EnemyId}' " +
                        $"(def '{enemy.EnemyDefId}', first-pass art mapping — see the file header).");
                }
            });
        }

        /// <summary>
        /// The enemy def id each pressure unit rides. FIRST-PASS ART MAPPING onto defs that
        /// ALREADY have R2 bundles — see the §16 note in the file header for why no new def is
        /// introduced. Returns empty for <see cref="EndlessPressureRole.WallBreacher"/>, which
        /// is not its own unit at all: it is a share of whatever the wave already spawned.
        /// </summary>
        public static string DefIdForRole(EndlessPressureRole role)
        {
            switch (role)
            {
                case EndlessPressureRole.SupportCaravan: return "hollow-acolyte";
                case EndlessPressureRole.SupportMage:    return "orc-shaman";
                case EndlessPressureRole.SiegeCatapult:  return "ogre";
                default:                                 return string.Empty;
            }
        }
    }
}

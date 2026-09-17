// =============================================================================
// RaidSpireAlarmRegression [raid-spire-alarm]  Marker: RAID_SPIRE_ALARM_OK / _FAIL
// -----------------------------------------------------------------------------
// WO-1830. Pins the raid spire ATTACK ALARM and the defender convergence it drives.
//
// OWNER RULING THIS EXISTS TO PROTECT (verbatim):
//   "also we should make it when the player starts attacking the spire in a raid an
//    alarm goes off and all the defenders start walking to the base to protect it"
//   and, on being told the leash system already exists:
//   "right now they just sit inside there leash range"
//
// WHAT EACH CASE PINS, AND WHY IT IS HERE
//   1 [alarm-once]   RaidSpire raises AlarmRaised on the FIRST damage and NEVER again,
//                    however many more hits land. A re-fire per hit would re-rally the
//                    whole garrison every swing - a fan-out on the hot damage path.
//                    A second, fresh spire raises its OWN alarm (once per raid, not
//                    once per process - a static LATCH would break every raid after
//                    the first).
//   2 [fan-out]      Every brain in the tracked list receives the rally; the returned
//                    count matches; a null entry is skipped, not thrown on.
//   3 [rally-ring]   ComputeRallyPoint is a RING, not the spire point (the return-home
//                    arrival test is ~2m, so one point makes bodies shove forever), the
//                    bearing keeps each post on its own side, a defender already inside
//                    the ring holds its post, and a degenerate bearing spreads by seat
//                    index instead of stacking every body on one spot.
//   4 [converges]    THE OWNER-FACING ASSERTION: an alarmed defender's home anchor ends
//                    up STRICTLY CLOSER to the spire than it started, regardless of where
//                    the hero is. That is the difference between "walking to the base"
//                    and "sitting inside their leash range".
//   5 [pool-reset]   The new _alarmed latch is cleared in EnemyBrain.ResetForPool, so a
//                    pooled body is never born already rallied to a spire from a raid it
//                    has left. (EnemyPoolResetRegression case 2 also covers this by field
//                    enumeration; this states it by name so the intent is not just a lint.)
//   6 [wiring]       Source-linted with comments AND string literals stripped, so prose
//                    cannot fake a pass: the alarm is raised behind the !_alarmRaised
//                    guard inside RaidSpire's ONE damage funnel; the spawner subscribes in
//                    Start and unsubscribes in OnDestroy (a STATIC event outlives the
//                    scene - a leaked handler fans the next raid's alarm at this raid's
//                    destroyed brains); Track re-rallies a late spawn (guards seat 1-2 per
//                    frame, so the alarm can land mid-spawn); the fan-out path calls no
//                    FindObjectsByType; and the four PURE leash helpers the alarm must not
//                    disturb still exist, because the whole design rests on reusing them.
//
// No bake, no PlayMode, no JSON writes. EditMode-safe: Awake does not run on an editor
// AddComponent (RaidSpire.cs:100-110 records this), and neither RallyTo nor the spire's
// damage funnel needs it - TakeDamage lazily initialises HP (RaidSpire.cs:296).
//
// Shape: public static bool Run(out string reason), DataRegression-shaped. Standalone:
// run-unity-method DeNelle.Editor.Regression.RaidSpireAlarmRegression.RunAll
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    public static class RaidSpireAlarmRegression
    {
        private const string SpireSrc   = "Assets/_Modules/Village/World/Camps/RaidSpire.cs";
        private const string SpawnerSrc = "Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs";
        private const string BrainSrc   = "Assets/_Modules/Village/Enemies/EnemyBrain.cs";

        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? "RAID_SPIRE_ALARM_OK :: " : "RAID_SPIRE_ALARM_FAIL :: ") + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            void Fail(string s) => failures.Add("RAID_SPIRE_ALARM FAIL: " + s);

            try
            {
                CaseAlarmOnce(Fail, log);
                CaseRallyRing(Fail, log);
                CaseFanOut(Fail, log);
                CaseConverges(Fail, log);
                CasePoolReset(Fail, log);
                CaseWiring(Fail, log);
            }
            catch (Exception ex)
            {
                Fail("threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = string.Join("\n", failures);
                return false;
            }
            reason = log.ToString().TrimEnd();
            return true;
        }

        // ── Case 1: the alarm fires exactly once per spire ────────────────────

        private static void CaseAlarmOnce(Action<string> Fail, StringBuilder log)
        {
            int raises = 0;
            RaidSpire seen = null;
            Action<RaidSpire> handler = s => { raises++; seen = s; };

            var goA = new GameObject("RaidSpireAlarmOracle_A");
            var goB = new GameObject("RaidSpireAlarmOracle_B");
            try
            {
                var spireA = goA.AddComponent<RaidSpire>();
                spireA.Configure("oracle-cfg", "oracle-art", 500f, 9f);

                RaidSpire.AlarmRaised += handler;

                if (spireA.IsAlarmRaised)
                    Fail("[alarm-once] a freshly configured spire already reports IsAlarmRaised - " +
                         "the alarm would be spent before the player ever swings.");

                spireA.TakeDamage(10f, DeNelle.Core.Combat.DamageElement.None);
                if (raises != 1)
                    Fail("[alarm-once] first damage raised the alarm " + raises + " time(s), expected 1 - " +
                         "the owner ruling is that the alarm goes off when the player STARTS attacking.");
                if (!spireA.IsAlarmRaised)
                    Fail("[alarm-once] IsAlarmRaised is still false after the first hit.");
                if (!ReferenceEquals(seen, spireA))
                    Fail("[alarm-once] the event did not carry the spire that was hit - the spawner " +
                         "reads WorldPosition off it to pick the rally centre.");

                // Four more hits, including one through the ENEMY contact seam, must add nothing.
                spireA.TakeDamage(10f, DeNelle.Core.Combat.DamageElement.None);
                spireA.TakeDamage(10f, DeNelle.Core.Combat.DamageElement.Flame);
                spireA.ApplyContactDamage(10f);
                spireA.ApplyContactDamage(10f);
                if (raises != 1)
                    Fail("[alarm-once] the alarm re-fired on later hits (" + raises + " total). A fan-out " +
                         "over the whole garrison on every swing is a hot-path cost, and the rally would " +
                         "be recomputed from moved anchors each time.");

                // A SECOND spire is a SECOND raid: it must raise its own alarm. A static latch
                // would leave every raid after the first with no alarm at all.
                var spireB = goB.AddComponent<RaidSpire>();
                spireB.Configure("oracle-cfg-2", "oracle-art", 500f, 9f);
                spireB.TakeDamage(10f, DeNelle.Core.Combat.DamageElement.None);
                if (raises != 2)
                    Fail("[alarm-once] a second (fresh) spire did not raise its own alarm - the latch is " +
                         "per-process, not per-raid, so only the first raid of a session would alarm.");

                // A zero/negative hit is not an attack.
                int before = raises;
                var goC = new GameObject("RaidSpireAlarmOracle_C");
                try
                {
                    var spireC = goC.AddComponent<RaidSpire>();
                    spireC.Configure("oracle-cfg-3", "oracle-art", 500f, 9f);
                    spireC.TakeDamage(0f, DeNelle.Core.Combat.DamageElement.None);
                    if (raises != before)
                        Fail("[alarm-once] a 0-damage hit raised the alarm - the damage funnel's " +
                             "amount<=0 early-out must stay AHEAD of the alarm.");
                }
                finally { UnityEngine.Object.DestroyImmediate(goC); }

                log.AppendLine("[alarm-once] once per spire across 5 hits on two seams, once more for a " +
                               "second spire, never on a 0-damage hit.");
            }
            finally
            {
                RaidSpire.AlarmRaised -= handler;
                UnityEngine.Object.DestroyImmediate(goA);
                UnityEngine.Object.DestroyImmediate(goB);
            }
        }

        // ── Case 3 (run second: pure, no scene objects) ───────────────────────

        private static void CaseRallyRing(Action<string> Fail, StringBuilder log)
        {
            Vector3 spire = new Vector3(100f, 3f, -50f);
            const float ring = 6f;

            // A far post: lands ON the ring, on its own bearing, at the spire's Y.
            Vector3 farHome = spire + new Vector3(40f, 1f, 0f);
            Vector3 rally = EnemyBrain.ComputeRallyPoint(spire, farHome, ring, 0, 8);
            Vector3 flat = rally - spire; flat.y = 0f;
            if (Mathf.Abs(flat.magnitude - ring) > 0.01f)
                Fail("[rally-ring] a far post rallied to " + flat.magnitude.ToString("0.###") + "m from the " +
                     "spire, expected the " + ring + "m ring. A single point cannot satisfy the ~2m " +
                     "return-home arrival test for a whole garrison at once.");
            if (Mathf.Abs(rally.y - spire.y) > 0.001f)
                Fail("[rally-ring] the rally point did not take the spire's Y - it must sit on the base's " +
                     "ground plane before the caller snaps it to the NavMesh.");
            Vector3 wantDir = new Vector3(1f, 0f, 0f);
            if (Vector3.Dot(flat.normalized, wantDir) < 0.999f)
                Fail("[rally-ring] the rally bearing does not follow the defender's own post direction - " +
                     "every post would converge onto one side of the base instead of holding its own.");

            // A post on the far side keeps ITS side.
            Vector3 otherHome = spire + new Vector3(0f, 0f, -30f);
            Vector3 other = EnemyBrain.ComputeRallyPoint(spire, otherHome, ring, 1, 8);
            Vector3 otherFlat = other - spire; otherFlat.y = 0f;
            if (Vector3.Dot(otherFlat.normalized, new Vector3(0f, 0f, -1f)) < 0.999f)
                Fail("[rally-ring] a post on the far side did not keep its bearing.");

            // Already inside the ring: hold the post, unchanged (the boss on BossAnchor case).
            Vector3 nearHome = spire + new Vector3(2f, 0f, 1f);
            Vector3 held = EnemyBrain.ComputeRallyPoint(spire, nearHome, ring, 0, 8);
            if ((held - nearHome).sqrMagnitude > 0.0001f)
                Fail("[rally-ring] a defender already inside the ring was re-anchored (" + held + " vs " +
                     nearHome + ") - it is already defending the base and would only shuffle sideways.");

            // Degenerate bearing (a defender exactly at the centre) spreads by seat index.
            Vector3 d0 = EnemyBrain.ComputeRallyPoint(spire, spire, ring, 0, 4);
            Vector3 d1 = EnemyBrain.ComputeRallyPoint(spire, spire, ring, 1, 4);
            // Degenerate home is INSIDE the ring, so the hold-post rule owns it: both return the
            // centre. What must not happen is a NaN / infinite point escaping into a nav call.
            foreach (var p in new[] { d0, d1 })
            {
                if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z))
                    Fail("[rally-ring] a degenerate (centre) post produced a non-finite rally point " + p +
                         " - that reaches NavMesh.SamplePosition and Enemy.SetBrainTargetPosition.");
            }

            // The index fallback is reachable only when the post is inside the BEARING deadzone
            // (<0.5m) yet outside the ring - i.e. a sub-metre ring. Driven explicitly so the
            // fallback is proven to SPREAD rather than stacking every body on one point.
            Vector3 tiny = spire + new Vector3(0.4f, 0f, 0f);
            Vector3 s0 = EnemyBrain.ComputeRallyPoint(spire, tiny, 0.3f, 0, 4);
            Vector3 s1 = EnemyBrain.ComputeRallyPoint(spire, tiny, 0.3f, 1, 4);
            if ((s0 - s1).sqrMagnitude <= 0.0001f)
                Fail("[rally-ring] the degenerate-bearing fallback gave seats 0 and 1 the same point (" +
                     s0 + ") - it must spread by seat index, or centred defenders stack.");

            // A zero/negative ring must not collapse to the spire point.
            Vector3 zero = EnemyBrain.ComputeRallyPoint(spire, farHome, 0f, 0, 8);
            Vector3 zeroFlat = zero - spire; zeroFlat.y = 0f;
            if (zeroFlat.magnitude < 1f)
                Fail("[rally-ring] ring<=0 collapsed the rally onto the spire point (" +
                     zeroFlat.magnitude.ToString("0.###") + "m) - it must fall back to a sane default.");

            log.AppendLine("[rally-ring] ring radius, per-post bearing, hold-post-inside-ring, finite " +
                           "degenerate fallback and the ring<=0 default all hold.");
        }

        // ── Case 2: the fan-out reaches every tracked brain ───────────────────

        private static void CaseFanOut(Action<string> Fail, StringBuilder log)
        {
            Vector3 spire = new Vector3(0f, 0f, 0f);
            const float ring = 6f;
            var brains = new List<EnemyBrain>();
            var hosts = new List<GameObject>();
            try
            {
                // Four posts on the compass, all far outside the ring.
                Vector3[] posts =
                {
                    new Vector3(30f, 0f, 0f), new Vector3(-28f, 0f, 0f),
                    new Vector3(0f, 0f, 26f), new Vector3(0f, 0f, -24f)
                };
                for (int i = 0; i < posts.Length; i++)
                {
                    var go = NewBrainHost("RaidAlarmOracleBrain_" + i, out EnemyBrain brain);
                    hosts.Add(go);
                    if (brain == null)
                    {
                        Fail("[fan-out] could not build an EnemyBrain host in EditMode - the oracle " +
                             "cannot prove the fan-out.");
                        return;
                    }
                    brain.SetDefendPost(posts[i], 14f, 16f);
                    if (brain.IsAlarmed)
                        Fail("[fan-out] a freshly posted brain already reports IsAlarmed.");
                    brains.Add(brain);
                }

                // A null entry in the middle: a dead body pruned by Unity must not throw.
                brains.Insert(2, null);

                int alerted = RaidGarrisonSpawner.FanOutAlarm(brains, spire, ring, null);
                if (alerted != posts.Length)
                    Fail("[fan-out] " + alerted + " defender(s) alerted, expected " + posts.Length +
                         " - the alarm must reach EVERY tracked brain (owner: \"ALL the defenders\").");

                for (int i = 0; i < brains.Count; i++)
                {
                    var b = brains[i];
                    if (b == null) continue;
                    if (!b.IsAlarmed)
                        Fail("[fan-out] brain '" + b.name + "' was not alarmed by the fan-out.");
                }

                // Null list / empty list are answered, not thrown on.
                if (RaidGarrisonSpawner.FanOutAlarm(null, spire, ring, null) != 0)
                    Fail("[fan-out] a null brain list did not answer 0.");
                if (RaidGarrisonSpawner.FanOutAlarm(new List<EnemyBrain>(), spire, ring, null) != 0)
                    Fail("[fan-out] an empty brain list did not answer 0.");

                log.AppendLine("[fan-out] " + alerted + " of " + posts.Length + " brains alarmed, null " +
                               "entry skipped, null/empty lists answer 0.");
            }
            finally
            {
                for (int i = 0; i < hosts.Count; i++) UnityEngine.Object.DestroyImmediate(hosts[i]);
            }
        }

        // ── Case 4: the anchor actually MOVES TOWARD the base ────────────────

        private static void CaseConverges(Action<string> Fail, StringBuilder log)
        {
            Vector3 spire = new Vector3(12f, 0f, -7f);
            const float ring = 6f;
            var go = NewBrainHost("RaidAlarmOracleConverge", out EnemyBrain brain);
            var idleGo = NewBrainHost("RaidAlarmOracleIdle", out EnemyBrain idle);
            try
            {
                if (brain == null || idle == null)
                {
                    Fail("[converges] could not build an EnemyBrain host in EditMode.");
                    return;
                }

                Vector3 post = spire + new Vector3(0f, 0f, 35f);
                brain.SetDefendPost(post, 14f, 16f);
                idle.SetDefendPost(post, 14f, 16f);

                float before = Vector3.Distance(new Vector3(brain.HomeAnchor.x, 0f, brain.HomeAnchor.z),
                                                new Vector3(spire.x, 0f, spire.z));

                // NO HERO ANYWHERE. This is the whole point: convergence must not be gated on hero
                // proximity, which is exactly what "they just sit inside there leash range" means.
                var one = new List<EnemyBrain> { brain };
                RaidGarrisonSpawner.FanOutAlarm(one, spire, ring, null);

                float after = Vector3.Distance(new Vector3(brain.HomeAnchor.x, 0f, brain.HomeAnchor.z),
                                               new Vector3(spire.x, 0f, spire.z));
                if (after >= before - 0.01f)
                    Fail("[converges] the alarmed defender's home anchor did not move toward the spire (" +
                         before.ToString("0.#") + "m -> " + after.ToString("0.#") + "m). The return-home " +
                         "arm of the leash gate walks to the home anchor, so if the anchor does not move " +
                         "the defender does not walk to the base - the owner's complaint, unfixed.");
                if (Mathf.Abs(after - ring) > 0.01f)
                    Fail("[converges] the anchor landed " + after.ToString("0.###") + "m from the spire, " +
                         "expected the " + ring + "m rally ring.");

                // The brain that was NOT in the fanned-out list is untouched: the alarm is not a
                // global mutation of every EnemyBrain in the process.
                if (idle.IsAlarmed)
                    Fail("[converges] a brain outside the fanned-out list was alarmed - the alarm must " +
                         "reach only the raid's tracked garrison.");
                if ((idle.HomeAnchor - post).sqrMagnitude > 0.0001f)
                    Fail("[converges] an un-alarmed brain's home anchor moved - unalarmed leash behaviour " +
                         "must be byte-identical to before this feature.");

                log.AppendLine("[converges] anchor " + before.ToString("0.#") + "m -> " +
                               after.ToString("0.#") + "m from the spire with NO hero present; an " +
                               "un-listed brain is untouched.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(idleGo);
            }
        }

        // ── Case 5: the latch is cleared on pool reset ────────────────────────

        private static void CasePoolReset(Action<string> Fail, StringBuilder log)
        {
            string body = ExtractMethod(ReadStripped(BrainSrc, Fail, "EnemyBrain.cs"), "ResetForPool");
            if (body == null)
            {
                Fail("[pool-reset] could not locate EnemyBrain.ResetForPool to verify the alarm latch " +
                     "is cleared.");
                return;
            }
            if (!Regex.IsMatch(body, @"_alarmed\s*=\s*false"))
                Fail("[pool-reset] EnemyBrain.ResetForPool does not clear _alarmed - a pooled body would " +
                     "be born already rallied to a spire from a raid it has left (the P0-2 latch shape, " +
                     "and EnemyPoolResetRegression's [brain-latch-coverage] will fail too).");
            else
                log.AppendLine("[pool-reset] ResetForPool clears the _alarmed latch.");
        }

        // ── Case 6: the wiring is real (comments + string literals stripped) ──

        private static void CaseWiring(Action<string> Fail, StringBuilder log)
        {
            string spire   = ReadStripped(SpireSrc, Fail, "RaidSpire.cs");
            string spawner = ReadStripped(SpawnerSrc, Fail, "RaidGarrisonSpawner.cs");
            string brain   = ReadStripped(BrainSrc, Fail, "EnemyBrain.cs");
            if (spire == null || spawner == null || brain == null) return;

            // The alarm is raised from the ONE damage funnel, behind the once-only guard.
            string applyDamage = ExtractMethod(spire, "ApplyDamage");
            if (applyDamage == null)
                Fail("[wiring] could not locate RaidSpire.ApplyDamage - the single damage funnel both " +
                     "the player seam (TakeDamage) and the enemy seam (ApplyContactDamage) route through.");
            else
            {
                if (!applyDamage.Contains("AlarmRaised"))
                    Fail("[wiring] RaidSpire.ApplyDamage does not raise AlarmRaised - only that funnel " +
                         "sees BOTH damage seams, so raising it anywhere else misses one of them.");
                if (!Regex.IsMatch(applyDamage, @"!\s*_alarmRaised"))
                    Fail("[wiring] the AlarmRaised raise is not behind a !_alarmRaised guard - it would " +
                         "fan out over the whole garrison on every single hit.");
            }
            if (!Regex.IsMatch(spire, @"static\s+event\s+System\.Action<\s*RaidSpire\s*>\s+AlarmRaised"))
                Fail("[wiring] RaidSpire.AlarmRaised is not a static event - the spawner has no spire " +
                     "reference at subscribe time, and the alternative is a per-frame scan.");

            // Subscribe in Start, unsubscribe in OnDestroy. The unsubscribe is load-bearing.
            string start = ExtractMethod(spawner, "Start");
            string onDestroy = ExtractMethod(spawner, "OnDestroy");
            if (start == null || !Regex.IsMatch(start, @"RaidSpire\.AlarmRaised\s*\+="))
                Fail("[wiring] RaidGarrisonSpawner.Start does not subscribe to RaidSpire.AlarmRaised - a " +
                     "subscription made later than Start can miss a first-frame hit.");
            if (onDestroy == null || !Regex.IsMatch(onDestroy, @"RaidSpire\.AlarmRaised\s*-="))
                Fail("[wiring] RaidGarrisonSpawner.OnDestroy does not unsubscribe from the STATIC " +
                     "AlarmRaised event - the handler outlives the scene and the NEXT raid's alarm would " +
                     "fan out to THIS raid's destroyed brains.");

            // The stagger hole: a guard tracked AFTER the alarm is rallied on spawn.
            string track = ExtractMethod(spawner, "Track");
            if (track == null || !track.Contains("RallyTo"))
                Fail("[wiring] RaidGarrisonSpawner.Track does not rally a late spawn - guards seat 1-2 " +
                     "per frame, so an alarm landing mid-spawn would leave the tail of the garrison at " +
                     "its post through the very fight the alarm exists to answer.");

            // No per-frame scan anywhere in the spawner (the cheap-wiring requirement).
            if (spawner.Contains("FindObjectsByType"))
                Fail("[wiring] RaidGarrisonSpawner now calls FindObjectsByType - the fan-out must read " +
                     "the brain list it already holds from spawn time.");

            // The four PURE leash helpers the design REUSES must still be there. The convergence is
            // the existing return-home arm re-anchored; if these are gone, it is gone.
            foreach (string helper in new[] { "ShouldLeashOut", "ShouldHoldChase", "ShouldWake",
                                              "HeroDistanceFromHome" })
                if (!brain.Contains(helper))
                    Fail("[wiring] EnemyBrain." + helper + " is gone. The alarm adds NO new Update " +
                         "branch - it re-anchors the home the existing leash gate already walks to, so " +
                         "removing these helpers removes the convergence.");

            // RallyTo must move the home anchor and set the latch, and must NOT touch the leash radii
            // (that would silently retune every defend post).
            string rally = ExtractMethod(brain, "RallyTo");
            if (rally == null)
                Fail("[wiring] EnemyBrain.RallyTo is missing.");
            else
            {
                if (!Regex.IsMatch(rally, @"_homeAnchor\s*="))
                    Fail("[wiring] EnemyBrain.RallyTo does not move _homeAnchor - re-anchoring the home " +
                         "IS the convergence.");
                if (Regex.IsMatch(rally, @"_leashRadius\s*=") ||
                    Regex.IsMatch(rally, @"_chaseLeashOverride\s*="))
                    Fail("[wiring] EnemyBrain.RallyTo rewrites a leash radius. The alarm must change the " +
                         "home anchor ONLY - retuning wake/chase would change every defend post's " +
                         "behaviour behind the ruling's back.");
            }

            log.AppendLine("[wiring] one damage funnel behind a once-guard, static event, Start subscribe " +
                           "+ OnDestroy unsubscribe, late-spawn rally, no FindObjectsByType, four leash " +
                           "helpers intact, RallyTo moves only the anchor.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        // Build a host carrying an EnemyBrain. [RequireComponent(typeof(Enemy))] pulls Enemy in,
        // which pulls NavMeshAgent + EnemyDamageable; none of their Awakes run in EditMode (none of
        // the three carries [ExecuteAlways] - grepped 2026-09-17), and RallyTo / SetDefendPost /
        // HomeAnchor touch none of them. PRECEDENT, so this is not a new risk:
        // DungeonRoomOwnershipRegression.cs:188 and :467 already do exactly this AddComponent in
        // EditMode to drive the same brain's pure leash helpers.
        private static GameObject NewBrainHost(string hostName, out EnemyBrain brain)
        {
            var go = new GameObject(hostName);
            EnemyBrain built = null;
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build alarm-oracle brain host",
                () => { built = go.AddComponent<EnemyBrain>(); });
            brain = built;
            return go;
        }

        private static string ReadStripped(string path, Action<string> Fail, string label)
        {
            if (!File.Exists(path)) { Fail("could not read " + label + " at " + path); return null; }
            return StripCode(File.ReadAllText(path));
        }

        // Named consts so this file carries no bare brace character literals - CLAUDE.md sec.1's
        // one-liner counts every brace in the file and a body extractor written with bare '{' / '}'
        // comparisons trips it with a false BRACE MISMATCH.
        private const char OpenBrace = '{';
        private const char CloseBrace = '}';

        /// <summary>
        /// Extract one method body from ALREADY-STRIPPED source by brace matching from the first
        /// occurrence of "<paramref name="method"/>(". Returns null when not found.
        /// </summary>
        private static string ExtractMethod(string stripped, string method)
        {
            if (stripped == null) return null;
            var m = Regex.Match(stripped, @"\b" + Regex.Escape(method) + @"\s*\(");
            while (m.Success)
            {
                int open = stripped.IndexOf(OpenBrace, m.Index);
                if (open < 0) return null;
                // A declaration, not a call: only whitespace sits between the closing paren and
                // the opening brace. (Written without a bare brace character in the prose - the
                // CLAUDE.md sec.1 one-liner counts braces inside comments too.)
                int close = stripped.IndexOf(')', m.Index);
                if (close > 0 && close < open && stripped.Substring(close + 1, open - close - 1).Trim().Length == 0)
                {
                    int depth = 0;
                    for (int i = open; i < stripped.Length; i++)
                    {
                        if (stripped[i] == OpenBrace) depth++;
                        else if (stripped[i] == CloseBrace)
                        {
                            depth--;
                            if (depth == 0) return stripped.Substring(open, i - open + 1);
                        }
                    }
                    return null;
                }
                m = m.NextMatch();
            }
            return null;
        }

        // Blank // and /* */ comments AND "..." / '...' literals, so a match can only come from real
        // code. Mirrors AggroLeashRegression.StripCode: deliberately simple, never reflows the file.
        private static string StripCode(string src)
        {
            if (src == null) return null;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

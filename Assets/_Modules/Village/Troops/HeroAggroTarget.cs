// =============================================================================
// HeroAggroTarget - "what is hitting the player RIGHT NOW", published once for the
// whole warband (WO-1752 ruling 3, owner 2026-09-15).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THE OWNER'S SENTENCE THIS EXISTS FOR, verbatim:
//   "they are running around attacking walls while i am getting damaged and killed"
//
// WHY A NEW FILE AND NOT A HOOK IN HeroHealth:
//   HeroHealth.cs is owned by another lane this session (WO-1750), and WO-1752 says to
//   find the EXISTING "last attacker of the hero" seam rather than add a second damage
//   listener. It was looked for and IT DOES NOT EXIST - stated as a finding, not glossed:
//     * HeroHealth.TakeDamage(float) takes an AMOUNT and no attacker (HeroHealth.cs:666).
//     * The contact tick attributes nothing outward: it scans for adjacent Enemy
//       components itself, sums their ContactDamage and keeps the buffer PRIVATE
//       (HeroHealth.cs:364-405, _attackerBuf). The nearest thing to a publisher is
//       _lastDamageSourceWorld, which is a POSITION, private, and only used to pick a
//       death-direction bucket.
//     * NoteDamageSource(Vector3) (HeroHealth.cs:664) is the inbound seam, also a bare
//       position, and its callers are outside this silo.
//     * HeroCombatEngagement (Core/Combat) is a reference-counted SET of opaque `object`
//       tokens feeding BattleLock - it cannot hand back an IDamageable to target, and it
//       is scoped to hero-only duelists, so it is not this signal either.
//   So this file DERIVES the signal from two public facts instead of hooking anything:
//   the hero's HP going DOWN (HeroHealth.Hp, public) and the nearest live hostile to her.
//
// ⚠ THE ATTRIBUTION IS A HEURISTIC AND IS NAMED AS ONE (CLAUDE.md sec.11B).
//   "Her HP dropped" is MEASURED. "...and this particular enemy is the one that did it"
//   is INFERRED from proximity. For the melee case the WO is about (a raid defender or
//   the boss standing on her) the inference is tight; for a tower or a ranged attacker
//   outside the ring it is wrong, and the warband will instead take the nearest body to
//   her, which is still the behaviour the owner asked for and still not a wall. The
//   towers case is carried to the owner as an open question in the WO-1752 RESULT rather
//   than silently solved here.
//
// COST: ONE scan for the whole warband, throttled, mirroring TroopController's existing
// SharedBreachFocus (0.4 s, FindObjectsByType<WallSegment>) rather than inventing a
// second cadence. Every troop reads the same published answer in the same frame.
//
// Reset-safe: every static below resets on a domain reload.
// =============================================================================

using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// The hostile currently damaging the hero, published for every troop to read.
    /// See the file header for why this is derived rather than hooked.
    /// </summary>
    public static class HeroAggroTarget
    {
        /// <summary>Seconds between scans. One scan serves the whole warband.</summary>
        private const float RefreshSeconds = 0.35f;

        private static float _nextRefreshAt;
        private static float _lastHeroHp = -1f;
        private static float _lastHeroHurtAt = -999f;
        private static IDamageable _current;

        /// <summary>
        /// The live hostile that is damaging the hero, or null. Safe to call every frame
        /// from every troop: the scan behind it is throttled to <see cref="RefreshSeconds"/>.
        /// </summary>
        /// <remarks>
        /// ⭐ THE HURT WINDOW IS <see cref="RaidAssaultAi.PeelHurtWindowSeconds"/>, DELIBERATELY
        /// NOT A SECOND NUMBER. That const is already "how long a blow keeps counting as aggro"
        /// for a troop (RaidAssaultAi.cs:43, 2.5 s); the hero's aggro counting as the warband's is
        /// the whole of ruling 3, so it counts for the same length of time. Minting a second knob
        /// here is the duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16 each record.
        /// </remarks>
        public static IDamageable Current
        {
            get
            {
                Refresh();
                if (_current == null || !_current.IsAlive) return null;
                if (Time.time - _lastHeroHurtAt > RaidAssaultAi.PeelHurtWindowSeconds) return null;
                return _current;
            }
        }

        /// <summary>True when <see cref="Current"/> would hand back a live attacker.</summary>
        public static bool HasLiveAttacker => Current != null;

        /// <summary>Editor/test hook: forget everything (a raid ending, a domain reload).</summary>
        public static void Reset()
        {
            _nextRefreshAt = 0f;
            _lastHeroHp = -1f;
            _lastHeroHurtAt = -999f;
            _current = null;
        }

        private static void Refresh()
        {
            if (Time.time < _nextRefreshAt) return;
            _nextRefreshAt = Time.time + RefreshSeconds;

            var hero = HeroHealth.Instance;
            if (hero == null || !hero.IsAlive)
            {
                // Not an error - town with no raid, or the hero is down. THROTTLED, NOT Once():
                // Once is global for the life of the domain, so the first scene transition would
                // consume it and a genuinely null Instance mid-raid - the case this line's own
                // text calls "NOT expected" - would then be silent forever. 10 s is quiet enough
                // for the benign town case and still says so if it persists (CLAUDE.md sec.12).
                FlowTrace.Throttle("TroopAI", "hero-aggro-no-hero", 10f,
                    "HeroAggroTarget: no live HeroHealth.Instance - defend-the-hero stands down " +
                    "(WO-1752 ruling 3). Expected outside a raid; NOT expected mid-raid.");
                _current = null;
                _lastHeroHp = -1f;
                return;
            }

            // ⚠ HP IS POLLED, NOT SUBSCRIBED, AND THAT IS THE CHEAPER CORRECT CHOICE.
            // HeroHealth.OnHealthChanged fires from ELEVEN sites (HeroHealth.cs - Heal,
            // RegenTick, RestoreToFull, Respawn, gear re-sync ...), so a subscriber would have
            // had to diff the value anyway; and Instance is replaced across scene loads
            // (HeroHealth.cs:246 / :279), so a subscription would need re-arming or it would
            // hold a dead hero forever. Polling a public float on a 0.35 s cadence has neither
            // problem and no lifetime to get wrong.
            float hp = hero.Hp;
            if (_lastHeroHp >= 0f && hp < _lastHeroHp - 0.01f)
                _lastHeroHurtAt = Time.time;
            _lastHeroHp = hp;

            if (Time.time - _lastHeroHurtAt > RaidAssaultAi.PeelHurtWindowSeconds)
            {
                _current = null;
                return;
            }

            // Still inside the hurt window: who is standing on her?
            Vector3 heroPos = hero.transform.position;
            float ringSqr = RaidAssaultAi.HeroAttackerRingMeters * RaidAssaultAi.HeroAttackerRingMeters;
            IDamageable best = null;
            float bestSqr = float.MaxValue;

            // EnemyDamageable is the IDamageable face of every Enemy (Enemy carries it by
            // RequireComponent - EnemyFactory.cs:159-161 says so in its own comment), so this
            // scan sees raid defenders, the raid boss and village wave enemies alike. It is
            // NOT widened to every IDamageable: a hostile STRUCTURE near the hero is not what
            // "break off to whatever is hitting her" means, and sending the warband at a wall
            // segment beside her would reintroduce precisely the bug WO-1752 closes.
            var hostiles = Object.FindObjectsByType<EnemyDamageable>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < hostiles.Length; i++)
            {
                var h = hostiles[i];
                if (h == null || !h.IsAlive) continue;
                if (h.Faction != CombatFaction.Hostile) continue;
                Vector3 d = h.WorldPosition - heroPos;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr > ringSqr) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = h; }
            }

            _current = best;

            if (best == null)
            {
                // The hero is LOSING HP with no hostile body near her: a tower, a trap, a
                // ranged attacker outside the ring, or fall damage. Named rather than
                // swallowed - this is the one case where the heuristic in the file header
                // provably does not cover the owner's symptom, and a device log should say so.
                FlowTrace.Throttle("TroopAI", "hero-aggro-unattributed", 5f,
                    $"HeroAggroTarget: hero lost HP (hp={hp:F0}) but NO live hostile body within " +
                    $"{RaidAssaultAi.HeroAttackerRingMeters:F0}m - nothing for the warband to " +
                    "break off to (tower / ranged / hazard). WO-1752 open question 2.");
            }
        }
    }
}

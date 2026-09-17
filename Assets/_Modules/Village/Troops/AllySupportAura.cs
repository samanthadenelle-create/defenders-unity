// =============================================================================
// AllySupportAura — the PLAYER's healers get AoE heal + regen too (WO-1835, piece 6)
// -----------------------------------------------------------------------------
// OWNER RULING (2026-09-17). Asked whether the enemy support mages' new "AoE heal spells
// and regen" should apply to her own healers as well, the owner answered: "so should mine".
// This is parity, not a separate feature — same concept, both sides, added to the same
// ticket rather than deferred, because a mechanic that only the enemy has reads as the game
// cheating.
//
// ── WHAT ALREADY EXISTED (measured at source, 2026-09-17, not assumed) ───────────────────
// The player DOES already have a healer: `troop-field-cleric`, role "support"
// (Assets/Resources/Data/Canonical/troops.json), flagged by TroopController.cs:485
// (`_isSupport`) and driven from its AI tick at TroopController.cs:690 into
// TroopController.TryHealSquadmate (:890-928). But that existing heal is:
//   • SINGLE-TARGET — a linear scan of the roster picking the ONE lowest-HP-ratio ally
//     (:895-903), then `target.Heal(_attackDamage)` (:917). Not an area effect.
//   • TROOPS ONLY — it iterates the static `Active` roster and never references HeroHealth,
//     so a Field Cleric standing beside a dying hero has never once healed her.
//   • BURST ONLY — no regen/heal-over-time component of any kind.
// The hero's own heals (HeroAbilities `AbilityEffect.Heal` :1349-1382, `ResolveHealOverTime`
// :2398-2410, `HealFromDrain` :1871) are ALL self-only and land on HeroHealth; none of them
// touches TroopController at all. So "AoE heal + regen for allies" is genuinely NEW work on
// the player side; it is not a rename of something that shipped.
//
// ── WHY THIS FILE TOUCHES NEITHER TroopController NOR HeroAbilities ──────────────────────
// Both are large files carrying other in-flight tickets this session (TroopController alone
// holds WO-1764 and WO-1830 changes). CLAUDE.md §9's serialization rule and §11's
// file-disjoint lane rule both point the same way, so this lane adds a component and a
// self-installing director instead of editing them. The existing single-target
// TryHealSquadmate is left EXACTLY as it is — this aura is ADDITIVE alongside it, so no
// behaviour the owner has already felt-tested changes.
//
// ── NO FRIENDLY PHYSICS QUERY (deliberate) ──────────────────────────────────────────────
// There is no friendly-mask OverlapSphere, no FindNearbyAllies and no ally LayerMask
// anywhere in the project — every player-side Physics.OverlapSphere is enemy-masked. Rather
// than invent a friendly layer (a project-wide change smuggled into player-facing work,
// which ARCHITECTURE_PRINCIPLES forbids), this reads the roster that already exists:
// TroopController.ActiveTroops (:58) plus HeroHealth.Instance. The roster is a handful of
// units, so a distance filter over it is cheaper than a physics query anyway.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Gives one player support troop (the Field Cleric) an area heal burst on a cadence plus a
    /// continuous regen aura, covering BOTH allied troops and the hero. Installed automatically
    /// by <see cref="AllySupportAuraDirector"/> — nothing needs to reference this class.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AllySupportAura : MonoBehaviour
    {
        /// <summary>FlowTrace system tag for the player-side half of WO-1835.</summary>
        public const string Sys = "AllySupport";

        // ── FIRST-PASS TUNING — OWNER FELT-TUNES (WO-1835 flags all numbers as estimates) ───
        /// <summary>Band for both the burst and the regen. Generous: a cleric should cover a squad.</summary>
        private const float AuraRadius = 10f;
        /// <summary>Seconds between AoE heal bursts.</summary>
        private const float BurstSeconds = 8f;
        /// <summary>Burst heal, as a share of each ally's max HP.</summary>
        private const float BurstHealFraction = 0.18f;
        /// <summary>Continuous regen, as a share of max HP per second.</summary>
        private const float RegenFractionPerSecond = 0.01f;
        private const float RegenTickSeconds = 1f;

        private TroopController _troop;
        private float _burstTimer;
        private float _regenTimer;
        private int _burstsCast;
        private float _hpRestoredTotal;

        /// <summary>Total HP this cleric has restored across troops + hero (captures + oracle).</summary>
        public float HpRestoredTotal => _hpRestoredTotal;

        /// <summary>Completed AoE bursts this life.</summary>
        public int BurstsCast => _burstsCast;

        /// <summary>
        /// Binds the aura to its host support troop. Idempotent — the director calls this on
        /// install and it is safe to re-call on an already-installed aura.
        /// </summary>
        public void Configure(TroopController troop)
        {
            _troop = troop != null ? troop : GetComponent<TroopController>();
            _burstTimer = BurstSeconds;
            _regenTimer = RegenTickSeconds;

            if (_troop == null)
            {
                FlowTrace.Fail(Sys,
                    $"AllySupportAura on '{name}' has no TroopController — it cannot tell whether it is " +
                    "alive or where its squad is; disabling rather than healing from a dead body.");
                enabled = false;
            }
        }

        private void Update()
        {
            if (_troop == null || !_troop.IsAlive) return;

            _regenTimer -= Time.deltaTime;
            if (_regenTimer <= 0f)
            {
                _regenTimer = RegenTickSeconds;
                TickRegen();
            }

            _burstTimer -= Time.deltaTime;
            if (_burstTimer <= 0f)
            {
                _burstTimer = BurstSeconds;
                CastAoeHeal();
            }
        }

        /// <summary>
        /// The "and regen" half: a quiet heal-over-time on every nearby ally AND the hero.
        /// Uses the SILENT hero path (<c>HeroHealth.RegenTick</c>, which exists precisely for
        /// per-frame drips) so a 1 Hz tick does not strobe the hero's heal VFX all battle.
        /// </summary>
        private void TickRegen()
        {
            Guard.Try(Sys, "ally support regen tick", () =>
            {
                float restored = 0f;
                int healed = 0;

                var roster = TroopController.ActiveTroops;
                if (roster != null)
                {
                    for (int i = 0; i < roster.Count; i++)
                    {
                        TroopController ally = roster[i];
                        if (!IsHealableAlly(ally)) continue;
                        float before = ally.Hp;
                        ally.Heal(ally.MaxHp * RegenFractionPerSecond * RegenTickSeconds);
                        float gained = ally.Hp - before;
                        if (gained > 0f) { healed++; restored += gained; }
                    }
                }

                // THE HERO IS INCLUDED, AND THAT IS THE POINT. The shipped single-target
                // TryHealSquadmate never once healed her; the owner's parity ruling is what
                // brings the hero inside her own cleric's aura.
                var hero = HeroHealth.Instance;
                if (IsHealableHero(hero))
                {
                    float before = hero.Hp;
                    hero.RegenTick(hero.MaxHp * RegenFractionPerSecond * RegenTickSeconds);
                    float gained = hero.Hp - before;
                    if (gained > 0f) { healed++; restored += gained; }
                }

                _hpRestoredTotal += restored;
                if (healed <= 0) return;

                // Throttled: this is a 1 Hz tick and an unthrottled line would flood a device
                // logcat and evict the boot window (memory: logcat-ring-buffer-destroys-evidence).
                FlowTrace.Throttle(Sys, $"auraregen-{GetInstanceID()}", 6f,
                    $"Field Cleric REGEN aura mending {healed} ally(ies) for +{restored:0.#} HP this tick " +
                    $"({RegenFractionPerSecond * 100f:0.#}%/s, {AuraRadius:0.#}m, {_hpRestoredTotal:0.#} HP total).");
            });
        }

        /// <summary>
        /// The AoE heal burst: one large mend across every nearby ally and the hero. Uses the
        /// LOUD hero path (<c>HeroHealth.Heal</c>, which fires the heal VFX) so the player sees
        /// her cleric land a real spell.
        /// </summary>
        private void CastAoeHeal()
        {
            Guard.Try(Sys, "ally support AoE heal burst", () =>
            {
                float restored = 0f;
                int healed = 0;

                var roster = TroopController.ActiveTroops;
                if (roster != null)
                {
                    for (int i = 0; i < roster.Count; i++)
                    {
                        TroopController ally = roster[i];
                        if (!IsHealableAlly(ally)) continue;
                        float before = ally.Hp;
                        ally.Heal(ally.MaxHp * BurstHealFraction);
                        float gained = ally.Hp - before;
                        if (gained > 0f) { healed++; restored += gained; }
                    }
                }

                var hero = HeroHealth.Instance;
                if (IsHealableHero(hero))
                {
                    float before = hero.Hp;
                    hero.Heal(hero.MaxHp * BurstHealFraction);
                    float gained = hero.Hp - before;
                    if (gained > 0f) { healed++; restored += gained; }
                }

                _burstsCast++;
                _hpRestoredTotal += restored;

                // Only captured when it actually did something, so the line never claims a heal
                // that healed nobody (a full-HP squad is the common quiet case).
                if (healed > 0)
                {
                    FlowTrace.Step(Sys,
                        $"Field Cleric CAST AoE HEAL #{_burstsCast} — mended {healed} ally(ies) " +
                        $"(hero included when in band) for {restored:0.#} HP " +
                        $"({BurstHealFraction * 100f:0.#}% of max each, {AuraRadius:0.#}m).");
                }
            });
        }

        /// <summary>
        /// A living, wounded, in-band hero. <c>HeroHealth</c> exposes no <c>IsAlive</c> — its
        /// liveness member is <c>IsDeathLatched</c> (HeroHealth.cs:505), so the test is written
        /// against the property that actually exists rather than the one the name symmetry with
        /// TroopController would suggest.
        /// </summary>
        private bool IsHealableHero(HeroHealth hero)
        {
            if (hero == null) return false;
            if (hero.IsDeathLatched || hero.Hp <= 0f) return false;
            if (hero.Hp >= hero.MaxHp) return false;
            return InRange(hero.transform);
        }

        /// <summary>A living, wounded, in-band ally that is not this cleric itself.</summary>
        private bool IsHealableAlly(TroopController ally)
        {
            if (ally == null || ally == _troop) return false;
            if (!ally.IsAlive) return false;
            if (ally.Hp >= ally.MaxHp) return false;
            return InRange(ally.transform);
        }

        private bool InRange(Transform other)
        {
            if (other == null) return false;
            return (other.position - transform.position).sqrMagnitude <= AuraRadius * AuraRadius;
        }
    }

    /// <summary>
    /// Installs <see cref="AllySupportAura"/> onto every player support troop, without any
    /// other class needing to know this feature exists.
    /// <para>
    /// ⛔ WHY A DIRECTOR AND NOT A LINE IN TroopController.Awake. TroopController is a
    /// serialization bottleneck this session (two other tickets are live in it) and CLAUDE.md
    /// §9/§11 put file-disjointness ahead of convenience. A self-installing director keeps the
    /// entire player-side half of WO-1835 inside files this lane created, so it can be reviewed,
    /// reverted or re-tuned as one unit and cannot collide with another lane's diff.
    /// </para>
    /// The scan is a throttled pass over <c>TroopController.ActiveTroops</c> — a roster of a
    /// handful of units, once a second — not a scene-wide FindObjectsByType, and it exits
    /// immediately when the roster is empty (the normal state in town with no squad deployed).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AllySupportAuraDirector : MonoBehaviour
    {
        private const float ScanSeconds = 1f;
        private float _timer;
        private int _installed;

        /// <summary>How many auras this director has installed this session.</summary>
        public int Installed => _installed;

        private static AllySupportAuraDirector s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Idempotent. RuntimeInitializeOnLoadMethod fires once per player run, but a domain
            // reload in the editor (or a future additive-load path) must not leave two directors
            // installing two auras onto the same cleric and double-healing the squad.
            if (s_instance != null) return;

            // One director for the whole process. Hidden + persistent so a scene change (town ->
            // raid -> town) does not need a second one, and so it never shows up in the hierarchy
            // as a mystery object during a felt-test.
            var host = new GameObject("AllySupportAuraDirector");
            host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(host);
            s_instance = host.AddComponent<AllySupportAuraDirector>();
            FlowTrace.Step(AllySupportAura.Sys,
                "AllySupportAuraDirector online — player support troops will receive the AoE heal + " +
                "regen aura (WO-1835 parity ruling: 'so should mine').");
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = ScanSeconds;

            Guard.Try(AllySupportAura.Sys, "install ally support auras", () =>
            {
                var roster = TroopController.ActiveTroops;
                if (roster == null || roster.Count == 0) return;

                for (int i = 0; i < roster.Count; i++)
                {
                    TroopController troop = roster[i];
                    if (troop == null || !troop.IsAlive) continue;
                    if (!IsSupportTroop(troop)) continue;
                    if (troop.GetComponent<AllySupportAura>() != null) continue;

                    var aura = troop.gameObject.AddComponent<AllySupportAura>();
                    aura.Configure(troop);
                    _installed++;
                    FlowTrace.Step(AllySupportAura.Sys,
                        $"installed AoE heal + regen aura on support troop '{troop.name}' " +
                        $"(aura #{_installed} this session).");
                }
            });
        }

        /// <summary>
        /// True for a player support troop.
        /// <para>
        /// ⚠ THE ROLE IS NOT READABLE FROM OUTSIDE, AND THAT IS WHY THIS MATCHES ON THE ID.
        /// TroopDef.Role is a string (TroopDef.cs:39) and TroopController latches it into the
        /// PRIVATE fields `_troopRole` / `_isSupport` (TroopController.cs:166 / :91, set at :484-485)
        /// with no public accessor. Exposing one would mean editing TroopController, which this
        /// lane is deliberately staying out of (see the class remarks). So the test runs against
        /// the PUBLIC `TroopId` (:309) — the def id, e.g. "troop-field-cleric" — which is the same
        /// authored record `_isSupport` is derived from, not a second classification invented here.
        /// </para>
        /// <para>
        /// If a later ticket adds a public role accessor, REPLACE this with it rather than adding
        /// tokens to the list: an id-substring test is the weaker seam and is marked as such.
        /// </para>
        /// </summary>
        public static bool IsSupportTroop(TroopController troop)
        {
            if (troop == null) return false;
            return IsSupportTroopId(troop.TroopId) || IsSupportTroopId(troop.name);
        }

        /// <summary>
        /// Pure id test, split out so the oracle can assert the classification with no scene:
        /// the Field Cleric matches, an ordinary footman does not.
        /// </summary>
        public static bool IsSupportTroopId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string s = id.ToLowerInvariant();
            return s.Contains("cleric") || s.Contains("support") || s.Contains("healer");
        }
    }
}

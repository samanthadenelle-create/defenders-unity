// =============================================================================
// WaveCompositionBuilder — smart per-wave enemy composition (WO-362).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES
//   Replaces the flat "8 of one type from one gate" spawn with a COMPOSED wave:
//   a tiered mix of REAL enemy ids drawn from enemies.json, each tagged with a
//   tactical SpawnRole the SmartEnemySpawner positions by. The composition is
//   generated per wave number with a difficulty curve:
//
//     waves 1-2 : 100% weak  (grunts/skirmishers)
//     waves 3-5 : ~60% weak + ~40% medium (brutes/casters)
//     waves 6+  : mixed weak / medium / strong, weak enemies PERSIST
//     every 5th : an ELITE (single, centred) is added on top - UNLESS waves.json
//                 already authors that wave's heavy (boss / apexBoss), in which
//                 case the authored heavy is the wave's ONE heavy (2026-08-16)
//
//   Total count + difficulty scale with the wave number. NO two consecutive
//   waves are identical — the builder varies the type pick + the counts using a
//   per-wave seed so the felt experience changes wave to wave.
//
// TIER → REAL ENEMY ID MAP  (read from enemies.json, family "hollow" = the wave
// siege faction; falls back to literal ids if the catalog is unavailable):
//   weak   → role "grunt"      → hollow-walker        (DPS-positioned)
//            role "skirmisher" → hollow-rogue         (Ranged-positioned)
//   medium → brute slot        → hollow-warrior       (front melee; warrior stats, not golem tank)
//            role "caster"     → hollow-acolyte       (Ranged/back-positioned)
//   strong → role "brute"      → hollow-warrior (more of them, front)
//   elite  → role "elite"      → necromancer          (Elite — single, centred)
//
// PURE DATA + ALGORITHM — no MonoBehaviour, no scene refs, no per-frame work.
// The build allocates once per wave (a small List) — never in the spawn hot path.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// WO-362: the tactical position bucket the <see cref="SmartEnemySpawner"/>
    /// places a composed enemy in. Distinct from <see cref="EnemyRole"/> (which
    /// drives EnemyBrain target selection) — this is purely about WHERE on the
    /// approach the unit materialises relative to the gate/Heart line.
    /// </summary>
    public enum SpawnRole
    {
        /// <summary>Tanks / brutes — front-centre, lead the push.</summary>
        FrontTank = 0,
        /// <summary>Melee line — mid, spread laterally.</summary>
        Melee = 1,
        /// <summary>Archers / casters — backline, away from the hero.</summary>
        Archer = 2,
        /// <summary>Weak fodder — back / sides, trail the formation.</summary>
        Weak = 3,
        /// <summary>Boss / elite — single, dead-centre.</summary>
        Elite = 4,
    }

    /// <summary>
    /// One composed slot: how many of a real enemy id to spawn and the tactical
    /// position bucket they hold. The <see cref="EnemyRole"/> (brain targeting)
    /// is carried alongside so the spawner can stamp it without re-deriving.
    /// </summary>
    public struct WaveCompositionEntry
    {
        public string     EnemyId;    // a REAL id from enemies.json (e.g. "hollow-walker")
        public int        Count;
        public SpawnRole  SpawnRole;  // tactical position bucket
        public EnemyRole  Role;       // EnemyBrain tactical role

        public WaveCompositionEntry(string enemyId, int count, SpawnRole spawnRole, EnemyRole role)
        {
            EnemyId   = enemyId;
            Count     = Mathf.Max(0, count);
            SpawnRole = spawnRole;
            Role      = role;
        }
    }

    /// <summary>
    /// The full composition for one wave: the ordered list of (enemyType, tier,
    /// spawnRole) slots plus the wave number it was generated for. Built by
    /// <see cref="WaveCompositionBuilder.Build"/>, consumed by
    /// <see cref="SmartEnemySpawner"/>.
    /// </summary>
    public sealed class EnemyWaveComposition
    {
        /// <summary>1-based wave this composition is for.</summary>
        public int WaveId;

        /// <summary>The composed slots (front tanks first, elite last).</summary>
        public readonly List<WaveCompositionEntry> Entries = new List<WaveCompositionEntry>();

        /// <summary>Total enemy count across all entries.</summary>
        public int TotalCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++) n += Entries[i].Count;
                return n;
            }
        }

        /// <summary>True if this wave contains an elite slot.</summary>
        public bool HasElite
        {
            get
            {
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].SpawnRole == SpawnRole.Elite) return true;
                return false;
            }
        }
    }

    /// <summary>
    /// WO-362: generates a smart, tiered <see cref="EnemyWaveComposition"/> for a
    /// given wave number. Pure: no Unity scene state, deterministic per wave +
    /// seed so a wave reads the same on retry but consecutive waves differ.
    /// </summary>
    public static class WaveCompositionBuilder
    {
        // ── Real enemy ids (family "hollow" — the wave-siege faction) ─────────
        // These ARE the ids in Resources/Data/Canonical/enemies.json. Resolved
        // against the live catalog when one is supplied (so a renamed/added id is
        // picked up), with these literals as the back-compat fallback.
        private const string IdWeakGrunt   = "hollow-walker";   // role grunt      → DPS
        private const string IdWeakSkirm   = "hollow-rogue";    // role skirmisher → Ranged
        private const string IdMediumBrute = "hollow-warrior";  // role brute      → Tank
        private const string IdMediumCast  = "hollow-acolyte";  // role caster     → Healer/back
        private const string IdElite       = "necromancer";     // role elite      → MiniBoss

        // ── Cross-family ids (orc / troll / ogre) ────────────────────────────
        // Verified present in enemies.json + mapped to distinct models by
        // EnemyFactory. The brute/caster/elite SLOTS draw from these by wave band
        // so waves stop being all-skeleton (owner break-log 2026-06-14: "no trolls
        // no orcs ogre"). Weak fodder (grunt/skirmisher) stays hollow by design.
        private const string IdOrcBrute   = "orc-berserker";
        private const string IdOrcCaster  = "orc-shaman";
        private const string IdOrcElite   = "orc-necromancer";
        private const string IdTrollBrute = "troll";
        private const string IdOgreBrute  = "ogre";

        // ── Difficulty pacing ────────────────────────────────────────────────
        private const int   BaseCount       = 4;     // total at wave 1
        private const float CountPerWave    = 0.9f;  // +~1 enemy per wave
        private const int   MaxCount        = 22;    // hard cap so the field never floods
        private const int   EliteEveryNth   = 5;     // an elite on every 5th wave

        /// <summary>
        /// Builds the composition for <paramref name="waveId"/> (1-based). When a
        /// <paramref name="catalog"/> is supplied the chosen tier ids are validated
        /// against it (and the closest family member substituted if an id is gone),
        /// so the builder always emits ids the spawner can actually resolve.
        ///
        /// <paramref name="seedSalt"/> lets the caller vary the RNG (e.g. the gate
        /// index) without changing the wave-to-wave anti-repeat guarantee.
        /// </summary>
        /// <param name="waveHasAuthoredHeavy">
        /// TRUE when waves.json already authors a heavy for this wave (a <c>boss</c> id or an
        /// <c>apexBoss</c> block). REQUIRED, with no default, ON PURPOSE - see the elite block
        /// below: ONE authority fields a wave's heavy, and a caller that forgets to say which
        /// one owns this wave must not compile.
        /// </param>
        public static EnemyWaveComposition Build(
            int waveId, bool waveHasAuthoredHeavy, EnemyCatalog catalog = null, int seedSalt = 0)
        {
            waveId = Mathf.Max(1, waveId);
            var comp = new EnemyWaveComposition { WaveId = waveId };

            // Deterministic-but-varying RNG: seeded on the wave (so a retry of the
            // same wave reads the same) plus an odd/even phase so consecutive waves
            // never resolve to the same counts/picks. State is restored after.
            UnityEngine.Random.State prev = UnityEngine.Random.state;
            UnityEngine.Random.InitState(waveId * 7919 + seedSalt * 104729 + (waveId & 1) * 31);

            // WO-1773: the roster CEILING is remote-tunable as a PERCENT on MaxCount, because that
            // ceiling binds from wave 21 onward and is half of why a wave-176 roster is the same
            // size as a wave-21 one. FoldMaxCount returns MaxCount itself at the shipping default
            // (100), so with no database row this line is byte-for-byte what it was. The percent
            // lives on the rail rather than as a new authored field because waves.json is not a
            // safe home for a new spawn knob (the _RETIRED_batchFields scar).
            int effectiveMaxCount = WaveDifficultyTunables.FoldMaxCount(MaxCount);
            int total = Mathf.Clamp(
                Mathf.RoundToInt(BaseCount + CountPerWave * (waveId - 1)),
                BaseCount, Mathf.Max(BaseCount, effectiveMaxCount));

            // ── Tier ratios by wave band ─────────────────────────────────────
            float weakFrac, mediumFrac, strongFrac;
            if (waveId <= 2)
            {
                weakFrac = 1f; mediumFrac = 0f; strongFrac = 0f;
            }
            else if (waveId <= 5)
            {
                weakFrac = 0.6f; mediumFrac = 0.4f; strongFrac = 0f;
            }
            else
            {
                // 6+: weak PERSISTS, medium core, a growing strong slice.
                strongFrac = Mathf.Min(0.4f, 0.12f + 0.03f * (waveId - 6));
                mediumFrac = 0.4f;
                weakFrac   = 1f - mediumFrac - strongFrac;   // weak never vanishes
            }

            int weakN   = Mathf.RoundToInt(total * weakFrac);
            int mediumN = Mathf.RoundToInt(total * mediumFrac);
            int strongN = Mathf.Max(0, total - weakN - mediumN);

            // ── WEAK tier: split grunt / skirmisher, ratio varies per wave ────
            if (weakN > 0)
            {
                // 50–75% grunts, the remainder skirmishers — the split swings per
                // wave so two consecutive weak-heavy waves don't feel identical.
                float gruntShare = UnityEngine.Random.Range(0.5f, 0.75f);
                int grunts = Mathf.Clamp(Mathf.RoundToInt(weakN * gruntShare), 0, weakN);
                int skirms = weakN - grunts;

                if (grunts > 0)
                    AddEntry(comp, catalog, IdWeakGrunt, "hollow", "grunt",
                             grunts, SpawnRole.Weak, EnemyRole.DPS);
                if (skirms > 0)
                    AddEntry(comp, catalog, IdWeakSkirm, "hollow", "skirmisher",
                             skirms, SpawnRole.Melee, EnemyRole.Ranged);
            }

            // ── MEDIUM tier: brutes (front tanks) + casters (backline), drawn
            //    from the wave's available FAMILIES so one wave MIXES skeletons /
            //    orcs / trolls / ogres instead of all-skeleton.
            if (mediumN > 0)
            {
                // ~60% brute, ~40% caster, jittered so it varies.
                float bruteShare = UnityEngine.Random.Range(0.5f, 0.7f);
                int brutes  = Mathf.Clamp(Mathf.RoundToInt(mediumN * bruteShare), 0, mediumN);
                int casters = mediumN - brutes;

                AddVaried(comp, catalog, BrutePool(waveId), brutes, "brute",
                          SpawnRole.FrontTank, EnemyRole.Tank);
                AddVaried(comp, catalog, CasterPool(waveId), casters, "caster",
                          SpawnRole.Archer, EnemyRole.Healer);
            }

            // ── STRONG tier (6+): more brutes leading the front (family-varied) ─
            if (strongN > 0)
                AddVaried(comp, catalog, BrutePool(waveId), strongN, "brute",
                          SpawnRole.FrontTank, EnemyRole.Tank);

            // ── ELITE: one, centred, every Nth wave (family-varied; on TOP) ───
            //
            // ONE HEAVY AUTHORITY PER WAVE (2026-08-16). This cadence and waves.json's own
            // "boss"/"apexBoss" fields are two independent producers of a wave's heavy, and
            // neither knew about the other: wave 5 authors a 1050 HP Cave Troll AND hit this
            // branch, so the owner fought TWO heavies; wave 20 authored the apex dragon and
            // got an elite on top of it. The AUTHORED wave wins - it is an explicit designer
            // statement with a pinned HP and a recorded ruling, while this cadence is generic
            // filler for waves nobody authored. Waves 10 and 15 author no boss, so they keep
            // their elite exactly as before: no count, no HP and no pool is touched here.
            bool eliteByCadence = waveId % EliteEveryNth == 0;
            if (eliteByCadence && !waveHasAuthoredHeavy)
            {
                var elites = ElitePool(waveId);
                string eliteId = elites[UnityEngine.Random.Range(0, elites.Length)];
                AddEntry(comp, catalog, eliteId, FamilyOf(eliteId), "elite",
                         1, SpawnRole.Elite, EnemyRole.MiniBoss);
            }
            else if (eliteByCadence)
            {
                FlowTrace.Step("Enemy",
                    $"WaveComposition wave={waveId}: elite cadence DEFERRED - waves.json already " +
                    "authors this wave's heavy (boss/apexBoss), and exactly one authority fields it.");
            }

            // TRACE: the ONE place a wave's variety is decided. Weak fodder stays
            // hollow; brute/caster/elite slots now draw from the wave-band family
            // pools (hollow → +orc → +troll/ogre), so a wave mixes families. The
            // slots dump below shows the actual ids requested for an F8 run.
            FlowTrace.Step("Enemy",
                $"WaveComposition wave={waveId}: total={total} weak={weakN} medium={mediumN} strong={strongN} " +
                $"elite={(eliteByCadence && !waveHasAuthoredHeavy ? 1 : 0)} " +
                $"(cadence={(eliteByCadence ? 1 : 0)}, authoredHeavy={waveHasAuthoredHeavy}) " +
                $"— brute families: {string.Join("/", BrutePool(waveId))}");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < comp.Entries.Count; i++)
            {
                var en = comp.Entries[i];
                sb.Append($"[{en.EnemyId} x{en.Count} pos={en.SpawnRole} role={en.Role}] ");
            }
            FlowTrace.Step("Enemy", $"WaveComposition wave={waveId} slots: {sb}");

            UnityEngine.Random.state = prev;
            return comp;
        }

        /// <summary>
        /// Adds one slot, resolving the preferred id against the catalog. If the
        /// literal id is missing, falls back to the first family member of the
        /// given role; if that also fails, keeps the literal so the spawner logs a
        /// clear "unknown enemy" rather than silently dropping the slot.
        /// </summary>
        private static void AddEntry(
            EnemyWaveComposition comp, EnemyCatalog catalog,
            string preferredId, string family, string role,
            int count, SpawnRole spawnRole, EnemyRole brainRole)
        {
            if (count <= 0) return;

            string id = preferredId;
            if (catalog != null)
            {
                if (catalog.Find(preferredId) == null)
                {
                    EnemyDef byRole = catalog.FindByRole(family, role);
                    if (byRole != null && !string.IsNullOrEmpty(byRole.Id))
                    {
                        id = byRole.Id;
                        FlowTrace.Warn("Enemy",
                            $"compose AddEntry: preferred id '{preferredId}' (family '{family}', role '{role}') " +
                            $"NOT in catalog — substituted by-role to '{id}'");
                    }
                    else
                    {
                        FlowTrace.Warn("Enemy",
                            $"compose AddEntry: preferred id '{preferredId}' (family '{family}', role '{role}') " +
                            $"NOT in catalog AND no by-role fallback — keeping literal '{preferredId}' (spawner will log unknown-enemy)");
                    }
                }
            }

            comp.Entries.Add(new WaveCompositionEntry(id, count, spawnRole, brainRole));
        }

        // ── Family pools by wave band (owner-tunable schedule) ───────────────
        // The roster the brute/caster/elite SLOTS may draw from at a given wave:
        // 1-2 hollow only · 3-5 + orc · 6+ + troll/ogre. Weak fodder stays hollow.
        private static string[] BrutePool(int waveId)
        {
            if (waveId <= 2) return new[] { IdMediumBrute };
            if (waveId <= 5) return new[] { IdMediumBrute, IdOrcBrute };
            return new[] { IdMediumBrute, IdOrcBrute, IdTrollBrute, IdOgreBrute };
        }

        private static string[] CasterPool(int waveId)
        {
            if (waveId <= 2) return new[] { IdMediumCast };
            return new[] { IdMediumCast, IdOrcCaster };
        }

        private static string[] ElitePool(int waveId)
            => waveId <= 5 ? new[] { IdElite } : new[] { IdElite, IdOrcElite };

        /// <summary>Best-effort family from an id prefix (for AddEntry's missing-id
        /// substitution; all pool ids resolve, so this is just a safety net).</summary>
        private static string FamilyOf(string id)
            => string.IsNullOrEmpty(id) ? "hollow"
             : id.StartsWith("orc")   ? "orc"
             : id.StartsWith("troll") ? "troll"
             : id.StartsWith("ogre")  ? "ogre"
             : "hollow";

        /// <summary>Spread <paramref name="count"/> across the family <paramref name="pool"/>
        /// (round-robin, remainder to the earlier families) so one tier shows a MIX of
        /// families in a single wave instead of all-one-model.</summary>
        private static void AddVaried(
            EnemyWaveComposition comp, EnemyCatalog catalog, string[] pool,
            int count, string role, SpawnRole spawnRole, EnemyRole brainRole)
        {
            if (count <= 0 || pool == null || pool.Length == 0) return;
            int n = pool.Length;
            for (int i = 0; i < n; i++)
            {
                int c = count / n + (i < count % n ? 1 : 0);
                if (c > 0)
                    AddEntry(comp, catalog, pool[i], FamilyOf(pool[i]), role,
                             c, spawnRole, brainRole);
            }
        }
    }
}

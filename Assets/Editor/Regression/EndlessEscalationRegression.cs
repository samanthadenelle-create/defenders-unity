// =============================================================================
// EndlessEscalationRegression — oracle for the WO-1835 post-wave-20 escalation
// -----------------------------------------------------------------------------
// WO-1835 names three assertions as the minimum bar:
//   • the wall-breacher targets a WallSegment, NOT the Heart, when active
//   • the catapult deals structure damage through IDamageableStructure
//   • the twin-dragon trigger fires ONLY past true wave 20
// All three are covered below, plus the rest of the escalation curve and the
// endless-only gate on every other unit.
//
// ⛔ WHY THIS IS AN ORACLE AND NOT A PLAY-MODE TEST. Every predicate under test was written
// as a PURE STATIC FUNCTION for exactly this reason: no scene, no NavMesh bake, no prefab,
// no R2 bundle, no Unity play loop. That is what lets it run inside DataRegression.RunAll in
// batchmode, which is the gate the project actually judges by. The two cases that need a
// real damage sink use a local stub implementing IDamageableStructure, the same shape
// StructureBurnRegression.StubStructure already uses in this folder.
//
// ⚠ THE BREACHER CASE ASSERTS THE ARBITRATION, NOT A MONOBEHAVIOUR TICK. What can actually
// go wrong in the breacher is a TARGET-SELECTION regression: someone re-orders
// EnemyBrain.ChooseTarget and the pinned wall stops beating the Heart fallback. A scene test
// spinning a NavMeshAgent would not catch that any earlier and could not run headless, so the
// coverage here is (a) the source-text pin that the pin is consulted BEFORE the role switch
// and the Heart fallbacks, and (b) the pure geometry/faction rule the pick is built on.
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class EndlessEscalationRegression
    {
        /// <summary>
        /// Minimal <see cref="IDamageableStructure"/> sink — the wall panel a catapult volley
        /// lands on. Mirrors StructureBurnRegression.StubStructure: Friendly, because in the
        /// player's town every wall IS friendly and a Hostile attacker must be allowed to hit it.
        /// </summary>
        private sealed class StubWall : IDamageableStructure
        {
            public float Hp = 100f;
            public bool IsAlive => Hp > 0f;
            public CombatFaction Faction => CombatFaction.Friendly;
            public void ApplyContactDamage(float amount) => Hp = Mathf.Max(0f, Hp - amount);
        }

        /// <summary>A same-faction structure — the thing a siege engine must REFUSE to hit.</summary>
        private sealed class StubEnemyOwnedWall : IDamageableStructure
        {
            public float Hp = 100f;
            public bool IsAlive => Hp > 0f;
            public CombatFaction Faction => CombatFaction.Hostile;
            public void ApplyContactDamage(float amount) => Hp = Mathf.Max(0f, Hp - amount);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- ENDLESS ESCALATION (WO-1835: twin dragons / breachers / caravans / mages / catapults) ---");

            Case1_TwinDragonOnlyPastWave20(failures, log);
            Case2_TwinDragonHpSplit(failures, log);
            Case3_EndlessOnlyGateOnEveryUnit(failures, log);
            Case4_BreacherTargetsWallNotHeart(failures, log);
            Case5_CatapultDealsStructureDamage(failures, log);
            Case6_CatapultVolleysAreSynchronized(failures, log);
            Case7_FlankScorePrefersTheSides(failures, log);
            Case8_TuningComesFromWavesJson(failures, log);
            Case9_PlayerHealerParity(failures, log);

            if (failures.Count > 0)
            {
                reason = "ENDLESS ESCALATION FAIL (" + failures.Count + "): " + string.Join(" | ", failures.ToArray());
                Debug.LogError(log.ToString() + "\n" + reason);
                return false;
            }

            reason = "endless escalation: 9 cases green — twin dragons gated past wave 20, breacher pins a "
                   + "WallSegment over the Heart, catapult damages via IDamageableStructure and refuses "
                   + "friendly fire, volleys synchronized, tuning read from waves.json, player healer parity wired";
            Debug.Log(log.ToString() + "\n[endless-escalation] " + reason);
            return true;
        }

        // ── CASE 1 — THE HEADLINE ACCEPTANCE CRITERION ────────────────────────────────────
        // "the twin-dragon trigger only fires past true wave 20".
        private static void Case1_TwinDragonOnlyPastWave20(List<string> failures, StringBuilder log)
        {
            // Wave 20 is the AUTHORED apex wave: exactly one dragon, unchanged from what shipped.
            int atTwenty = EndlessPressure.ApexDragonCount(20, WaveManager.IsRecurringDragonWave(20));
            if (atTwenty != 1)
                failures.Add($"authored wave 20 must field exactly ONE apex dragon, got {atTwenty} " +
                             "(this is the shipped fight; twins past 20 must not change it)");

            // Every recurring dragon wave PAST 20 fields two.
            int[] pastTwenty = { 25, 30, 35, 40 };
            for (int i = 0; i < pastTwenty.Length; i++)
            {
                int w = pastTwenty[i];
                if (!WaveManager.IsRecurringDragonWave(w))
                {
                    failures.Add($"wave {w} was expected to be a recurring dragon wave (cadence 20/25/30/...) " +
                                 "but WaveManager.IsRecurringDragonWave says no — the cadence moved, so this " +
                                 "case's premise is stale, not the code");
                    continue;
                }
                int n = EndlessPressure.ApexDragonCount(w, true);
                if (n != 2)
                    failures.Add($"endless dragon wave {w} must field TWO dragons (owner ruling), got {n}");
            }

            // A NON-dragon wave fields none, at any wave number. This is the arm that stops the
            // twin trigger leaking onto ordinary endless waves.
            if (EndlessPressure.ApexDragonCount(33, false) != 0)
                failures.Add("a non-cadence wave must field ZERO apex dragons even in endless");
            if (EndlessPressure.ApexDragonCount(7, false) != 0)
                failures.Add("authored wave 7 must field ZERO apex dragons");

            // And nothing below 20 ever gets a twin, even if a future edit made it a dragon wave.
            if (EndlessPressure.ApexDragonCount(15, true) != 1)
                failures.Add("a dragon wave BELOW 20 must stay solo — the twin gate is strictly past 20");

            log.AppendLine($"  [1] dragon count: w20=1, w25/30/35/40=2, non-cadence=0, w15(forced)=1 — gate holds");
        }

        // ── CASE 2 — the twin HP split is a real reduction, and wave 20 is untouched ───────
        private static void Case2_TwinDragonHpSplit(List<string> failures, StringBuilder log)
        {
            var tuning = EndlessPressureTuning.Default;

            float solo = EndlessPressure.PerDragonHp(4200f, 1, tuning);
            if (!Mathf.Approximately(solo, 4200f))
                failures.Add($"a SOLO dragon must keep its authored HP exactly (got {solo}, expected 4200) — " +
                             "otherwise the shipped wave-20 fight silently changed");

            float twin = EndlessPressure.PerDragonHp(4200f, 2, tuning);
            if (twin >= 4200f)
                failures.Add($"each twin must carry LESS than the solo HP (got {twin}) — two full-HP dragons " +
                             "is the flat doubling WO-1835 flags as unfair on the first felt-test");
            if (twin * 2f <= 4200f)
                failures.Add($"the twin PAIR must still be a harder fight than the solo dragon " +
                             $"(pair total {twin * 2f} vs solo 4200)");

            // A nonsense authored share must not produce a zero-HP or negative-HP boss.
            float clamped = EndlessPressure.PerDragonHp(4200f, 2, new EndlessPressureTuning { TwinDragonHpShare = -5f });
            if (clamped <= 0f)
                failures.Add($"a bad twinDragonHpShare must clamp to a live boss, got {clamped} HP");

            log.AppendLine($"  [2] HP split: solo 4200 unchanged; each twin {twin:0} (pair {twin * 2f:0}); bad share clamps to {clamped:0}");
        }

        // ── CASE 3 — NOTHING leaks into the authored 1-20 schedule ────────────────────────
        private static void Case3_EndlessOnlyGateOnEveryUnit(List<string> failures, StringBuilder log)
        {
            var tuning = EndlessPressureTuning.Default;

            for (int w = 1; w <= 20; w++)
            {
                if (EndlessPressure.IsEndlessWave(w))
                    failures.Add($"wave {w} must NOT be classed endless (endless begins at 21)");
                if (EndlessPressure.BreacherCount(w, 20, tuning) != 0)
                    failures.Add($"wave {w} must field ZERO wall-breachers (authored schedule is out of scope)");
                if (EndlessPressure.SupportCaravanCount(w) != 0)
                    failures.Add($"wave {w} must field ZERO healing caravans");
                if (EndlessPressure.SupportMageCount(w) != 0)
                    failures.Add($"wave {w} must field ZERO support mages");
                if (EndlessPressure.CatapultCount(w) != 0)
                    failures.Add($"wave {w} must field ZERO catapults");
            }

            if (!EndlessPressure.IsEndlessWave(21))
                failures.Add("wave 21 must be classed endless");

            // Past the boundary every unit type appears, and the catapult floor is TWO because the
            // ruling is "from the sides AT THE SAME TIME" — one engine cannot satisfy that.
            if (EndlessPressure.BreacherCount(21, 20, tuning) <= 0)
                failures.Add("endless wave 21 must field at least one wall-breacher");
            if (EndlessPressure.SupportCaravanCount(21) <= 0)
                failures.Add("endless wave 21 must field at least one healing caravan");
            if (EndlessPressure.SupportMageCount(21) <= 0)
                failures.Add("endless wave 21 must field at least one support mage");
            int cats = EndlessPressure.CatapultCount(21);
            if (cats < 2)
                failures.Add($"endless waves must field at least TWO catapults so they can bombard two faces " +
                             $"simultaneously (owner: \"from the sides at the same time\"), got {cats}");

            // Breachers are a SHARE, never the whole wave — the push must still read as a push.
            int all = EndlessPressure.BreacherCount(30, 20, tuning);
            if (all >= 20)
                failures.Add($"breachers must be a SHARE of the squad, not all of it (got {all} of 20)");

            // The curve climbs but stays capped, so a very deep endless run cannot starve the
            // DEF-48 concurrency cap with support units.
            if (EndlessPressure.CatapultCount(500) > 4)
                failures.Add("catapult count must stay capped (<=4) however deep the endless run goes");
            if (EndlessPressure.SupportMageCount(500) > 3)
                failures.Add("support mage count must stay capped (<=3)");
            if (EndlessPressure.SupportCaravanCount(500) > 2)
                failures.Add("healing caravan count must stay capped (<=2)");

            log.AppendLine($"  [3] endless-only gate: waves 1-20 field none of the five; w21 breach="
                         + $"{EndlessPressure.BreacherCount(21, 20, tuning)} caravan={EndlessPressure.SupportCaravanCount(21)} "
                         + $"mage={EndlessPressure.SupportMageCount(21)} catapult={cats}; deep-run caps hold");
        }

        // ── CASE 4 — ACCEPTANCE CRITERION: the breacher targets a WallSegment, not the Heart ──
        private static void Case4_BreacherTargetsWallNotHeart(List<string> failures, StringBuilder log)
        {
            // (a) The pin exists on the ONE mover and is cleared on pool release.
            string brainPath = "Assets/_Modules/Village/Enemies/EnemyBrain.cs";
            string brain = ReadRepoFile(brainPath);
            if (brain == null)
            {
                failures.Add($"could not read {brainPath} — cannot verify the breacher target pin");
            }
            else
            {
                if (!brain.Contains("SetStructureFocus"))
                    failures.Add("EnemyBrain no longer declares SetStructureFocus — the breacher has no way to " +
                                 "pin a wall, so it would fall back to normal scoring and march the Heart");
                if (!brain.Contains("ClearStructureFocus"))
                    failures.Add("EnemyBrain no longer declares ClearStructureFocus — a pooled body would " +
                                 "inherit a previous life's wall and refuse the Heart forever");

                // ⭐ THE ORDERING IS THE ASSERTION. The pin must be consulted BEFORE the role switch,
                // because every role arm ends in a `?? _heartTransform` fallback or the weighted
                // scorer (which also considers the Heart). A pin placed after the switch would be
                // unreachable and the breacher would silently become an ordinary marcher — the exact
                // regression this case exists to catch, and one no count-based check would notice.
                int pin = brain.IndexOf("_structureFocus != null", System.StringComparison.Ordinal);
                int roleSwitch = brain.IndexOf("switch (Role)", System.StringComparison.Ordinal);
                if (pin < 0)
                    failures.Add("EnemyBrain.ChooseTarget no longer consults _structureFocus — the wall pin is dead code");
                else if (roleSwitch < 0)
                    failures.Add("EnemyBrain.ChooseTarget no longer has its `switch (Role)` — this case's premise is stale");
                else if (pin > roleSwitch)
                    failures.Add("the _structureFocus pin is checked AFTER `switch (Role)` — every role arm falls back " +
                                 "to _heartTransform or the weighted scorer, so the pin is unreachable and a breacher " +
                                 "would target the HEART instead of its wall panel");

                // The pin must be reset on pool release (the [brain-latch-coverage] doctrine).
                int reset = brain.IndexOf("public void ResetForPool()", System.StringComparison.Ordinal);
                if (reset >= 0 && brain.IndexOf("_structureFocus", reset, System.StringComparison.Ordinal) < 0)
                    failures.Add("EnemyBrain.ResetForPool does not clear _structureFocus — a recycled body would " +
                                 "carry a stale (possibly destroyed) wall pin into its next life");
            }

            // (b) The breacher component asks for a WALL and never for the Heart.
            string breacher = ReadRepoFile("Assets/_Modules/Village/Enemies/EnemyWallBreacher.cs");
            if (breacher == null)
            {
                failures.Add("could not read EnemyWallBreacher.cs");
            }
            else
            {
                if (!breacher.Contains("NearestBreachTarget"))
                    failures.Add("EnemyWallBreacher no longer resolves its target through " +
                                 "EndlessPressure.NearestBreachTarget (the WallSegment picker)");
                if (breacher.Contains("HeartController") || breacher.Contains("HeartTarget"))
                    failures.Add("EnemyWallBreacher references the Heart — it must target a WallSegment ONLY; " +
                                 "the Heart is what this unit exists NOT to walk to");
            }

            // (c) The picker honours the ONE faction predicate: a wall is only a candidate for the
            //     other side. A breacher in an enemy-owned raid base must not smash its own camp.
            var townWall = new StubWall();               // Friendly  — the player's town
            var campWall = new StubEnemyOwnedWall();     // Hostile   — an enemy-owned base
            if (!CombatFactionRules.MayAttack(CombatFaction.Hostile, townWall))
                failures.Add("a Hostile breacher must be allowed to attack a Friendly town wall");
            if (CombatFactionRules.MayAttack(CombatFaction.Hostile, campWall))
                failures.Add("a Hostile breacher must NOT be allowed to attack a Hostile (own-side) wall");

            log.AppendLine("  [4] breacher: SetStructureFocus/ClearStructureFocus present, pin consulted BEFORE "
                         + "`switch (Role)` (so it beats every `?? _heartTransform` fallback), cleared on pool "
                         + "release, no Heart reference in the component, faction rule enforced");
        }

        // ── CASE 5 — ACCEPTANCE CRITERION: the catapult damages via IDamageableStructure ──
        private static void Case5_CatapultDealsStructureDamage(List<string> failures, StringBuilder log)
        {
            var tuning = EndlessPressureTuning.Default;

            // The real damage path, exercised through the interface the catapult actually calls.
            var wall = new StubWall { Hp = 100f };
            IDamageableStructure asStructure = wall;

            float before = wall.Hp;
            asStructure.ApplyContactDamage(tuning.CatapultDamagePerHit);
            if (wall.Hp >= before)
                failures.Add($"a catapult stone must LOWER the panel's HP through " +
                             $"IDamageableStructure.ApplyContactDamage ({before} -> {wall.Hp})");
            if (!Mathf.Approximately(before - wall.Hp, tuning.CatapultDamagePerHit))
                failures.Add($"the panel must take exactly the authored catapult damage " +
                             $"({tuning.CatapultDamagePerHit}), took {before - wall.Hp}");

            // Repeated volleys bring a panel DOWN — otherwise the unit is decorative.
            int volleys = 0;
            while (wall.IsAlive && volleys < 1000)
            {
                asStructure.ApplyContactDamage(tuning.CatapultDamagePerHit);
                volleys++;
            }
            if (wall.IsAlive)
                failures.Add("repeated catapult volleys never destroyed the panel — the engine cannot breach");

            // The engine must route through the ONE faction predicate, not a bare IsAlive test.
            var ownCamp = new StubEnemyOwnedWall();
            if (CombatFactionRules.MayAttack(CombatFaction.Hostile, ownCamp))
                failures.Add("a Hostile catapult must NOT be cleared to bombard a Hostile wall");

            // And the source must actually call that interface + that predicate.
            string cat = ReadRepoFile("Assets/_Modules/Village/Enemies/EnemySiegeCatapult.cs");
            if (cat == null)
            {
                failures.Add("could not read EnemySiegeCatapult.cs");
            }
            else
            {
                if (!cat.Contains("IDamageableStructure"))
                    failures.Add("EnemySiegeCatapult no longer damages through IDamageableStructure");
                if (!cat.Contains("ApplyContactDamage"))
                    failures.Add("EnemySiegeCatapult no longer calls ApplyContactDamage — it deals no structure damage");
                if (!cat.Contains("CombatFactionRules.MayAttack"))
                    failures.Add("EnemySiegeCatapult does not gate its volley on CombatFactionRules.MayAttack — " +
                                 "the WO-1439 friendly-fire hole would reopen at a brand-new damage site");
            }

            log.AppendLine($"  [5] catapult: {tuning.CatapultDamagePerHit} dmg/stone via "
                         + $"IDamageableStructure.ApplyContactDamage, panel razed in {volleys + 1} volleys, "
                         + "friendly fire refused by the one predicate");
        }

        // ── CASE 6 — "at the same time": volleys are quantized, not per-engine timers ─────
        private static void Case6_CatapultVolleysAreSynchronized(List<string> failures, StringBuilder log)
        {
            const float interval = 6f;

            // Two engines released at DIFFERENT times must still agree on the volley index — that
            // is the whole "at the same time" clause. A per-engine countdown started at spawn
            // would drift and this is the case that would catch it.
            int a = EndlessPressure.VolleyIndex(61.4f, interval);   // engine released at t=0
            int b = EndlessPressure.VolleyIndex(61.9f, interval);   // engine released at t=13
            if (a != b)
                failures.Add($"two engines reading the clock inside the same window must get the SAME volley " +
                             $"index (got {a} and {b}) — otherwise they do not fire together");

            // The index must advance exactly once per interval.
            int i0 = EndlessPressure.VolleyIndex(0f, interval);
            int i1 = EndlessPressure.VolleyIndex(interval + 0.01f, interval);
            int i2 = EndlessPressure.VolleyIndex(interval * 2f + 0.01f, interval);
            if (i1 != i0 + 1 || i2 != i0 + 2)
                failures.Add($"the volley index must advance once per interval (got {i0}, {i1}, {i2})");

            // A degenerate authored interval must not divide by zero or fire every frame.
            int guarded = EndlessPressure.VolleyIndex(10f, 0f);
            if (guarded < 0)
                failures.Add($"a zero/negative volley interval must clamp, not produce a negative index (got {guarded})");
            // Negative time (an unscaled-clock edge) must not produce a negative index either.
            if (EndlessPressure.VolleyIndex(-5f, interval) != 0)
                failures.Add("a negative time must clamp to volley index 0");

            log.AppendLine($"  [6] volleys: two clocks in one window agree (index {a}), index advances 1/interval, "
                         + "degenerate interval + negative time clamped");
        }

        // ── CASE 7 — "from the sides": the flank score really prefers lateral panels ──────
        private static void Case7_FlankScorePrefersTheSides(List<string> failures, StringBuilder log)
        {
            Vector3 approach = Vector3.forward;

            float deadAhead = EndlessPressure.FlankScore(Vector3.forward * 30f, approach);
            float square    = EndlessPressure.FlankScore(Vector3.right * 30f, approach);
            float behind    = EndlessPressure.FlankScore(Vector3.back * 30f, approach);

            if (square <= deadAhead)
                failures.Add($"a panel SQUARE to the approach must score higher than one dead ahead " +
                             $"({square:0.00} vs {deadAhead:0.00}) — otherwise the catapults are not flanking, " +
                             "they are just another front rank");
            if (deadAhead > 0.01f)
                failures.Add($"a panel dead ahead on the approach lane must score ~0 (got {deadAhead:0.00})");
            if (square < 0.99f)
                failures.Add($"a panel square to the approach must score ~1 (got {square:0.00})");
            if (behind > 0.01f)
                failures.Add($"a panel directly BEHIND the approach is also on the axis and must score ~0 " +
                             $"(got {behind:0.00})");

            // Y is ignored — a wall higher up the hill is not thereby a flank.
            float raised = EndlessPressure.FlankScore(new Vector3(30f, 25f, 0f), approach);
            if (raised < 0.99f)
                failures.Add($"the flank score must be measured on the XZ plane only (got {raised:0.00} for a raised side panel)");

            // Degenerate inputs must return 0, not NaN — a NaN score would win every comparison
            // and hand every engine the same arbitrary panel.
            float degenerate = EndlessPressure.FlankScore(Vector3.zero, approach);
            if (float.IsNaN(degenerate) || degenerate != 0f)
                failures.Add($"a zero-length offset must score exactly 0, got {degenerate}");

            log.AppendLine($"  [7] flank score: ahead {deadAhead:0.00}, side {square:0.00}, behind {behind:0.00}, "
                         + $"raised side {raised:0.00}, degenerate {degenerate:0.00}");
        }

        // ── CASE 8 — the owner's tunables actually reach the units ────────────────────────
        private static void Case8_TuningComesFromWavesJson(List<string> failures, StringBuilder log)
        {
            // The whole point of putting these in waves.json is that the owner can re-tune the
            // endless curve without a rebuild. If the mapping method drops a field, she changes a
            // number and nothing happens — a silent authoring dead end, which is the defect class
            // the data-rot guards in WaveManager already exist for.
            var authored = new EndlessDef
            {
                BreacherFraction      = 0.11f,
                TwinDragonHpShare     = 0.42f,
                CatapultVolleySeconds = 3.5f,
                CatapultDamagePerHit  = 77f,
                MageCastSeconds       = 4.25f,
            };
            EndlessPressureTuning mapped = authored.ToPressureTuning();

            if (!Mathf.Approximately(mapped.BreacherFraction, 0.11f))
                failures.Add("EndlessDef.breacherFraction does not reach EndlessPressureTuning");
            if (!Mathf.Approximately(mapped.TwinDragonHpShare, 0.42f))
                failures.Add("EndlessDef.twinDragonHpShare does not reach EndlessPressureTuning");
            if (!Mathf.Approximately(mapped.CatapultVolleySeconds, 3.5f))
                failures.Add("EndlessDef.catapultVolleySeconds does not reach EndlessPressureTuning");
            if (!Mathf.Approximately(mapped.CatapultDamagePerHit, 77f))
                failures.Add("EndlessDef.catapultDamagePerHit does not reach EndlessPressureTuning");
            if (!Mathf.Approximately(mapped.MageCastSeconds, 4.25f))
                failures.Add("EndlessDef.mageCastSeconds does not reach EndlessPressureTuning");

            // An authored share must actually change the escalation, not just be carried around.
            int few  = EndlessPressure.BreacherCount(30, 20, new EndlessPressureTuning { BreacherFraction = 0.1f });
            int many = EndlessPressure.BreacherCount(30, 20, new EndlessPressureTuning { BreacherFraction = 0.9f });
            if (many <= few)
                failures.Add($"raising breacherFraction must raise the breacher count ({few} -> {many})");

            // A default EndlessDef (an older waves.json with no WO-1835 keys) must still be sane,
            // so the feature degrades to its first-pass defaults rather than to zeros.
            EndlessPressureTuning fallback = new EndlessDef().ToPressureTuning();
            if (fallback.CatapultDamagePerHit <= 0f || fallback.MageCastSeconds <= 0f
                || fallback.TwinDragonHpShare <= 0f || fallback.BreacherFraction <= 0f
                || fallback.CatapultVolleySeconds <= 0f)
                failures.Add("an endless block with no WO-1835 keys must fall back to live defaults, not zeros");

            log.AppendLine($"  [8] tuning: all five authored fields reach the runtime; fraction 0.1->{few} vs "
                         + $"0.9->{many}; missing-key fallback is non-zero");
        }

        // ── CASE 9 — the owner's parity ruling: "so should mine" ─────────────────────────
        private static void Case9_PlayerHealerParity(List<string> failures, StringBuilder log)
        {
            // Owner, 2026-09-17, asked whether her own healers should get the AoE heal + regen the
            // enemy mages were getting: "so should mine". This case pins that the player-side half
            // shipped and, specifically, that it covers the HERO — the existing single-target
            // TroopController.TryHealSquadmate iterates the troop roster only and has NEVER healed
            // her, which is the exact gap the ruling closes.
            string aura = ReadRepoFile("Assets/_Modules/Village/Troops/AllySupportAura.cs");
            if (aura == null)
            {
                failures.Add("could not read AllySupportAura.cs — the player-side healer parity is missing");
            }
            else
            {
                if (!aura.Contains("HeroHealth"))
                    failures.Add("AllySupportAura does not reference HeroHealth — the player's cleric would heal " +
                                 "troops only, which is the gap the owner's parity ruling closes");
                if (!aura.Contains("RegenTick"))
                    failures.Add("AllySupportAura does not call HeroHealth.RegenTick — the \"and regen\" half of " +
                                 "the ruling is missing for the hero");
                if (!aura.Contains("ActiveTroops"))
                    failures.Add("AllySupportAura does not read TroopController.ActiveTroops — it cannot find allies");
            }

            // The support-troop classification is a pure predicate so it can be asserted here.
            if (!AllySupportAuraDirector.IsSupportTroopId("troop-field-cleric"))
                failures.Add("the Field Cleric (troop-field-cleric) must classify as a support troop or it " +
                             "never receives the aura");
            if (AllySupportAuraDirector.IsSupportTroopId("troop-footman"))
                failures.Add("an ordinary footman must NOT classify as a support troop");
            if (AllySupportAuraDirector.IsSupportTroopId(null) ||
                AllySupportAuraDirector.IsSupportTroopId(string.Empty))
                failures.Add("a null/empty troop id must not classify as support (fail closed)");

            // The ENEMY mage must carry both halves too: an AoE heal cast AND a regen tick.
            string mage = ReadRepoFile("Assets/_Modules/Village/Enemies/EnemySupportMage.cs");
            if (mage == null)
            {
                failures.Add("could not read EnemySupportMage.cs");
            }
            else
            {
                if (!mage.Contains("CastAoeHeal"))
                    failures.Add("EnemySupportMage has no AoE heal cast");
                if (!mage.Contains("TickRegen"))
                    failures.Add("EnemySupportMage has no regen tick — \"AoE heal spells and regen\" is two asks, " +
                                 "and a single burst satisfies only one of them");
                if (!mage.Contains("CastRage"))
                    failures.Add("EnemySupportMage has no rage cast (owner ruling: \"rage spells\")");
                if (!mage.Contains("GetTelegraph"))
                    failures.Add("EnemySupportMage casts with no telegraph — WO-1835 requires a visible " +
                                 "wind-up consistent with the DEF-52 pattern");
            }

            // The heal and the rage alternate, so a felt-test sees both rather than one forever.
            if (!EnemySupportMage.CastIsHeal(0))
                failures.Add("the support mage's FIRST cast should be the heal (a pack arriving wounded is the common case)");
            if (EnemySupportMage.CastIsHeal(1))
                failures.Add("the support mage's second cast should be rage — the two must alternate");
            if (!EnemySupportMage.CastIsHeal(2))
                failures.Add("the support mage's cast alternation must repeat");

            log.AppendLine("  [9] parity: AllySupportAura heals troops AND the hero (burst + RegenTick); "
                         + "Field Cleric classifies as support, footman does not; enemy mage carries "
                         + "AoE heal + regen + rage, telegraphed, alternating");
        }

        /// <summary>
        /// Reads a repo-relative file for the source-text pins.
        /// <para>
        /// ⚠ REPO-RELATIVE, NEVER AN ABSOLUTE PATH. The repo root is machine-dependent — it is
        /// <c>C:\eoa</c> on one machine and <c>D:\eoa</c> on another (CLAUDE.md §0) — so a
        /// hardcoded drive letter here would red this suite on the other seat. Resolved from
        /// <c>Application.dataPath</c>, which Unity always gives correctly.
        /// </para>
        /// </summary>
        private static string ReadRepoFile(string assetsRelative)
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string full = Path.Combine(projectRoot, assetsRelative.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

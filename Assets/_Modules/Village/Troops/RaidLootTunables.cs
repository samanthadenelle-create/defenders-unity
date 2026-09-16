// =============================================================================
// RaidLootTunables - the ONE reader of the WO-1374 raid-reward knobs, and the
// owner of their clamps.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Spec: docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sections 1 and 12.7 (the
// north-star map). Rail: DeNelle.Core.Ops.RemoteTunables - reused end to end, not
// re-invented, exactly as OverTimeTuning and NightStoreAuraSelector reuse it.
//
// -----------------------------------------------------------------------------
// WHAT THE MAP ASKED FOR, IN ONE TABLE.
// -----------------------------------------------------------------------------
//   Perfect run (3 stars AND 100% razed), Camp I tier, before the camp's own
//   rewardMultiplier:      1,800 WOOD   1,100 IRON   2,200 GOLD
//   Crystals at that same perfect run:  20-30 (this build pays 26)
//
//   GOLD PER CAMP (map section 1, "sized to the army it replaces"):
//       Camp I       2,200   against a designed 1,650 army
//       Camp II      3,100   against 2,300
//       Camp III     4,500   against 3,300
//       Iron Bastion 6,500   against 4,800
//
//   Performance ladder, as a share of that:
//       failed attack   15-20%   (shipped at 18 - the middle of her band)
//       1 star             50%
//       2 stars            75%
//       3 stars           100%
//       3 stars + 100%    110%
//
// -----------------------------------------------------------------------------
// (!) ONE AMBIGUITY IN THE MAP, RESOLVED HERE IN THE OPEN RATHER THAN GUESSED.
// -----------------------------------------------------------------------------
// Section 1's table is headed "perfect 3 stars / 100%, Camp I" and gives 1,800
// wood - while the ladder in the same section lists "3 stars 100%" AND "3 stars +
// 100% destruction 110%". Read strictly, those two cannot both be true of 1,800:
// either 1,800 IS the 110% rung (making the base 1,636), or 1,800 is the 3-star
// rung and a total razing pays 1,980.
//
// THIS FILE TAKES THE SECOND READING: 1,800 is the BASE, the 3-star rung is 100%
// of it, and the top rung pays 110% = 1,980. Why: the map calls 110% the rung that
// pays ABOVE the others, and a base that no rung ever equals would be a number
// nobody could reason about while tuning. The cost of being wrong is one row on
// the Command Center - which is exactly why every one of these is a knob.
// FLAGGED TO THE OWNER rather than settled quietly; if she meant the first
// reading, set raid.lootWoodBase to 1636 and raid.lootIronBase to 1000.
//
// (S) "Now getting better at raiding has an economic payoff." That sentence is
// the whole reason the ladder is a LADDER and not a linear ramp off destruction
// percent: a linear ramp pays a sloppy 80% clear almost as well as a perfect one,
// so mastery buys nothing and the player optimises for speed instead of skill.
//
// -----------------------------------------------------------------------------
// (!) GOLD IS HERE NOW. THE OLD "GOLD IS BLOCKED" FENCE IS DELETED, NOT MOVED.
// -----------------------------------------------------------------------------
// This header used to carry a paragraph saying gold was BLOCKED because WO-1372
// (troops cost TIME) and the map (troops cost 1,650 GOLD) contradicted each
// other and the owner had not picked. SHE HAS: commit 281902df0 closed the fork.
// Troops COST GOLD, they ALSO take time, and a SECOND gold spend hires
// mercenaries to skip the clock. The paragraph is removed rather than annotated,
// because a stale fence left in place makes the next seat re-open a settled
// question (CLAUDE.md section 15).
//
// (!) GOLD DOES NOT RIDE THE CAMP rewardMultiplier, AND THAT IS DELIBERATE.
// The map publishes a DESIGNED gold target per camp rather than one base times a
// difficulty multiplier. x1.5 of 2,200 is 3,300 - her Camp II number is 3,100;
// x2.2 is 4,840 - her Camp III number is 4,500. The escalation therefore lives in
// the knob VALUES (CoinsBaseFor below), and multiplying on top would make all
// four published numbers unpayable. Wood and iron keep the multiplier exactly as
// they had it, so the selection card's "x1.5 Loot" stays honest for them.
//
// (!) CRYSTALS ARE HERE NOW TOO, AND THEY GO DOWN.
// They were two serialized fields on RaidScoring paying base 25 + 3x10 = 55 at a
// perfect clear. The map cuts that to 20-30 and gives the reason: "Crystals are
// timer compression. If raids dump huge amounts of crystals, you accidentally
// accelerate the already-too-short progression curve." This build pays 20 + 3x2
// = 26, and crystals are EXCLUDED from the camp multiplier for the same reason -
// a harder camp should pay more gold, wood and iron, not more instant-finish.
//
//   FOOD is still NOT here. It keeps its serialized fields and its own scaling.
//     The map's food target (3,000, up from 120) is a real gap and it is called
//     out in this lane's report rather than changed in passing.
//
// -----------------------------------------------------------------------------
// EVERY VALUE IS CLAMPED HERE AND NOWHERE ELSE (the OverTimeTuning contract).
// A knob is an operator surface, so it can receive a typo, a paste, or a number
// from a future build. The clamp lives at the consumer so there is exactly one
// answer to "what does raid.lootWoodBase = -5 do", and it is LOUD.
//
// ASCII only. FlowTrace tag "Raid". Never stripped (CLAUDE.md section 12).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Village
{
    /// <summary>
    /// Resolves and clamps the raid-reward knobs (wood/iron bases, the five ladder
    /// rungs, the four per-camp GOLD bases and the two crystal knobs). Pure static; every
    /// method answers the shipping default when no row, no network and no parse
    /// (RemoteTunables' standing invariant).
    /// </summary>
    public static class RaidLootTunables
    {
        /// <summary>Upper bound on either resource base. A million is far past any sane
        /// reward and still safely inside int arithmetic once the camp multiplier and the
        /// 110% rung have both been applied.</summary>
        public const int MaxBase = 1000000;

        /// <summary>Upper bound on a ladder rung, as a percent. 1000 = ten times the base,
        /// which is generous enough for an experiment and short of an overflow.</summary>
        public const int MaxLadderPct = 1000;

        /// <summary>Wood at a perfect run, clamped to 0..<see cref="MaxBase"/>.</summary>
        public static int WoodBase => ClampBase(RemoteTunables.KeyRaidLootWoodBase);

        /// <summary>Iron at a perfect run, clamped to 0..<see cref="MaxBase"/>.</summary>
        public static int IronBase => ClampBase(RemoteTunables.KeyRaidLootIronBase);

        /// <summary>Percent paid by a failed attack, clamped to 0..<see cref="MaxLadderPct"/>.</summary>
        public static int FailPct => ClampPct(RemoteTunables.KeyRaidLootFailPct);

        /// <summary>Percent paid at 1 star.</summary>
        public static int OneStarPct => ClampPct(RemoteTunables.KeyRaidLootOneStarPct);

        /// <summary>Percent paid at 2 stars.</summary>
        public static int TwoStarPct => ClampPct(RemoteTunables.KeyRaidLootTwoStarPct);

        /// <summary>Percent paid at 3 stars.</summary>
        public static int ThreeStarPct => ClampPct(RemoteTunables.KeyRaidLootThreeStarPct);

        /// <summary>Percent paid at 3 stars with 100% destruction.</summary>
        public static int PerfectPct => ClampPct(RemoteTunables.KeyRaidLootPerfectPct);

        /// <summary>Gold at a perfect Camp I run, clamped to 0..<see cref="MaxBase"/>.</summary>
        public static int CoinsBaseCamp1 => ClampBase(RemoteTunables.KeyRaidLootCoinsBaseCamp1);

        /// <summary>Gold at a perfect Camp II run.</summary>
        public static int CoinsBaseCamp2 => ClampBase(RemoteTunables.KeyRaidLootCoinsBaseCamp2);

        /// <summary>Gold at a perfect Camp III run.</summary>
        public static int CoinsBaseCamp3 => ClampBase(RemoteTunables.KeyRaidLootCoinsBaseCamp3);

        /// <summary>Gold at a perfect Iron Bastion run.</summary>
        public static int CoinsBaseBastion => ClampBase(RemoteTunables.KeyRaidLootCoinsBaseBastion);

        /// <summary>Crystals at 100% destruction, before the per-star bonus.</summary>
        public static int CrystalsBase => ClampBase(RemoteTunables.KeyRaidLootCrystalsBase);

        /// <summary>Extra crystals per earned star.</summary>
        public static int CrystalsPerStar => ClampBase(RemoteTunables.KeyRaidLootCrystalsPerStar);

        // =====================================================================
        //  THE PER-CAMP GOLD TABLE.
        // =====================================================================

        /// <summary>
        /// The raid config ids this table knows, in map order. These are LIVE SAVE-ADJACENT
        /// IDS read out of <c>scene-configs.json</c> (verified 2026-09-04: the three enemy
        /// raid configs on disk are exactly <c>raider_camp_small</c> / <c>fortified_garrison</c>
        /// / <c>mage_enclave</c>). They are matched, never renamed.
        ///
        /// <para><c>iron_bastion</c> landed in scene-configs.json on 2026-09-04 (scene
        /// <c>RaidBase_IronBastion</c>). Its <c>rewardMultiplier</c> - whatever that file
        /// currently authors, READ IT THERE - is deliberately NOT applied to gold, because the
        /// map publishes a DESIGNED gold target per camp sized against that camp's expected army
        /// cost; no single base times a multiplier pays all four published numbers.
        /// ⚠ This paragraph restated a reward multiplier as a LITERAL until WO-1763, by which
        /// date the JSON had moved off it - and the fix is to point at the file, not to swap in
        /// the new number, which would simply re-arm the same trap.</para>
        /// </summary>
        public const string CampIdCamp1 = "raider_camp_small";
        /// <summary>Camp II's live config id.</summary>
        public const string CampIdCamp2 = "fortified_garrison";
        /// <summary>Camp III's live config id.</summary>
        public const string CampIdCamp3 = "mage_enclave";
        /// <summary>The Iron Bastion's live config id. The scene-config row EXISTS and the owner
        /// has played the raid (WO-1763, 2026-09-16) - this line said "no scene-config row yet"
        /// until then.</summary>
        public const string CampIdBastion = "iron_bastion";

        /// <summary>
        /// The GOLD a perfect run pays on the camp identified by <paramref name="configId"/>.
        ///
        /// <para>An id this table does not know falls back to the CAMP I knob and says so
        /// ONCE - loudly, by id. Falling back to zero would silently delete the whole
        /// gold arrow for that camp, which is exactly the class of invisible failure
        /// CLAUDE.md section 12 forbids; falling back to the top rung would overpay it.
        /// Camp I is the honest floor.</para>
        ///
        /// <para>Matching is ordinal-case-insensitive and trimmed, because the id arrives
        /// from a JSON catalog an operator authors by hand.</para>
        /// </summary>
        public static int CoinsBaseFor(string configId)
        {
            string id = string.IsNullOrEmpty(configId) ? "" : configId.Trim();

            if (string.Equals(id, CampIdCamp1, System.StringComparison.OrdinalIgnoreCase))
                return CoinsBaseCamp1;
            if (string.Equals(id, CampIdCamp2, System.StringComparison.OrdinalIgnoreCase))
                return CoinsBaseCamp2;
            if (string.Equals(id, CampIdCamp3, System.StringComparison.OrdinalIgnoreCase))
                return CoinsBaseCamp3;
            if (string.Equals(id, CampIdBastion, System.StringComparison.OrdinalIgnoreCase))
                return CoinsBaseBastion;

            FlowTrace.Once("Raid", "raidloot-coins-unknown-camp-" + id,
                "raid config id '" + (id.Length == 0 ? "(none)" : id) + "' is not in the per-camp " +
                "GOLD table (" + CampIdCamp1 + " / " + CampIdCamp2 + " / " + CampIdCamp3 + " / " +
                CampIdBastion + "). Paying the CAMP I base (" + CoinsBaseCamp1 + ") rather than 0, " +
                "so the gold arrow is never silently deleted for this camp - but if this camp is " +
                "meant to pay a different tier, add its id here and give it a knob.");
            return CoinsBaseCamp1;
        }

        // =====================================================================
        //  THE LADDER - pure, so an oracle asserts the whole table with nothing
        //  loaded: no scene, no save, no network, no PlayerPrefs.
        // =====================================================================

        /// <summary>
        /// Destruction at or above which a 3-star clear counts as PERFECT and earns the
        /// top rung. Not 1.0f exactly: <c>DestructionPct</c> is a float sum over a live
        /// structure census, so a genuinely total razing can land at 0.9999 and a strict
        /// equality would quietly never pay the rung the map wrote.
        /// </summary>
        public const float PerfectDestructionPct = 0.999f;

        /// <summary>
        /// The map's performance ladder as a 0..N fraction: which share of the base this
        /// result earns. <paramref name="stars"/> is clamped to 0..3;
        /// <paramref name="destructionPct"/> is only consulted at 3 stars, to separate a
        /// win from a total razing.
        ///
        /// <para>⛔ A FAILED ATTACK IS "0 STARS", AND IT PAYS. That is the map's ruling,
        /// not an accident of the arithmetic: <i>"A failed attack still pays 15-20%. That
        /// is deliberate - it keeps a loss from being a dead end."</i> Do not "fix" the
        /// zero-star rung to zero.</para>
        ///
        /// <para>Ladder rungs are supplied by the caller so this stays pure. The
        /// instance-side <see cref="Fraction(int,float)"/> reads them off the knobs.</para>
        /// </summary>
        public static float FractionFrom(int stars, float destructionPct,
                                         int failPct, int oneStarPct, int twoStarPct,
                                         int threeStarPct, int perfectPct)
        {
            int s = Mathf.Clamp(stars, 0, 3);
            int pct;
            switch (s)
            {
                case 0: pct = failPct; break;
                case 1: pct = oneStarPct; break;
                case 2: pct = twoStarPct; break;
                default:
                    pct = Mathf.Clamp01(destructionPct) >= PerfectDestructionPct
                        ? perfectPct
                        : threeStarPct;
                    break;
            }
            if (pct < 0) pct = 0;
            return pct / 100f;
        }

        /// <summary>The live ladder fraction for a result, reading the knobs.</summary>
        public static float Fraction(int stars, float destructionPct)
            => FractionFrom(stars, destructionPct,
                            FailPct, OneStarPct, TwoStarPct, ThreeStarPct, PerfectPct);

        // =====================================================================
        //  Clamps. Loud, once per offending value, never silent.
        // =====================================================================

        private static int ClampBase(string key) => ClampAndReport(key, 0, MaxBase);

        private static int ClampPct(string key) => ClampAndReport(key, 0, MaxLadderPct);

        private static int ClampAndReport(string key, int min, int max)
        {
            int raw = RemoteTunables.Int(key);
            int clamped = Mathf.Clamp(raw, min, max);
            if (clamped != raw)
            {
                // Once per key per process: a knob is read on every raid settle, and a
                // per-settle Warn would bury the raid trace it is meant to annotate.
                FlowTrace.Once("Raid", "raidloot-clamp-" + key,
                    "raid reward knob '" + key + "' resolved to " + raw + ", outside " + min +
                    ".." + max + " - CLAMPED to " + clamped + ". The payout below uses the " +
                    "clamped value, not the authored one.");
            }
            return clamped;
        }
    }
}

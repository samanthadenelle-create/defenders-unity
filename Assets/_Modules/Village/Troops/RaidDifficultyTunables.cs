// =============================================================================
// RaidDifficultyTunables - the ONE reader of the WO-1763 per-camp raid-DIFFICULTY
// knobs, and the owner of their clamps.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Rail: DeNelle.Core.Ops.RemoteTunables - reused end to end, not re-invented,
// exactly as RaidLootTunables beside this file reuses it. This file is modelled on
// that one deliberately: same ClampAndReport shape, the same per-camp id constants
// (reused, never re-declared), and the same unknown-id fallback that says so ONCE
// and by id.
//
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (the owner-visible symptom, then the seam).
// -----------------------------------------------------------------------------
// The owner nearly 3-starred the Iron Bastion with a LEVEL-4 hero, and believed
// raid difficulty was already tunable from the database. It was not: every raid.*
// knob on the rail was loot, honor, heartfire, staging or rough-stone. The only
// way to make a camp harder was to edit a canonical JSON and ship a build - a
// ~30-minute rebuild per opinion about feel, on the number that decides whether
// the main loop is a fight or a formality.
//
// ⛔ AND THE BASTION IS A CLONE. Read the four enemy rows of
// Assets/Resources/Data/Canonical/scene-configs.json: the Bastion's garrison block
// matches Camp III's on every difficulty field, so the map's fourth and EVERGREEN
// target fights exactly like its third. This file is how that stops being true
// without touching the authored JSON and without a build.
//
// ⛔ NO NUMBER FROM THAT JSON IS RESTATED ANYWHERE IN THIS FILE. The authored
// values arrive as PARAMETERS. A baseline copied into a comment is the duplicated
// state CLAUDE.md sections 2 / 5 / 8 / 16 each record a scar from - and this very
// lane had to delete a "rewardMultiplier of 2.8" sentence from two files whose
// JSON had moved on beneath it.
//
// -----------------------------------------------------------------------------
// THE TWO AXES, AND THE ONE THAT CANNOT BE READ TWO WAYS.
// -----------------------------------------------------------------------------
//   difficultyMultPct<Camp>  MULTIPLICATIVE on the authored difficultyMultiplier.
//       effective = authored * pct/100. At 100 this file SHORT-CIRCUITS and hands
//       back the authored float itself - not authored*100/100 - so an empty table
//       is today's behaviour BIT FOR BIT, with no float round-trip to argue about.
//
//   levelOffset<Camp>        REPLACES the authored levelOffset. NOT a delta.
//       The sentinel (RemoteTunables.RaidLevelOffsetUseJsonSentinel) means "use
//       the authored value", and deleting the row does the same thing.
//
// ⛔ REPLACE, NOT ADD, AND THAT IS A RULING. The owner's 2026-09-16 seed for the
// Bastion offset was given against REPLACE semantics; under delta semantics that
// same number would mean a different, larger offset. A levelOffsetDelta<Camp>
// shape (default 0, no sentinel needed) was considered and REJECTED for exactly
// that reason. A ruling that can be read two ways is the failure this paragraph
// exists to prevent.
//
// -----------------------------------------------------------------------------
// WHAT THE EFFECTIVE VALUES REACH.
// -----------------------------------------------------------------------------
// The multiplier is folded by RaidGarrisonSpawner.FoldDifficulty into HP AND
// contact damage - the ONE place it touches combat - and the offset feeds the
// spawner's max(baseEnemyLevel, playerLevel + offset), so baseEnemyLevel still
// FLOORS a low-level hero's raid and this only ever raises the ceiling. Neither
// axis changes how MANY defenders there are (that is liveCombatantCap and
// eliteCount, both deliberately out of scope).
//
// -----------------------------------------------------------------------------
// EVERY VALUE IS CLAMPED HERE AND NOWHERE ELSE (the RaidLootTunables contract).
// A knob is an operator surface, so it can receive a typo, a paste, or a number
// from a future build. The clamp lives at the consumer so there is exactly one
// answer to "what does raid.difficultyMultPctBastion = 40000 do", and it is LOUD.
//
// ⛔ AND IT NEVER FALLS BACK TO ZERO. FoldDifficulty is a NO-OP at <=0, so a
// silent zero would read as "difficulty applied" while applying nothing - the
// exact class of invisible failure CLAUDE.md section 12 forbids. An unknown camp
// id resolves to the values it was PASSED, and says so once, by id.
//
// Pure and static: no scene, no save, no network, no PlayerPrefs. An oracle
// asserts the whole table with nothing loaded.
//
// ASCII only. FlowTrace tag "Raid". Never stripped (CLAUDE.md section 12).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Village
{
    /// <summary>
    /// Resolves and clamps the per-camp raid DIFFICULTY knobs (a percent on the authored
    /// difficulty multiplier, and a replacement for the authored level offset). Pure
    /// static; every method answers the AUTHORED values when no row, no network and no
    /// parse (RemoteTunables' standing invariant).
    /// </summary>
    public static class RaidDifficultyTunables
    {
        /// <summary>Lowest percent an operator may drive the authored multiplier to (a quarter).</summary>
        public const int MinMultiplierPct = 25;

        /// <summary>Highest percent an operator may drive the authored multiplier to (four times).</summary>
        public const int MaxMultiplierPct = 400;

        /// <summary>Lowest REAL level offset. Below this, defenders drop under the camp's own floor anyway.</summary>
        public const int MinLevelOffset = -5;

        /// <summary>Highest REAL level offset. Twenty levels is already a wall at any hero level.</summary>
        public const int MaxLevelOffset = 20;

        /// <summary>The percent at which this file hands back the authored multiplier untouched.</summary>
        public const int IdentityMultiplierPct = RemoteTunables.RaidDifficultyMultPctIdentity;

        /// <summary>The "use the authored levelOffset" sentinel, aliased so there is one literal.</summary>
        public const int UseAuthoredOffsetSentinel = RemoteTunables.RaidLevelOffsetUseJsonSentinel;

        /// <summary>Source label: the value came from the authored scene config, not a row.</summary>
        public const string SourceJson = "json";

        /// <summary>Source label: a row (or a local override) is in play for this axis.</summary>
        public const string SourceRemote = "remote";

        /// <summary>
        /// The EFFECTIVE difficulty for one camp, plus enough provenance for the RAID START
        /// line to say where each half came from.
        ///
        /// <para>⛔ <see cref="MultiplierSource"/> / <see cref="OffsetSource"/> are a HUMAN HINT
        /// on that line, never the authority. <c>RemoteTunables.Int</c> already emits the
        /// authoritative <c>KNOB &lt;key&gt; = &lt;v&gt; provenance=&lt;default|remote|cache|local&gt;</c>
        /// line once per key, and THAT is the proof of where a value came from. This file does
        /// not re-implement provenance detection - it reports whether the resolved value
        /// differs from the registry default, which is a different and weaker question.</para>
        /// </summary>
        public struct Resolved
        {
            /// <summary>What RaidGarrisonSpawner.FoldDifficulty should scale HP and contact damage by.</summary>
            public float EffectiveMultiplier;

            /// <summary>What the spawner should add to the player level before the baseEnemyLevel floor.</summary>
            public int EffectiveLevelOffset;

            /// <summary>The post-clamp percent actually applied. 100 means the authored value stands.</summary>
            public int MultiplierPct;

            /// <summary>"json" or "remote" - where <see cref="EffectiveMultiplier"/> came from.</summary>
            public string MultiplierSource;

            /// <summary>"json" or "remote" - where <see cref="EffectiveLevelOffset"/> came from.</summary>
            public string OffsetSource;

            /// <summary>True when NEITHER axis is overridden, i.e. this raid is today's raid.</summary>
            public bool IsIdentity
                => MultiplierSource == SourceJson && OffsetSource == SourceJson;
        }

        // =====================================================================
        //  THE PER-CAMP KNOB TABLE. Ids are REUSED from RaidLootTunables - they
        //  are live, save-adjacent data and there is exactly one copy of each.
        // =====================================================================

        /// <summary>
        /// Resolve the EFFECTIVE difficulty for one camp. Pure: no scene, no save, no network.
        ///
        /// <para>An id this table does not know resolves to the values it was PASSED, unchanged,
        /// and says so ONCE - loudly, by id (the <c>CoinsBaseFor</c> precedent). It never falls
        /// back to another camp's knob, which would make a raid secretly wear a different camp's
        /// tuning, and never to zero, which <c>FoldDifficulty</c> treats as a no-op and would
        /// therefore read as "difficulty applied" while applying nothing.</para>
        ///
        /// <para>Matching is ordinal-case-insensitive and trimmed, because the id arrives from a
        /// JSON catalog an operator authors by hand.</para>
        /// </summary>
        /// <param name="configId">The stored scene-config id the raid base carries.</param>
        /// <param name="authoredMultiplier">The camp's authored <c>difficultyMultiplier</c>.</param>
        /// <param name="authoredLevelOffset">The camp's authored <c>levelOffset</c>.</param>
        public static Resolved Resolve(string configId, float authoredMultiplier, int authoredLevelOffset)
        {
            string id = string.IsNullOrEmpty(configId) ? "" : configId.Trim();

            string multKey = null;
            string offsetKey = null;

            if (string.Equals(id, RaidLootTunables.CampIdCamp1, System.StringComparison.OrdinalIgnoreCase))
            {
                multKey = RemoteTunables.KeyRaidDifficultyMultPctCamp1;
                offsetKey = RemoteTunables.KeyRaidLevelOffsetCamp1;
            }
            else if (string.Equals(id, RaidLootTunables.CampIdCamp2, System.StringComparison.OrdinalIgnoreCase))
            {
                multKey = RemoteTunables.KeyRaidDifficultyMultPctCamp2;
                offsetKey = RemoteTunables.KeyRaidLevelOffsetCamp2;
            }
            else if (string.Equals(id, RaidLootTunables.CampIdCamp3, System.StringComparison.OrdinalIgnoreCase))
            {
                multKey = RemoteTunables.KeyRaidDifficultyMultPctCamp3;
                offsetKey = RemoteTunables.KeyRaidLevelOffsetCamp3;
            }
            else if (string.Equals(id, RaidLootTunables.CampIdBastion, System.StringComparison.OrdinalIgnoreCase))
            {
                multKey = RemoteTunables.KeyRaidDifficultyMultPctBastion;
                offsetKey = RemoteTunables.KeyRaidLevelOffsetBastion;
            }

            if (multKey == null)
            {
                FlowTrace.Once("Raid", "raiddiff-unknown-camp-" + id,
                    "raid config id '" + (id.Length == 0 ? "(none)" : id) + "' is not in the per-camp " +
                    "DIFFICULTY table (" + RaidLootTunables.CampIdCamp1 + " / " +
                    RaidLootTunables.CampIdCamp2 + " / " + RaidLootTunables.CampIdCamp3 + " / " +
                    RaidLootTunables.CampIdBastion + "). Using the AUTHORED values from " +
                    "scene-configs.json unchanged - never another camp's knob, and never 0, which " +
                    "FoldDifficulty treats as a no-op and would read as 'difficulty applied' while " +
                    "applying nothing. If this camp is meant to be tunable, add its id here and " +
                    "give it a pair of knobs.");
                return ResolveFrom(authoredMultiplier, authoredLevelOffset,
                                   IdentityMultiplierPct, UseAuthoredOffsetSentinel);
            }

            return ResolveFrom(authoredMultiplier, authoredLevelOffset,
                               RemoteTunables.Int(multKey), RemoteTunables.Int(offsetKey),
                               multKey, offsetKey);
        }

        /// <summary>
        /// The pure arithmetic, with the knob values SUPPLIED - the
        /// <see cref="RaidLootTunables.FractionFrom(int,float,int,int,int,int,int)"/> precedent, so
        /// an oracle can drive every branch of the table with nothing loaded and no row written.
        ///
        /// <para>Clamping happens HERE, and the caller receives the CLAMPED result: there is
        /// exactly one answer to "what does a bad value do". The key names are optional and are
        /// used only to make a clamp report name the offending row.</para>
        /// </summary>
        public static Resolved ResolveFrom(float authoredMultiplier, int authoredLevelOffset,
                                           int rawMultiplierPct, int rawLevelOffset,
                                           string multiplierKeyForLog = null,
                                           string offsetKeyForLog = null)
        {
            // The spawner's own >0 guard, restated at the boundary rather than trusted. An
            // authored multiplier of 0 or a negative would otherwise survive the percent and
            // land on FoldDifficulty's no-op, which looks identical to "the knob did nothing".
            float baseMultiplier = authoredMultiplier;
            if (!(baseMultiplier > 0f))
            {
                FlowTrace.Once("Raid", "raiddiff-authored-nonpositive",
                    "a camp's authored difficultyMultiplier resolved to " + authoredMultiplier +
                    ", which is not positive. Treating it as 1 (the spawner's own long-standing " +
                    "guard) so a percent knob has something real to scale - a 0 here would reach " +
                    "FoldDifficulty, which no-ops at <=0, and the raid would read as 'difficulty " +
                    "applied' while nothing was applied.");
                baseMultiplier = 1f;
            }

            int pct = ClampAndReport(multiplierKeyForLog, rawMultiplierPct,
                                     MinMultiplierPct, MaxMultiplierPct, "percent");

            var r = new Resolved { MultiplierPct = pct };

            if (pct == IdentityMultiplierPct)
            {
                // ⭐ THE BIT-IDENTITY BRANCH. Handing back the authored float itself rather than
                // authored * 100 / 100f is what makes "no row => today's raid, byte for byte" a
                // provable statement instead of a float-rounding argument.
                r.EffectiveMultiplier = baseMultiplier;
                r.MultiplierSource = SourceJson;
            }
            else
            {
                r.EffectiveMultiplier = baseMultiplier * (pct / 100f);
                r.MultiplierSource = SourceRemote;
            }

            if (rawLevelOffset == UseAuthoredOffsetSentinel)
            {
                r.EffectiveLevelOffset = authoredLevelOffset;
                r.OffsetSource = SourceJson;
            }
            else
            {
                r.EffectiveLevelOffset = ClampAndReport(offsetKeyForLog, rawLevelOffset,
                                                        MinLevelOffset, MaxLevelOffset, "level offset");
                r.OffsetSource = SourceRemote;
            }

            return r;
        }

        // =====================================================================
        //  Clamps. Loud, once per key per process, never silent.
        // =====================================================================

        private static int ClampAndReport(string key, int raw, int min, int max, string what)
        {
            int clamped = Mathf.Clamp(raw, min, max);
            if (clamped == raw) return clamped;

            // Once per key per process: these are read on every raid entry, and a per-entry
            // Warn would bury the RAID START line it is meant to annotate.
            FlowTrace.Once("Raid", "raiddiff-clamp-" + (key ?? what) + "-" + raw,
                "raid difficulty " + what + " '" + (key ?? "(supplied directly)") + "' resolved to " +
                raw + ", outside " + min + ".." + max + " - CLAMPED to " + clamped + ". The raid " +
                "below uses the CLAMPED value, not the authored one. " +
                (what == "level offset"
                    ? "Note that " + UseAuthoredOffsetSentinel + " is the sentinel for 'use the " +
                      "authored offset' and is NOT clamped; any other out-of-range value is."
                    : "100 is identity - the authored multiplier unchanged."));
            return clamped;
        }
    }
}

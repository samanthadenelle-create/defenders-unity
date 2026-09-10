// =============================================================================
// RaidRoughStoneDropRegression [raid-rough-stone]  --  markers
//   RAID_ROUGH_STONE_OK / RAID_ROUGH_STONE_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Edit mode, no PlayMode. Mostly BEHAVIOURAL
// (it calls the real DeNelle.Village / DeNelle.Dungeons statics and reads the real
// authored JSON off disk) plus source-lint pins read with comments AND string
// literals stripped, so a symbol named only in a comment or a log line can never
// satisfy one.
// Registered in DataRegression.RunAll. NEVER throws.
//
// -----------------------------------------------------------------------------
// THE RULING THIS SUITE EXISTS TO HOLD  (owner, 2026-09-09, verbatim)
// -----------------------------------------------------------------------------
//   "there is only one stone type till it gets to jeweler, and then its RND. So
//    only top two tiers of raids can drop stone and no more than 1 per day. 5%
//    drop rate in dungeons not included the starter dungeons"
//
// Four separate claims, and each one is a case below:
//   A  ONE STONE TYPE. ing_rough_stone and nothing else; the grade is decided at
//      the bench, not at the drop. Nothing here invents a second material.
//   B  ONLY THE TOP TWO RAID TIERS. The camps sit on an ordered I..IV ladder
//      (RaidLootTunables.CampIdCamp1..CampIdBastion) and the minimum tier is a
//      ROW (raid.roughStoneMinTier, default 3 = the lower of the top two).
//   C  NO MORE THAN ONE PER DAY. A GLOBAL per-UTC-day count across every camp,
//      not a per-camp stamp, on a row (raid.roughStonePerDayCap, default 1).
//   D  5% IN DUNGEONS, STARTERS EXCLUDED. dungeon.roughStoneDropPct default 5,
//      and layout tier 1 pays nothing at all.
//
// -----------------------------------------------------------------------------
// WHY IT ALSO PINS THE HONOR CACHE  (WO-1373 lane RAID-3 item 2)
// -----------------------------------------------------------------------------
// The same lane hoisted RaidScoring's three honor-milestone tunable reads out of
// the per-frame path. That is a PERFORMANCE property with no player-visible
// symptom until a device drops frames, which is exactly the class of change that
// silently regresses the first time someone "simplifies" a getter. The pin is a
// stripped-source lint that PresentationStars' getter names none of the three
// Honor* properties, plus a behavioural check that the hoisted seven-argument
// overload and the pinned four-argument one agree.
//
// -----------------------------------------------------------------------------
// WHAT THIS SUITE DELIBERATELY DOES NOT DO
// -----------------------------------------------------------------------------
//  * It does NOT re-point DungeonGemExclusivityRegression. That suite scans only
//    purchasable/grantable JSON CATALOGS (packs, cosmetics, stake-rewards, quests,
//    daily-quests) and vendor shelves, so a code-path raid drop does not trip it
//    at all - verified by reading CheckCatalogExclusivity at source 2026-09-09.
//    What it DOES do is restate the surviving half of that invariant here: the
//    rough stone must still be on the DungeonExclusiveItems fence, because "a raid
//    may drop it" and "it may never be BOUGHT" are different claims and only the
//    first one moved.
//  * It does NOT assert a star axis on the raid drop. WO-1373 section 5.2 asks
//    whether the drop should scale by stars and the owner has not answered; an
//    oracle that pinned an unruled axis would make an implementer's guess load
//    bearing.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.Ops;
using DeNelle.Core.Catalog;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class RaidRoughStoneDropRegression
    {
        // Relative to Application.dataPath.
        private const string ScoringRel = "_Modules/Village/Troops/RaidScoring.cs";
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string DungeonRel = "_Modules/Dungeons/DungeonController.cs";
        private const string PayoutRel = "_Modules/Core/Catalog/DungeonRunPayout.cs";
        private const string LayoutsRel = "Resources/Data/Canonical/dungeon-layouts";
        private const string SceneConfigsRel = "Resources/Data/Canonical/scene-configs.json";

        // The ruled numbers as LITERALS. Never read from the thing under test - a threshold
        // expressed relative to a value that can move underneath it measures nothing. Same
        // house rule RemoteTunablesDefaultsRegression states for its own table.
        private const int RuledMinTier = 3;
        private const int RuledPerDayCap = 1;
        private const int RuledDungeonPct = 5;

        /// <summary>The rate the build shipped BEFORE the ruling, kept so the failure text can
        /// say what a red means: 15% is the old compiled const, not a random wrong number.</summary>
        private const int PreRulingDungeonPct = 15;

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            // Snapshot the day ledger, so a developer machine is byte-identical afterwards
            // however the run exits. The ledger is PlayerPrefs, and this suite writes it.
            string keptDay = PlayerPrefs.GetString("dotr-raid-stoneday", null);
            int keptCount = PlayerPrefs.GetInt("dotr-raid-stonecount", 0);

            // A LOCAL override on any of the three knobs is, correctly, not the shipping
            // default. Clear all three for the whole run and restore them in the finally, and
            // say so loudly - an override left armed on a build machine is worth knowing about.
            var overrides = new List<KeyValuePair<string, string>>();
            SuspendOverride(RemoteTunables.KeyRaidRoughStoneMinTier, overrides);
            SuspendOverride(RemoteTunables.KeyRaidRoughStonePerDayCap, overrides);
            SuspendOverride(RemoteTunables.KeyDungeonRoughStoneDropPct, overrides);

            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "raid-rough-stone: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                foreach (var kv in overrides) PlayerPrefs.SetString(kv.Key, kv.Value);
                try
                {
                    if (string.IsNullOrEmpty(keptDay))
                    {
                        PlayerPrefs.DeleteKey("dotr-raid-stoneday");
                        PlayerPrefs.DeleteKey("dotr-raid-stonecount");
                    }
                    else
                    {
                        PlayerPrefs.SetString("dotr-raid-stoneday", keptDay);
                        PlayerPrefs.SetInt("dotr-raid-stonecount", keptCount);
                    }
                    PlayerPrefs.Save();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("raid-rough-stone: day-ledger restore failed: " + ex.Message);
                }
            }
        }

        /// <summary>Clear a local PlayerPrefs override for the duration of the run, recording it
        /// so the finally block puts it back exactly as it was.</summary>
        private static void SuspendOverride(string key, List<KeyValuePair<string, string>> kept)
        {
            string pref = RemoteTunables.LocalPrefix + key;
            if (!PlayerPrefs.HasKey(pref)) return;
            kept.Add(new KeyValuePair<string, string>(pref, PlayerPrefs.GetString(pref, "")));
            PlayerPrefs.DeleteKey(pref);
            Debug.LogWarning("raid-rough-stone: a LOCAL override on '" + pref + "' was armed on this " +
                             "machine and has been cleared for the run, then restored.");
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_ROUGH_STONE_OK - " + reason);
            else Debug.LogError("RAID_ROUGH_STONE_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();

            string scoring = ReadCode(ScoringRel, fails);
            string victory = ReadCode(VictoryRel, fails);
            string dungeon = ReadCode(DungeonRel, fails);

            CheckOneStoneType(fails);
            CheckTierLadder(fails);
            CheckRuleTable(fails);
            CheckShippingDefaults(fails);
            CheckDayLedger(fails);
            CheckSingleRaidGrantSite(victory, fails);
            CheckOneGrantAuthority(dungeon, fails);
            CheckStarterDungeonsExcluded(dungeon, fails);
            CheckHonorCacheIsNotPerFrame(scoring, fails);

            if (fails.Count == 0)
            {
                Debug.Log("RAID_ROUGH_STONE_OK");
                reason = "RAID ROUGH STONE OK -- one stone type ('" + DungeonExclusiveItems.RoughStoneId +
                         "', still on the DungeonExclusiveItems purchase fence), the camp ladder maps " +
                         "the four live scene-config ids to tiers 1..4 with an unknown id answering 0, " +
                         "only tiers >= " + RaidScoring.RoughStoneMinTier + " (" +
                         RemoteTunables.KeyRaidRoughStoneMinTier + ") may drop and only " +
                         RaidScoring.RoughStonePerDayCap + " per UTC day globally (" +
                         RemoteTunables.KeyRaidRoughStonePerDayCap + "), the day ledger round-trips " +
                         "and self-expires, the raid REACHES the one shared grant authority " +
                         "(DungeonController.BankRoughStone, the single writer of LastPolishScore) " +
                         "through the DeNelle.Core seam rather than banking a second time, gated by " +
                         "the pure rule and stamped only after that authority reported the larder " +
                         "took the stone, starter dungeons " +
                         "(layout tier 1) pay nothing while the post-first roll sits at " +
                         DeNelle.Dungeons.DungeonController.PostFirstRoughStoneDropPct + "% (" +
                         RemoteTunables.KeyDungeonRoughStoneDropPct + "), and PresentationStars reads " +
                         "the cached honor milestones rather than three tunable lookups per frame";
                return true;
            }

            reason = "raid-rough-stone (" + fails.Count + "): " + string.Join(" | ", fails.ToArray());
            Debug.LogError("RAID_ROUGH_STONE_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  A - ONE STONE TYPE, and it is still un-purchasable.
        // =====================================================================

        private static void CheckOneStoneType(List<string> fails)
        {
            if (!string.Equals(RaidScoring.RoughStoneItemId, DungeonExclusiveItems.RoughStoneId,
                               StringComparison.Ordinal))
            {
                fails.Add("[one-stone] RaidScoring.RoughStoneItemId is '" + RaidScoring.RoughStoneItemId +
                          "' but the catalog's rough stone is '" + DungeonExclusiveItems.RoughStoneId +
                          "'. The owner ruled ONE stone type until it reaches the Jeweler; a second id " +
                          "here is a second material family nobody asked for.");
            }

            // THE SURVIVING HALF OF THE EXCLUSIVITY INVARIANT, restated rather than deleted
            // (the WO-1159 precedent: a ruling moved, so the pin was re-pointed and made
            // STRICTER, never dropped). Raids may now EARN the stone. Nothing may BUY it, and
            // the fence that enforces that is membership of this set - VendorStockResolver
            // filters shelves through it and DungeonGemExclusivityRegression scans the
            // purchasable catalogs with it. An id quietly removed from the set would open the
            // shops without failing anything else.
            if (!DungeonExclusiveItems.Contains(DungeonExclusiveItems.RoughStoneId))
            {
                fails.Add("[one-stone] '" + DungeonExclusiveItems.RoughStoneId + "' is no longer in " +
                          "DungeonExclusiveItems. The 2026-09-09 ruling let RAIDS earn the stone; it did " +
                          "not make the stone purchasable. Removing it from the set unfences every " +
                          "vendor shelf and every pack at once.");
            }
        }

        // =====================================================================
        //  B - THE CAMP LADDER. Behavioural, plus a DATA premise off the real
        //      catalog so the ladder cannot quietly stop matching the game.
        // =====================================================================

        private static void CheckTierLadder(List<string> fails)
        {
            var expected = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>(RaidLootTunables.CampIdCamp1, 1),
                new KeyValuePair<string, int>(RaidLootTunables.CampIdCamp2, 2),
                new KeyValuePair<string, int>(RaidLootTunables.CampIdCamp3, 3),
                new KeyValuePair<string, int>(RaidLootTunables.CampIdBastion, 4),
            };

            foreach (var kv in expected)
            {
                int got = RaidScoring.TierOf(kv.Key);
                if (got != kv.Value)
                    fails.Add("[tier-ladder] RaidScoring.TierOf('" + kv.Key + "') is " + got +
                              ", expected " + kv.Value + ". The ladder is what 'the top two tiers' " +
                              "means; a wrong rung silently moves which camps drop stone.");
            }

            // An id nothing authored must answer 0, NOT a tier. Falling back to a tier would
            // open a faucet on a camp nobody sized.
            if (RaidScoring.TierOf("zz-regression-no-such-camp") != 0)
                fails.Add("[tier-ladder] an UNKNOWN camp id no longer answers tier 0. An unknown camp " +
                          "must be below every possible minimum tier, so it pays nothing and says so - " +
                          "the opposite default from RaidLootTunables.CoinsBaseFor, which falls back to " +
                          "Camp I to protect a payout that already exists.");

            // DATA PREMISE. scene-configs.json carries NO 'tier' field (read at source
            // 2026-09-09), so this suite cannot assert the ladder against one. What it CAN
            // assert is that all four ids are still live config ids, and that the ladder's
            // order agrees with the one ordering the catalog does author: unlockVictories.
            string path = Path.Combine(Application.dataPath, SceneConfigsRel).Replace('\\', '/');
            if (!File.Exists(path))
            {
                fails.Add("[tier-ladder] PREMISE FAILED: scene-configs.json not found at Assets/" +
                          SceneConfigsRel + " - the ladder was not checked against the live catalog.");
                return;
            }

            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex)
            {
                fails.Add("[tier-ladder] PREMISE FAILED: could not read scene-configs.json (" +
                          ex.GetType().Name + ": " + ex.Message + ").");
                return;
            }

            int lastAt = -1;
            foreach (var kv in expected)
            {
                int at = json.IndexOf("\"" + kv.Key + "\"", StringComparison.Ordinal);
                if (at < 0)
                {
                    fails.Add("[tier-ladder] config id '" + kv.Key + "' (ladder rung " + kv.Value +
                              ") is no longer in scene-configs.json. The ladder is naming a camp the " +
                              "game cannot load.");
                    continue;
                }
                if (at < lastAt)
                    fails.Add("[tier-ladder] '" + kv.Key + "' appears BEFORE the rung above it in " +
                              "scene-configs.json. The catalog is authored in map order and the ladder " +
                              "mirrors it; a disagreement means one of the two was reordered alone.");
                lastAt = at;
            }
        }

        // =====================================================================
        //  B + C - THE PURE RULE TABLE. Nothing loaded: no scene, no save, no
        //          network, no PlayerPrefs. Every camp x every day-count that
        //          matters, at the ruled knob values passed IN.
        // =====================================================================

        private static void CheckRuleTable(List<string> fails)
        {
            // At the ruled defaults: Camps I and II never drop; III and IV drop exactly once a
            // day, and the second clear of the day pays nothing however good it was.
            Expect(fails, RaidLootTunables.CampIdCamp1, 0, false, "Camp I is below the minimum tier");
            Expect(fails, RaidLootTunables.CampIdCamp2, 0, false, "Camp II is below the minimum tier");
            Expect(fails, RaidLootTunables.CampIdCamp3, 0, true, "Camp III is tier 3 and the day is unspent");
            Expect(fails, RaidLootTunables.CampIdBastion, 0, true, "the Bastion is tier 4 and the day is unspent");
            Expect(fails, RaidLootTunables.CampIdCamp3, 1, false, "the one stone of the day is already paid");
            Expect(fails, RaidLootTunables.CampIdBastion, 1, false,
                   "the cap is GLOBAL - clearing a second eligible camp the same day pays nothing");
            Expect(fails, "zz-regression-no-such-camp", 0, false, "an unknown camp has no tier");

            // The two OFF rows, because a knob that cannot turn the feature off is not an
            // escape hatch. minTier above the ladder, and a zero cap, must each refuse alone.
            if (RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdBastion, 0, 5, RuledPerDayCap, out _))
                fails.Add("[rule-table] minTier 5 did not turn the raid drop off. That row is the ONE " +
                          "way to restore the pre-WO-1373 build without a rebuild.");
            if (RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdBastion, 0, RuledMinTier, 0, out _))
                fails.Add("[rule-table] a per-day cap of 0 did not turn the raid drop off.");

            // A widened tier row must reach down the ladder and no further - the documented
            // effect of the one row an operator is most likely to move.
            if (!RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdCamp2, 0, 2, RuledPerDayCap, out _))
                fails.Add("[rule-table] minTier 2 did not admit Camp II (" + RaidLootTunables.CampIdCamp2 +
                          "). The doc row for raid.roughStoneMinTier states exactly that effect.");
            if (RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdCamp1, 0, 2, RuledPerDayCap, out _))
                fails.Add("[rule-table] minTier 2 admitted Camp I, which is tier 1.");

            // The reason string is never empty, on EITHER branch. A player who cleared the top
            // camp and got no stone must be explainable from a capture (CLAUDE.md section 12).
            RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdCamp1, 0, RuledMinTier, RuledPerDayCap,
                                             out string refusedWhy);
            RaidScoring.ShouldDropRoughStone(RaidLootTunables.CampIdBastion, 0, RuledMinTier, RuledPerDayCap,
                                             out string paidWhy);
            if (string.IsNullOrEmpty(refusedWhy) || string.IsNullOrEmpty(paidWhy))
                fails.Add("[rule-table] ShouldDropRoughStone left its 'why' empty on a branch. A silent " +
                          "refusal is the failure this project keeps paying for.");
        }

        private static void Expect(List<string> fails, string campId, int grantedToday,
                                   bool want, string because)
        {
            bool got = RaidScoring.ShouldDropRoughStone(campId, grantedToday, RuledMinTier,
                                                        RuledPerDayCap, out string why);
            if (got != want)
                fails.Add("[rule-table] ShouldDropRoughStone('" + campId + "', grantedToday=" +
                          grantedToday + ", minTier=" + RuledMinTier + ", cap=" + RuledPerDayCap +
                          ") returned " + got + ", expected " + want + " because " + because +
                          ". The rule said: " + why);
        }

        // =====================================================================
        //  THE SHIPPING DEFAULTS - what an offline player gets.
        // =====================================================================

        private static void CheckShippingDefaults(List<string> fails)
        {
            if (RaidScoring.RoughStoneMinTier != RuledMinTier)
                fails.Add("[defaults] RaidScoring.RoughStoneMinTier is " + RaidScoring.RoughStoneMinTier +
                          ", not the ruled " + RuledMinTier + " (the LOWER of the top two rungs). Owner " +
                          "2026-09-09: 'only top two tiers of raids can drop stone'.");

            if (RaidScoring.RoughStonePerDayCap != RuledPerDayCap)
                fails.Add("[defaults] RaidScoring.RoughStonePerDayCap is " +
                          RaidScoring.RoughStonePerDayCap + ", not the ruled " + RuledPerDayCap +
                          ". Owner 2026-09-09: 'no more than 1 per day'.");

            int pct = DeNelle.Dungeons.DungeonController.PostFirstRoughStoneDropPct;
            if (pct != RuledDungeonPct)
            {
                fails.Add("[defaults] DungeonController.PostFirstRoughStoneDropPct is " + pct +
                          ", not the ruled " + RuledDungeonPct + "%" +
                          (pct == PreRulingDungeonPct
                              ? " - that is the PRE-RULING rate this build shipped as a compiled " +
                                "const float 0.15f, so the ruling has been reverted rather than retuned."
                              : ".") +
                          " Owner 2026-09-09: '5% drop rate in dungeons'.");
            }

            // The fraction and the percent are the same number. They are two members and a
            // reader has to be able to trust that neither drifted.
            float rate = DeNelle.Dungeons.DungeonController.PostFirstRoughStoneDropRate;
            if (Mathf.Abs(rate - pct / 100f) > 0.0001f)
                fails.Add("[defaults] PostFirstRoughStoneDropRate (" + rate + ") is not " +
                          "PostFirstRoughStoneDropPct/100 (" + pct + "). Two statements of one number " +
                          "is the duplicated state CLAUDE.md keeps naming.");

            // The roll must actually consume the rate. A rate nothing reads is a dead knob.
            if (DeNelle.Dungeons.DungeonController.ShouldAwardPostFirstStone(rate + 0.001f))
                fails.Add("[defaults] ShouldAwardPostFirstStone awarded on a roll ABOVE the rate.");
            if (!DeNelle.Dungeons.DungeonController.ShouldAwardPostFirstStone(0f) && pct > 0)
                fails.Add("[defaults] ShouldAwardPostFirstStone refused a roll of 0 while the rate is " +
                          pct + "% - the roll no longer consumes the knob.");
        }

        // =====================================================================
        //  C - THE DAY LEDGER. Round-tripped on the REAL keys and restored.
        //      An unexercised reset hook proves nothing - the claim set went
        //      write-only for months for exactly that reason.
        // =====================================================================

        private static void CheckDayLedger(List<string> fails)
        {
            RaidScoring.ClearRoughStoneDayLedger();
            if (RaidScoring.RoughStonesGrantedToday() != 0)
            {
                fails.Add("[day-ledger] a cleared ledger does not read 0.");
                return;
            }

            RaidScoring.MarkRoughStoneGranted();
            if (RaidScoring.RoughStonesGrantedToday() != 1)
                fails.Add("[day-ledger] one MarkRoughStoneGranted did not read back as 1.");

            RaidScoring.MarkRoughStoneGranted();
            if (RaidScoring.RoughStonesGrantedToday() != 2)
                fails.Add("[day-ledger] the ledger does not COUNT - it must hold a count, not a bool, " +
                          "because raid.roughStonePerDayCap is a row an operator may raise above 1.");

            // SELF-EXPIRY. Stamp yesterday's key by hand and the count must read 0 again with
            // nothing having to clean it up - the same property RaidClaimService's crystal
            // day-stamp relies on, and the whole reason this needs no save-schema bump.
            PlayerPrefs.SetString("dotr-raid-stoneday",
                DeNelle.Core.UtcDay.Key(DateTime.UtcNow.AddDays(-1)));
            PlayerPrefs.Save();
            if (RaidScoring.RoughStonesGrantedToday() != 0)
                fails.Add("[day-ledger] a stamp from YESTERDAY still counts against today's cap. The " +
                          "ledger must self-expire on the UTC day key; anything else needs a cleanup " +
                          "pass nobody wrote.");

            RaidScoring.ClearRoughStoneDayLedger();
        }

        // =====================================================================
        //  ONE GRANT SITE, GATED, AND STAMPED ONLY AFTER THE LARDER TOOK IT.
        //  Source-lint, comments and strings stripped.
        // =====================================================================

        private static void CheckSingleRaidGrantSite(string victory, List<string> fails)
        {
            if (victory == null) return;

            string body = Body(victory, @"void\s+GrantRoughStoneIfEarned\s*\([^)]*\)");
            if (string.IsNullOrEmpty(body))
            {
                fails.Add("[grant-site] RaidVictoryController has no GrantRoughStoneIfEarned body - the " +
                          "raid rough-stone grant has no single site, or it was renamed without this pin.");
                return;
            }

            int iRule = body.IndexOf("ShouldDropRoughStone(", StringComparison.Ordinal);
            int iGrant = body.IndexOf("DungeonRunPayout.GrantRoughStone(", StringComparison.Ordinal);
            int iStamp = body.IndexOf("MarkRoughStoneGranted(", StringComparison.Ordinal);

            if (iRule < 0)
                fails.Add("[grant-site] the raid grant no longer consults " +
                          "RaidScoring.ShouldDropRoughStone. An ungated grant pays every camp every " +
                          "clear, which is neither of the two bounds the owner ruled.");
            if (iGrant < 0)
                fails.Add("[grant-site] the raid no longer routes through " +
                          "DungeonRunPayout.GrantRoughStone - the ONE 'a rough stone was earned' seam. " +
                          "WO-1112's law is that exactly one site writes LastPolishScore and everything " +
                          "else REACHES it; this lane's first attempt inlined the bank here and " +
                          "ComposedDungeonRunRegression [exit-pays] caught it as a duplicated payout.");
            if (iStamp < 0)
                fails.Add("[grant-site] the raid grant no longer stamps the day ledger " +
                          "(RaidScoring.MarkRoughStoneGranted). Unstamped, the cap bounds nothing and " +
                          "every clear of the day pays a stone.");

            if (iRule >= 0 && iGrant >= 0 && iRule > iGrant)
                fails.Add("[grant-site] the rule is consulted (at " + iRule + ") AFTER the stone is " +
                          "granted (at " + iGrant + "). The gate must precede the grant.");
            if (iStamp >= 0 && iGrant >= 0 && iStamp < iGrant)
                fails.Add("[grant-site] the day is stamped (at " + iStamp + ") BEFORE the grant call " +
                          "(at " + iGrant + "). Same lesson written on MarkCrystalsPaid: a stamp ahead " +
                          "of a grant that then fails burns the player's one stone of the day for " +
                          "nothing.");

            // ⛔ THE RAID MUST NOT BANK OR GRADE ANYTHING ITSELF. These two are the exact
            // mutations the [exit-pays] oracle reds on, caught here first and by name so the
            // next seat sees the reason rather than a scan result in another suite.
            if (victory.IndexOf("AddEarned(", StringComparison.Ordinal) >= 0)
                fails.Add("[grant-site] RaidVictoryController calls AddEarned directly. The stone is " +
                          "banked by DungeonController.BankRoughStone and nowhere else - a second " +
                          "bank site is the duplicate-authority bug WO-1112 spent a day undoing.");
            if (victory.IndexOf("LastPolishScore", StringComparison.Ordinal) >= 0)
                fails.Add("[grant-site] RaidVictoryController names DungeonRunPayout.LastPolishScore. " +
                          "Exactly ONE site under Assets/_Modules may write it; the raid supplies its " +
                          "grade as an INPUT to the authority instead.");
        }

        // =====================================================================
        //  ONE AUTHORITY, REACHED BY INVERSION. The seam that makes the single
        //  writer callable from an assembly that cannot name it.
        // =====================================================================

        private static void CheckOneGrantAuthority(string dungeon, List<string> fails)
        {
            // The declaration side, in DeNelle.Core, must exist and must NOT bank anything
            // itself - a local fallback there would be the second producer wearing a delegate.
            string payoutSrc = ReadCode(PayoutRel, fails);
            if (payoutSrc != null)
            {
                if (payoutSrc.IndexOf("RoughStoneGrantAuthority", StringComparison.Ordinal) < 0)
                    fails.Add("[one-authority] DungeonRunPayout no longer declares " +
                              "RoughStoneGrantAuthority. That delegate is the only way DeNelle.Village " +
                              "can reach the single grant site: DeNelle.Dungeons references " +
                              "DeNelle.Village, so a direct call back is a circular assembly reference " +
                              "and does not compile.");
                if (payoutSrc.IndexOf("AddEarned(", StringComparison.Ordinal) >= 0)
                    fails.Add("[one-authority] DungeonRunPayout.GrantRoughStone banks a stone itself. " +
                              "It must FORWARD or FAIL - a local fallback makes the seam a second " +
                              "producer, which is worse than not paying.");
            }

            if (dungeon == null) return;

            // The implementation side must be installed at BOOT, because a raid earns a stone in
            // a RaidBase_* scene where no DungeonController instance exists.
            if (dungeon.IndexOf("RoughStoneGrantAuthority", StringComparison.Ordinal) < 0)
                fails.Add("[one-authority] DungeonController never installs itself as the grant " +
                          "authority, so DungeonRunPayout.GrantRoughStone forwards to nothing and a " +
                          "raid stone is silently never paid.");
            if (dungeon.IndexOf("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal) < 0)
                fails.Add("[one-authority] the grant authority is no longer installed via " +
                          "RuntimeInitializeOnLoadMethod. A scene-load install would leave the seam " +
                          "empty in every RaidBase_* scene, which is exactly where the raid needs it.");

            string bank = Body(dungeon, @"bool\s+BankRoughStone\s*\([^)]*\)");
            if (string.IsNullOrEmpty(bank))
            {
                fails.Add("[one-authority] DungeonController.BankRoughStone body not found - the ONE " +
                          "writer of LastPolishScore was renamed or removed.");
                return;
            }

            int add = bank.IndexOf("AddEarned(", StringComparison.Ordinal);
            int score = bank.IndexOf("LastPolishScore", StringComparison.Ordinal);
            int raise = bank.IndexOf("RaiseRoughStoneGranted(", StringComparison.Ordinal);
            if (add < 0 || score < 0 || raise < 0)
                fails.Add("[one-authority] BankRoughStone no longer does all three of bank / record " +
                          "the grade / announce. Splitting them is how the FIFO desynchronises from " +
                          "the larder: stones are indistinguishable, so a stone banked without a " +
                          "grade silently consumes the NEXT stone's.");
            else if (raise < add)
                fails.Add("[one-authority] BankRoughStone announces (at " + raise + ") before it banks " +
                          "(at " + add + ") - the fanfare would celebrate a stone the player does not " +
                          "own yet.");
        }

        // =====================================================================
        //  D - STARTER DUNGEONS PAY NOTHING. Source-lint for the gate, plus a
        //      DATA check against the real authored layouts.
        // =====================================================================

        private static void CheckStarterDungeonsExcluded(string dungeon, List<string> fails)
        {
            if (dungeon != null)
            {
                string body = Body(dungeon, @"void\s+GrantRunPayout\s*\([^)]*\)");
                if (string.IsNullOrEmpty(body))
                {
                    fails.Add("[starter-gate] DungeonController.GrantRunPayout body not found - the " +
                              "one dungeon payout authority was renamed or removed.");
                }
                else
                {
                    int iTier = body.IndexOf("ResolveDungeonTier(", StringComparison.Ordinal);
                    int iMin = body.IndexOf("RoughStoneMinDungeonTier", StringComparison.Ordinal);
                    // WO-1373: the bank left this body for the shared authority, so the ORDER pin
                    // now runs against the CALL to it. Same property, correct anchor.
                    int iBank = body.IndexOf("BankRoughStone(", StringComparison.Ordinal);

                    if (iTier < 0 || iMin < 0)
                        fails.Add("[starter-gate] GrantRunPayout no longer resolves the layout tier and " +
                                  "compares it against RoughStoneMinDungeonTier. Owner 2026-09-09: '5% " +
                                  "drop rate in dungeons not included the starter dungeons' - with no " +
                                  "gate, dg_starter_loop pays stone again.");
                    if (iBank < 0)
                        fails.Add("[starter-gate] GrantRunPayout no longer calls BankRoughStone - the " +
                                  "dungeon payout has stopped reaching the one grant authority.");
                    else if (iTier >= 0 && iTier > iBank)
                        fails.Add("[starter-gate] the tier is resolved (at " + iTier + ") AFTER the " +
                                  "stone is banked (at " + iBank + "). The gate must precede the grant.");
                }
            }

            if (DeNelle.Dungeons.DungeonController.RoughStoneMinDungeonTier <= 1)
                fails.Add("[starter-gate] RoughStoneMinDungeonTier is " +
                          DeNelle.Dungeons.DungeonController.RoughStoneMinDungeonTier +
                          ". Tier 1 IS the starter band, so a minimum of 1 or less excludes nothing.");

            // THE DATA HALF. 'Starter' is not a flag anywhere in the dungeon data - it is the
            // authored layout tier, and this is the premise that claim rests on. Read the real
            // files: at least one layout must be tier 1 (or the gate guards an empty set) and
            // at least one must be eligible (or the gate excludes every dungeon in the game).
            string dir = Path.Combine(Application.dataPath, LayoutsRel).Replace('\\', '/');
            if (!Directory.Exists(dir))
            {
                fails.Add("[starter-gate] PREMISE FAILED: no layouts at Assets/" + LayoutsRel +
                          " - the tier premise was not checked against the authored data.");
                return;
            }

            int starters = 0, eligible = 0, untiered = 0;
            var starterNames = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                var m = Regex.Match(text, "\"tier\"\\s*:\\s*(\\d+)");
                if (!m.Success) { untiered++; continue; }

                int tier = int.Parse(m.Groups[1].Value);
                if (tier < DeNelle.Dungeons.DungeonController.RoughStoneMinDungeonTier)
                {
                    starters++;
                    if (starterNames.Count < 6) starterNames.Add(name + " (tier " + tier + ")");
                }
                else eligible++;
            }

            if (starters == 0)
                fails.Add("[starter-gate] PREMISE FAILED: no authored layout is below tier " +
                          DeNelle.Dungeons.DungeonController.RoughStoneMinDungeonTier + ", so the " +
                          "starter exclusion guards an empty set and proved nothing. Either the data " +
                          "moved or the minimum did.");
            if (eligible == 0)
                fails.Add("[starter-gate] every authored layout is BELOW the minimum tier, so no " +
                          "dungeon in the game can pay a rough stone. That is not the ruling - it " +
                          "excluded the starters, not the pillar.");

            Debug.Log("[raid-rough-stone] starter-gate premise: " + starters + " starter layout(s) [" +
                      string.Join(", ", starterNames.ToArray()) + "], " + eligible + " eligible, " +
                      untiered + " with no authored tier (those fail OPEN by design).");
        }

        // =====================================================================
        //  THE HONOR CACHE (lane RAID-3 item 2). One read per rail payload, not
        //  three tunable lookups per frame, twice a frame.
        // =====================================================================

        private static void CheckHonorCacheIsNotPerFrame(string scoring, List<string> fails)
        {
            // The two projectors must agree. The four-argument form is PINNED elsewhere
            // (RaidWatchdogHonorRegression calls it directly) and now delegates to the
            // seven-argument one, so a divergence here means somebody re-implemented the
            // arithmetic in one of them.
            float t3 = RaidScoring.HonorThirdStarSeconds;
            float t2 = RaidScoring.HonorSecondStarSeconds;
            float d2 = RaidScoring.HonorSecondStarMinDestruction;
            for (int e = 0; e <= 200; e += 10)
            {
                for (int d = 0; d <= 10; d++)
                {
                    for (int h = 0; h < 2; h++)
                    {
                        bool died = h == 1;
                        int viaProps = RaidScoring.ComputeHonorStars(true, e, d / 10f, died);
                        int viaArgs = RaidScoring.ComputeHonorStars(true, e, d / 10f, died, t3, t2, d2);
                        if (viaProps != viaArgs)
                        {
                            fails.Add("[honor-cache] the four-argument ComputeHonorStars (" + viaProps +
                                      ") and the explicit-threshold overload (" + viaArgs +
                                      ") disagree at elapsed=" + e + "s destruction=" + (d / 10f) +
                                      " heroDied=" + died + ". They must be one formula.");
                            return;
                        }
                    }
                }
            }

            if (scoring == null) return;

            // PresentationStars is read by the live HUD AND by the per-frame snuff detector.
            // Its getter must name NONE of the three Honor* properties - each of those walks
            // RemoteTunables.SpecFor and then RemoteTunables.Int, which on the shipped build
            // was six tunable lookups per frame on a device already measured at 22 fps.
            string getter = Section(scoring, "public int PresentationStars");
            if (string.IsNullOrEmpty(getter))
            {
                fails.Add("[honor-cache] RaidScoring no longer declares PresentationStars - the HUD " +
                          "binds it (RaidWatchdogHonorRegression pins that) and this suite pins its cost.");
                return;
            }

            string[] banned = { "HonorThirdStarSeconds", "HonorSecondStarSeconds",
                                "HonorSecondStarMinDestruction" };
            foreach (var b in banned)
            {
                if (getter.IndexOf(b, StringComparison.Ordinal) >= 0)
                    fails.Add("[honor-cache] PresentationStars' getter reads '" + b + "' directly. That " +
                              "property walks RemoteTunables.SpecFor then RemoteTunables.Int on EVERY " +
                              "read, and this getter is read twice a frame for the whole raid. Read the " +
                              "cached field and let RefreshHonorCacheIfStale re-seed it when " +
                              "RemoteTunables.Generation moves.");
            }

            if (getter.IndexOf("RefreshHonorCacheIfStale(", StringComparison.Ordinal) < 0)
                fails.Add("[honor-cache] PresentationStars no longer refreshes the milestone cache. " +
                          "Without that one integer compare a knob pushed mid-raid never lands, and the " +
                          "tunable ruling stops holding.");

            // The refresh must be keyed on the rail's own change signal, not on a timer or a
            // frame count - anything else either misses a payload or re-reads on a cadence.
            string refresh = Body(scoring, @"void\s+RefreshHonorCacheIfStale\s*\(\s*\)");
            if (string.IsNullOrEmpty(refresh))
                fails.Add("[honor-cache] RaidScoring has no RefreshHonorCacheIfStale body.");
            else if (refresh.IndexOf("RemoteTunables.Generation", StringComparison.Ordinal) < 0)
                fails.Add("[honor-cache] RefreshHonorCacheIfStale does not read " +
                          "RemoteTunables.Generation. Generation is the rail's ONE change signal " +
                          "(bumped by ApplyPayload and by Clear); anything else is a poll.");

            // And it must be seeded at the one authority that starts the raid, so the first
            // frame of the fight is already a cache hit.
            string engage = Body(scoring, @"void\s+NotifyEngagement\s*\([^)]*\)");
            if (!string.IsNullOrEmpty(engage) &&
                engage.IndexOf("RefreshHonorCacheIfStale(", StringComparison.Ordinal) < 0)
                fails.Add("[honor-cache] NotifyEngagement does not seed the honor cache. It is the ONE " +
                          "authority that starts the clock (WO-1520), so it is the one place the " +
                          "milestones are guaranteed to be wanted next frame.");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        // The two brace characters as named consts, NOT as repeated char literals. CLAUDE.md
        // section 1's quality gate counts '{' and '}' occurrences across the whole file to catch
        // a mount-garbled edit, and a scanner cannot know a literal from syntax - so a helper
        // that writes '{' three times and '}' once fails a gate it has not actually broken.
        // One of each, here, keeps the crude check honest instead of teaching anyone to ignore it.
        private const char OpenBrace = '{';
        private const char CloseBrace = '}';

        /// <summary>The brace-balanced body of the first member matching <paramref name="sigPattern"/>,
        /// or null. Operates on ALREADY-STRIPPED source.</summary>
        private static string Body(string stripped, string sigPattern)
        {
            if (string.IsNullOrEmpty(stripped)) return null;
            var m = Regex.Match(stripped, sigPattern);
            if (!m.Success) return null;

            int open = stripped.IndexOf(OpenBrace, m.Index + m.Length);
            if (open < 0) return null;

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

        /// <summary>The text from a member declaration up to the next member declaration - used
        /// for a property whose body is a block or an expression. Operates on stripped source.</summary>
        private static string Section(string stripped, string decl)
        {
            if (string.IsNullOrEmpty(stripped)) return null;
            int at = stripped.IndexOf(decl, StringComparison.Ordinal);
            if (at < 0) return null;

            int next = stripped.IndexOf("public ", at + decl.Length, StringComparison.Ordinal);
            int priv = stripped.IndexOf("private ", at + decl.Length, StringComparison.Ordinal);
            if (priv >= 0 && (next < 0 || priv < next)) next = priv;
            if (next < 0) next = stripped.Length;
            return stripped.Substring(at, next - at);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return 0;
            int n = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        /// <summary>Reads a source file with comments and string literals emptied, so a symbol
        /// named only inside a comment or a log message can never satisfy a pin. Returns null
        /// and records a fail when the file is missing.</summary>
        private static string ReadCode(string rel, List<string> fails)
        {
            string path = Path.Combine(Application.dataPath, rel).Replace('\\', '/');
            if (!File.Exists(path))
            {
                fails.Add("source file missing: Assets/" + rel);
                return null;
            }
            try { return StripCommentsAndStrings(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                fails.Add("could not read Assets/" + rel + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string StripCommentsAndStrings(string src)
        {
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                if (c == '/' && n == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < src.Length && src[i] != '\'')
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("''");
                    continue;
                }
                if (c == '@' && n == '"')
                {
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { i += 2; continue; }
                            break;
                        }
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    while (i < src.Length && src[i] != '"')
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

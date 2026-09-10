// =============================================================================
// SpoilsAreBankableRegression [spoils-bankable]  --  markers SPOILS_BANKABLE_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Edit mode, no PlayMode: mostly BEHAVIOURAL
// (it calls the real DeNelle.Village statics) plus one SOURCE-LINT pin, read with
// comments AND string literals stripped so a symbol named only inside a comment or
// a log message can never satisfy it.
// Registered in DataRegression.RunAll.  NEVER throws.
//
// docs/reference/REGRESSION_COVERAGE_MATRIX.md item 14 names this suite and states
// the law it exists for in five words:
//
//              quoted == banked + pending
//
// ONE LAW, THREE PRODUCERS (WO-1461 raids, WO-1445 the offline-harvest grant
// remainder, WO-1475). This suite owns the RAID producer.
//
// -----------------------------------------------------------------------------
// THE DEFECT, MEASURED, NOT INFERRED  (troop-ai-blind-2026-09-06.log)
// -----------------------------------------------------------------------------
//   14:37:40.331  loot settled ... 1800w 1100i
//                 repeat-clear multiplier x0.25 -> 450w / 275i
//   14:37:40.333  [Flow:Bank] BANK FULL [Grant] Wood: requested 450, banked 25, LOST 425
//
// The deploy screen for that same camp read "Spoils: ~1800 wood, ~1100 iron". So
// 1,800 was promised and 25 arrived, with TWO subtractions in between that the card
// knew nothing about, and the remainder was burned.
//
// -----------------------------------------------------------------------------
// THE THREE PINS, AND THE OWNER RULINGS THEY CARRY (2026-09-06 20:33)
// -----------------------------------------------------------------------------
//   PIN A  NOTHING IS BURNED WHILE THE CACHE HAS ROOM.
//          Verbatim: "Never destroy raid loot because storage is full. Put overflow
//          into a temporary Raid Cache with a modest cap." So RaidClaimService.
//          SplitAxis must satisfy Banked + Cached + Refused == amount on every
//          input, and Refused must be ZERO whenever the cache has the room. This is
//          the WO-1434 retention law applied to a third producer: the bank REFUSES
//          units, it does not destroy them, and retention is the caller's business.
//
//   PIN B  THE REPEAT SHARE IS THE RULED ONE, AND IT IS A KNOB.
//          Verbatim: "100% first clear after cooldown, 60% repeat clear during the
//          same cycle, then reset to 100% when the camp's cooldown expires."
//          RaidClaimService.RepeatClearLootMultiplier was a compiled
//          `const float = 0.25f` from 2026-08-15 to 2026-09-09 - which is precisely
//          WHY it could disagree with a ruling for three days. It is now read off
//          RemoteTunables (raid.lootRepeatClearPct, default 60) and this pin asserts
//          the default, the clamp direction, and the arithmetic through the real
//          ScaleLootForClear.
//
//          THE RESET HALF IS A SEPARATE PREDICATE AND THAT IS THE POINT.
//          IsClaimed is PERMANENT (WO-1134 records why: it also gates the one-time
//          companion unlock, so day-scoping it would re-grant a companion forever).
//          Read alone it says "reduced forever", which is the behaviour the player
//          met. The ruling resets at cooldown expiry, so IsRepeatClearInCycle is the
//          AND of a prior claim and a still-running cooldown window.
//
//   PIN C  THE QUOTE EQUALS WHAT ARRIVES.
//          RaidSelectionVM must quote the SCALED estimate and must name the part
//          headed for the cache, both computed through the SAME methods the settle
//          path uses. A second loot table on the card is how the promise and the
//          bank drifted in the first place.
//
// -----------------------------------------------------------------------------
// (!) THE ONE PIN THIS LANE CANNOT MAKE GREEN BY ITSELF - STATED, NOT HIDDEN.
// -----------------------------------------------------------------------------
// PIN A's LAST CASE [settle-retains] reads RaidVictoryController.cs and requires it
// to call RaidClaimService.RetainOverflow. That file belongs to lane RAID and the
// SPOILS lane did not edit it, so this case is RED until ONE line lands there,
// immediately after the measured grant in GrantLoot:
//
//     RaidClaimService.RetainOverflow(configId, loot, _credited);
//
// That is deliberate and it is the RED-first half: a retention API with no caller is
// exactly the write-only shape that let the claim set go unread for months, and this
// suite is written to say so out loud rather than to go green on a half-wired fix.
//
// -----------------------------------------------------------------------------
// ZERO NETWORK, ZERO SCENE. PlayerPrefs is touched only on SCRATCH keys and the
// cache axes, and every one of them is snapshotted and restored in a finally.
// ASCII only. FlowTrace is never stripped (CLAUDE.md section 12).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.Ops;
using DeNelle.Village;
using DeNelle.Village.Hero;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    public static class SpoilsAreBankableRegression
    {
        // Relative to Application.dataPath.
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string ClaimRel   = "_Modules/Village/World/Camps/RaidClaimService.cs";
        private const string VmRel      = "_Modules/Village/Hero/RaidSelectionVM.cs";

        /// <summary>A scratch camp id no scene-config will ever carry.</summary>
        private const string ScratchId = "zz-regression-scratch-spoils";

        /// <summary>The owner's ruled repeat share, as a LITERAL. Never read from the thing
        /// under test - RemoteTunablesDefaultsRegression's own house rule: a threshold
        /// expressed relative to a value that can move underneath it measures nothing.</summary>
        private const int RuledRepeatPct = 60;

        /// <summary>The shipping cache ceiling, as a LITERAL. NOT owner-ruled - she said
        /// "a modest cap" and named none; 1800 is one perfect Camp I wood haul. Pinned so it
        /// cannot drift silently, NOT because the number is settled.</summary>
        private const int ShippingCacheCap = 1800;

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            // Snapshot every piece of ambient state this suite writes, so a developer machine
            // is byte-identical afterwards however the run exits.
            int keptWood = 0, keptIron = 0, keptFood = 0;
            bool snapped = false;
            try
            {
                keptWood = RaidClaimService.Cached(BankResource.Wood);
                keptIron = RaidClaimService.Cached(BankResource.Iron);
                keptFood = RaidClaimService.Cached(BankResource.Food);
                snapped = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("spoils-bankable: could not snapshot the Raid Cache (" +
                                 ex.GetType().Name + ": " + ex.Message + ") - it will not be restored.");
            }

            // A LOCAL override on either knob is, correctly, not the shipping default. Snapshot
            // and clear BOTH for the whole run - not just the one whose case reads it - because
            // [cache-roundtrip] runs first and would otherwise measure a developer's override
            // and call it the default. Noted loudly: an override armed on a build machine is
            // worth knowing about.
            var overrides = new List<KeyValuePair<string, string>>();
            SuspendOverride(RemoteTunables.KeyRaidLootRepeatClearPct, overrides);
            SuspendOverride(RemoteTunables.KeyRaidCacheCapPerResource, overrides);

            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "spoils-bankable: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                foreach (var kv in overrides) PlayerPrefs.SetString(kv.Key, kv.Value);
                try { RaidClaimService.ClearClaim(ScratchId); }
                catch (Exception ex) { Debug.LogWarning("spoils-bankable: scratch claim cleanup failed: " + ex.Message); }
                if (snapped)
                {
                    try
                    {
                        RaidClaimService.SetCached(BankResource.Wood, keptWood);
                        RaidClaimService.SetCached(BankResource.Iron, keptIron);
                        RaidClaimService.SetCached(BankResource.Food, keptFood);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("spoils-bankable: Raid Cache restore failed: " + ex.Message);
                    }
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
            Debug.LogWarning("spoils-bankable: a LOCAL override on '" + pref + "' was armed on this " +
                             "machine and has been cleared for the run, then restored. An override " +
                             "left armed on a build machine is worth knowing about.");
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("SPOILS_BANKABLE_OK - " + reason);
            else Debug.LogError("SPOILS_BANKABLE_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();

            string victory = ReadCode(VictoryRel, fails);
            string claim   = ReadCode(ClaimRel,   fails);
            string vm      = ReadCode(VmRel,      fails);

            CheckSplitLaw(fails);
            CheckCacheRoundTrip(fails);
            CheckRetainOverflow(fails);
            CheckRepeatShare(fails);
            CheckCyclePredicate(claim, fails);
            CheckQuote(vm, fails);
            CheckSettleRetains(victory, fails);

            if (fails.Count == 0)
            {
                Debug.Log("SPOILS_BANKABLE_OK");
                reason = "SPOILS ARE BANKABLE OK -- quoted == banked + cached on every axis " +
                         "(RaidClaimService.SplitAxis: Banked+Cached+Refused == amount, and Refused is " +
                         "ZERO whenever the cache has room), the Raid Cache round-trips through " +
                         "PlayerPrefs and refuses to hold uncapped crystals/gold, a repeat clear inside " +
                         "the cooldown cycle pays " + RaidClaimService.RepeatClearPct + "% off the " +
                         RemoteTunables.KeyRaidLootRepeatClearPct + " knob (never a const, never above " +
                         "100), the share RESETS to 100% once the cooldown expires because " +
                         "IsRepeatClearInCycle is the AND of the claim and the cooldown rather than the " +
                         "permanent claim alone, the selection card quotes the SCALED estimate through " +
                         "the settle path's own methods, and the victory settle RETAINS what the bank " +
                         "refused instead of burning it";
                return true;
            }

            reason = "spoils-bankable (" + fails.Count + "): " + string.Join(" | ", fails.ToArray());
            Debug.LogError("SPOILS_BANKABLE_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  PIN A1 [split-law] - the arithmetic that makes "never burned" checkable
        // =====================================================================

        private static void CheckSplitLaw(List<string> fails)
        {
            // The measured case from the log, with the ruled share: 1800 wood, repeat clear,
            // a bank with 25 units of headroom and an empty cache.
            AssertSplit(fails, "the logged case", amount: 1080, bankRoom: 25, cacheRoom: 1800,
                        wantBanked: 25, wantCached: 1055, wantRefused: 0);

            // A bank with room takes everything and the cache stays empty - the common case,
            // and the one where a cache notice on the card would be a worry the player does
            // not have.
            AssertSplit(fails, "bank has room", amount: 400, bankRoom: 10000, cacheRoom: 1800,
                        wantBanked: 400, wantCached: 0, wantRefused: 0);

            // A completely full bank against an empty cache: NOTHING is refused. This is the
            // owner's ruling stated as arithmetic.
            AssertSplit(fails, "bank full, cache empty", amount: 1080, bankRoom: 0, cacheRoom: 1800,
                        wantBanked: 0, wantCached: 1080, wantRefused: 0);

            // Both ceilings reached is the ONLY path on which a unit leaves the world, and the
            // player can raise the second one.
            AssertSplit(fails, "bank full, cache full", amount: 1080, bankRoom: 0, cacheRoom: 0,
                        wantBanked: 0, wantCached: 0, wantRefused: 1080);

            // An UNCAPPED resource is expressed as int.MaxValue room and must not overflow.
            AssertSplit(fails, "uncapped resource", amount: 26, bankRoom: int.MaxValue, cacheRoom: 0,
                        wantBanked: 26, wantCached: 0, wantRefused: 0);

            // Degenerate inputs answer zero rather than a negative payout.
            AssertSplit(fails, "zero amount", amount: 0, bankRoom: 5, cacheRoom: 5,
                        wantBanked: 0, wantCached: 0, wantRefused: 0);
            AssertSplit(fails, "negative rooms", amount: 100, bankRoom: -5, cacheRoom: -5,
                        wantBanked: 0, wantCached: 0, wantRefused: 100);

            // THE LAW ITSELF, over a sweep rather than a sample: the three numbers always sum
            // back to what was asked for, and nothing is ever negative.
            int[] amounts = { 0, 1, 25, 450, 1080, 1800, 999999 };
            int[] bankRooms = { 0, 1, 25, 450, 8000, int.MaxValue };
            int[] cacheRooms = { 0, 1, 900, 1800 };
            foreach (int a in amounts)
                foreach (int b in bankRooms)
                    foreach (int c in cacheRooms)
                    {
                        var d = RaidClaimService.SplitAxis(a, b, c);
                        if (d.Banked < 0 || d.Cached < 0 || d.Refused < 0)
                        {
                            fails.Add("SplitAxis(" + a + "," + b + "," + c + ") produced a NEGATIVE " +
                                      "destiny (banked " + d.Banked + " cached " + d.Cached +
                                      " refused " + d.Refused + ")");
                            return;
                        }
                        long sum = (long)d.Banked + d.Cached + d.Refused;
                        if (sum != a)
                        {
                            fails.Add("SplitAxis(" + a + "," + b + "," + c + ") broke the law " +
                                      "banked+cached+refused==amount: " + d.Banked + "+" + d.Cached +
                                      "+" + d.Refused + " = " + sum + ", not " + a + ". A payout that " +
                                      "does not add up is a payout with a leak in it, which is the " +
                                      "whole defect WO-1461 exists to close");
                            return;
                        }
                        if (c >= a - d.Banked && d.Refused != 0)
                        {
                            fails.Add("SplitAxis(" + a + "," + b + "," + c + ") REFUSED " + d.Refused +
                                      " units while the cache had room for them. Owner ruling " +
                                      "2026-09-06: 'Never destroy raid loot because storage is full'");
                            return;
                        }
                    }
        }

        private static void AssertSplit(List<string> fails, string label, int amount, int bankRoom,
                                        int cacheRoom, int wantBanked, int wantCached, int wantRefused)
        {
            var d = RaidClaimService.SplitAxis(amount, bankRoom, cacheRoom);
            if (d.Banked != wantBanked || d.Cached != wantCached || d.Refused != wantRefused)
                fails.Add("SplitAxis [" + label + "] (amount " + amount + ", bankRoom " + bankRoom +
                          ", cacheRoom " + cacheRoom + ") = banked " + d.Banked + " / cached " +
                          d.Cached + " / refused " + d.Refused + "; expected " + wantBanked + " / " +
                          wantCached + " / " + wantRefused);
        }

        // =====================================================================
        //  PIN A2 [cache-roundtrip] - the store persists, and it is bounded
        // =====================================================================

        private static void CheckCacheRoundTrip(List<string> fails)
        {
            int cap = RaidClaimService.CacheCapPerResource;
            if (cap != ShippingCacheCap)
                fails.Add("RaidClaimService.CacheCapPerResource is " + cap + ", not the shipping " +
                          ShippingCacheCap + " (one perfect Camp I wood haul). This number is NOT " +
                          "owner-ruled - she said 'a modest cap' and named none - but it may not " +
                          "drift silently either; change it on the " +
                          RemoteTunables.KeyRaidCacheCapPerResource + " knob and here together");

            RaidClaimService.SetCached(BankResource.Wood, 0);
            if (RaidClaimService.Cached(BankResource.Wood) != 0)
                fails.Add("RaidClaimService.SetCached(Wood, 0) did not clear the cache - the store " +
                          "does not round-trip through PlayerPrefs");
            if (RaidClaimService.CacheRoomFor(BankResource.Wood) != cap)
                fails.Add("an EMPTY Raid Cache reported " + RaidClaimService.CacheRoomFor(BankResource.Wood) +
                          " room instead of the full cap " + cap);

            RaidClaimService.SetCached(BankResource.Wood, 300);
            if (RaidClaimService.Cached(BankResource.Wood) != 300)
                fails.Add("the Raid Cache did not persist 300 wood (read back " +
                          RaidClaimService.Cached(BankResource.Wood) + ")");
            if (RaidClaimService.CacheRoomFor(BankResource.Wood) != cap - 300)
                fails.Add("a cache holding 300 of " + cap + " reported " +
                          RaidClaimService.CacheRoomFor(BankResource.Wood) + " room, not " + (cap - 300));

            // The claim door's half: consuming what the wallet APPLIED, never what was asked
            // for - the WO-1392 lesson, or the haul burns a second time on the way out.
            RaidClaimService.ConsumeCached(BankResource.Wood, 100);
            if (RaidClaimService.Cached(BankResource.Wood) != 200)
                fails.Add("ConsumeCached(Wood, 100) left " + RaidClaimService.Cached(BankResource.Wood) +
                          " instead of 200");
            RaidClaimService.ConsumeCached(BankResource.Wood, 999999);
            if (RaidClaimService.Cached(BankResource.Wood) != 0)
                fails.Add("ConsumeCached could not be over-drawn safely - the cache read back " +
                          RaidClaimService.Cached(BankResource.Wood) + " after a claim larger than it held");

            // A cache that could go negative would pay out forever.
            RaidClaimService.SetCached(BankResource.Wood, -50);
            if (RaidClaimService.Cached(BankResource.Wood) != 0)
                fails.Add("the Raid Cache accepted a NEGATIVE balance (" +
                          RaidClaimService.Cached(BankResource.Wood) + ")");
        }

        // =====================================================================
        //  PIN A3 [retain] - the retention itself, on the real store
        // =====================================================================

        private static void CheckRetainOverflow(List<string> fails)
        {
            RaidClaimService.SetCached(BankResource.Wood, 0);
            RaidClaimService.SetCached(BankResource.Iron, 0);
            RaidClaimService.SetCached(BankResource.Food, 0);

            // The logged case: 1080 wood asked for, 25 credited by a full bank.
            var requested = new ResourceCost(wood: 1080, food: 0, iron: 660, crystals: 26, coins: 2200);
            var credited  = new ResourceCost(wood: 25,   food: 0, iron: 0,   crystals: 26, coins: 2200);
            var retained = RaidClaimService.RetainOverflow(ScratchId, requested, credited);

            if (retained.Wood != 1055)
                fails.Add("RetainOverflow retained " + retained.Wood + " wood, not the 1055 the bank " +
                          "refused (requested 1080, credited 25). Every unit the bank refuses and the " +
                          "cache has room for must be held, never burned");
            if (retained.Iron != 660)
                fails.Add("RetainOverflow retained " + retained.Iron + " iron, not the 660 the bank refused");
            if (RaidClaimService.Cached(BankResource.Wood) != 1055)
                fails.Add("the Raid Cache holds " + RaidClaimService.Cached(BankResource.Wood) +
                          " wood after a retention of 1055 - the retention did not persist");

            // CRYSTALS AND GOLD ARE NEVER CACHED. TownBankCapacity Law 1 (owner ruling WO-901
            // section 6) says they have no ceiling at all, so a shortfall on those axes is not
            // an overflow, and caching it would invent a second wallet for an uncapped currency.
            if (retained.Crystals != 0 || retained.Coins != 0)
                fails.Add("RetainOverflow cached an UNCAPPED resource (crystals " + retained.Crystals +
                          ", coins " + retained.Coins + "). TownBankCapacity.IsCapped is false for both " +
                          "by owner ruling; they are never clamped, so there is nothing to retain");

            // A payout the bank took in full retains nothing and says so.
            RaidClaimService.SetCached(BankResource.Wood, 0);
            var full = RaidClaimService.RetainOverflow(ScratchId, requested, requested);
            if (full.Wood != 0 || full.Iron != 0 || full.Food != 0)
                fails.Add("RetainOverflow retained something (" + full.Wood + "w/" + full.Iron +
                          "i/" + full.Food + "f) from a payout the bank credited IN FULL");

            // At the ceiling: the cache takes what it can and the remainder is REFUSED by a
            // stated cap the player can raise - the only path on which a unit leaves the world.
            RaidClaimService.SetCached(BankResource.Wood, RaidClaimService.CacheCapPerResource);
            var atCap = RaidClaimService.RetainOverflow(ScratchId,
                            new ResourceCost(wood: 500), new ResourceCost(wood: 0));
            if (atCap.Wood != 0)
                fails.Add("a FULL Raid Cache still retained " + atCap.Wood + " wood - the cap is not " +
                          "enforced, and an unbounded cache removes the upgrade pressure it exists to " +
                          "create (WO-1461 section 4)");
        }

        // =====================================================================
        //  PIN B [repeat-share] - the ruled number, and it is a knob
        // =====================================================================

        private static void CheckRepeatShare(List<string> fails)
        {
            // Both local overrides are already suspended for the whole run by Run() above, so
            // every read below is the shipping default rather than a developer's row.
            {
                int pct = RaidClaimService.RepeatClearPct;
                if (pct != RuledRepeatPct)
                    fails.Add("RaidClaimService.RepeatClearPct is " + pct + ", not the owner-ruled " +
                              RuledRepeatPct + ". Verbatim, 2026-09-06 20:33: '100% first clear after " +
                              "cooldown, 60% repeat clear during the same cycle, then reset to 100% when " +
                              "the camp's cooldown expires.' It shipped as a compiled const 0.25f, which " +
                              "is exactly why it could disagree with a ruling for three days");

                float m = RaidClaimService.RepeatClearLootMultiplier;
                if (Mathf.Abs(m - RuledRepeatPct / 100f) > 0.0001f)
                    fails.Add("RaidClaimService.RepeatClearLootMultiplier is " + m.ToString("0.###") +
                              " but RepeatClearPct is " + pct + " - the fraction and the percent disagree, " +
                              "so the card and the settle would quote two different rates");
                if (m > 1f)
                    fails.Add("RaidClaimService.RepeatClearLootMultiplier is " + m.ToString("0.###") +
                              " - a repeat clear would pay MORE than the first clear, inverting the whole " +
                              "first-clear gate. The consumer clamp is 0..100 for this reason");

                // The arithmetic, through the real gate. 1800 at the ruled share is 1080; at the
                // shipped-and-wrong 0.25 it was 450, which is the number in the owner's log.
                var loot = new ResourceCost(wood: 1800, food: 0, iron: 1100, crystals: 26, coins: 2200);
                var repeat = RaidClaimService.ScaleLootForClear(loot, true, false);
                int wantWood = Mathf.FloorToInt(1800f * (RuledRepeatPct / 100f));
                if (repeat.Wood != wantWood)
                    fails.Add("a repeat clear of an 1800-wood payout settled " + repeat.Wood +
                              ", not " + wantWood + " (the ruled " + RuledRepeatPct + "%). The owner's " +
                              "device logged 450 here, which is the 25% this ticket retires");

                // Crystals are a SEPARATE axis and the share must not touch them.
                if (repeat.Crystals != loot.Crystals)
                    fails.Add("the repeat share scaled CRYSTALS (" + repeat.Crystals + " of " +
                              loot.Crystals + "). Crystals are all-or-nothing on the once-per-UTC-day " +
                              "stamp (WO-1134); folding them into this multiplier gets the day-two case " +
                              "silently wrong");

                var first = RaidClaimService.ScaleLootForClear(loot, false, false);
                if (first.Wood != loot.Wood || first.Iron != loot.Iron)
                    fails.Add("a FIRST clear did not pay in full (" + first.Wood + "w/" + first.Iron +
                              "i of " + loot.Wood + "w/" + loot.Iron + "i) - the ruling says 100% for " +
                              "the first clear after a cooldown");

                // Registered on the rail, with the ruled default, in the registry the operator
                // console reads. A knob nobody registered answers 0 and would zero the payout.
                AssertRegistered(fails, RemoteTunables.KeyRaidLootRepeatClearPct, RuledRepeatPct);
                AssertRegistered(fails, RemoteTunables.KeyRaidCacheCapPerResource, ShippingCacheCap);
            }
        }

        private static void AssertRegistered(List<string> fails, string key, int expectedDefault)
        {
            var spec = RemoteTunables.SpecFor(key);
            if (spec == null)
            {
                fails.Add("'" + key + "' is NOT in RemoteTunables.Registry. An unregistered key resolves " +
                          "to 0, not to its default - which would zero the thing it governs, silently");
                return;
            }
            if (spec.Default != expectedDefault)
                fails.Add("'" + key + "' is registered with default " + spec.Default + ", not " +
                          expectedDefault + ". A default may be changed, but not in one place only");
        }

        // =====================================================================
        //  PIN B2 [cycle-predicate] - the share RESETS, it does not stick
        // =====================================================================

        private static void CheckCyclePredicate(string claim, List<string> fails)
        {
            // A camp never cleared is never a repeat, whatever the cooldown record says.
            RaidClaimService.ClearClaim(ScratchId);
            if (RaidClaimService.IsRepeatClearInCycle(ScratchId))
                fails.Add("IsRepeatClearInCycle returned TRUE for a camp with no claim on record - a " +
                          "first clear would be paid the reduced share");

            if (RaidClaimService.IsRepeatClearInCycle(null) || RaidClaimService.IsRepeatClearInCycle(""))
                fails.Add("IsRepeatClearInCycle returned TRUE for an empty config id");

            // ⛔ THE PREDICATE MAY NOT BE THE CLAIM FLAG ALONE. Source-lint, because the
            // BEHAVIOURAL half needs a loaded GameState to stamp a cooldown and this suite
            // deliberately loads nothing. IsClaimed is permanent (WO-1134: it also gates the
            // one-time companion unlock), so a predicate built on it alone says "reduced
            // forever" - which is the behaviour the owner's ruling replaces.
            if (claim != null)
            {
                if (claim.IndexOf("IsRepeatClearInCycle", StringComparison.Ordinal) < 0)
                    fails.Add("RaidClaimService.cs does not define IsRepeatClearInCycle - there is no " +
                              "cooldown-cycle predicate, so the repeat share can only be permanent");
                if (claim.IndexOf("RaidCooldownService.IsOnCooldown", StringComparison.Ordinal) < 0)
                    fails.Add("RaidClaimService.cs never reads RaidCooldownService.IsOnCooldown, so " +
                              "nothing can reset the repeat share to 100% when the camp's cooldown " +
                              "expires. Owner ruling 2026-09-06: 'then reset to 100% when the camp's " +
                              "cooldown expires'");
                if (claim.IndexOf("public const float RepeatClearLootMultiplier", StringComparison.Ordinal) >= 0)
                    fails.Add("RepeatClearLootMultiplier is a COMPILED CONST again. That is the shape of " +
                              "the original defect: a constant cannot be flipped, so it disagreed with " +
                              "an owner ruling for three days and needed a rebuild to correct");
            }
        }

        // =====================================================================
        //  PIN C [quote] - the card promises what will arrive
        // =====================================================================

        private static void CheckQuote(string vm, List<string> fails)
        {
            var est = new ResourceCost(wood: 1800, food: 0, iron: 1100, crystals: 26, coins: 2200);

            // A first clear quotes the estimate untouched.
            var quotedFirst = RaidSelectionVM.RepeatScaled(est, false);
            if (quotedFirst.Wood != est.Wood)
                fails.Add("the card's first-clear quote altered the estimate (" + quotedFirst.Wood +
                          " of " + est.Wood + " wood)");

            // A repeat inside the cycle quotes the SCALED figure - the defect was quoting 1800
            // for a payout of 450.
            var quoted = RaidSelectionVM.RepeatScaled(est, true);
            int wantWood = Mathf.FloorToInt(1800f * (RuledRepeatPct / 100f));
            if (quoted.Wood != wantWood)
                fails.Add("the card's REPEAT quote is " + quoted.Wood + " wood, not " + wantWood +
                          ". The owner's card read '~1800 wood' for a settle of 450 - the quote must " +
                          "carry the repeat share or it is a promise the settle cannot keep");

            // THE LAW: what the card quotes equals what banks plus what caches. Asserted on the
            // NUMBERS, because the rendered spoils line is deliberately rounded ("~") by WO-1402
            // and the cache notice deliberately is not.
            var d = RaidClaimService.SplitAxis(quoted.Wood, 25, 1800);
            if (d.Banked + d.Cached != quoted.Wood)
                fails.Add("quoted " + quoted.Wood + " wood but banked+cached is " + (d.Banked + d.Cached) +
                          " - the card is promising units that reach neither the bank nor the cache");

            // The notice names the cached part, exactly, and only when there IS one.
            string notice = RaidSelectionVM.CacheNotice(quoted, 25, int.MaxValue, int.MaxValue, 1800, 1800, 1800);
            if (string.IsNullOrEmpty(notice))
                fails.Add("no Raid Cache notice was produced for a quote of " + quoted.Wood +
                          " wood against 25 units of bank headroom - the player is told nothing about " +
                          "where the rest of their win went");
            else
            {
                if (notice.IndexOf(d.Cached.ToString(), StringComparison.Ordinal) < 0)
                    fails.Add("the Raid Cache notice \"" + notice + "\" does not name the " + d.Cached +
                              " units actually headed for the cache");
                if (!notice.StartsWith(RaidSelectionVM.CacheNoticePrefix, StringComparison.Ordinal) ||
                    !notice.EndsWith(RaidSelectionVM.CacheNoticeSuffix, StringComparison.Ordinal))
                    fails.Add("the Raid Cache notice \"" + notice + "\" does not wear the pinned grammar " +
                              "\"" + RaidSelectionVM.CacheNoticePrefix + "..." +
                              RaidSelectionVM.CacheNoticeSuffix + "\"");
                if (!IsAscii(notice))
                    fails.Add("the Raid Cache notice is not ASCII (device tofu risk): \"" + notice + "\"");
            }

            // A bank with room paints NOTHING - a worry the player does not have is not
            // disclosure, it is noise.
            if (RaidSelectionVM.CacheNotice(quoted, int.MaxValue, int.MaxValue, int.MaxValue, 1800, 1800, 1800) != null)
                fails.Add("a Raid Cache notice was painted while the bank had room for the whole quote");

            // ⛔ NO SECOND LOOT TABLE ON THE CARD. The promise and the bank drifted precisely
            // because two places derived the same number.
            if (vm != null)
            {
                if (vm.IndexOf("RaidClaimService.ScaleLootForClear", StringComparison.Ordinal) < 0)
                    fails.Add("RaidSelectionVM does not scale its quote through " +
                              "RaidClaimService.ScaleLootForClear - the card is deriving a payout of its " +
                              "own, which is how the promise and the settle drifted");
                if (vm.IndexOf("RaidClaimService.SplitAxis", StringComparison.Ordinal) < 0)
                    fails.Add("RaidSelectionVM does not split its quote through " +
                              "RaidClaimService.SplitAxis - the card and the settle would answer " +
                              "'what banks' differently");
            }
        }

        // =====================================================================
        //  PIN A4 [settle-retains] - the retention has a CALLER
        // =====================================================================

        private static void CheckSettleRetains(string victory, List<string> fails)
        {
            if (victory == null) return;
            if (victory.IndexOf("RaidClaimService.RetainOverflow", StringComparison.Ordinal) < 0)
                fails.Add("RaidVictoryController never calls RaidClaimService.RetainOverflow, so the " +
                          "raid settle still BURNS everything the bank refuses (the owner's log: " +
                          "'requested 450, banked 25, LOST 425'). The retention API exists and is " +
                          "pinned above, but an API with no caller is the same write-only shape that " +
                          "let the claim set go unread for months. THE ONE LINE, in HandleVictory " +
                          "immediately after the GrantLoot(loot) call - NOT inside GrantLoot, whose " +
                          "signature carries no configId, while HandleVictory's local is in scope and " +
                          "the _credited field is set by the call above it: " +
                          "RaidClaimService.RetainOverflow(configId, loot, _credited);");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] > 126 || s[i] < 32) return false;
            return true;
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
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
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

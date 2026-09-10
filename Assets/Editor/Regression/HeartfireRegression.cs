// =============================================================================
// HeartfireRegression [heartfire]  --  markers HEARTFIRE_OK / HEARTFIRE_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Edit mode, no PlayMode. Registered ONCE in
// DataRegression.RunAll. NEVER throws.
//
// WO-1379. Canon: docs/CREATIVE_CANON_ELARION_2026-09-04.md section 4.
//
// WHAT IT PROTECTS, and why each pin is worth its line:
//
//   PIN A  THE REGEN TABLE IS THE ACCEPTANCE CRITERION, LITERALLY.
//          0 charges stamped at T -> 1 at T+4h, 2 at +8h, 3 at +12h, STILL 3 at
//          +24h; spending one at +12h leaves 2. Driven against the PURE function
//          DeNelle.Core.State.HeartfireCharges.Regenerate with an injected "now",
//          so it needs no save, no clock, no scene and no PlayMode. The "+24h is
//          still 3" row is the one that catches the two opposite defects: a pool
//          that keeps counting past its ceiling, and a pool that banks a hidden
//          backlog and refills instantly the moment one is spent.
//
//   PIN B  HEARTFIRE NEVER BECOMES A CURRENCY.
//          Source-lint, because this is exactly the drift a behavioural test
//          cannot see: a wallet row, a ResourceType member, a storage cap or a
//          vendor price would all keep the regen table green while breaking the
//          one ruling the ticket is built on ("if the implementation grows a
//          balance, it is wrong"). Lints the two Heartfire files with comments
//          AND string literals stripped, so a symbol named only in a comment can
//          never satisfy - or trip - a pin, and lints the canonical resource
//          catalogs for any Heartfire row.
//
//   PIN C  "RAID ORDER" IS DEAD, AND "MARCH" IS ALIVE.
//          The rename is the ticket. A shipped string still saying "Raid Order"
//          means the fiction did not land, however correct the integer is. The
//          verb is deliberately NOT banned - canon section 2 keeps "march" and
//          bans only "Marches" as a NOUN for the pool.
//
//   PIN D  THE CLOCK IS TimeSource, NEVER DateTime.UtcNow.
//          Source-lint, because it can only be seen at the call site: a single
//          DateTimeOffset.UtcNow in the service re-opens the device-clock exploit
//          in full (roll the phone clock forward, refill the pool) and would test
//          GREEN against every behavioural case in this file - the runtime cannot
//          tell which clock it was handed. Same pin, same reasoning, as
//          RaidCooldownRegression PIN C.
//
//   PIN E  A PLAYER HOLDING HEARTFIRE ALWAYS HAS SOMEWHERE TO SPEND IT.
//          Since WO-1379 retired the per-camp wall AS A GATE (PIN F) this is
//          structural - no camp door is ever shut by a timer - so the pin is now a
//          TRIPWIRE ON THE RETAINED FIELD: the rekindle interval is <= the SHORTEST
//          authored raidCooldownSeconds, measured out of scene-configs.json rather
//          than restated here. The WO forbids retuning that field ("retired is not
//          retuned"), and a superseded number that drifts is how the next seat
//          reads a value that means nothing. Goes red if either side moves alone.
//
//   PIN F  ONE GATE ON WHEN YOU MAY RAID, AT THE ONE DOOR, AND IT IS HEARTFIRE.
//          Owner, asked directly: "Heartfire replaces the camp wall" (WO-1379
//          section 3). Source-lint on RaidSelectionScreen, because a second gate
//          cannot be seen by any behavioural case: (1) the file references NO
//          RaidCooldownService / IsOnCooldown at all - not the door, and not the
//          card, which used to paint "Recovering - raidable in 12h" over a door that
//          would now open; (2) OnCardTapped consults HeartfireService.HasCharge
//          BEFORE RaidDeployScreen.Open, refuses in HeartfireService.BlockedMessage
//          words through a toast, and traces; (3) the door READS and never SPENDS -
//          the spend is at the entry seam (RaidDeployController.TryInstall), which
//          still spends and still carries its empty-pool FlowTrace.Fail tripwire
//          (never strip FlowTrace). Proven RED first against the pre-WO tree: that
//          tree's OnCardTapped refused on IsOnCooldown(id) and had zero Heartfire
//          mentions, so F1 and F2 both fired. One-line mutation that must red it:
//          re-insert `if (RaidCooldownService.IsOnCooldown(id)) return;` in
//          OnCardTapped (F1), or replace `!HeartfireService.HasCharge` with `false`
//          (F2). Two lockouts "reads as a bug" - this is the pin that keeps it one.
//
//   PIN I  EXACTLY TWO SOURCES, AND THE SECOND ONE IS BOUNDED.
//          Added 2026-09-10 (WO-1678/HEART-005) with the owner's Q-HEARTFIRE
//          ruling: "allow a second source for stakers - the single-source lint is
//          re-pointed to permit exactly Heartfire Spark." PIN B is UNCHANGED and
//          still forbids a balance; what moved is the source COUNT. So this pin
//          asserts the ruled seam EXISTS (a permit whose subject was deleted is a
//          lie) and that it is CLAMPED at the same ceiling as time, grants nothing
//          to a full pool and never moves the accrual stamp - the three properties
//          the ruling was granted on - and that no THIRD mutator has appeared
//          beside it. Driven on the pure function, like PIN A.
//
// Standalone:
//   -Method DeNelle.Editor.Regression.HeartfireRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    public static class HeartfireRegression
    {
        // Relative to Application.dataPath.
        private const string CoreRel    = "_Modules/Core/State/HeartfireCharges.cs";
        private const string ServiceRel = "_Modules/Village/World/Camps/HeartfireService.cs";
        private const string SceneCfgResRel    = "Resources/Data/Canonical/scene-configs.json";
        private const string SceneCfgStreamRel = "StreamingAssets/Data/Canonical/scene-configs.json";
        // PIN F: the ONE door and the ONE entry seam.
        private const string SelectRel = "_Modules/Village/Hero/RaidSelectionScreen.cs";
        private const string DeployRel = "_Modules/Village/Troops/RaidDeployController.cs";

        private const double Hour = 3600d * 1000d;   // one hour in unix-MS

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "heartfire: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Batchmode entry point (marker on a fresh log; never the exit code).</summary>
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log(ok ? "HEARTFIRE_OK " + reason : "HEARTFIRE_FAIL " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var f = new List<string>();

            RegenTableCases(f);        // PIN A
            CopyCases(f);              // PIN A (words half)
            CurrencyLintCases(f);      // PIN B
            NamingCases(f);            // PIN C
            ClockLintCases(f);         // PIN D
            SpendRoomCases(f);         // PIN E
            DoorGateCases(f);          // PIN F
            PlateCopyCases(f);         // PIN G (the plate says what a charge BUYS)
            IntroducedCases(f);        // PIN H (the game introduces the word at all)
            SecondSourceCases(f);      // PIN I (the ONE ruled second source, capped)

            if (f.Count == 0)
            {
                reason = "HEARTFIRE OK -- the pool regenerates 0->1->2->3 on the ruled interval and " +
                         "STOPS at the ceiling with no hidden backlog; spending one leaves the rest and " +
                         "does not restart the accrual window; a backwards clock can neither shorten the " +
                         "wait nor conjure a charge; Heartfire has no balance, no wallet row, no cap and " +
                         "no vendor anywhere; no shipped string says 'Raid Order' and 'Marches' is not a " +
                         "noun for the pool; the service reads TimeSource only; the rekindle interval " +
                         "is no longer than the shortest authored (retained, superseded) camp cooldown; " +
                         "and the raid door consults HasCharge ONLY -- no RaidCooldownService reference " +
                         "anywhere on RaidSelectionScreen, the refusal is the Heart's sentence, the door " +
                         "reads and the entry seam spends, with its empty-pool Fail tripwire intact; " +
                         "the plate names what a charge BUYS at both states; the guide, the " +
                         "introduction dialogue and the tutorial step all carry the ruled sentence; " +
                         "and Heartfire has EXACTLY TWO sources -- time, and the ruled Heartfire Spark, " +
                         "which is clamped at the same ceiling, grants nothing to a full pool, never " +
                         "moves the accrual stamp and is bought by nobody";
                return true;
            }
            reason = "HEARTFIRE FAIL x" + f.Count + ": " + string.Join(" | ", f);
            return false;
        }

        // =====================================================================
        //  PIN A -- the acceptance table, driven on the pure function
        // =====================================================================

        private static void RegenTableCases(List<string> f)
        {
            const int Max = 3;
            const double Regen = 4d * 3600d;   // seconds

            // The SHIPPED defaults must be the ones this table is written against. If a
            // tunable default moves, the table below stops describing the game and the
            // failure has to name that, not silently re-derive.
            if (HeartfireCharges.MaxChargesDefault != Max)
                f.Add("A0 the shipped Heartfire ceiling is " + HeartfireCharges.MaxChargesDefault +
                      ", not " + Max + " -- canon section 4 says three charges, and this whole table " +
                      "is written against three");
            if (Math.Abs(HeartfireCharges.RegenSecondsDefault - Regen) > 0.5d)
                f.Add("A0 the shipped rekindle interval is " + HeartfireCharges.RegenSecondsDefault +
                      "s, not " + Regen + "s -- canon section 4 says one charge every four hours");

            double t = 1_700_000_000_000d;   // an arbitrary fixed epoch; only deltas matter

            // The WO's acceptance table, row for row. Each row RE-READS the pool stamped at
            // T with 0 charges, so a row cannot be carried by the row before it.
            var atT = new HeartfireCharges.Pool(0, t, true);

            AssertCharges(f, "A1 +0h", atT, t + 0d * Hour, Max, Regen, 0);
            AssertCharges(f, "A2 +4h", atT, t + 4d * Hour, Max, Regen, 1);
            AssertCharges(f, "A3 +8h", atT, t + 8d * Hour, Max, Regen, 2);
            AssertCharges(f, "A4 +12h", atT, t + 12d * Hour, Max, Regen, 3);
            AssertCharges(f, "A5 +24h", atT, t + 24d * Hour, Max, Regen, 3);

            // Just SHORT of an interval must not round up. A pool that grants at 3h59m is
            // a four-hour gate in name only.
            AssertCharges(f, "A6 +3h59m", atT, t + 4d * Hour - 60d * 1000d, Max, Regen, 0);

            // ── The spend row: spending one at +12h leaves 2 ───────────────────────
            var full = HeartfireCharges.Regenerate(atT, t + 12d * Hour, Max, Regen, out _);
            if (!HeartfireCharges.TrySpend(full, out var afterSpend))
                f.Add("A7 TrySpend refused with a FULL pool -- the march could never start");
            else if (afterSpend.Charges != 2)
                f.Add("A7 spending one charge at +12h left " + afterSpend.Charges + ", expected 2");

            // Spending must NOT restart the accrual window (a player who marches the instant
            // a charge lands would otherwise silently lose the partial progress toward the
            // next one -- a punishment nobody authored).
            if (Math.Abs(afterSpend.LastRegenUnixMs - full.LastRegenUnixMs) > 0.5d)
                f.Add("A7b spending moved the accrual stamp from " + full.LastRegenUnixMs.ToString("F0") +
                      " to " + afterSpend.LastRegenUnixMs.ToString("F0") + " -- marching must never " +
                      "restart the rekindle clock");

            // ── NO HIDDEN BACKLOG. A pool that sat FULL for two days must not refill the
            // instant a charge is spent. This is the defect the "+24h is still 3" row exists
            // to set up, and this is the row that actually catches it.
            var sattedFull = HeartfireCharges.Regenerate(atT, t + 48d * Hour, Max, Regen, out _);
            if (!HeartfireCharges.TrySpend(sattedFull, out var spentAfterSitting))
                f.Add("A8 setup: a pool that sat FULL for two days REFUSED a spend -- the backlog row " +
                      "below would then be measuring an unspent pool, not a spent one");
            var oneSecondLater = HeartfireCharges.Regenerate(spentAfterSitting, t + 48d * Hour + 1000d,
                                                            Max, Regen, out _);
            if (oneSecondLater.Charges != 2)
                f.Add("A8 a pool that sat FULL for two days refilled to " + oneSecondLater.Charges +
                      " one second after a spend -- it banked a hidden backlog, which is a stacking " +
                      "pool with no ceiling");

            // ── An EMPTY pool refuses, and refuses without changing anything ───────
            var empty = new HeartfireCharges.Pool(0, t, true);
            if (HeartfireCharges.TrySpend(empty, out var afterEmptySpend))
                f.Add("A9 TrySpend SUCCEEDED on an empty pool -- the gate does not exist");
            else if (afterEmptySpend.Charges != 0 ||
                     Math.Abs(afterEmptySpend.LastRegenUnixMs - empty.LastRegenUnixMs) > 0.5d)
                f.Add("A9 a refused spend still mutated the pool -- a refusal must change nothing");

            // ── A BACKWARDS clock can neither shorten the wait nor conjure a charge ─
            // Arithmetically identical to (and cheaper than) moving a clock: push the STAMP
            // into the future. RaidCooldownRegression case 3's precedent.
            var future = new HeartfireCharges.Pool(0, t + 10d * 60d * 1000d, true);
            var repaired = HeartfireCharges.Regenerate(future, t, Max, Regen, out int backGranted);
            if (backGranted != 0 || repaired.Charges != 0)
                f.Add("A10 a BACKWARDS clock granted " + backGranted + " charge(s) -- rolling the phone " +
                      "clock back must never manufacture Heartfire");
            double waitAfterBack = HeartfireCharges.SecondsToNextCharge(repaired, t, Max, Regen);
            if (waitAfterBack > Regen + 1d)
                f.Add("A10 after a backwards clock the wait is " + waitAfterBack.ToString("F0") +
                      "s, LONGER than one full " + Regen.ToString("F0") + "s interval -- an honest " +
                      "player who crossed a timezone would be stalled. REFUSE, DON'T PUNISH");

            // ── An UNSTAMPED pool must not resolve ~57 years of accrual off the epoch ─
            var unstamped = new HeartfireCharges.Pool(0, 0d, false);
            var seeded = HeartfireCharges.Regenerate(unstamped, t, Max, Regen, out int epochGranted);
            if (epochGranted != 0)
                f.Add("A11 an unstamped pool granted " + epochGranted + " charge(s) off the epoch -- " +
                      "that looks identical to a working regen and would hide a real defect forever");
            if (Math.Abs(seeded.LastRegenUnixMs - t) > 0.5d)
                f.Add("A11 an unstamped pool was not anchored at now -- it will re-detect this every read");

            // ── The countdown must count DOWN and never lie ────────────────────────
            double next = HeartfireCharges.SecondsToNextCharge(atT, t + 1d * Hour, Max, Regen);
            if (Math.Abs(next - 3d * 3600d) > 1d)
                f.Add("A12 one hour into a four-hour rekindle the countdown reads " + next.ToString("F0") +
                      "s, expected 10800s");
            var fullPool = new HeartfireCharges.Pool(Max, t, true);
            if (HeartfireCharges.SecondsToNextCharge(fullPool, t, Max, Regen) != 0d)
                f.Add("A13 a FULL pool reports a countdown -- there is nothing pending to count down to");
        }

        private static void AssertCharges(List<string> f, string label, HeartfireCharges.Pool from,
                                          double nowUnixMs, int max, double regen, int expected)
        {
            var got = HeartfireCharges.Regenerate(from, nowUnixMs, max, regen, out _);
            if (got.Charges != expected)
                f.Add(label + " expected " + expected + " charge(s), got " + got.Charges);
            if (got.Charges > max)
                f.Add(label + " exceeded the ceiling (" + got.Charges + " > " + max + ")");
        }

        // =====================================================================
        //  PIN A (words) -- the copy is the point of the ticket
        // =====================================================================

        private static void CopyCases(List<string> f)
        {
            // THE sentence. Not "you may not raid because TIMER", but the Heart is not ready
            // to send you back yet (canon section 4). And it must always name the wait: a
            // player told "no" with no "when" cannot act on it.
            string blocked = HeartfireCharges.BlockedMessage(3d * 3600d + 42d * 60d + 18d);
            if (blocked.IndexOf("Heart is not ready", StringComparison.OrdinalIgnoreCase) < 0)
                f.Add("the refusal '" + blocked + "' does not say the Heart is not ready -- the rename " +
                      "is the whole ticket, and a bare timer sentence is what it replaced");
            if (blocked.IndexOf("3:42:18", StringComparison.Ordinal) < 0)
                f.Add("the refusal '" + blocked + "' does not name the wait");
            AssertAscii(f, "refusal", blocked);

            // FlameRow remains the trace form; the player-facing plate binds these same states
            // to greyscale-distinct Images (WO-1419).
            string two = HeartfireCharges.FlameRow(2, 3);
            string three = HeartfireCharges.FlameRow(3, 3);
            string zero = HeartfireCharges.FlameRow(0, 3);
            if (two == three || two == zero)
                f.Add("FlameRow renders different charge counts identically ('" + two + "') -- the state " +
                      "would only be readable by colour, which says nothing to a colourblind player");
            bool[] twoStates = HeartfireCharges.FlameStates(2, 3);
            bool[] threeStates = HeartfireCharges.FlameStates(3, 3);
            if (twoStates.Length != 3 || !twoStates[0] || !twoStates[1] || twoStates[2])
                f.Add("FlameStates(2,3) does not expose two lit slots followed by one spent slot");
            if (threeStates.Length != 3 || !threeStates[0] || !threeStates[1] || !threeStates[2])
                f.Add("FlameStates(3,3) does not expose a fully lit pool");
            AssertAscii(f, "flame row", two);

            // Out-of-range inputs must clamp, never throw and never render a ragged row.
            if (HeartfireCharges.FlameRow(9, 3) != three)
                f.Add("FlameRow(9,3) did not clamp to the ceiling");
            if (HeartfireCharges.FlameRow(-4, 3) != zero)
                f.Add("FlameRow(-4,3) did not clamp to empty");

            string fullLine = HeartfireCharges.RekindleLine(3, 3, 0d);
            if (fullLine.IndexOf("full", StringComparison.OrdinalIgnoreCase) < 0)
                f.Add("a FULL pool's line reads '" + fullLine + "' -- it must say so, not count down to nothing");
            string waitLine = HeartfireCharges.RekindleLine(1, 3, 90d);
            if (waitLine.IndexOf("1:30", StringComparison.Ordinal) < 0)
                f.Add("the rekindle line '" + waitLine + "' does not carry the countdown");
            AssertAscii(f, "rekindle line", waitLine);
            AssertAscii(f, "count label", HeartfireCharges.CountLabel(2, 3));

            // The clock formatter must not round a real wait down to nothing: a UI that says
            // "0:00" while still refusing reads as a frozen game (the WO-1110 dead-tap shape).
            if (HeartfireCharges.Clock(0.4d) == "0:00")
                f.Add("Clock(0.4s) rounded a live wait down to 0:00 -- a refused march with a zeroed " +
                      "countdown reads as a broken button");
        }

        private static void AssertAscii(List<string> f, string what, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] <= 127) continue;
                f.Add("the " + what + " string carries non-ASCII ('" + s + "') -- TMP renders it as tofu " +
                      "on the mobile font atlas");
                return;
            }
        }

        // =====================================================================
        //  PIN B -- HEARTFIRE IS A CHARGE, NOT A CURRENCY
        // =====================================================================

        private static void CurrencyLintCases(List<string> f)
        {
            // Comments and string literals are STRIPPED by ReadCode, which matters in both
            // directions here: this file's own headers say the words "wallet", "vendor" and
            // "currency" repeatedly while forbidding them, and a lint that read comments
            // would fail on the prose that exists to prevent the defect.
            string core = SourceLint.ReadCode(CoreRel, f);
            string svc  = SourceLint.ReadCode(ServiceRel, f);

            // Every symbol that would mean a balance had appeared. This is a DENY list on
            // purpose: the ways to add a currency are few and named, while the ways to add a
            // charge are many, so denying is the direction that stays true as the code grows.
            string[] banned =
            {
                "ResourceType", "ResourceCost", "CurrencyKind", "Wallet", "PurchaseCatalog",
                "PackStore", "AddResource", "SpendResource", "TrySpendResources", "Vendor",
                "StorageCap", "TownBankCapacity", "Price",
            };

            AssertNoBanned(f, "HeartfireCharges", core, banned);
            AssertNoBanned(f, "HeartfireService", svc, banned);

            // The catalogs a currency would have to be authored into. A Heartfire row in any
            // of them means somebody made it buyable, cappable or tradeable.
            // resources.json was listed here and has NEVER existed in git (2026-09-04, checked);
            // the guard used to swallow that silently. The two real resource-authoring files stay.
            AssertNoHeartfireRow(f, "Resources/Data/Canonical/storage-caps.json");
            AssertNoHeartfireRow(f, "Resources/Data/Canonical/packs.json");
            AssertNoHeartfireRow(f, "StreamingAssets/Data/Canonical/storage-caps.json");
            AssertNoHeartfireRow(f, "StreamingAssets/Data/Canonical/packs.json");

            // The enum itself. NO NEW CURRENCY: Wood, Iron, Food, Gold, Crystals is the set.
            string enums = SourceLint.ReadCode("_Modules/Core/State/Enums.cs", null);
            if (!string.IsNullOrEmpty(enums) &&
                enums.IndexOf("Heartfire", StringComparison.OrdinalIgnoreCase) >= 0)
                f.Add("Core/State/Enums.cs now names Heartfire -- a charge has been promoted into an " +
                      "enum that carries currencies. Wood, Iron, Food, Gold, Crystals is the set");

            // And the shape of the pool itself: two numbers and a diagnostic flag. A pool that
            // grows an "amount", a "balance" or a "max" FIELD is on its way to being money.
            if (!string.IsNullOrEmpty(core))
            {
                if (core.IndexOf("public double Balance", StringComparison.Ordinal) >= 0 ||
                    core.IndexOf("public int Balance", StringComparison.Ordinal) >= 0 ||
                    core.IndexOf("public int Amount", StringComparison.Ordinal) >= 0)
                    f.Add("HeartfireCharges.Pool grew a Balance/Amount field -- if the implementation " +
                          "grows a balance, it is wrong (WO-1379 section 2)");
            }
        }

        private static void AssertNoBanned(List<string> f, string where, string code, string[] banned)
        {
            if (string.IsNullOrEmpty(code)) return;
            for (int i = 0; i < banned.Length; i++)
            {
                if (code.IndexOf(banned[i], StringComparison.Ordinal) < 0) continue;
                f.Add(where + " now references '" + banned[i] + "' -- Heartfire is a CHARGE, never a " +
                      "currency: never earned, traded, stored, gifted or bought. If the implementation " +
                      "grows a balance, it is wrong (WO-1379 section 2)");
            }
        }

        private static void AssertNoHeartfireRow(List<string> f, string relativeToAssets)
        {
            string path = Path.Combine(Application.dataPath, relativeToAssets);
            string text = TryReadText(path);
            if (text == null)
            {
                // Fixture-absent -> FAIL naming the path (hollow-pass rule): an unreadable catalog
                // here would otherwise green this case having asserted nothing about it.
                f.Add(relativeToAssets + " could not be read, so the no-Heartfire-row check did not run");
                return;
            }
            if (text.IndexOf("heartfire", StringComparison.OrdinalIgnoreCase) >= 0)
                f.Add(relativeToAssets + " now carries a Heartfire row -- that file authors resources, " +
                      "storage caps or purchasable packs, and Heartfire is none of those things");
        }

        // =====================================================================
        //  PIN C -- "Raid Order" is dead; "march" survives as the VERB
        // =====================================================================

        private static void NamingCases(List<string> f)
        {
            // The retired name, and the superseded FIRST-PASS name (canon section 2:
            // implementing a superseded name is a defect, not a preference). Both are
            // checked in the two places a player could actually read them: the Heartfire
            // sources and the canonical string tables.
            string[] files =
            {
                CoreRel, ServiceRel,
            };
            for (int i = 0; i < files.Length; i++)
            {
                string code = SourceLint.ReadCode(files[i], null);
                if (string.IsNullOrEmpty(code)) continue;
                if (code.IndexOf("RaidOrder", StringComparison.OrdinalIgnoreCase) >= 0)
                    f.Add(files[i] + " still names 'Raid Order' in code -- the player is the ruler and " +
                          "nobody issues them orders (canon section 4)");
            }

            // Player-facing copy: the canon string tables. "Raid Orders" must be gone; the
            // VERB "march" is explicitly NOT banned, so nothing here looks for it.
            AssertNoRaidOrderCopy(f, "Resources/Data/Canonical/canon-strings.json");
            AssertNoRaidOrderCopy(f, "StreamingAssets/Data/Canonical/canon-strings.json");

            // The pool's own name must be the canon one, and must not have quietly become
            // the superseded first-pass noun.
            if (!string.Equals(HeartfireCharges.Name, "Heartfire", StringComparison.Ordinal))
                f.Add("the pool calls itself '" + HeartfireCharges.Name + "' -- canon section 2 rules " +
                      "the name Heartfire and records 'Marches' as the superseded first pass");
        }

        private static void AssertNoRaidOrderCopy(List<string> f, string relativeToAssets)
        {
            string text = TryReadText(Path.Combine(Application.dataPath, relativeToAssets));
            if (text == null)
            {
                f.Add(relativeToAssets + " could not be read, so the no-'Raid Order' check did not run");
                return;
            }
            if (text.IndexOf("Raid Order", StringComparison.OrdinalIgnoreCase) >= 0)
                f.Add(relativeToAssets + " still ships the string 'Raid Order' -- that name is dead " +
                      "(canon section 4)");
        }

        // =====================================================================
        //  PIN D -- the clock is TimeSource, never the device clock
        // =====================================================================

        private static void ClockLintCases(List<string> f)
        {
            string svc = SourceLint.ReadCode(ServiceRel, f);
            if (string.IsNullOrEmpty(svc)) return;

            if (svc.IndexOf("DateTime.UtcNow", StringComparison.Ordinal) >= 0 ||
                svc.IndexOf("DateTimeOffset.UtcNow", StringComparison.Ordinal) >= 0)
                f.Add("HeartfireService reads the DEVICE clock -- the pool would refill in ten seconds " +
                      "for anyone who opens Settings > Date & Time, and every behavioural case in this " +
                      "file would still pass. Read TimeSource.NowUnixMs()");
            if (svc.IndexOf("TimeSource.NowUnixMs()", StringComparison.Ordinal) < 0)
                f.Add("HeartfireService never reads TimeSource.NowUnixMs() -- the server-anchored clock " +
                      "seam is not being read at all");
            if (svc.IndexOf("TimeSource.IsServerAnchored", StringComparison.Ordinal) < 0)
                f.Add("HeartfireService never records TimeSource.IsServerAnchored -- an offline rekindle " +
                      "would be indistinguishable from a trusted one and nothing could reconcile it");

            // The Core half must stay clock-FREE. That asymmetry is what makes reading the
            // wrong clock impossible from the file that owns the arithmetic.
            string core = SourceLint.ReadCode(CoreRel, f);
            if (!string.IsNullOrEmpty(core))
            {
                if (core.IndexOf("UtcNow", StringComparison.Ordinal) >= 0)
                    f.Add("HeartfireCharges now reads a clock -- 'now' is a PARAMETER there on purpose, " +
                          "which is what keeps the whole regen table drivable by an oracle");
                if (core.IndexOf("UnityEngine", StringComparison.Ordinal) >= 0)
                    f.Add("HeartfireCharges now references UnityEngine -- the pure half must stay pure");
            }

            // No silent failures (CLAUDE.md section 12): the refusal path must SAY it refused.
            var spend = SourceLint.Body(svc, @"public\s+static\s+bool\s+TrySpend\s*\(\s*string\s+reason\s*\)");
            if (string.IsNullOrEmpty(spend))
                f.Add("HeartfireService.TrySpend(string) not found -- the spend seam moved");
            else if (spend.IndexOf("FlowTrace", StringComparison.Ordinal) < 0)
                f.Add("HeartfireService.TrySpend has no FlowTrace line -- a refused march would be a " +
                      "SILENT no-op, and CLAUDE.md section 12 forbids silent failures");
        }

        // =====================================================================
        //  PIN E -- a player holding Heartfire always has somewhere to spend it
        // =====================================================================

        private static void SpendRoomCases(List<string> f)
        {
            // WO-1379 retired the per-camp wall AS A GATE (PIN F), so "somewhere to spend it"
            // is structural now. This case stays as the TRIPWIRE on the retained field:
            // raidCooldownSeconds is superseded, not deleted and not retuned, and the shortest
            // authored value is MEASURED out of the canonical data rather than restated here --
            // a number copied from a doc is hearsay (CLAUDE.md section 11B).
            double shortest = ShortestAuthoredCooldownSeconds(SceneCfgResRel, out int campCount, f);

            if (campCount <= 0)
            {
                f.Add("scene-configs.json authors NO camp with a raidCooldownSeconds -- either the raid " +
                      "targets are gone or the field was renamed, and the spend-room criterion cannot " +
                      "be evaluated either way");
                return;
            }

            if (HeartfireCharges.RegenSecondsDefault > shortest + 0.5d)
                f.Add("a Heartfire charge rekindles every " + HeartfireCharges.RegenSecondsDefault +
                      "s but the SHORTEST authored camp cooldown is " + shortest.ToString("F0") + "s -- a " +
                      "player can be handed a charge with every camp still recovering, which breaks the " +
                      "WO-1379 criterion 'a player holding Heartfire always has somewhere to spend it'. " +
                      "The fix is NOT to shorten raidCooldownSeconds (that file's authoring note explains " +
                      "at length why those hours are not the lever) -- it is an owner ruling on the stack");

            // The two canonical copies must agree, because RESOURCES WINS at runtime and the
            // build the owner plays is the one that read the other file.
            double shortestStream = ShortestAuthoredCooldownSeconds(SceneCfgStreamRel, out int streamCount, null);
            if (streamCount != campCount || Math.Abs(shortestStream - shortest) > 0.5d)
                f.Add("the Resources and StreamingAssets copies of scene-configs.json disagree about the " +
                      "raid cooldowns (" + campCount + " camps / shortest " + shortest.ToString("F0") +
                      "s vs " + streamCount + " / " + shortestStream.ToString("F0") + "s) -- Resources " +
                      "wins at runtime, so the twin is a lie waiting to be read");
        }

        /// <summary>
        /// Smallest positive authored <c>raidCooldownSeconds</c> in a scene-configs file, and
        /// how many were found. Deliberately a plain scan rather than a JSON parse: the file's
        /// own SCHEMA BLOCK carries the key with a prose string value, and a numeric parse is
        /// what tells the two apart without dragging a serializer into an oracle.
        /// </summary>
        private static double ShortestAuthoredCooldownSeconds(string relativeToAssets, out int count,
                                                              List<string> f)
        {
            count = 0;
            double shortest = double.MaxValue;
            string text = TryReadText(Path.Combine(Application.dataPath, relativeToAssets));
            if (text == null)
            {
                if (f != null) f.Add("scene-configs.json missing at " + relativeToAssets);
                return 0d;
            }

            const string Needle = "\"raidCooldownSeconds\"";
            int i = text.IndexOf(Needle, StringComparison.Ordinal);
            while (i >= 0)
            {
                int colon = text.IndexOf(':', i + Needle.Length);
                if (colon > 0)
                {
                    int j = colon + 1;
                    while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
                    int start = j;
                    while (j < text.Length && (char.IsDigit(text[j]) || text[j] == '.')) j++;
                    if (j > start &&
                        double.TryParse(text.Substring(start, j - start),
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double v) &&
                        v > 0d)
                    {
                        count++;
                        if (v < shortest) shortest = v;
                    }
                }
                i = text.IndexOf(Needle, i + Needle.Length, StringComparison.Ordinal);
            }

            return count > 0 ? shortest : 0d;
        }

        private static string TryReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
        }

        // =====================================================================
        //  PIN G -- THE PLATE SAYS WHAT A CHARGE BUYS, NOT ONLY THAT IT IS FULL
        // =====================================================================
        // WO-1415, owner felt-test 2026-09-05 on build 2026.09.05.356468, verbatim:
        // "Heartfire is full, i dont understand as a new player what to do with that."
        // The plate reported a STATE with no consequence attached while Heartfire was the
        // ONE gate on raiding (WO-1379). The owner ruled the exact plate strings, and they
        // are asserted BYTE-EXACT here because "roughly this" is how copy drifts back.
        //
        // MUTATION THAT REDS IT (one line): delete the " + SpendTag" from
        // HeartfireCharges.PlateLabel - the plate goes back to a bare state word and G1/G2
        // both fail by name.
        private static void PlateCopyCases(List<string> f)
        {
            string charged = HeartfireCharges.PlateLabel(3, 3);
            string spentLabel = HeartfireCharges.PlateLabel(0, 3);
            string spentTail = HeartfireCharges.PlateRekindle(0, 3, 3d * 3600d + 12d * 60d);

            // G1 - the ruled charged string, exactly.
            if (!string.Equals(charged, "Heartfire 3/3 (raids)", StringComparison.Ordinal))
                f.Add("G1 the charged plate row reads '" + charged + "' -- the owner ruled " +
                      "'Heartfire 3/3 (raids)' (WO-1415). The parenthetical is what makes the row say " +
                      "what a charge BUYS in a width that seats on one fitted line");

            // G2 - the ruled spent string, composed exactly as the View composes it.
            string spent = HeartfireCharges.PlateCombined(spentLabel, spentTail);
            if (!string.Equals(spent, "Heartfire 0/3 (raids) - next in 3h 12m", StringComparison.Ordinal))
                f.Add("G2 the spent plate reads '" + spent + "' -- the owner ruled " +
                      "'Heartfire 0/3 (raids) - next in 3h 12m'");

            // G3 - it is not a bare state word. This is the ticket in one assertion: a row that
            // says only "Heartfire" or only "Heartfire is full" is the defect being fixed.
            if (charged.IndexOf(HeartfireCharges.SpendTag, StringComparison.Ordinal) < 0 ||
                string.Equals(charged, HeartfireCharges.Name, StringComparison.Ordinal))
                f.Add("G3 the plate row '" + charged + "' carries no consequence clause -- a state word " +
                      "with nothing attached is exactly what the owner could not act on");

            // G4 - a full pool shows NO countdown row (there is nothing to count down to), and a
            // live wait is never rendered as zero (the Clock(0.4s) rule, one row down).
            if (HeartfireCharges.PlateRekindle(3, 3, 0d).Length != 0)
                f.Add("G4 a FULL pool still paints a rekindle row ('" +
                      HeartfireCharges.PlateRekindle(3, 3, 0d) + "') -- there is nothing pending to name");
            if (HeartfireCharges.ShortWait(20d) == "0m")
                f.Add("G4 ShortWait(20s) rounded a live wait down to '0m' -- a refused march with a zeroed " +
                      "wait reads as a broken button");

            AssertAscii(f, "plate row", charged);
            AssertAscii(f, "plate rekindle row", spentTail);
        }

        // =====================================================================
        //  PIN H -- SOMETHING IN THE GAME ACTUALLY INTRODUCES THE WORD
        // =====================================================================
        // THE MEASURED RED THIS TICKET WAS MINTED ON (2026-09-05): grep -ci "heartfire"
        // returned ZERO in guide-content.json, dialogues.json AND tutorial-steps.json. The
        // word was introduced to the player by being printed on a HUD plate. This pin holds
        // all three closed at once, and holds the SENTENCE identical across them -- the
        // three surfaces are copy, and copy in three files is exactly the duplicated state
        // CLAUDE.md sections 2/5/16 exist for. One owner: HeartfireCharges.SpendSentence.
        //
        // MUTATION THAT REDS IT (one line each): delete the "heartfire" section from
        // guide-content.json (H1); reword the sentence in tut_ctx_heartfire's line (H2);
        // delete the ctx_heartfire step (H3).
        private const string GuideResRel  = "Resources/Data/Canonical/guide-content.json";
        private const string DlgResRel    = "Resources/Data/Canonical/dialogue/dialogues.json";
        private const string StepsResRel  = "Resources/Data/Canonical/tutorial/tutorial-steps.json";

        private static void IntroducedCases(List<string> f)
        {
            CheckIntroFile(f, "H1", GuideResRel, "the Game Guide", true);
            CheckIntroFile(f, "H2", DlgResRel, "the introduction dialogue", true);
            // The STEP file carries the beat, not the copy: it names the word and the dialogue
            // it plays, while the sentence itself lives in the dialogue record.
            CheckIntroFile(f, "H3", StepsResRel, "the tutorial beat", false);
        }

        private static void CheckIntroFile(List<string> f, string tag, string relativeToAssets,
                                           string what, bool mustCarrySentence)
        {
            string text = TryReadText(Path.Combine(Application.dataPath, relativeToAssets));
            if (text == null)
            {
                f.Add(tag + " " + relativeToAssets + " is missing -- " + what + " cannot be checked");
                return;
            }
            if (text.IndexOf(HeartfireCharges.Name, StringComparison.OrdinalIgnoreCase) < 0)
                f.Add(tag + " " + relativeToAssets + " never says '" + HeartfireCharges.Name + "' -- " +
                      what + " is back to introducing the ONE gate on raiding by printing it on a HUD " +
                      "plate and hoping (the measured WO-1415 red: zero mentions in all three files)");
            if (mustCarrySentence &&
                text.IndexOf(HeartfireCharges.SpendSentence, StringComparison.Ordinal) < 0)
                f.Add(tag + " " + relativeToAssets + " does not carry the owner's sentence '" +
                      HeartfireCharges.SpendSentence + "' verbatim -- the plate, the guide and the " +
                      "introduction must say the same thing about what a charge buys, and a reworded " +
                      "copy in a data file is how that drifts");
        }

        // =====================================================================
        //  PIN F -- ONE gate on WHEN you may raid, at the ONE door, and it is Heartfire
        // =====================================================================

        private static void DoorGateCases(List<string> f)
        {
            // Comments and string literals are STRIPPED by ReadCode, so the retirement notes in
            // RaidSelectionScreen that NAME RaidCooldownService cannot trip F1, and a
            // HeartfireService.HasCharge that appears only in a log message cannot satisfy F2.
            string select = SourceLint.ReadCode(SelectRel, f);
            if (!string.IsNullOrEmpty(select))
            {
                // F1 -- the WHOLE raid surface, not only the door. The card used to paint
                // "Recovering - raidable in 12h" off the same service; a card that says "wait"
                // over a door that opens is wrong advice, so the file must not touch it at all.
                if (select.IndexOf("RaidCooldownService", StringComparison.Ordinal) >= 0)
                    f.Add("F1 RaidSelectionScreen references RaidCooldownService -- the per-camp wall is " +
                          "back on the raid surface. Owner ruling (WO-1379 section 3): Heartfire replaces " +
                          "the camp wall; one gate on WHEN you may raid. Two lockouts reads as a bug");
                if (select.IndexOf("IsOnCooldown(", StringComparison.Ordinal) >= 0)
                    f.Add("F1 RaidSelectionScreen calls IsOnCooldown -- a second WHEN-you-may-raid gate has " +
                          "reappeared. The recovery record is save evidence, never a door");

                var tap = SourceLint.Body(select, @"private\s+void\s+OnCardTapped\s*\(\s*string\s+id\s*\)");
                if (string.IsNullOrEmpty(tap))
                {
                    f.Add("F2 RaidSelectionScreen.OnCardTapped(string) not found -- the ONE door into " +
                          "RaidDeployScreen moved, and the Heartfire gate may no longer be on it");
                }
                else
                {
                    // F2 -- the door consults the charge, and does so BEFORE it opens.
                    int iGate = tap.IndexOf("HeartfireService.HasCharge", StringComparison.Ordinal);
                    int iOpen = tap.IndexOf("RaidDeployScreen.Open(", StringComparison.Ordinal);
                    if (iGate < 0)
                        f.Add("F2 OnCardTapped never consults HeartfireService.HasCharge -- a player with an " +
                              "EMPTY Heart can march, and the only thing left to notice is the entry seam's " +
                              "Fail line after the scene has already loaded");
                    else if (iOpen < 0)
                        f.Add("F2 OnCardTapped no longer opens RaidDeployScreen -- the lint has lost its anchor");
                    else if (iGate > iOpen)
                        f.Add("F2 OnCardTapped consults HasCharge AFTER opening the deploy screen -- the gate " +
                              "is downstream of the door it is supposed to guard");

                    // F3 -- the refusal is the Heart's sentence, on screen, and traced. Words, never
                    // a colour (the owner is red/green colourblind); never a silent no-op (the
                    // WO-1110 dead-tap defect shipped on this exact screen).
                    if (tap.IndexOf("HeartfireService.BlockedMessage(", StringComparison.Ordinal) < 0)
                        f.Add("F3 OnCardTapped does not show HeartfireService.BlockedMessage -- the refusal is " +
                              "not the Heart's sentence, so the rename did not reach the one place a player " +
                              "is actually told no");
                    if (tap.IndexOf("ShowToast(", StringComparison.Ordinal) < 0)
                        f.Add("F3 OnCardTapped has no ShowToast -- a refused tap would be a SILENT no-op");
                    if (tap.IndexOf("FlowTrace.Step(", StringComparison.Ordinal) < 0)
                        f.Add("F3 OnCardTapped has no FlowTrace line -- a refused or opened door leaves no " +
                              "[Flow:Heartfire] breadcrumb, and CLAUDE.md section 12 forbids that");

                    // F4 -- the door READS; only the entry seam SPENDS. A spend here would charge a
                    // player who backs out of the deploy screen without marching.
                    if (tap.IndexOf("HeartfireService.TrySpend(", StringComparison.Ordinal) >= 0)
                        f.Add("F4 OnCardTapped SPENDS Heartfire -- the spend belongs at the raid ENTRY seam " +
                              "(RaidDeployController.TryInstall); spending at the door double-charges a " +
                              "player who backs out of the deploy screen");
                }
            }

            // F5 -- the entry seam still spends, and its empty-pool tripwire is still there.
            // From the door that Fail is unreachable; it stays as the detector for any OTHER
            // path that loads a RaidBase_* scene without passing the door. Never strip FlowTrace.
            string deploy = SourceLint.ReadCode(DeployRel, f);
            if (!string.IsNullOrEmpty(deploy))
            {
                var install = SourceLint.Body(deploy, @"private\s+static\s+void\s+TryInstall\s*\(\s*string\s+sceneName\s*\)");
                if (string.IsNullOrEmpty(install))
                {
                    f.Add("F5 RaidDeployController.TryInstall(string) not found -- the raid ENTRY seam moved " +
                          "and the Heartfire spend may have gone with it");
                }
                else
                {
                    if (install.IndexOf("HeartfireService.TrySpend(", StringComparison.Ordinal) < 0)
                        f.Add("F5 RaidDeployController.TryInstall no longer spends Heartfire -- the door reads " +
                              "a charge that nothing ever consumes, so the pool is decoration");
                    if (install.IndexOf("FlowTrace.Fail(", StringComparison.Ordinal) < 0)
                        f.Add("F5 the empty-pool FlowTrace.Fail in TryInstall is gone -- instrumentation was " +
                              "STRIPPED (CLAUDE.md section 12 forbids it), and a bypassed door would now be " +
                              "silent");
                }
            }
        }

        // =====================================================================
        //  PIN I -- THE ONE RULED SECOND SOURCE, AND NO THIRD
        // =====================================================================
        //
        // Owner ruling 2026-09-10 (Q-HEARTFIRE, WO-1678/HEART-005), verbatim:
        //
        //     "allow a second source for stakers - the single-source lint is
        //      re-pointed to permit exactly Heartfire Spark."
        //
        // ⛔ "RE-POINTED TO PERMIT EXACTLY" IS THE WHOLE INSTRUCTION, AND IT HAS TWO
        // HALVES. PIN B still forbids Heartfire becoming a currency (no balance, no
        // wallet row, no price, no vendor, no cap row, no enum member) and none of
        // that moved. What moved is the SOURCE COUNT, from one to two - so this pin
        // asserts the second source EXISTS and is bounded, and that a THIRD has not
        // quietly appeared beside it. A permit with no bound is not a permit.
        //
        // ⚠ WHY THE POSITIVE HALF MATTERS AS MUCH AS THE NEGATIVE. A lint that only
        // forbade things would stay green if somebody deleted Spark and re-added a
        // faucet somewhere else. Asserting the ruled seam exists, by name, means the
        // ruled design is what the build is measured against.
        //
        // Driven against the PURE function, so it needs no save, no clock and no
        // scene - the same reasoning as PIN A.

        private static void SecondSourceCases(List<string> f)
        {
            const int Max = 3;
            double t = 1_700_000_000_000d;

            // I1 - it lights charges, up to the number asked for.
            var empty = new HeartfireCharges.Pool(0, t, true);
            int lit = HeartfireCharges.Spark(empty, 1, Max, out var afterOne);
            if (lit != 1 || afterOne.Charges != 1)
                f.Add("I1 Spark on an empty pool lit " + lit + " charge(s) leaving " + afterOne.Charges +
                      " -- the ruled second source must light exactly what the event authored");

            // I2 - THE CLAMP. This is the property the ruling was granted on: a staker
            // reaches the ceiling sooner, never holds more than an idle player who slept.
            var nearFull = new HeartfireCharges.Pool(Max - 1, t, true);
            int litNear = HeartfireCharges.Spark(nearFull, 5, Max, out var clamped);
            if (litNear != 1 || clamped.Charges != Max)
                f.Add("I2 Spark carried the pool to " + clamped.Charges + "/" + Max + " (lit " + litNear +
                      ") -- the second source MUST clamp at the same ceiling as time. An uncapped faucet " +
                      "influenced by a staked position is the currency the design forbids");

            // I3 - a FULL pool gains nothing, and says so by returning 0.
            var full = new HeartfireCharges.Pool(Max, t, true);
            int litFull = HeartfireCharges.Spark(full, 1, Max, out var stillFull);
            if (litFull != 0 || stillFull.Charges != Max)
                f.Add("I3 Spark on a FULL pool lit " + litFull + " leaving " + stillFull.Charges +
                      " -- a full pool must gain nothing from the second source");

            // I4 - a non-positive request is REFUSED, not silently treated as zero-and-fine.
            if (HeartfireCharges.Spark(empty, 0, Max, out _) != 0 ||
                HeartfireCharges.Spark(empty, -3, Max, out var negative) != 0 || negative.Charges != 0)
                f.Add("I4 Spark accepted a non-positive charge count -- an authoring error must never " +
                      "read as a grant, and must never remove a charge");

            // I5 - THE STAMP IS NOT TOUCHED. Moving it forward would push the next rekindle
            // away, so a spark would silently cost time and the reward would be part illusion.
            if (Math.Abs(afterOne.LastRegenUnixMs - t) > 0.5d)
                f.Add("I5 Spark moved the accrual stamp (" + afterOne.LastRegenUnixMs + " vs " + t +
                      ") -- a spark must be purely additive, never a charge that costs time");

            // I6 - THE SOURCE COUNT ITSELF. `out Pool` is how this file hands a caller a
            // MUTATED pool, so counting the parameter counts the mutators: TrySpend (spend)
            // and Spark (the ruled grant). Regenerate returns its pool instead and is the
            // third, named separately below. A FOURTH mutator arriving without a ruling
            // fails here, which is exactly the drift a behavioural test cannot see.
            //
            // ⚠ COUNTED BY REGEX ON A WORD BOUNDARY, not by the literal "out Pool " with a
            // trailing space. The text being counted has already been through
            // SourceLint.ReadCode, whose whitespace handling is not this pin's to assume -
            // and a miscount here would report "3 mutators, not 2" about a file nobody
            // changed, which is the failure mode that gets a pin deleted rather than read.
            string core = SourceLint.ReadCode(CoreRel, f);
            if (!string.IsNullOrEmpty(core))
            {
                int mutators = Regex.Matches(core, @"\bout\s+Pool\b").Count;
                if (mutators != 2)
                    f.Add("I6 HeartfireCharges now has " + mutators + " 'out Pool' mutators, not 2 -- " +
                          "Heartfire has exactly TWO sources (time via Regenerate, and the ruled Spark) " +
                          "and one sink (TrySpend). A new one needs an owner ruling, not a commit");

                if (core.IndexOf("public static int Spark(", StringComparison.Ordinal) < 0)
                    f.Add("I6 HeartfireCharges.Spark is GONE -- the ruled second source (owner, " +
                          "2026-09-10) was removed. The ruling and the code move together");

                if (core.IndexOf("public static Pool Regenerate(", StringComparison.Ordinal) < 0)
                    f.Add("I6 HeartfireCharges.Regenerate is gone -- the FIRST source (the passage of " +
                          "time) is the one Heartfire has always had");
            }

            // I7 - the service exposes exactly ONE grant seam, and it routes through the
            // clamped pure function rather than writing the pool itself.
            string svc = SourceLint.ReadCode(ServiceRel, f);
            if (!string.IsNullOrEmpty(svc))
            {
                int seams = CountOccurrences(svc, "public static int TryGrantSpark(");
                if (seams != 1)
                    f.Add("I7 HeartfireService declares " + seams + " TryGrantSpark seams, not 1 -- the " +
                          "ruled second source has exactly one door");

                if (svc.IndexOf("HeartfireCharges.Spark(", StringComparison.Ordinal) < 0)
                    f.Add("I7 HeartfireService.TryGrantSpark no longer calls HeartfireCharges.Spark -- a " +
                          "service that writes the pool itself has bypassed the ceiling clamp, which is " +
                          "the property the second source was permitted on");
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0;
            int at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }
    }
}

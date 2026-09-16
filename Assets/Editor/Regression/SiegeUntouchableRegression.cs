// =============================================================================
// SiegeUntouchableRegression — [siege-untouchable]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Entry point: Run(out string reason).
//
// ⭐ THE OWNER RULING THIS ENFORCES (2026-08-26, recorded at the end of
//    WorkOrders/WORK_ORDER_1026_IMPLEMENTATION_PLAN.md), verbatim:
//
//        "UNTOUCHABLE, absolutely: Crystals. SKR. Purchased goods. Equipped gear.
//         None of these may ever be damaged, taken, or put at risk by a siege —
//         not at any percentage, not under any cap."
//
//    and, in the same ruling:
//
//        "the game is LIVE and money is real. A bug that takes a purchased good is
//         not a balance issue, it is a refund and a one-star review. THE UNTOUCHABLE
//         LIST MUST BE ENFORCED BY AN ORACLE, NOT BY CARE."
//
// ! UPDATED 2026-08-27 -- NARROWLY, AND ONLY ON THE LOOTABLE HALF.
//    The owner's ruling of that date states the sets verbatim:
//        LOOTABLE      Wood, Iron, Stone, Coins
//        UNTOUCHABLE   Crystals, SKR, purchased goods, equipped gear
//    COINS therefore moved OUT of this suite's untouchable expectation and into the
//    lootable list, and the "coin"/"gold" fragments were dropped from the ledger-shape
//    ban because StakesLedger now legitimately carries a Coins bucket. Coins were never
//    on the owner's untouchable list in EITHER the 08-26 or the 08-27 ruling -- they sat
//    here only because the superseded 08-22 collector-loot ruling had no coin harvest to
//    loot.
//    ! THE UNTOUCHABLE HALF IS UNCHANGED AND UNWEAKENED. Crystals, SKR, purchased goods
//    and equipped gear are asserted exactly as hard as before, on both axes, and the
//    scanner self-test still proves the lint is not hollow.
//
// ⛔ WHY THIS SUITE EXISTS SEPARATELY FROM SiegeLossStakesRegression.
//    That suite proves the CURRENT stakes ruling is implemented correctly — it will
//    be rewritten every time the ruling changes, and it has been rewritten once
//    already (the 2026-08-21 flat-bank ruling was superseded on 08-22 and its whole
//    arithmetic deleted). This suite proves something that survives EVERY ruling:
//    a siege may never reach a crystal, an SKR balance, a purchased item or equipped
//    gear, whatever the theft rule of the day happens to be. Folding it into the
//    stakes suite would mean the guard rail gets rewritten alongside the thing it
//    guards — which is how a guard rail quietly disappears.
//
// ⛔ TWO INDEPENDENT AXES, DELIBERATELY. Neither relies on the other:
//    (A/B/C) BEHAVIOURAL — nothing non-lootable can be classified lootable, written
//            into a ledger, or even have a bucket to be written into.
//    (D)     SOURCE LINT — no file in the siege blast radius may so much as NAME a
//            purchased-goods / equipped-gear / SKR symbol. A siege that cannot
//            mention a purchased good cannot take one, and this catches a future
//            ruling that reaches for the wallet through a path the behavioural cases
//            have never heard of.
//
// ⛔ NO HOLLOW PASS. Case E runs the case-D scanner against a SYNTHETIC source that
//    is known to contain a banned symbol and FAILS if the scanner does not find it —
//    so a green case D means "the files are clean", never "the scanner did nothing".
//    A scanned file that is MISSING is a FAIL, by name, never a skip: every path in
//    the ledger below is a shipped file, so an absence is a rename this suite must
//    scream about rather than silently stop covering.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Core.Defense;
using DeNelle.Core.Economy;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// The standing guard on the owner's untouchable list: crystals, SKR, purchased goods and
    /// equipped gear are out of a siege's reach under every present and future stakes ruling.
    /// </summary>
    public static class SiegeUntouchableRegression
    {
        // =====================================================================
        //  The ledger of what a siege may touch, and what it may not name
        // =====================================================================

        /// <summary>
        /// The ONLY bank buckets a siege may ever take from. Hand-authored here rather than read
        /// from <c>StakeRules</c> on purpose: an oracle that derives its expectation from the code
        /// under test asserts nothing.
        ///
        /// <para>! UPDATED 2026-08-27, DELIBERATELY AND NARROWLY. The owner's ruling of that date
        /// states the lootable set verbatim -- <c>LOOTABLE Wood, Iron, Stone, Coins</c> /
        /// <c>UNTOUCHABLE Crystals, SKR, purchased goods, equipped gear</c> -- so COINS moved into
        /// this list. Coins were never on the owner's untouchable list in the 08-26 or 08-27
        /// ruling; they sat here because the superseded 08-22 collector-loot ruling had no coin
        /// harvest to loot. THE UNTOUCHABLE HALF IS UNCHANGED AND UNWEAKENED: crystals, SKR,
        /// purchased goods and equipped gear are asserted exactly as hard as before, on both axes.
        /// If a future ruling moves this list again, move it HERE, in the same change, and say why
        /// -- never by relaxing an assertion elsewhere.</para>
        ///
        /// <para>! "Food" IS "Stone" (owner: "food was depreicated and is stone"). BankResource has
        /// no Stone member and must never grow one -- it is a live save and wire key.</para>
        /// </summary>
        private static readonly BankResource[] Lootable =
        {
            BankResource.Wood,
            BankResource.Iron,
            BankResource.Stone,    // "STONE" player-facing -- BankResource has no Stone member
            BankResource.Coins,   // "GOLD" player-facing
        };

        /// <summary>
        /// Files that make up the siege blast radius — everything that could plausibly grow a
        /// line that takes something. Project-relative, forward slashes. A file here that has
        /// been renamed or deleted FAILS this suite rather than dropping out of coverage.
        /// </summary>
        private static readonly string[] SiegeBlastRadius =
        {
            "Assets/_Modules/Core/Defense/StakeRules.cs",
            "Assets/_Modules/Core/Defense/SiegeStakesBalance.cs",
            "Assets/_Modules/Core/Defense/DefenseReport.cs",
            "Assets/_Modules/Core/Defense/DefenseReportLedger.cs",
            "Assets/_Modules/Village/Waves/DefenseReportBuilder.cs",
            "Assets/_Modules/Village/Waves/SiegeSession.cs",
            "Assets/_Modules/Village/Waves/SiegeScheduler.cs",
            "Assets/_Modules/Village/Waves/SiegeSchedulerBootstrap.cs",
        };

        /// <summary>
        /// Symbols a siege file may never name IN CODE (comments are stripped before the scan —
        /// these files are full of prose explaining exactly what they must not do, and that prose
        /// is the documentation, not a violation).
        /// <para>Each entry is (symbol, what it would be reaching for) so a failure names the
        /// player-facing consequence, not just a token.</para>
        /// </summary>
        private static readonly (string Symbol, string Reaching)[] BannedSymbols =
        {
            ("OwnedItemIds",      "PURCHASED GOODS — GameState.OwnedItemIds is the store-purchase roster"),
            ("GearInventory",     "PURCHASED GOODS — GameState.GearInventory is shop-bought gear counts"),
            ("GearLevels",        "PURCHASED PROGRESS — GameState.GearLevels is paid-for gear progression"),
            ("EquippedRingId",    "EQUIPPED GEAR — the worn ring"),
            ("EquippedAmuletId",  "EQUIPPED GEAR — the worn amulet"),
            ("EquippedActiveIds", "EQUIPPED GEAR — the worn active-ability set"),
            ("ArenaWallet",       "SKR — the arena wager wallet"),
            ("ArenaProgress",     "SKR — the arena W/L + purse ledger"),
            ("PackStore",         "PURCHASED GOODS — the real-money pack store"),
            ("BoundWallet",       "SKR / WALLET IDENTITY — the player's on-chain wallet binding"),
        };

        // =====================================================================
        //  Entry point
        // =====================================================================

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            try
            {
                ClassificationCases(failures);   // A
                WriterCases(failures);           // B
                LedgerShapeCases(failures);      // C
                SourceLintCases(failures);       // D
                ScannerSelfTestCases(failures);  // E
            }
            catch (Exception ex)
            {
                failures.Add($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count == 0)
            {
                reason = "SIEGE UNTOUCHABLE OK -- every BankResource is classified exactly as the " +
                         "2026-08-27 ruling says (wood/iron/stone(=Food)/coins lootable; CRYSTALS " +
                         "NEVER, at any amount, under any cap); StakeRules.Add refuses every " +
                         "non-lootable bucket and still writes " +
                         "the lootable ones; StakesLedger carries NO purchased-goods, equipped-gear " +
                         "or SKR bucket for a future rule to hang off; no file in the siege blast " +
                         "radius NAMES a purchased/equipped/SKR symbol in code; and the scanner that " +
                         "proved that was itself proved against a planted violation";
                return true;
            }

            reason = $"SIEGE UNTOUCHABLE FAIL x{failures.Count}: " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  A — classification. THE WHOLE ENUM IS SWEPT, not the buckets we remembered.
        // =====================================================================

        /// <summary>
        /// Sweeps every declared <see cref="BankResource"/> value — not a hand-picked few — so a
        /// bucket ADDED to the enum later cannot default into lootability unnoticed. That is the
        /// N+1 call site the whole exemption exists to survive.
        /// </summary>
        private static void ClassificationCases(List<string> f)
        {
            var all = (BankResource[])Enum.GetValues(typeof(BankResource));
            if (all == null || all.Length == 0)
            {
                f.Add("classification: BankResource enumerated to NOTHING — the reflection seam moved");
                return;
            }

            foreach (var r in all)
            {
                bool expected = Array.IndexOf(Lootable, r) >= 0;
                bool actual = StakeRules.IsLootable(r);

                if (actual == expected) continue;

                f.Add(expected
                    ? $"classification: {r} SHOULD be lootable and is not — a siege can no longer " +
                      "report an earned-resource loss, so the consequence loop silently has no consequence"
                    : $"⛔ classification: {r} IS CLASSIFIED LOOTABLE AND MUST NEVER BE. The owner's " +
                      "2026-08-26 ruling puts crystals/SKR/purchased goods/equipped gear out of reach " +
                      "at ANY percentage under ANY cap. This is a live-money defect, not a balance one");
            }
        }

        // =====================================================================
        //  B — the writer. THE REFUSAL **AND** THE SUCCESS PATH.
        // =====================================================================

        /// <summary>
        /// A failure-only assertion would pass just as happily on a <c>StakeRules.Add</c> that
        /// refuses EVERYTHING — an oracle that proves the feature is broken. So both directions
        /// are asserted: non-lootable buckets are refused and leave the ledger untouched, and
        /// lootable buckets actually record.
        /// </summary>
        private static void WriterCases(List<string> f)
        {
            var all = (BankResource[])Enum.GetValues(typeof(BankResource));

            // --- the refusal, at an absurd amount so no cap/floor could excuse a partial take ---
            foreach (var r in all)
            {
                if (Array.IndexOf(Lootable, r) >= 0) continue;

                var ledger = StakeRules.Empty();
                bool wrote = StakeRules.Add(ledger, r, 999999);

                if (wrote)
                    f.Add($"⛔ writer: StakeRules.Add REPORTED WRITING {r} — the untouchable list is " +
                          "not enforced at the one writer");

                if (!AllBucketsZero(ledger, out string nonZero))
                    f.Add($"⛔ writer: adding {r} moved a bucket ({nonZero}) — a non-lootable resource " +
                          "found a home in the ledger");
            }

            // --- the SUCCESS path: the feature still works ---
            foreach (var r in Lootable)
            {
                var ledger = StakeRules.Empty();
                if (!StakeRules.Add(ledger, r, 7))
                {
                    f.Add($"writer [GOOD PATH]: StakeRules.Add refused {r}, which IS lootable — " +
                          "the oracle would otherwise pass on a writer that refuses everything");
                    continue;
                }

                int got = BucketOf(ledger, r);
                if (got != 7)
                    f.Add($"writer [GOOD PATH]: {r} recorded {got}, expected 7");
            }

            // --- a "loss" that gives resources back is a bug, not a stake ---
            foreach (var r in Lootable)
            {
                var ledger = StakeRules.Empty();
                if (StakeRules.Add(ledger, r, -50))
                    f.Add($"writer: a NEGATIVE {r} amount was accepted — a siege that hands resources " +
                          "back would read to the player as a reward for being attacked");
                if (!AllBucketsZero(ledger, out string nz))
                    f.Add($"writer: a negative {r} amount moved bucket {nz}");
            }
        }

        // =====================================================================
        //  C — shape. NO BUCKET FOR A FUTURE RULE TO HANG OFF.
        // =====================================================================

        /// <summary>
        /// The crystal bucket exists on <see cref="StakesLedger"/> for wire completeness and is
        /// pinned at zero by <c>SiegeLossStakesRegression</c>. What must NOT exist is any bucket
        /// for coins/gold, gear, items or SKR — because the cheapest way for a future ruling to
        /// take a purchased good is to find a field already waiting for it.
        /// </summary>
        private static void LedgerShapeCases(List<string> f)
        {
            // ! "coin"/"gold" were removed from this list on 2026-08-27: the owner's ruling makes
            //   COINS LOOTABLE, so StakesLedger legitimately carries a Coins bucket. Nothing else
            //   moved -- gear, items, SKR, equipped and purchased anything are still forbidden to
            //   have a field at all, because the cheapest way for a future ruling to take a
            //   purchased good is to find a bucket already waiting for it.
            string[] forbiddenFragments =
            {
                "gear", "item", "skr", "equip", "purchase", "pack", "wallet",
            };

            var t = typeof(StakesLedger);
            var members = new List<string>();

            foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                members.Add(fi.Name);
            foreach (var pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                members.Add(pi.Name);

            if (members.Count == 0)
            {
                f.Add("shape: StakesLedger reflected to ZERO public members — the type moved and this " +
                      "case is asserting nothing");
                return;
            }

            foreach (string m in members)
            {
                string lower = m.ToLowerInvariant();
                foreach (string frag in forbiddenFragments)
                {
                    if (!lower.Contains(frag)) continue;
                    f.Add($"⛔ shape: StakesLedger.{m} is a bucket for something the ruling puts out of " +
                          $"reach (matched '{frag}'). Its mere existence is the invitation — delete the " +
                          "field, do not rely on nobody filling it");
                }
            }
        }

        // =====================================================================
        //  D — the source lint over the siege blast radius
        // =====================================================================

        private static void SourceLintCases(List<string> f)
        {
            string repoRoot = RepoRoot();

            foreach (string rel in SiegeBlastRadius)
            {
                string full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(full))
                {
                    // ⛔ NOT A SKIP. Every path here is a shipped file; an absence means a rename
                    //    that silently dropped a file out of coverage, which is exactly the event
                    //    this suite must be loudest about.
                    f.Add($"⛔ lint: '{rel}' DOES NOT EXIST — a siege file was renamed or deleted and " +
                          "is no longer covered by the untouchable lint. Update SiegeBlastRadius in " +
                          "the SAME change that moved it");
                    continue;
                }

                string src;
                try { src = File.ReadAllText(full); }
                catch (Exception ex)
                {
                    f.Add($"⛔ lint: '{rel}' could not be read ({ex.GetType().Name}) — coverage lost");
                    continue;
                }

                foreach (var hit in ScanForBanned(src))
                    f.Add($"⛔ lint: {rel} names {hit.Symbol} in CODE — reaching for {hit.Reaching}. " +
                          "The 2026-08-26 ruling puts it out of a siege's reach absolutely");
            }
        }

        /// <summary>
        /// Returns every banned symbol present in <paramref name="source"/> once comments are
        /// stripped. Exposed to case E so the scanner itself can be proven.
        /// </summary>
        private static List<(string Symbol, string Reaching)> ScanForBanned(string source)
        {
            var hits = new List<(string, string)>();
            string code = StripComments(source);

            foreach (var banned in BannedSymbols)
                if (code.IndexOf(banned.Symbol, StringComparison.Ordinal) >= 0)
                    hits.Add((banned.Symbol, banned.Reaching));

            return hits;
        }

        /// <summary>
        /// Removes `//` line comments and block comments. The siege files document at length what
        /// they must never touch — naming a forbidden symbol in prose is the documentation working,
        /// and linting the prose would force the explanations to be deleted to make the gate green.
        /// </summary>
        private static string StripComments(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            var sb = new StringBuilder(source.Length);
            bool inLine = false, inBlock = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = (i + 1 < source.Length) ? source[i + 1] : '\0';

                if (inLine)
                {
                    if (c == '\n') { inLine = false; sb.Append(c); }
                    continue;
                }
                if (inBlock)
                {
                    if (c == '*' && next == '/') { inBlock = false; i++; }
                    continue;
                }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // =====================================================================
        //  E — THE ANTI-HOLLOW-PASS PROOF. The scanner is itself under test.
        // =====================================================================

        /// <summary>
        /// A green case D means one of two very different things: the files are clean, or the
        /// scanner is broken. These cases separate them by planting a violation the scanner MUST
        /// find, and a comment-only mention it MUST NOT.
        /// </summary>
        private static void ScannerSelfTestCases(List<string> f)
        {
            const string plantedViolation =
                "namespace X { static class Y { static void Z(object s) { " +
                "var bad = ((dynamic)s).OwnedItemIds; bad.Clear(); } } }";

            var found = ScanForBanned(plantedViolation);
            if (found.Count == 0)
                f.Add("⛔ self-test: the untouchable scanner did NOT find a PLANTED 'OwnedItemIds' " +
                      "in code — case D's green result proves nothing. Fix the scanner before " +
                      "trusting any pass from this suite");

            const string commentOnly =
                "namespace X { static class Y {\n" +
                "  // ⛔ never touch OwnedItemIds or GearInventory here.\n" +
                "  /* EquippedRingId is out of reach too. */\n" +
                "  static void Z() { } } }";

            var falsePositives = ScanForBanned(commentOnly);
            if (falsePositives.Count != 0)
                f.Add("⛔ self-test: the scanner flagged a symbol that appeared ONLY IN A COMMENT — " +
                      "it would force the siege files to delete the prose that explains what they " +
                      "must not do, in order to go green");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool AllBucketsZero(StakesLedger ledger, out string nonZero)
        {
            nonZero = null;
            if (ledger == null) { nonZero = "<null ledger>"; return false; }

            if (ledger.Wood != 0) { nonZero = $"Wood={ledger.Wood}"; return false; }
            if (ledger.Iron != 0) { nonZero = $"Iron={ledger.Iron}"; return false; }
            if (ledger.Stone != 0) { nonZero = $"Food={ledger.Stone}"; return false; }
            if (ledger.Coins != 0) { nonZero = $"Coins={ledger.Coins}"; return false; }
            if (ledger.Crystals != 0) { nonZero = $"Crystals={ledger.Crystals}"; return false; }
            if (ledger.Magic != 0) { nonZero = $"Magic={ledger.Magic}"; return false; }
            return true;
        }

        private static int BucketOf(StakesLedger ledger, BankResource r)
        {
            if (ledger == null) return -1;
            switch (r)
            {
                case BankResource.Wood: return ledger.Wood;
                case BankResource.Iron: return ledger.Iron;
                case BankResource.Stone: return ledger.Stone;
                case BankResource.Coins: return ledger.Coins;
                case BankResource.Crystals: return ledger.Crystals;
                default: return -1;
            }
        }

        /// <summary>
        /// ⚠ The repo root is MACHINE-DEPENDENT (CLAUDE.md §0 — `C:\eoa` on one machine, `D:\eoa`
        /// on another). Resolve it at runtime off <c>Application.dataPath</c>; never hardcode it.
        /// </summary>
        private static string RepoRoot()
        {
            string assets = Application.dataPath;                 // <repo>/Assets
            var dir = Directory.GetParent(assets);
            return dir != null ? dir.FullName : assets;
        }
    }
}

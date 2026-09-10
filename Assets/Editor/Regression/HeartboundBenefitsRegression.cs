// =============================================================================
// HeartboundBenefitsRegression — WO-1679 / HEART-006 D4 + WO-1682 / HEART-009 D1.
// -----------------------------------------------------------------------------
// TWO PROMISES, POLICED AS BUILD GATES:
//
//   1. ⛔ THE ECONOMIC CEILING IS MEASURED, NOT ASSERTED.
//      WO-1682 §0c: "A ceiling with no meter is a comment, not a guardrail" — and
//      it names THREE gates in this exact feature area (JewelPolishRegression,
//      SkrStakingRegression, StakingComplianceRegression) that were cited in code
//      as live protection and had never existed. This suite SUMS the shipped
//      benefit table and fails on the number, so the ceiling cannot become a
//      fourth imaginary gate.
//
//   2. ⛔ NO CLIENT FILE CARRIES THE TIER LADDER.
//      Product rule 6 (spec :21) makes the backend authoritative for the tier.
//      Spec :1274: "UI contains no staking calculations." A ladder under Assets/
//      would be a SECOND authority on a number the server owns — the
//      duplicated-state failure CLAUDE.md §2/§5/§8/§16 each record a scar from.
//
// ⭐ AND BOTH ARE DERIVED FROM THE AUTHORED JSON AT RUN TIME, NEVER COPIED HERE.
// The tier NAMES come out of api/_lib/heartbound-resonance-config.json and the
// benefit rows out of api/_lib/heartbound-tiers-config.json, every run. A hardcoded
// ceiling, tier list or benefit table in this file would be exactly the duplicated
// state it exists to forbid — the mistake MonetizationCovenantRegression's own
// header warns about ("The allowlist is DERIVED, not blind-hardcoded... If the JSON
// list grows, the gate grows with it").
//
// ⚠ THE KIND CLASSIFICATION IS THE JSON'S, NOT THIS FILE'S. Which kinds count
// toward the ceiling is read from `benefitKinds[kind].countsTowardCeiling` in the
// config — the Q-METER ruling (owner, 2026-09-10 13:36: "production-rate modifiers
// only; timers and event drops are not counted") expressed as data. Hardcoding
// "productionRate" here would be a SECOND definition of the metric, and WO-1682
// acceptance 1 requires exactly one.
//
// ⚠ NOT RUN BY THIS LANE. No Unity was fired here (the lane holds no editor lock),
// so the RED path below is DESIGNED AND NOT EXERCISED. The equivalent assertions
// ARE exercised, in Node, by test/heartbound-tiers.test.js — including the
// red-before-green case, which feeds an injected 13% table to the same meter and
// watches it fail. Say so plainly rather than implying a run that did not happen.
//
// Register in DataRegression.RunAll beside the covenant gate (DataRegression.cs:332).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HeartboundBenefitsRegression
    {
        private const string LadderJson = "api/_lib/heartbound-resonance-config.json";
        private const string BenefitsJson = "api/_lib/heartbound-tiers-config.json";

        /// <summary>
        /// The member set <c>IHeartboundBonusProvider</c> is allowed to have.
        /// ⛔ THIS IS A CLOSED LIST ON PURPOSE, mirroring what
        /// DungeonGemExclusivityRegression does for IPolishBonusProvider. A member added
        /// here is a member that must also be a benefit row the server computes and the
        /// meter has counted — otherwise a call site could apply a modifier the ceiling
        /// never measured, which is the one way the guardrail can be defeated from inside.
        /// </summary>
        private static readonly string[] AllowedProviderMembers =
        {
            "Tier", "OfflineProductionRateBonus", "ExtraWeeklyRerolls", "RollCapDelta",
            "UnlockedEventIds",
        };

        /// <summary>Shapes that must never appear on the provider — odds, luck, weights, bags.</summary>
        private static readonly string[] ForbiddenProviderShapes =
        {
            "odds", "chance", "weight", "luck", "probability", "roll(", "random",
            "Dictionary<", "float Get(", "IReadOnlyDictionary",
        };

        public static bool Run(out string reason)
        {
            var f = new List<string>();
            string root = Path.GetDirectoryName(Application.dataPath); // project root (parent of /Assets)

            string ladder = ReadOrFail(root, LadderJson, f);
            string benefits = ReadOrFail(root, BenefitsJson, f);
            if (ladder == null || benefits == null)
            {
                reason = "HeartboundBenefits: " + string.Join(" | ", f.ToArray());
                return false;
            }

            CheckCeiling(benefits, f);
            CheckNoLadderOnTheClient(root, ladder, f);
            CheckProviderShape(root, f);

            if (f.Count > 0)
            {
                reason = "HeartboundBenefits FAILED (" + f.Count + "): " + string.Join(" | ", f.ToArray());
                return false;
            }
            reason = "HeartboundBenefits OK";
            return true;
        }

        // =====================================================================
        //  1. THE METER
        // =====================================================================

        private static void CheckCeiling(string benefits, List<string> f)
        {
            double ceiling;
            if (!TryReadCeiling(benefits, out ceiling))
            {
                f.Add(BenefitsJson + " has no readable economicCeiling.productionRateCeiling. " +
                      "A ceiling that cannot be read is the imaginary gate WO-1682 §0c refuses.");
                return;
            }

            // Which kinds count is the JSON's own classification (Q-METER as data).
            HashSet<string> counting = CountingKinds(benefits);
            if (counting.Count == 0)
            {
                f.Add(BenefitsJson + " declares no benefit kind that counts toward the ceiling. " +
                      "Either the file's shape moved or the metric was deleted; both are defects, " +
                      "and a meter that measures nothing must never read as a pass.");
                return;
            }

            double sum = 0.0;
            int counted = 0;
            var seenKinds = new HashSet<string>();
            // `BenefitRow`, not `var`. Benefits() is generic so `var` WOULD infer correctly here,
            // but this file's rule after the CS1061 above is: any local that gets dereferenced is
            // spelled out, so the safety never depends on remembering which enumerable is generic.
            foreach (BenefitRow row in Benefits(benefits))
            {
                seenKinds.Add(row.Kind);
                if (!counting.Contains(row.Kind)) continue;
                counted++;
                sum += row.Value;
            }

            if (counted == 0)
            {
                f.Add(BenefitsJson + " holds no benefit row of a counting kind (" +
                      string.Join("/", new List<string>(counting).ToArray()) + "). Either the " +
                      "extraction below no longer matches the file's shape, or every rate modifier " +
                      "was removed. A zero measured through a broken parse is not a pass.");
                return;
            }

            // Undeclared kinds cannot be classified, so they cannot be measured.
            HashSet<string> declared = DeclaredKinds(benefits);
            foreach (string kind in seenKinds)
            {
                if (!declared.Contains(kind))
                    f.Add(BenefitsJson + ": benefit kind \"" + kind + "\" is used but not declared in " +
                          "benefitKinds, so the ceiling cannot know whether to count it.");
            }

            sum = Math.Round(sum, 6);
            if (sum > ceiling)
            {
                f.Add("ECONOMIC CEILING BREACHED: the cumulative production-rate sum across the whole " +
                      "Heartbound ladder is " + sum.ToString("0.####", CultureInfo.InvariantCulture) +
                      " against a ceiling of " + ceiling.ToString("0.####", CultureInfo.InvariantCulture) +
                      " (" + counted + " counted rows). HEART-009 spec :903-910. Retune " +
                      BenefitsJson + " or get a ruling to raise the ceiling; do not raise it silently.");
            }

            Debug.Log("[heartbound] ceiling measured: sum=" +
                      sum.ToString("0.####", CultureInfo.InvariantCulture) + " ceiling=" +
                      ceiling.ToString("0.####", CultureInfo.InvariantCulture) +
                      " countedRows=" + counted +
                      " (Q-METER: production-rate modifiers only; timers and event drops excluded)");
        }

        // =====================================================================
        //  2. NO LADDER ON THE CLIENT
        // =====================================================================

        private static void CheckNoLadderOnTheClient(string root, string ladder, List<string> f)
        {
            List<string> tierNames = TierNames(ladder);
            if (tierNames.Count < 5)
            {
                f.Add(LadderJson + ": fewer than five tier names could be extracted, so the sweep " +
                      "below would pass by not looking. Fix the extraction, do not ignore it.");
                return;
            }

            string assets = Path.Combine(root, "Assets");
            foreach (string path in EnumerateRuntimeCs(assets))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (Exception) { continue; }

                string rel = path.Replace('\\', '/');
                // This file names the tokens in order to forbid them.
                if (rel.EndsWith("Editor/Regression/HeartboundBenefitsRegression.cs", StringComparison.Ordinal))
                    continue;

                if (text.IndexOf("minScore", StringComparison.Ordinal) >= 0)
                {
                    f.Add("CLIENT COPY OF THE LADDER: " + rel + " names \"minScore\". Tier thresholds " +
                          "are backend-only (" + LadderJson + "); the client is TOLD its tier by " +
                          "GET /api/heartbound/status and never derives one. Spec :1274.");
                }

                int hits = 0;
                var hit = new List<string>();
                foreach (string name in tierNames)
                {
                    if (text.IndexOf("\"" + name + "\"", StringComparison.Ordinal) < 0) continue;
                    hits++;
                    hit.Add(name);
                }
                // ⚠ THREE, NOT ONE. A single tier name can legitimately appear in a string —
                // "Heartbound" is the feature's own name and will show up in copy. THREE OR MORE
                // of them together is a LADDER, and that is the thing being forbidden.
                if (hits >= 3)
                {
                    f.Add("CLIENT COPY OF THE LADDER: " + rel + " carries " + hits + " tier names (" +
                          string.Join(", ", hit.ToArray()) + "). That is a tier table. The ladder " +
                          "lives in " + LadderJson + " and is served to the client, never compiled " +
                          "into it.");
                }
            }
        }

        // =====================================================================
        //  3. THE PROVIDER STAYS NARROW  (WO-1679 D4)
        // =====================================================================

        private static void CheckProviderShape(string root, List<string> f)
        {
            string rel = "Assets/_Modules/Core/Catalog/HeartboundBenefits.cs";
            string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { f.Add("cannot read " + rel + ": " + e.Message); return; }

            // The interface body, and only it.
            // Explicitly `Match` rather than `var` — see the MatchCollection note below. Regex.Match
            // (singular) does return Match, so `var` would compile here; it is spelled out anyway so
            // that every regex result in this file reads the same way and no future edit has to
            // remember which of the two overloads is the safe one.
            Match m = Regex.Match(text, @"interface\s+IHeartboundBonusProvider\s*\{(.*?)\n    \}",
                                  RegexOptions.Singleline);
            if (!m.Success)
            {
                f.Add(rel + ": IHeartboundBonusProvider's declaration could not be located, so its " +
                      "shape is unpoliced. A gate that cannot find its subject must fail, not pass.");
                return;
            }
            string body = m.Groups[1].Value;

            // Strip doc comments before looking for member names.
            var code = new StringBuilder();
            foreach (string line in body.Split('\n'))
                if (!line.TrimStart().StartsWith("///", StringComparison.Ordinal)) code.Append(line).Append('\n');
            string decl = code.ToString();

            // ⛔ `Match`, NEVER `var`. Regex.Matches returns MatchCollection, which implements
            // only the NON-GENERIC IEnumerable, so `foreach (var m in ...)` infers `object` and
            // `m.Groups` is CS1061 at compile time. Every Regex.Matches loop in this file is
            // explicitly typed for that reason. (This line shipped as `var` and was RED at the
            // compile gate; a Python port of this logic cannot see it, because the trap is in the
            // C# type system and not in the rule being expressed.)
            foreach (Match member in Regex.Matches(decl, @"\b(\w+)\s*\{\s*get;"))
            {
                string name = member.Groups[1].Value;
                if (Array.IndexOf(AllowedProviderMembers, name) >= 0) continue;
                f.Add("IHeartboundBonusProvider grew a member \"" + name + "\", which is not one of " +
                      string.Join("/", AllowedProviderMembers) + ". Every member must be a value the " +
                      "SERVER computed and the economic meter has already counted; a new one is a " +
                      "modifier the ceiling never measured. See PolishBonusProvider.cs:20-23 for the " +
                      "same rule on the sibling seam.");
            }

            foreach (string shape in ForbiddenProviderShapes)
            {
                if (decl.IndexOf(shape, StringComparison.OrdinalIgnoreCase) < 0) continue;
                f.Add("IHeartboundBonusProvider names \"" + shape + "\". The interface exposes only " +
                      "server-computed, meter-counted grants: no odds, weights, luck, probabilities " +
                      "or general-purpose lookup bags. A staker's roll is exactly as likely as a free " +
                      "player's, and adding one of these breaks the property the economy rests on.");
            }

            // The distribution flag is read in EXACTLY ONE place (WO-1679 acceptance 3).
            int flagReads = CountOccurrences(text, "FeatureFlags.HeartboundPassives");
            if (flagReads != 1)
            {
                f.Add(rel + " reads FeatureFlags.HeartboundPassives " + flagReads + " times. It must be " +
                      "read exactly ONCE, in HeartboundBonuses.Active, so no call site anywhere carries " +
                      "a distribution check. (PolishBonusProvider.cs:33-34 states the same rule for its " +
                      "own flag.)");
            }

            // ⛔ AND THE CORE FILE CARRIES NO CHAIN TOKEN. It compiles into every artifact,
            //    including a Google Play one, where GooglePlayPackagingGate sweeps the built
            //    AAB for exactly these — they survive as IL2CPP member names and string literals.
            foreach (string token in new[] { "skr", "solana", "wallet", "usdc", "crypto", "blockchain", "web3" })
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                f.Add(rel + " contains the token \"" + token + "\". This file is in DeNelle.Core, which " +
                      "ships in EVERY artifact including Google Play, and GooglePlayPackagingGate sweeps " +
                      "the AAB for it. Everything chain-facing belongs in DeNelle.Wallet " +
                      "(!GOOGLE_PLAY-constrained). Spec :1316-1322.");
            }
        }

        // =====================================================================
        //  extraction helpers — deliberately small, and they FAIL LOUD
        // =====================================================================
        //
        // ⚠ WHY REGEX AND NOT A JSON PARSER. This assembly has no guaranteed JSON
        // dependency, and MonetizationCovenantRegression (the sibling gate) reads its
        // JSON as text for the same reason. The shapes below are narrow, they only ever
        // face two files this repo owns, and EVERY caller above treats "extracted
        // nothing" as a FAILURE rather than as a pass — which is the property that makes
        // a text scan safe here.

        private struct BenefitRow { public string Kind; public double Value; }

        private static IEnumerable<BenefitRow> Benefits(string json)
        {
            // Each benefit object carries "kind": "...", and a counting one also "value": n.
            foreach (Match m in Regex.Matches(json,
                "\\{[^{}]*?\"kind\"\\s*:\\s*\"(?<k>[A-Za-z]+)\"[^{}]*?\\}", RegexOptions.Singleline))
            {
                var row = new BenefitRow { Kind = m.Groups["k"].Value, Value = 0.0 };
                Match v = Regex.Match(m.Value, "\"value\"\\s*:\\s*(?<v>-?[0-9.]+)");
                if (v.Success)
                {
                    double parsed;
                    if (double.TryParse(v.Groups["v"].Value, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out parsed)) row.Value = parsed;
                }
                yield return row;
            }
        }

        private static HashSet<string> DeclaredKinds(string json)
        {
            var set = new HashSet<string>();
            foreach (Match m in Regex.Matches(json,
                "\"(?<k>[A-Za-z]+)\"\\s*:\\s*\\{\\s*\"countsTowardCeiling\"")) set.Add(m.Groups["k"].Value);
            return set;
        }

        private static HashSet<string> CountingKinds(string json)
        {
            var set = new HashSet<string>();
            foreach (Match m in Regex.Matches(json,
                "\"(?<k>[A-Za-z]+)\"\\s*:\\s*\\{\\s*\"countsTowardCeiling\"\\s*:\\s*true"))
                set.Add(m.Groups["k"].Value);
            return set;
        }

        private static bool TryReadCeiling(string json, out double ceiling)
        {
            ceiling = 0.0;
            Match m = Regex.Match(json, "\"productionRateCeiling\"\\s*:\\s*(?<v>[0-9.]+)");
            return m.Success && double.TryParse(m.Groups["v"].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out ceiling);
        }

        private static List<string> TierNames(string json)
        {
            var names = new List<string>();
            foreach (Match m in Regex.Matches(json, "\"name\"\\s*:\\s*\"(?<n>[^\"]+)\""))
            {
                string n = m.Groups["n"].Value;
                if (!names.Contains(n)) names.Add(n);
            }
            return names;
        }

        private static string ReadOrFail(string root, string rel, List<string> f)
        {
            string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            try { return File.ReadAllText(path); }
            catch (Exception e) { f.Add("cannot read " + rel + ": " + e.Message); return null; }
        }

        private static IEnumerable<string> EnumerateRuntimeCs(string assetsRoot)
        {
            foreach (string path in Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string rel = path.Replace('\\', '/');
                // Editor-only code never ships in a player, so a ladder there is not a
                // client copy — and this gate lives there itself.
                if (rel.IndexOf("/Editor/", StringComparison.Ordinal) >= 0) continue;
                yield return path;
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0;
            int i = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (i >= 0) { n++; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal); }
            return n;
        }
    }
}

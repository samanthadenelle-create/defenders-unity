// =============================================================================
// JewelPolishRegression — WO-1673 D1/D7. The gate two files have CLAIMED for weeks.
// -----------------------------------------------------------------------------
// ⛔ THIS FILE EXISTS BECAUSE TWO PLACES ALREADY SAID IT DID, AND NEITHER WAS TRUE.
//
//   JewelPolishService.cs (DescribeOdds' doc block):
//       "JewelPolishRegression fails if a second odds source appears or if these
//        numbers stop matching the table."
//   jewel-polish.json (_tuning.fairness):
//       "...and JewelPolishRegression FAILS if a second table ever appears. This is
//        the fairness property the whole design rests on."
//
// Before 2026-09-10, `grep -rn "JewelPolishRegression" .` over the whole repo returned
// exactly TWO hits: those two claims. The only mechanic in the game that discloses odds
// to the player had an IMAGINARY gate protecting the accuracy of that disclosure.
//
// That is not a novel failure here — it is the SAME one MonetizationCovenantRegression's
// own header was written to close ("skr_staking.json says a 'SkrStakingRegression' rejects
// combat/stat grants — that code never existed; the firewall was comment-only"). A claim
// in a comment is not a gate. This file turns both sentences above into facts.
//
// -----------------------------------------------------------------------------
// WHAT IT ACTUALLY EXECUTES — stated honestly, in the shape WO-1494 forced onto
// InventoryArmoryRailRegression's header after that one described cases it did not run:
//
//   DATA + ARITHMETIC : the odds cases. They read jewel-polish.json OFF DISK and compare
//                       it against what JewelPolishService.DescribeOdds returns through
//                       the CATALOG. Two different readers of one file — if the catalog
//                       ever grew a second table, or the shatter model were "simplified",
//                       the two disagree and this reds.
//   DATA + CONST      : the rough-stone cases. Authored dungeon-layout `tier` values on
//                       disk vs the code consts that gate the drop.
//   SOURCE LINT       : that DungeonController still reads the RAIL KEY rather than a
//                       literal, and that no second odds source exists.
//   TEXT              : that the json's own drop-model note is not the retired lie.
//
//   NO PlayMode, no scene, no Unity UI. Never throws — an unreadable file becomes a
//   failure LINE, never a crash.
//
// -----------------------------------------------------------------------------
// ⛔ THE RATE IS NOT THIS SUITE'S OPINION. 5% post-first, tier-2+ dungeons, raid tier 3 /
// 1-per-day are the OWNER'S RULING of 2026-09-09 (WO-1373). This file PINS them; it does
// not argue with them. If the owner re-rules, change the const and this suite follows —
// never the other way round.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using DeNelle.Village.Crafting;

namespace DeNelle.Editor.Regression
{
    public static class JewelPolishRegression
    {
        // ── the artefacts ────────────────────────────────────────────────────
        private const string PolishRes    = "Assets/Resources/Data/Canonical/jewel-polish.json";
        private const string PolishStream = "Assets/StreamingAssets/Data/Canonical/jewel-polish.json";
        private const string LayoutDir    = "Assets/Resources/Data/Canonical/dungeon-layouts";
        private const string DungeonSrc   = "Assets/_Modules/Dungeons/DungeonController.cs";
        private const string ServiceSrc   = "Assets/_Modules/Village/Crafting/JewelPolishService.cs";
        private const string RaidSrc      = "Assets/_Modules/Village/Troops/RaidScoring.cs";

        /// <summary>The four layouts the owner's ruling calls "the starters" — every one of
        /// them must stay authored at tier 1, or <c>RoughStoneMinDungeonTier</c> stops meaning
        /// "anything past the starters" without a single line of code changing.</summary>
        private static readonly string[] StarterLayouts =
        {
            "dg_starter_loop", "dg_healers_cottage", "dg_folks_granary", "dg_hollow_roads",
        };

        /// <summary>Fragments of the RETIRED drop-model sentence. Any of them surviving in the
        /// note means the file is back to declaring a guaranteed drop the game does not have.</summary>
        private static readonly string[] RetiredNoteFragments =
        {
            "per COMPLETED run, guaranteed",
            "runs = gems",
            "the arithmetic below is exact",
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("JEWEL_POLISH_OK - " + reason);
            else Debug.LogError("JEWEL_POLISH_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CaseDropModelNoteIsNotStale(failures, notes);
                CaseTwinsAreByteIdentical(failures, notes);
                CaseDropRateAuthority(failures, notes);
                CaseTierFloorMatchesAuthoredLayouts(failures, notes);
                CaseOddsAreDisclosedAndDerived(failures, notes);
                CaseOneOddsSource(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures);
                return false;
            }
            reason = "jewel polish + rough-stone drop ok (odds disclosed == odds rolled; the " +
                     "WO-1373 drop model pinned against the authored layouts) - " +
                     string.Join("; ", notes);
            return true;
        }

        // =====================================================================
        //  CASE — the drop-model note is not the retired lie   ⭐ THE RED-FIRST ONE
        // =====================================================================
        //
        //  ⛔ WHY A TEXT ASSERTION ON A COMMENT FIELD IS WORTH A GATE, WHICH IS NOT OBVIOUS.
        //  jewel-polish.json's `_tuning` block is the ONLY prose in the repo that describes
        //  the rough-stone economy in one place, so it is what a seat reads when asked
        //  "does this game have probabilistic items, and what are the odds?" — a Google Play
        //  per-country disclosure question (WO-1673). Until 2026-09-10 it read:
        //
        //      "ONE rough stone per COMPLETED run, guaranteed; one gem per polished stone.
        //       So runs = gems, and the arithmetic below is exact."
        //
        //  That had been FALSE since 2026-09-09, when WO-1373 made it a 5% roll on tier-2+
        //  dungeons. A seat answering a store questionnaire from it would have declared a
        //  guaranteed drop the game does not have. The code changed and the note did not —
        //  the duplicated-state failure CLAUDE.md sections 2, 5 and 16 each describe.
        //
        //  RED-FIRST PROOF (quote this in any RESULT that touches the note):
        //      git stash push -- Assets/Resources/Data/Canonical/jewel-polish.json \
        //                        Assets/StreamingAssets/Data/Canonical/jewel-polish.json
        //  then run this suite. It MUST fail with "the RETIRED drop-model sentence is back".
        //  `git stash pop` restores it and the case goes green.
        private static void CaseDropModelNoteIsNotStale(List<string> failures, List<string> notes)
        {
            const string Tag = "[drop-model-note-is-not-stale]";
            foreach (string path in new[] { PolishRes, PolishStream })
            {
                string raw = ReadOrNull(path);
                if (raw == null) { failures.Add(Tag + " cannot read " + path); continue; }

                string note = ExtractJsonString(raw, "dropModel");
                if (note == null)
                {
                    failures.Add(Tag + " " + path + " has no _tuning.dropModel note at all -- the one " +
                                 "place the rough-stone economy is described in prose is gone, and a " +
                                 "store-disclosure question has nothing to read.");
                    continue;
                }

                for (int i = 0; i < RetiredNoteFragments.Length; i++)
                {
                    if (note.IndexOf(RetiredNoteFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        failures.Add(Tag + " " + path + " -- the RETIRED drop-model sentence is back " +
                                     "(found '" + RetiredNoteFragments[i] + "'). The stone has NOT been " +
                                     "guaranteed since WO-1373 (2026-09-09): it is 5% past the first one, " +
                                     "tier-2+ dungeons only. This note is what a seat reads to answer a " +
                                     "Google Play probabilistic-item question, so a stale sentence here " +
                                     "becomes a false statement to a regulator.");
                    }
                }

                // It must also positively SAY the current model, not merely omit the old one.
                if (note.IndexOf("5 percent", StringComparison.OrdinalIgnoreCase) < 0 &&
                    note.IndexOf("5%", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " " + path + " -- the note never states the post-first drop chance. " +
                                 "Deleting the lie is only half the fix; the note has to carry the truth.");
                if (note.IndexOf("GUARANTEED", StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add(Tag + " " + path + " -- the note never states that the FIRST stone is " +
                                 "guaranteed, which is the half of the model that did NOT change and the " +
                                 "half that keeps the Jeweler reachable at all.");
                if (note.IndexOf("RaidScoring", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " " + path + " -- the note does not distinguish the RAID path, which " +
                                 "is NOT a roll (RaidScoring.ShouldDropRoughStone has no RNG). Describing " +
                                 "both faucets as one probability is the next version of this same bug.");
            }
            if (failures.Count == 0) notes.Add("[drop-model-note] both twins state 5% post-first, first guaranteed");
        }

        // =====================================================================
        //  CASE — the two canonical copies are byte-identical
        // =====================================================================
        //
        //  The catalog loads Resources FIRST and falls back to StreamingAssets, so a drift
        //  means the odds the player is SHOWN can differ from the odds that are ROLLED
        //  depending on which copy a given build target resolves. For a disclosure surface
        //  that is not a tidiness issue, it is the disclosure being wrong on one platform.
        private static void CaseTwinsAreByteIdentical(List<string> failures, List<string> notes)
        {
            const string Tag = "[twins-byte-identical]";
            byte[] a = ReadBytesOrNull(PolishRes), b = ReadBytesOrNull(PolishStream);
            if (a == null || b == null) { failures.Add(Tag + " cannot read one or both canonical copies"); return; }
            if (a.Length != b.Length) { failures.Add(Tag + " sizes differ (" + a.Length + " vs " + b.Length + ")"); return; }
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) { failures.Add(Tag + " copies diverge at byte " + i); return; }
            notes.Add("[twins] identical, " + a.Length + " bytes");
        }

        // =====================================================================
        //  CASE — the drop RATE has one authority and the code still reads it
        // =====================================================================
        //
        //  ⚠ THIS CONST IS ALREADY READ BY A SECOND SUITE, AND THAT IS RECORDED HERE ON
        //  PURPOSE: JewelerDiscoveryFtueRegression asserts the same `== 5` (it re-pointed
        //  there when WO-1373 retired its old literal `0.15f` pin). Two readers of one const
        //  is redundant coverage, not duplicated state — but a seat deleting one of them
        //  should know the other exists rather than discovering it by going red.
        private static void CaseDropRateAuthority(List<string> failures, List<string> notes)
        {
            const string Tag = "[drop-rate-authority]";

            int def = DeNelle.Core.Ops.RemoteTunables.DungeonRoughStoneDropPctDefault;
            if (def != 5)
                failures.Add(Tag + " RemoteTunables.DungeonRoughStoneDropPctDefault is " + def +
                             ", not the owner-ruled 5 (2026-09-09, WO-1373). If the owner re-ruled, " +
                             "move this pin WITH the ruling and say so; do not edit it to go green.");

            // The spec ROW must exist: DungeonController.PostFirstRoughStoneDropPct falls back to
            // the const when SpecFor(...) returns null, so a missing row silently strands the knob
            // as un-tunable while every log still reads 5.
            if (DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyDungeonRoughStoneDropPct) == null)
                failures.Add(Tag + " no TunableSpec row for '" +
                             DeNelle.Core.Ops.RemoteTunables.KeyDungeonRoughStoneDropPct +
                             "' -- the rate silently stops being remotely tunable and the fallback " +
                             "hides it, because the value stays 5 either way.");

            string dungeon = ReadOrNull(DungeonSrc);
            if (dungeon == null) { failures.Add(Tag + " cannot read " + DungeonSrc); return; }
            if (dungeon.IndexOf("KeyDungeonRoughStoneDropPct", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " DungeonController no longer reads the rail key " +
                             "KeyDungeonRoughStoneDropPct -- the rate has been re-hardcoded at the roll " +
                             "site, which is how it shipped at 0.15f before WO-1373.");
            if (dungeon.IndexOf("UnityEngine.Random.value", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " DungeonController has no RNG call left -- if the post-first stone " +
                             "stopped being a roll that is a design change, not a refactor, and this " +
                             "suite's whole drop model is stale.");

            notes.Add("[drop-rate] default 5, rail row present, roll site reads the key");
        }

        // =====================================================================
        //  CASE — the tier floor still means what its doc block says
        // =====================================================================
        //
        //  ⭐ THE INDEPENDENT AUTHORITY IS THE AUTHORED LAYOUT FILES, and that pairing is the
        //  point. `RoughStoneMinDungeonTier = 2` is documented as "anything past the starters",
        //  which is only true while the starters are authored at tier 1. Nothing connects the
        //  const to those JSONs at runtime, so re-tiering a layout silently changes which
        //  dungeons pay — with no code change to review.
        //
        //  ⛔ AND THE FLOOR MUST NOT ORPHAN THE DROP. If every authored layout fell below the
        //  floor, the post-first stone would become UNOBTAINABLE, the Jeweler chain would
        //  dead-end, and nothing else in the repo would notice: the roll simply never fires.
        //  Measured 2026-09-10: seven layouts at tier 1, and exactly three at or above the
        //  floor (dg_sunken_vault 2, dg_bonecrypt 3, dg_ember_deep 4).
        private static void CaseTierFloorMatchesAuthoredLayouts(List<string> failures, List<string> notes)
        {
            const string Tag = "[tier-floor-matches-authored-layouts]";

            int floor = DeNelle.Dungeons.DungeonController.RoughStoneMinDungeonTier;
            if (floor != 2)
                failures.Add(Tag + " RoughStoneMinDungeonTier is " + floor + ", not 2. The owner's " +
                             "2026-09-09 ruling was 'dungeons 5% minus starters', and 2 is what " +
                             "'past the starters' resolves to while the starters are tier 1.");

            if (!Directory.Exists(LayoutDir))
            {
                failures.Add(Tag + " layout directory missing: " + LayoutDir +
                             " -- the authored side of this assertion is gone, so the const's meaning " +
                             "cannot be checked against anything.");
                return;
            }

            int atOrAboveFloor = 0, parsed = 0;
            var starterTiers = new Dictionary<string, int>();
            foreach (string file in Directory.GetFiles(LayoutDir, "*.json"))
            {
                string raw = ReadOrNull(file);
                if (raw == null) continue;
                if (!TryJsonInt(raw, "tier", out int tier)) continue;   // catalogs/kits carry no tier
                parsed++;
                if (tier >= floor) atOrAboveFloor++;
                string id = Path.GetFileNameWithoutExtension(file);
                if (Array.IndexOf(StarterLayouts, id) >= 0) starterTiers[id] = tier;
            }

            if (parsed == 0)
            {
                failures.Add(Tag + " no dungeon layout declared a 'tier' at all -- either the schema " +
                             "moved or this reader is broken; both make the floor unverifiable.");
                return;
            }

            for (int i = 0; i < StarterLayouts.Length; i++)
            {
                string id = StarterLayouts[i];
                if (!starterTiers.TryGetValue(id, out int t))
                    failures.Add(Tag + " starter layout '" + id + "' is missing or carries no tier -- " +
                                 "the owner's ruling names the starters as the excluded band, so one " +
                                 "vanishing changes what the drop excludes.");
                else if (t >= floor)
                    failures.Add(Tag + " starter layout '" + id + "' is authored tier " + t +
                                 ", at or above RoughStoneMinDungeonTier (" + floor + ") -- a STARTER " +
                                 "dungeon now pays a rough stone, which inverts the owner's " +
                                 "'5% minus starters' ruling with no code change to review.");
            }

            if (atOrAboveFloor == 0)
                failures.Add(Tag + " NO authored dungeon layout reaches tier " + floor + " -- the " +
                             "post-first rough stone is UNOBTAINABLE and the Jeweler chain dead-ends. " +
                             "The roll would simply never fire, so nothing else would report this.");

            notes.Add("[tier-floor] floor " + floor + ", " + parsed + " layouts tiered, " +
                      atOrAboveFloor + " eligible");
        }

        // =====================================================================
        //  CASE — the odds DISCLOSED are the odds ROLLED (the claim at DescribeOdds)
        // =====================================================================
        //
        //  Two independent readers of one file: this case parses jewel-polish.json OFF DISK;
        //  JewelPolishService.DescribeOdds reads it through JewelPolishCatalog. They must agree
        //  on every authored score row, for a first polish and for a re-polish.
        //
        //  ⛔ THE SHATTER MODEL IS ASSERTED, NOT ASSUMED. The shatter roll happens FIRST and
        //  independently (JewelPolishService: `if (isRePolish && Random.value < ...)`), so the
        //  gem odds are the REMAINING probability mass: weight/total * (1 - shatter). A future
        //  "simplification" that reports the raw normalised weights on a re-polish would
        //  OVERSTATE every gem chance by 1/(1-0.15) ~ 18% and still look perfectly plausible in
        //  the panel. That is precisely the disclosure error a regulator would find.
        private static void CaseOddsAreDisclosedAndDerived(List<string> failures, List<string> notes)
        {
            const string Tag = "[odds-disclosed-equal-odds-rolled]";

            string raw = ReadOrNull(PolishRes);
            if (raw == null) { failures.Add(Tag + " cannot read " + PolishRes); return; }

            if (!TryJsonFloat(raw, "rePolishShatterChance", out float shatterAuthored))
            {
                failures.Add(Tag + " rePolishShatterChance is not authored in " + PolishRes +
                             " -- the panel discloses a shatter line that would have no source.");
                return;
            }

            // Harness capability, not a product defect: the catalog resolves through Resources,
            // which a batch context may refuse. Stand down by NAME rather than passing silently.
            if (JewelPolishCatalog.RowFor(0) == null)
            {
                notes.Add(RegressionOutcome.PartialSkip("JEWEL POLISH ODDS",
                    "JewelPolishCatalog could not resolve jewel-polish.json through Resources in this " +
                    "context - the disclosed-vs-rolled comparison did not run"));
                return;
            }

            if (Mathf.Abs(JewelPolishCatalog.RePolishShatterChance - shatterAuthored) > 0.0005f)
                failures.Add(Tag + " the catalog's shatter chance (" +
                             JewelPolishCatalog.RePolishShatterChance.ToString("0.###") +
                             ") does not match the authored " + shatterAuthored.ToString("0.###"));

            var rows = ParseOutcomeRows(raw);
            if (rows.Count == 0)
            {
                failures.Add(Tag + " no outcome rows parsed from " + PolishRes);
                return;
            }

            foreach (var row in rows)
            {
                float total = 0f;
                foreach (var kv in row.Value) if (kv.Value > 0f) total += kv.Value;
                if (total <= 0f) { failures.Add(Tag + " score " + row.Key + " has zero total weight"); continue; }

                foreach (bool isRe in new[] { false, true })
                {
                    var disclosed = JewelPolishService.DescribeOdds(row.Key, isRe);
                    string at = " [score " + row.Key + (isRe ? " re-polish]" : " first polish]");
                    if (disclosed == null || disclosed.Count == 0)
                    {
                        failures.Add(Tag + at + " DescribeOdds returned nothing -- the confirm panel " +
                                     "REFUSES in this state (it logs 'no odds to disclose'), so the " +
                                     "player cannot polish at all.");
                        continue;
                    }

                    float shatter = isRe ? shatterAuthored : 0f;
                    float sum = 0f;
                    int shatterLines = 0;

                    foreach (var d in disclosed)
                    {
                        sum += d.Chance;
                        if (d.IsShatter)
                        {
                            shatterLines++;
                            if (Mathf.Abs(d.Chance - shatter) > 0.0015f)
                                failures.Add(Tag + at + " the disclosed shatter chance is " +
                                             d.Chance.ToString("0.####") + " but the table authors " +
                                             shatter.ToString("0.####"));
                            continue;
                        }
                        if (!row.Value.TryGetValue(d.Id, out float w))
                        {
                            failures.Add(Tag + at + " discloses gem '" + d.Id +
                                         "' which is NOT in the authored weights -- a SECOND odds " +
                                         "source has appeared, which is the exact property " +
                                         "JewelPolishService's doc block and jewel-polish.json's " +
                                         "_tuning.fairness both promise this suite prevents.");
                            continue;
                        }
                        float expected = (w / total) * (1f - shatter);
                        if (Mathf.Abs(d.Chance - expected) > 0.0015f)
                            failures.Add(Tag + at + " '" + d.Id + "' is disclosed at " +
                                         d.Chance.ToString("0.####") + " but the table rolls " +
                                         expected.ToString("0.####") + " (weight " + w.ToString("0.###") +
                                         " of " + total.ToString("0.###") +
                                         (isRe ? ", times the 1-shatter remainder" : "") +
                                         "). The disclosure and the roll have drifted apart.");
                    }

                    if (!isRe && shatterLines != 0)
                        failures.Add(Tag + at + " a FIRST polish discloses a shatter line. " +
                                     "jewel-polish.json's own schema note says a first polish can never " +
                                     "shatter, because that stone is the run's guaranteed payout.");
                    if (isRe && shatterAuthored > 0f && shatterLines != 1)
                        failures.Add(Tag + at + " expected exactly one shatter line, found " + shatterLines);

                    if (Mathf.Abs(sum - 1f) > 0.005f)
                        failures.Add(Tag + at + " the disclosed set sums to " + sum.ToString("0.####") +
                                     ", not 1.0 -- a disclosure that does not add up is a disclosure " +
                                     "that lies by omission about the missing mass.");
                }
            }

            notes.Add("[odds] " + rows.Count + " score rows: disclosed == rolled, sets sum to 1.0");
        }

        // =====================================================================
        //  CASE — there is exactly ONE odds source
        // =====================================================================
        //
        //  The literal property both existing claims promise. DescribeOdds must derive from the
        //  roll table rather than carry its own numbers, and no probability may be re-authored
        //  at the call site.
        private static void CaseOneOddsSource(List<string> failures, List<string> notes)
        {
            const string Tag = "[one-odds-source]";
            string src = ReadOrNull(ServiceSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + ServiceSrc); return; }

            if (src.IndexOf("DescribeOdds", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " JewelPolishService no longer exposes DescribeOdds -- the panel's " +
                             "only way to disclose the odds is gone.");
            if (src.IndexOf("RowFor(score)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " DescribeOdds no longer reads the roll table via RowFor(score) -- " +
                             "if it has grown its own numbers, the disclosure has become a SECOND " +
                             "source and can drift from what is rolled.");

            string raid = ReadOrNull(RaidSrc);
            if (raid != null && raid.IndexOf("ShouldDropRoughStone", StringComparison.Ordinal) >= 0)
            {
                int i = raid.IndexOf("ShouldDropRoughStone", StringComparison.Ordinal);
                int end = Math.Min(raid.Length, i + 2500);
                if (raid.IndexOf("UnityEngine.Random", i, end - i, StringComparison.Ordinal) >= 0)
                    failures.Add(Tag + " RaidScoring.ShouldDropRoughStone has grown an RNG call. The " +
                                 "raid faucet is DETERMINISTIC by the owner's 2026-09-09 ruling (tier " +
                                 "floor AND a per-day cap); turning it into a roll adds a second " +
                                 "probabilistic surface that nothing discloses.");
            }
            notes.Add("[one-odds-source] DescribeOdds derives from the roll table; the raid faucet is not a roll");
        }

        // =====================================================================
        //  tiny readers — deliberately hand-rolled, never a JSON dependency
        // =====================================================================
        //
        //  These parse only what they need out of the raw text. That is on purpose: this suite
        //  is the INDEPENDENT reader in every pairing above, so borrowing the same catalog /
        //  deserializer the product uses would make several cases structurally unable to fail.

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static byte[] ReadBytesOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
            catch { return null; }
        }

        /// <summary>The string value of "key": "..." — honouring backslash escapes so a quote
        /// inside the note cannot truncate the read.</summary>
        private static string ExtractJsonString(string raw, string key)
        {
            string needle = "\"" + key + "\"";
            int k = raw.IndexOf(needle, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = raw.IndexOf(':', k + needle.Length);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length || raw[i] != '"') return null;
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '\\' && i + 1 < raw.Length) { sb.Append(raw[i + 1]); i += 2; continue; }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool TryJsonInt(string raw, string key, out int value)
        {
            value = 0;
            if (!TryJsonFloat(raw, key, out float f)) return false;
            value = Mathf.RoundToInt(f);
            return true;
        }

        private static bool TryJsonFloat(string raw, string key, out float value)
        {
            value = 0f;
            var m = System.Text.RegularExpressions.Regex.Match(
                raw, "\"" + System.Text.RegularExpressions.Regex.Escape(key) +
                     "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            if (!m.Success) return false;
            return float.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out value);
        }

        /// <summary>score -> (gem id -> weight), read straight out of the outcomes array.</summary>
        private static Dictionary<int, Dictionary<string, float>> ParseOutcomeRows(string raw)
        {
            var result = new Dictionary<int, Dictionary<string, float>>();
            var rowRx = new System.Text.RegularExpressions.Regex(
                "\"score\"\\s*:\\s*(\\d+)\\s*,\\s*(?:\"[^\"]*\"\\s*:\\s*(?:\"[^\"]*\"|[^,{}\\[\\]]+)\\s*,\\s*)*?" +
                "\"weights\"\\s*:\\s*\\[(.*?)\\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var entryRx = new System.Text.RegularExpressions.Regex(
                "\"id\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"weight\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");

            foreach (System.Text.RegularExpressions.Match m in rowRx.Matches(raw))
            {
                if (!int.TryParse(m.Groups[1].Value, out int score)) continue;
                var weights = new Dictionary<string, float>();
                foreach (System.Text.RegularExpressions.Match e in entryRx.Matches(m.Groups[2].Value))
                {
                    if (float.TryParse(e.Groups[2].Value, NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out float w))
                        weights[e.Groups[1].Value] = w;
                }
                if (weights.Count > 0) result[score] = weights;
            }
            return result;
        }
    }
}

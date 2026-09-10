// =============================================================================
// AuthoredFieldGateRegression [authored-field-gate] -- WO-1430 Group B, lane
// FIELDS (2026-09-09). The BEHAVIOURAL half of AuthoredFieldReaderRegression.
// -----------------------------------------------------------------------------
// AuthoredFieldReaderRegression asks a structural question -- "does ANY production
// line name this field?" -- and it is deliberately blunt: it matches `.Member` by
// name over stripped source. It therefore CANNOT tell a gate from a `Debug.Log`,
// and it goes green the moment one line mentions the member.
//
// This suite asks the question the oracle cannot: **does the authored string
// actually DECIDE anything?** It pins the two decision tables WO-1430 Group B
// wired on 2026-09-09, so a later refactor that keeps the mention and drops the
// meaning fails here rather than shipping a field that is read and ignored.
//
//   unlockMethod  -> CosmeticCatalog.IsAchievementUnlock is the ONE definition,
//                    and CosmeticOwnershipService.GrantAchievement refuses a row
//                    that does not claim "achievement".
//   requiresHero  -> DailyQuestService.HeroRequirementMet decides whether a daily
//                    template is eligible for the player's chosen hero class.
//
// -----------------------------------------------------------------------------
// WHAT THIS SUITE CANNOT PROVE -- read before trusting a green
// -----------------------------------------------------------------------------
//  * It does NOT prove either gate has a LIVE EFFECT on today's data. Measured
//    2026-09-09: all 37 cosmetics.json rows author "achievement" (so nothing is
//    refused), and daily-quests.json authors requiresHero on ZERO rows (so nothing
//    is skipped). Both gates are correct-and-currently-inert BY DESIGN: they exist
//    so the day the data changes, the game honours it instead of silently ignoring
//    it (the WO-1038 shape). That is exactly what a decision table can be pinned
//    for and a live playthrough cannot.
//  * It does NOT drive CosmeticOwnershipService itself. That is a MonoBehaviour
//    singleton whose success path WRITES PlayerPrefs, and the refusal path cannot
//    be reached from real data (no "buy" row exists to refuse). Case B therefore
//    pins the predicate BEHAVIOURALLY and case C pins the WIRING as source text,
//    labelled as such -- a source-text assertion is weaker evidence and is used
//    here only where the behavioural route is unavailable.
//  * It does NOT re-assert what EconomyMetaCatalogRegression.cs:129-138 already
//    asserts (that the authored string is in {buy,achievement}). NOT DUPLICATED.
//
// Marker: AUTHORED_FIELD_GATE_OK / AUTHORED_FIELD_GATE_FAIL <case>.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "authored-field-gate suite", () => { if (!DeNelle.Editor.AuthoredFieldGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[authored-field-gate] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Cosmetics;
using DeNelle.Core.Quests;
using DeNelle.Core.State;

namespace DeNelle.Editor
{
    /// <summary>Behavioural pins for the two authored fields WO-1430 Group B wired.</summary>
    public static class AuthoredFieldGateRegression
    {
        private const string Tag = "[authored-field-gate]";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== AuthoredFieldGateRegression (WO-1430 Group B) ===\n");
            try
            {
                CheckCosmeticCorpus(failures, log);
                CheckCosmeticUnlockDecisionTable(failures, log);
                CheckGrantAchievementConsultsTheField(failures, log);
                CheckHeroRequirementDecisionTable(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "AUTHORED_FIELD_GATE_OK unlockMethod gates the achievement grant and " +
                         "requiresHero gates daily-quest eligibility";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "AUTHORED_FIELD_GATE_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        // CASE A  [cosmetic-corpus-present] -- the anti-vacuity floor.
        // Without it, case B's "every row agrees with the predicate" would pass
        // perfectly over an EMPTY catalog and prove nothing at all.
        // REVERT RECIPE (RED): point CosmeticCatalog.StreamingRelativePath at
        // "Data/Canonical/cosmetics-missing.json" -- All goes empty and this fires.
        // =====================================================================
        private const int CosmeticFloor = 30;   // measured 37 rows, both twins, 2026-09-09

        private static void CheckCosmeticCorpus(List<string> failures, StringBuilder log)
        {
            int count = CosmeticCatalog.All != null ? CosmeticCatalog.All.Count : 0;
            if (count < CosmeticFloor)
            {
                failures.Add(Tag + " [cosmetic-corpus-present] CosmeticCatalog.All returned " + count +
                             " rows (floor " + CosmeticFloor + ", measured 37 on 2026-09-09). The catalog did " +
                             "not load, so every unlockMethod question below would pass vacuously. FAIL, not a skip");
                return;
            }
            log.AppendLine("cosmetics rows: " + count);
        }

        // =====================================================================
        // CASE B  [unlock-method-is-the-definition] -- the predicate agrees with
        // the AUTHORED string on every real row, and at least one real row makes
        // the predicate return TRUE (so it is not vacuously-false everywhere).
        // REVERT RECIPE (RED): change CosmeticCatalog.AchievementUnlock from
        // "achievement" to "earned" -- every authored row stops matching and the
        // "at least one" assertion fires.
        // =====================================================================
        private static void CheckCosmeticUnlockDecisionTable(List<string> failures, StringBuilder log)
        {
            if (CosmeticCatalog.All == null || CosmeticCatalog.All.Count < CosmeticFloor) return;

            int achievements = 0, disagreements = 0;
            foreach (var row in CosmeticCatalog.All)
            {
                if (row == null) continue;
                bool expected = string.Equals(row.UnlockMethod, "achievement", StringComparison.OrdinalIgnoreCase);
                bool actual = CosmeticCatalog.IsAchievementUnlock(row);
                if (actual) achievements++;
                // The derived property must not become a SECOND definition (WO-1430: one
                // definition, delegated). Checked on EVERY row, not only disagreeing ones --
                // the two can drift apart while both still match the authored string.
                if (row.IsAchievement != actual && disagreements <= 3)
                    failures.Add(Tag + " [unlock-method-is-the-definition] cosmetic '" + (row.Id ?? "<null>") +
                                 "': CosmeticDef.IsAchievement (" + row.IsAchievement + ") disagrees with " +
                                 "CosmeticCatalog.IsAchievementUnlock (" + actual + "). There must be exactly ONE " +
                                 "definition of unlockMethod; the property is required to delegate");
                if (actual == expected) continue;
                disagreements++;
                if (disagreements <= 3)
                    failures.Add(Tag + " [unlock-method-is-the-definition] cosmetic '" + (row.Id ?? "<null>") +
                                 "' authors unlockMethod='" + (row.UnlockMethod ?? "<null>") + "' but " +
                                 "CosmeticCatalog.IsAchievementUnlock returned " + actual + ". The predicate no " +
                                 "longer means what the authored string says, so the gate decides by something " +
                                 "other than the data");
            }

            if (achievements == 0)
                failures.Add(Tag + " [unlock-method-is-the-definition] NOT ONE of the " + CosmeticCatalog.All.Count +
                             " authored cosmetics is recognised as an achievement unlock. Measured 2026-09-09: " +
                             "37 of 37 author \"achievement\". Either the predicate stopped matching the authored " +
                             "vocabulary or the data was re-authored without this gate being revisited - a green " +
                             "here would mean the achievement door opens for nothing");

            // The discrimination itself: a table, not a tautology. Rows are
            // (unlockMethod, expected) pairs; the last two prove FAIL-CLOSED.
            // REVERT RECIPE (RED): make IsAchievementUnlock `=> def != null` -- the
            // "buy"/null/blank rows below flip and this fires four times.
            var table = new[]
            {
                new object[] { "achievement", true },
                new object[] { "ACHIEVEMENT", true },   // case-insensitive by design
                new object[] { "buy", false },
                new object[] { "", false },
                new object[] { (string)null, false },
                new object[] { "achievements", false }, // near-miss must NOT match
            };
            foreach (var row in table)
            {
                string authored = (string)row[0];
                bool expected = (bool)row[1];
                bool actual = CosmeticCatalog.IsAchievementUnlock(new CosmeticDef { Id = "probe", UnlockMethod = authored });
                if (actual != expected)
                    failures.Add(Tag + " [unlock-method-is-the-definition] IsAchievementUnlock(unlockMethod='" +
                                 (authored ?? "<null>") + "') returned " + actual + ", expected " + expected +
                                 ". The gate no longer discriminates on the authored value");
            }
            if (CosmeticCatalog.IsAchievementUnlock((CosmeticDef)null))
                failures.Add(Tag + " [unlock-method-is-the-definition] IsAchievementUnlock(null) returned true. " +
                             "The gate must FAIL CLOSED: an unknown row is not an earned row");

            log.AppendLine("cosmetics recognised as achievement unlocks: " + achievements);
        }

        // =====================================================================
        // CASE C  [grant-achievement-consults-the-field] -- WIRING, as SOURCE TEXT.
        // ⚠ WEAKER EVIDENCE, AND SAID SO. The behavioural route is unavailable:
        // CosmeticOwnershipService is a MonoBehaviour singleton whose success path
        // writes PlayerPrefs, and the REFUSAL path cannot be reached from real data
        // because no authored row is "buy". Without this case, deleting the gate
        // from GrantAchievement leaves BOTH oracles green - the reader oracle is
        // satisfied by the read inside CosmeticCatalog.cs itself.
        // REVERT RECIPE (RED): delete the IsAchievementUnlock call from
        // CosmeticOwnershipService.GrantAchievement.
        // =====================================================================
        private static void CheckGrantAchievementConsultsTheField(List<string> failures, StringBuilder log)
        {
            string path = Application.dataPath.Replace('\\', '/') + "/_Modules/Cosmetics/CosmeticOwnershipService.cs";
            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add(Tag + " [grant-achievement-consults-the-field] could not read '" + path + "': " +
                             ex.GetType().Name + ". The wiring assertion cannot be made, so it FAILS rather " +
                             "than passing in silence");
                return;
            }

            int grantAt = src.IndexOf("public bool GrantAchievement", StringComparison.Ordinal);
            if (grantAt < 0)
            {
                failures.Add(Tag + " [grant-achievement-consults-the-field] GrantAchievement is gone from " +
                             "CosmeticOwnershipService.cs. If it was deliberately retired, retire this case in " +
                             "the same change and say where the achievement door now lives");
                return;
            }
            int end = src.IndexOf("\n        public ", grantAt + 10, StringComparison.Ordinal);
            string body = end > grantAt ? src.Substring(grantAt, end - grantAt) : src.Substring(grantAt);
            if (body.IndexOf("IsAchievementUnlock", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " [grant-achievement-consults-the-field] GrantAchievement no longer calls " +
                             "CosmeticCatalog.IsAchievementUnlock, so the achievement door opens for ANY catalog " +
                             "row again and a purchase-only cosmetic is handed out free by every milestone caller " +
                             "(TierSystem.cs:198). WO-1430 Group B wired this gate on 2026-09-09");
            else
                log.AppendLine("GrantAchievement consults IsAchievementUnlock: yes");
        }

        // =====================================================================
        // CASE D  [requires-hero-decides-eligibility] -- the daily-quest hero gate,
        // pinned as a decision table over the PURE predicate (no GameStateService,
        // no PlayerPrefs, no roll).
        //
        // ⚠ THE INTERPRETATION IS PINNED HERE, NOT INVENTED HERE. No spec defines
        // requiresHero (git log -S over DailyQuests.cs returns one commit that
        // introduced it unread; WORK_ORDER_558 says the authored pack carries none).
        // A save holds exactly ONE hero class and the tree has no owned-hero roster,
        // so "requires hero X" is read as "the chosen class IS X". If the owner rules
        // otherwise, THIS TABLE is the thing to rewrite - which is the point of
        // writing the interpretation down as executable rows.
        //
        // REVERT RECIPE (RED): delete the numeric guard in HeroRequirementMet --
        // Enum.TryParse then accepts "2" as Knight and the ("2", Knight) row flips.
        // SECOND RECIPE (RED): `return true` on an unrecognised name -- the
        // ("Paladin", Knight) row flips, which is the silent-ignore this closes.
        // =====================================================================
        private static void CheckHeroRequirementDecisionTable(List<string> failures, StringBuilder log)
        {
            var table = new object[][]
            {
                // authored requiresHero | chosen class | eligible?
                new object[] { null,        HeroClassOpt.Knight, true  },  // no requirement at all
                new object[] { "",          HeroClassOpt.Knight, true  },
                new object[] { "   ",       HeroClassOpt.Knight, true  },
                new object[] { null,        HeroClassOpt.None,   true  },  // and none is needed
                new object[] { "Knight",    HeroClassOpt.Knight, true  },
                new object[] { "knight",    HeroClassOpt.Knight, true  },  // case-insensitive
                new object[] { " Ranger ",  HeroClassOpt.Ranger, true  },  // authored whitespace
                new object[] { "Mage",      HeroClassOpt.Knight, false },  // wrong hero
                new object[] { "Knight",    HeroClassOpt.None,   false },  // no class chosen yet
                new object[] { "Paladin",   HeroClassOpt.Knight, false },  // unrecognised -> fail CLOSED
                new object[] { "2",         HeroClassOpt.Knight, false },  // the enum's number is not a hero
                new object[] { "None",      HeroClassOpt.Knight, false },  // the sentinel is not a hero
            };

            int checkedRows = 0;
            foreach (var row in table)
            {
                string requires = (string)row[0];
                var current = (HeroClassOpt)row[1];
                bool expected = (bool)row[2];
                bool actual = DailyQuestService.HeroRequirementMet(requires, current);
                checkedRows++;
                if (actual == expected) continue;
                failures.Add(Tag + " [requires-hero-decides-eligibility] HeroRequirementMet(requiresHero='" +
                             (requires ?? "<null>") + "', chosen=" + current + ") returned " + actual +
                             ", expected " + expected + ". A daily template's authored hero requirement is no " +
                             "longer decided the way WO-1430 Group B wired it on 2026-09-09");
            }

            // Anti-vacuity: the table must be REACHED. A silently-emptied table would
            // let every assertion above disappear without a single failure.
            if (checkedRows < table.Length)
                failures.Add(Tag + " [requires-hero-decides-eligibility] only " + checkedRows + " of " +
                             table.Length + " decision rows were evaluated. FAIL, not a skip");
            else
                log.AppendLine("hero-requirement decision rows checked: " + checkedRows);
        }
    }
}

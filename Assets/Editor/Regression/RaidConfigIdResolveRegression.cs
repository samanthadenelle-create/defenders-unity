// =============================================================================
// RaidConfigIdResolveRegression - WO-1869: the raid's config id must come from the
// AUTHORED catalog, never from surgery on the scene name.
// Markers: RAID_CONFIG_ID_OK / RAID_CONFIG_ID_FAIL.   Tag: [raid-config-id]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Shape: public static bool Run(out string reason) - registered into
// DeNelle.Editor.DataRegression.RunAll with ONE line by the lead. NEVER throws.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS - MEASURED ON THE OWNER'S DEVICE, NOT REASONED FROM SOURCE
//  (CLAUDE.md §11B / §12)
// -----------------------------------------------------------------------------
// Owner Seeker run, build 2026.09.18.374427
// (Logs/device/logcat-bastion-victory-20260918.txt): a 3-star, 65-second clear of
// RaidBase_IronBastion with OwnedBase == null did NOT capture the town.
//
//   :202350  [Flow:Raid] OBJECTIVE COMPLETE - RaidSpire 'RaidSpire'
//                        (config 'iron_bastion') RAZED.      <- the SPAWNER's id
//   :202352  [Flow:Raid] VICTORY - raid 'IronBastion' won (SPIRE RAZED).
//                                              ^^^^^^^^^^^   <- the CONTROLLER's id
//   :202395  [Flow:EndState] RAID VICTORY composed: baseClaimed=False
//                        captureStarsRequired=0 stars=3 lead=ordinary-clear.
//
// `grep -c 'CAPTURE ELIGIBLE'` = 0 and `grep -c 'highest raid settled'` = 0 across
// the whole 208k-line log (and across the 2026-09-16 owner run). One of those two
// lines MUST print whenever the ordinal compare at RaidVictoryController :387/:395
// holds, so the compare itself failed: the controller stripped "RaidBase_" off the
// PascalCase scene name and produced "IronBastion", while
// OwnedBaseProgression.FinalRaidId is "iron_bastion". The catalog had the right
// answer the whole time (scene-configs.json: id "iron_bastion", sceneName
// "RaidBase_IronBastion") - nobody asked it.
//
// -----------------------------------------------------------------------------
//  THE RED-FIRST DISCRIMINATOR
// -----------------------------------------------------------------------------
// Case A calls the SAME public resolver the controller calls -
// RaidVictoryController.ResolveConfigId(sceneName, spawnerConfigId) - once per
// authored raid row, and demands it return that row's own id. Against the
// strip-only body:
//     RaidBase_raider_camp_small  -> raider_camp_small   PASS (snake_case, lucky)
//     RaidBase_fortified_garrison -> fortified_garrison  PASS
//     RaidBase_mage_enclave       -> mage_enclave        PASS
//     RaidBase_IronBastion        -> IronBastion         FAIL, expected iron_bastion
// i.e. exactly one row is red, and it is the row that owns the town capture.
//
// REVERT RECIPE (body-only - see the RESULT file): keep the (string, string)
// signature and restore the strip block as the whole body. Case A then reports
//   "raid-config-id CASE A: row 'iron_bastion' (scene 'RaidBase_IronBastion')
//    resolved to 'IronBastion'"
// A signature revert would not compile, so it is NOT the revert recipe.
//
// -----------------------------------------------------------------------------
//  WHAT THIS SUITE DOES NOT PROVE - an unproven thing named as unproven is useful,
//  an unproven thing stated as fact costs someone a day (CLAUDE.md §11B)
// -----------------------------------------------------------------------------
//  * That a live 3-star Bastion clear now routes to the owned town. The capture
//    gate is computed INLINE inside RaidVictoryController.HandleVictory and cannot
//    be invoked without running the whole victory flow (and WO-1869 forbids
//    extracting it). Case B is therefore a PROXY, and says so in its own reason
//    strings: it drives the resolved id through the real
//    OwnedBaseProgression.TryCapture contract - the same FinalRaidId and star gates
//    the controller's expression reads - and then pins, by source, that the
//    controller still feeds that expression from this resolver. The device capture
//    is WO-1869 acceptance item 2, an owner felt-verify.
//  * That the rough-stone ladder is fixed. It reads the same id, so it should
//    follow, but that is a separate observation on the next device log.
//  * Anything about star SCORING. RaidScoring is untouched by WO-1869.
//
// NO REFLECTION: every call below is a public API. CaptureStrandExitRegression's
// reflection probe is untouched and must NOT be weakened (WO-1869 §RED-first).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Catalog parity (Case A) + capture-gate reachability (Case B) + fallback
    /// robustness (Case C) for the one law WO-1869 lands: a raid's config id is
    /// AUTHORED, never derived. DataRegression-shaped: true = pass with a one-line
    /// summary, false = fail with the offending detail. NEVER throws.
    /// </summary>
    public static class RaidConfigIdResolveRegression
    {
        /// <summary>Relative to Application.dataPath.</summary>
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                // OwnedBaseProgression.Fail and the resolver's own fallback both emit FlowTrace,
                // and this suite deliberately drives refusals. Muted for the duration, restored
                // in the finally - never left muted (CLAUDE.md §12: flag off, never strip).
                DeNelle.Core.Diagnostics.FlowTrace.Mute("Raid", "OwnedBase", "CanonJson", "World");
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "RAID_CONFIG_ID_FAIL raid-config-id suite THREW: " +
                         ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                DeNelle.Core.Diagnostics.FlowTrace.AllOn();
            }
        }

        private static bool RunCore(out string reason)
        {
            var notes = new StringBuilder();

            if (!CaseA_CatalogParity(out string caseA)) { reason = "RAID_CONFIG_ID_FAIL " + caseA; return false; }
            notes.Append(caseA);

            if (!CaseB_CaptureGateReachable(out string caseB)) { reason = "RAID_CONFIG_ID_FAIL " + caseB; return false; }
            notes.Append("; ").Append(caseB);

            if (!CaseC_FallbackStillDegrades(out string caseC)) { reason = "RAID_CONFIG_ID_FAIL " + caseC; return false; }
            notes.Append("; ").Append(caseC);

            reason = "RAID_CONFIG_ID_OK " + notes.ToString();
            return true;
        }

        // =====================================================================
        //  CASE A - CATALOG PARITY, over the REAL rows through the REAL loader
        // =====================================================================
        /// <summary>
        /// For every authored raid row in scene-configs.json, resolving that row's own
        /// sceneName through the controller's resolver returns that row's own id.
        ///
        /// <para>Rows are read through <see cref="SceneConfigCatalog.All"/> - the same lazy
        /// loader the game uses (CanonicalJson: Resources first, StreamingAssets fallback) -
        /// and filtered exactly the way <c>RaidVictoryController.KnownRaidConfigIds</c> filters
        /// them, so the suite and the game agree on what a raid row IS.</para>
        ///
        /// <para>TWO VACUITY GUARDS, both load-bearing: an empty catalog (JSON moved, Resources
        /// copy dropped) and a catalog with no <c>iron_bastion</c> row would each let this case
        /// "pass" while proving nothing, and iron_bastion is the ONE row the defect was on.</para>
        /// </summary>
        private static bool CaseA_CatalogParity(out string note)
        {
            note = null;

            var rows = new List<SceneConfigDef>();
            foreach (var cfg in SceneConfigCatalog.All)
            {
                if (cfg == null || string.IsNullOrEmpty(cfg.id) || string.IsNullOrEmpty(cfg.sceneName)) continue;
                if (!cfg.sceneName.StartsWith("RaidBase", StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add(cfg);
            }

            if (rows.Count == 0)
            {
                note = "raid-config-id CASE A: SceneConfigCatalog yielded ZERO RaidBase_* rows, so this case " +
                       "would pass without checking anything. The catalog load itself is broken (missing " +
                       "Assets/Resources/Data/Canonical/scene-configs.json dual copy?) - fix that first; a " +
                       "vacuous pass here is how the capture defect survived four months of green suites.";
                return false;
            }

            bool sawFinal = false;
            foreach (var row in rows)
                if (string.Equals(row.id, OwnedBaseProgression.FinalRaidId, StringComparison.Ordinal))
                    sawFinal = true;

            if (!sawFinal)
            {
                note = "raid-config-id CASE A: no authored raid row carries id '" +
                       OwnedBaseProgression.FinalRaidId + "' (found " + rows.Count + " raid row(s)). That id is " +
                       "the ONLY one the town capture accepts (OwnedBaseProgression.TryCapture), so with the row " +
                       "renamed or gone the capture can never fire and this case must not report parity.";
                return false;
            }

            foreach (var row in rows)
            {
                // The SAME function HandleVictory calls, with no spawner id - i.e. the worst case,
                // where only the scene name is known. That is the path the owner's run took.
                string resolved = RaidVictoryController.ResolveConfigId(row.sceneName, null);
                if (!string.Equals(resolved, row.id, StringComparison.Ordinal))
                {
                    note = "raid-config-id CASE A: row '" + row.id + "' (scene '" + row.sceneName +
                           "') resolved to '" + resolved + "'. The resolver is deriving the id from the scene " +
                           "name instead of reading the catalog row that already holds it - the WO-1869 defect. " +
                           "Measured cost: a 3-star 65s Bastion clear that never captured the town " +
                           "(Logs/device/logcat-bastion-victory-20260918.txt :202350 vs :202352).";
                    return false;
                }
            }

            var ids = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) ids.Append(',');
                ids.Append(rows[i].id);
            }
            note = "A catalog parity over " + rows.Count + " authored raid row(s) [" + ids + "]";
            return true;
        }

        // =====================================================================
        //  CASE B - THE CAPTURE GATE IS REACHABLE WITH THE RESOLVED ID
        // =====================================================================
        /// <summary>
        /// A PROXY for the inline gate at <c>RaidVictoryController.HandleVictory</c>, which cannot
        /// be invoked without running the whole victory flow (and WO-1869 forbids extracting it).
        ///
        /// <para>B1/B2/B3 are BEHAVIOURAL against the real contract: the resolved Bastion id is
        /// driven through <see cref="OwnedBaseProgression.TryCapture"/>, which applies the SAME
        /// <c>FinalRaidId</c> and <c>CaptureStarsRequired</c> gates the controller's expression
        /// reads. With a correct id and a full star count the refusal moves PAST the id gate to
        /// "Captured template is missing"; with the pre-fix 'IronBastion' it stops at "A completed
        /// final-tier raid receipt is required", which is the red-on-HEAD line for this case.
        /// B4 pins, by source, that the controller still feeds its gate from this resolver - so
        /// the behavioural half cannot drift away from the shipping expression.</para>
        /// </summary>
        private static bool CaseB_CaptureGateReachable(out string note)
        {
            note = null;
            // The const, never a re-typed literal: SceneRouter.cs:195 is where this scene name
            // is authored, and HubSceneLiteralRegression polices hand-typed scene strings.
            const string BastionScene = DeNelle.Core.SceneRouter.RaidBaseIronBastion;

            // ---- B1 - the resolver agrees with the capture contract's one accepted id ----
            string resolved = RaidVictoryController.ResolveConfigId(BastionScene, null);
            if (!string.Equals(resolved, OwnedBaseProgression.FinalRaidId, StringComparison.Ordinal))
            {
                note = "raid-config-id CASE B1: scene '" + BastionScene + "' resolved to '" + resolved +
                       "', but the capture contract accepts only '" + OwnedBaseProgression.FinalRaidId +
                       "'. This inequality IS the defect: RaidVictoryController compares the resolved id " +
                       "against OwnedBaseProgression.FinalRaidId, so neither 'CAPTURE ELIGIBLE' nor the " +
                       "shortfall line can ever print.";
                return false;
            }

            // ---- B2 - a full-star clear gets PAST the id + star gates ----
            bool ok2 = OwnedBaseProgression.TryCapture(null, resolved, "wo1869-probe-receipt", null,
                OwnedBaseProgression.CaptureStarsRequired, out _, out string reason2);
            if (ok2 || reason2 == null || reason2.IndexOf("template is missing", StringComparison.OrdinalIgnoreCase) < 0)
            {
                note = "raid-config-id CASE B2: TryCapture(id='" + resolved + "', stars=" +
                       OwnedBaseProgression.CaptureStarsRequired + ", template=null) returned ok=" + ok2 +
                       " reason='" + (reason2 ?? "(null)") + "'. Expected a refusal that has already cleared " +
                       "the id and star gates ('Captured template is missing') - the probe supplies no " +
                       "template on purpose, because the template is the town builder's lane, not this one. " +
                       "A 'final-tier raid receipt is required' here means the resolved id is still wrong.";
                return false;
            }

            // ---- B3 - one star short still refuses, for the STAR reason ----
            bool ok3 = OwnedBaseProgression.TryCapture(null, resolved, "wo1869-probe-receipt", null,
                OwnedBaseProgression.CaptureStarsRequired - 1, out _, out string reason3);
            if (ok3 || reason3 == null || reason3.IndexOf("three-star", StringComparison.OrdinalIgnoreCase) < 0)
            {
                note = "raid-config-id CASE B3: TryCapture(id='" + resolved + "', stars=" +
                       (OwnedBaseProgression.CaptureStarsRequired - 1) + ") returned ok=" + ok3 + " reason='" +
                       (reason3 ?? "(null)") + "'. A clear one star short of the bar must still refuse, and " +
                       "refuse for the STAR reason - that shortfall is the branch WO-1783 surfaces to the " +
                       "player. Fixing the id must not loosen the bar.";
                return false;
            }

            // ---- B4 - the shipping gate is still fed BY this resolver ----
            string victory = ReadSource(VictoryRel);
            if (victory == null)
            {
                note = "raid-config-id CASE B4: could not read " + VictoryRel + " to pin the gate wiring.";
                return false;
            }

            int assign = victory.IndexOf("captureRaidId = ResolveConfigId(", StringComparison.Ordinal);
            if (assign < 0)
            {
                note = "raid-config-id CASE B4: RaidVictoryController no longer assigns captureRaidId from " +
                       "ResolveConfigId(...). B1-B3 prove the RESOLVER agrees with the capture contract; they " +
                       "prove nothing about the shipping gate unless the gate reads the resolver. (This match " +
                       "is comment-stripped, so the RCA prose in that file cannot satisfy it.)";
                return false;
            }

            int gates = Count(victory, "captureRaidId == OwnedBaseProgression.FinalRaidId");
            if (gates < 2)
            {
                note = "raid-config-id CASE B4: found " + gates + " compare(s) of captureRaidId against " +
                       "OwnedBaseProgression.FinalRaidId; expected both - the capture gate (_captureRequired) " +
                       "AND the WO-1783 shortfall latch (_captureRaidShortOfStars). They are two branches of " +
                       "one question and a fix to one that skips the other leaves the player un-told again.";
                return false;
            }

            note = "B resolved '" + resolved + "' clears the TryCapture id gate at " +
                   OwnedBaseProgression.CaptureStarsRequired + " star(s), still refuses at " +
                   (OwnedBaseProgression.CaptureStarsRequired - 1) + ", and " + gates +
                   " shipping gate compare(s) read it";
            return true;
        }

        // =====================================================================
        //  CASE C - THE PREFERENCE ORDER, AND A SAFE DEGRADE
        // =====================================================================
        /// <summary>
        /// The spawner's stored id wins outright (it is the id the spawner itself already logged
        /// correctly on the owner's device while the controller printed the wrong one), and an
        /// unknown scene still returns something usable instead of throwing - a raid scene added
        /// without a catalog row must degrade, never hard-stop a victory settlement.
        /// </summary>
        private static bool CaseC_FallbackStillDegrades(out string note)
        {
            note = null;

            string viaSpawner = RaidVictoryController.ResolveConfigId(
                DeNelle.Core.SceneRouter.RaidBaseIronBastion, OwnedBaseProgression.FinalRaidId);
            if (!string.Equals(viaSpawner, OwnedBaseProgression.FinalRaidId, StringComparison.Ordinal))
            {
                note = "raid-config-id CASE C1: the spawner's stored id '" + OwnedBaseProgression.FinalRaidId +
                       "' did not win - got '" +
                       viaSpawner + "'. The spawner is the FIRST authority precisely because it printed the " +
                       "right id on the owner's device (log :202350) in the same second the controller " +
                       "printed the wrong one.";
                return false;
            }

            string unknown = RaidVictoryController.ResolveConfigId("RaidBase_not_in_the_catalog", null);
            if (!string.Equals(unknown, "not_in_the_catalog", StringComparison.Ordinal))
            {
                note = "raid-config-id CASE C2: an un-catalogued RaidBase_* scene resolved to '" + unknown +
                       "'; the legacy strip must remain the LAST-RESORT degrade (warned, not silent) so a " +
                       "newly baked raid scene settles its victory instead of throwing.";
                return false;
            }

            string empty = RaidVictoryController.ResolveConfigId(null, null);
            if (!string.Equals(empty, "unknown", StringComparison.Ordinal))
            {
                note = "raid-config-id CASE C3: a null/empty scene name resolved to '" + empty +
                       "'; the contract is the literal 'unknown', unchanged from the pre-WO-1869 body.";
                return false;
            }

            note = "C spawner id wins, un-catalogued scene degrades to the warned strip, empty scene -> 'unknown'";
            return true;
        }

        // =====================================================================
        //  SOURCE READING - comments stripped, string literals preserved
        // =====================================================================
        /// <summary>
        /// Reads one source file as CODE. The RCA paragraph WO-1869 leaves in
        /// RaidVictoryController quotes the old behaviour and the gate by name (CLAUDE.md §15
        /// keeps that record), so a raw-text match would match the explanation instead of the
        /// code - the exact false FAIL CaptureStrandExitRegression documents. Literal-aware, so
        /// a '//' inside a string cannot swallow the rest of a line (the same trap CLAUDE.md §1
        /// documents for the brace gate).
        /// </summary>
        private static string ReadSource(string relative)
        {
            string path = Path.Combine(UnityEngine.Application.dataPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? StripComments(File.ReadAllText(path)) : null;
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                char ch = src[i];

                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }

                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '*')
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

                if (ch == '@' && i + 1 < src.Length && src[i + 1] == '"')
                {
                    sb.Append(src[i]).Append(src[i + 1]);
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"').Append('"'); i += 2; continue; }
                            sb.Append('"');
                            break;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    char quote = ch;
                    sb.Append(ch);
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i]).Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == '\n') break;
                        sb.Append(src[i]);
                        i++;
                    }
                    if (i < src.Length) sb.Append(src[i]);
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static int Count(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}

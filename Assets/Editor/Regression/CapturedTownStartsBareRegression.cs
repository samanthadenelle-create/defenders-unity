// =============================================================================
// CapturedTownStartsBareRegression - WO-1872: the captured town converts as the
// DESTROYED camp. Its walls and watchtowers arrive as rubble the player clears
// for salvage; the Heart converts standing.
// Markers: CAPTURED_TOWN_BARE_OK / CAPTURED_TOWN_BARE_FAIL.  Tag: [captured-town-bare]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Shape: public static bool Run(out string reason) - registered into
// DeNelle.Editor.DataRegression.RunAll with ONE line by the lead. NEVER throws.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS - PROVED AT SOURCE, NOT REASONED FROM THE SYMPTOM (CLAUDE.md 11B)
// -----------------------------------------------------------------------------
// Owner, 2026-09-18: "When the playere converts to a town We wan them to create
// their own town so we shuold not give them two fortified sections of walls with
// defense structures", then: "they arrive that way cause you repair the current
// camp", then: "I think we should load a destroyed camp and then clear the rubble
// and give them resources to make player designed layouts".
//
// NOTHING repaired anything - and that matters, because a lane hunting for a
// repair call would have found none and bounced the ticket. The real path, read
// at source 2026-09-18:
//
//   1. RaidCaptureCensus.cs:47-49 freezes a Func<float> per body returning its
//      LIVE HpFraction; :71 samples it at Settle() time - the victory-time health.
//   2. OwnedTownSnapshotImporter.cs:97-102 replays that fraction onto the BAKED
//      twin in OwnedTown_IronBastion.unity via RestoreOwnedTownCondition.
//   3. A three-star clear does not require breaking the perimeter: razing the
//      spire alone wins (RaidVictoryController.cs:271), and a garrison wipe wins
//      too (:208-212).
//
// So every wall and tower the player never attacked carried condition 1.0 across
// and arrived STANDING. "You repair the current camp" is the owner describing the
// felt result exactly right: the camp she wrecked handed itself back intact.
//
// -----------------------------------------------------------------------------
//  THE RED-FIRST DISCRIMINATOR - AND WHY A PURE-MATH CASE WOULD NOT HAVE BEEN ONE
// -----------------------------------------------------------------------------
// A suite that only exercised CapturedTownStanddown.ConditionOnCapture would go
// green the moment that function existed, whether or not anything CALLED it -
// which is precisely the HEAD behaviour it is meant to catch. So Case A has two
// halves and needs BOTH: the filter returns the ruled conditions, AND
// RaidCaptureCensus.Settle actually routes every entry through it. The second
// half is matched against COMMENT-STRIPPED source (the precedent
// RaidWatchdogHonorRegression sets and RaidConfigIdResolveRegression repeats:
// this repo documents its own history in prose, so a raw-text match matches the
// explanation instead of the code - the exact false FAIL WO-1778 burned a gate
// run on).
//
// REVERT RECIPE (what turns each case red):
//   A  restore `record.condition01 = Mathf.Clamp01(entry.Condition())` in
//      RaidCaptureCensus.Settle, or make ConditionOnCapture pass the measured
//      value through -> A red on both halves.
//   B  make ConditionOnCapture defensive-only (drop the Heart branch) -> B red.
//   C  delete a RestoreOwnedTownCondition raze branch, or stop the importer
//      replaying condition -> C red.
//   D  round the salvage instead of flooring it, or credit the full build cost
//      -> D red; skip the TryRetire and the ruin is still clearable -> D red.
//   E  hardcode 50 (or 0.5f) at the salvage site instead of reading the knob,
//      or drop the Registry row -> E red.
//
// -----------------------------------------------------------------------------
//  WHAT THIS SUITE DELIBERATELY DOES NOT CLAIM
// -----------------------------------------------------------------------------
//  * That the town LOOKS right. Cases A/B/D are contract-level and C is a source
//    pin; the ruin field is WO-1872 acceptance item 2, an owner felt-verify on a
//    device capture. This suite cannot see a screen and does not pretend to.
//  * That an EXISTING owned-base save is migrated. It is not - see the RESULT's
//    open owner question. Only a NEW capture converts destroyed.
//  * Anything about the raid-side Bastion layout. Raiders still face the walls;
//    the WO holds that out of scope and nothing here touches it.
//
// NO REFLECTION: every call below is a public API.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.Catalog;
using DeNelle.Core.Ops;
using DeNelle.Core.State;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// The capture standdown (A/B), the scene replay path (C), and the rubble-clearing
    /// salvage contract (D/E) for WO-1872. DataRegression-shaped: true = pass with a
    /// one-line summary, false = fail with the offending detail. NEVER throws.
    /// </summary>
    public static class CapturedTownStartsBareRegression
    {
        /// <summary>All relative to Application.dataPath.</summary>
        private const string CensusRel = "_Modules/Village/World/Camps/RaidCaptureCensus.cs";
        private const string ImporterRel = "_Modules/Village/World/Camps/OwnedTownSnapshotImporter.cs";
        private const string WallRel = "_Modules/Village/Walls/WallSegment.cs";
        private const string TowerRel = "_Modules/Village/Buildings/DefenseTower.cs";
        private const string SpireRel = "_Modules/Village/World/Camps/RaidSpire.cs";
        private const string ConstructionRel = "_Modules/Village/World/Camps/OwnedTownConstructionService.cs";

        /// <summary>The owner's ruled salvage share, stated as a LITERAL here on purpose. It is this
        /// suite's own independent statement of the contract, so a default changed in one place only
        /// reds instead of measuring itself against itself (the three-way pin
        /// RemoteTunablesDefaultsRegression documents).</summary>
        private const int ExpectedSalvagePct = 50;

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                // OwnedBaseProgression.Fail traces every refusal and this suite deliberately drives
                // several. Muted for the duration, restored in the finally - never left muted, and
                // never stripped (CLAUDE.md section 12: flag off, the calls stay).
                DeNelle.Core.Diagnostics.FlowTrace.Mute("OwnedBase", "OwnedTown", "Raid");
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "CAPTURED_TOWN_BARE_FAIL captured-town-bare suite THREW: " +
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

            if (!CaseA_DefensiveStructuresConvertRazed(out string a)) { reason = "CAPTURED_TOWN_BARE_FAIL " + a; return false; }
            notes.Append(a);

            if (!CaseB_HeartSurvivesTheFilter(out string b)) { reason = "CAPTURED_TOWN_BARE_FAIL " + b; return false; }
            notes.Append("; ").Append(b);

            if (!CaseC_SceneReplayRazesAtZero(out string c)) { reason = "CAPTURED_TOWN_BARE_FAIL " + c; return false; }
            notes.Append("; ").Append(c);

            if (!CaseD_ClearingPaysFlooredSalvage(out string d)) { reason = "CAPTURED_TOWN_BARE_FAIL " + d; return false; }
            notes.Append("; ").Append(d);

            if (!CaseE_FractionComesFromTheTunable(out string e)) { reason = "CAPTURED_TOWN_BARE_FAIL " + e; return false; }
            notes.Append("; ").Append(e);

            reason = "CAPTURED_TOWN_BARE_OK " + notes.ToString();
            return true;
        }

        // =====================================================================
        //  CASE A - the defensive kinds convert RAZED, and the census uses the filter
        // =====================================================================

        private static bool CaseA_DefensiveStructuresConvertRazed(out string note)
        {
            note = null;

            // Half 1 - the rule itself, driven at every measured condition a body can hold. The
            // measurement is irrelevant BY DESIGN: a wall the player never touched (1.0) must arrive
            // exactly as razed as one she flattened (0.0), which is the whole ruling.
            var kinds = new[] { CapturedStructureKind.Wall, CapturedStructureKind.DefenseTower };
            float[] measured = { 0f, .17f, .5f, .99f, 1f };
            foreach (var kind in kinds)
            {
                if (!CapturedTownStanddown.IsDefensive(kind))
                { note = "[A] " + kind + " is not classified defensive, so the filter will never raze it."; return false; }
                foreach (float m in measured)
                {
                    float converted = CapturedTownStanddown.ConditionOnCapture(kind, m);
                    if (converted != CapturedTownStanddown.RazedCondition)
                    {
                        note = "[A] " + kind + " measured " + m.ToString("0.00") + " converted at " +
                               converted.ToString("0.00") + ", not " +
                               CapturedTownStanddown.RazedCondition.ToString("0.00") +
                               ". A defensive structure the player never attacked is arriving STANDING - " +
                               "this is the WO-1872 defect itself.";
                        return false;
                    }
                }
            }

            // Half 2 - the filter is WIRED. Without this the rule above can be perfectly correct and
            // completely unused, which is exactly what HEAD looked like.
            string census = ReadSource(CensusRel);
            if (census == null) { note = "[A] " + CensusRel + " is missing; the capture seam cannot be pinned."; return false; }
            if (census.IndexOf("CapturedTownStanddown.ConditionOnCapture", StringComparison.Ordinal) < 0)
            {
                note = "[A] RaidCaptureCensus does not call CapturedTownStanddown.ConditionOnCapture " +
                       "(comment-stripped source). The standdown is unreachable, so every untouched wall " +
                       "and tower still converts at its victory-time health and the captured town arrives " +
                       "fortified. Restore the call in Settle().";
                return false;
            }
            if (census.IndexOf("Mathf.Clamp01(entry.Condition())", StringComparison.Ordinal) >= 0 &&
                census.IndexOf("float measured", StringComparison.Ordinal) < 0)
            {
                note = "[A] RaidCaptureCensus still assigns the raw measured condition straight onto the " +
                       "record. That is the pre-WO-1872 body.";
                return false;
            }
            // The repair-supplies guard must stay standing-only, or the standdown re-creates the very
            // "repair the camp" flow the owner removed: after it, EVERY razed body is below 1.
            if (census.IndexOf("s.condition01 > 0f && s.condition01 < 1f", StringComparison.Ordinal) < 0)
            {
                note = "[A] the capture-supplies guard in RaidCaptureCensus.Settle is not restricted to " +
                       "STANDING damage (expected 's.condition01 > 0f && s.condition01 < 1f'). With the " +
                       "standdown in place a bare '< 1f' asks OwnedTownRepairService to quote a repair for " +
                       "rubble, which WO-753 says can never be repaired.";
                return false;
            }

            note = "[A] both defensive kinds convert razed at all 5 measured conditions and " +
                   "RaidCaptureCensus.Settle routes every entry through the filter";
            return true;
        }

        // =====================================================================
        //  CASE B - the non-defensive entry survives
        // =====================================================================

        private static bool CaseB_HeartSurvivesTheFilter(out string note)
        {
            note = null;
            if (CapturedTownStanddown.IsDefensive(CapturedStructureKind.Heart))
            { note = "[B] the Heart is classified defensive; the filter would raze the town's objective."; return false; }

            // Including measured 0: razing the spire is itself a win condition
            // (RaidVictoryController.cs:271), so on the commonest three-star route the Heart's
            // measured condition IS zero. Passing that through would convert a town with nothing
            // standing in it at all.
            foreach (float m in new[] { 0f, .5f, 1f })
            {
                float converted = CapturedTownStanddown.ConditionOnCapture(CapturedStructureKind.Heart, m);
                if (converted != CapturedTownStanddown.HeartCondition)
                {
                    note = "[B] the Heart measured " + m.ToString("0.00") + " converted at " +
                           converted.ToString("0.00") + ", not " +
                           CapturedTownStanddown.HeartCondition.ToString("0.00") +
                           ". The owner's ruling keeps the Heart: \"The Heart and non-defensive dressing " +
                           "still convert.\"";
                    return false;
                }
            }

            // And the Heart is not clearable rubble, whatever else is.
            var heart = Ruin("owned:heart", "tower_arcane_spire", "iron_bastion.spire.0");
            heart.condition01 = CapturedTownStanddown.HeartCondition;
            if (CapturedTownStanddown.IsClearableRubble(heart))
            { note = "[B] a standing Heart reads as clearable rubble; the objective could be cleared away."; return false; }

            note = "[B] the Heart converts standing at every measured condition (including 0) and is not clearable";
            return true;
        }

        // =====================================================================
        //  CASE C - the scene path, pinned by source
        // =====================================================================

        private static bool CaseC_SceneReplayRazesAtZero(out string note)
        {
            note = null;
            string importer = ReadSource(ImporterRel);
            if (importer == null) { note = "[C] " + ImporterRel + " is missing."; return false; }
            if (importer.IndexOf("RestoreOwnedTownCondition", StringComparison.Ordinal) < 0)
            {
                note = "[C] OwnedTownSnapshotImporter no longer replays the saved condition onto the baked " +
                       "twin. The standdown writes condition 0 into the save and NOTHING would act on it - " +
                       "the town would load fortified with a save that says otherwise.";
                return false;
            }

            // Each of the three condition components must still turn 0 into a ruin. This is the ONLY
            // mechanism that makes the razed save visible, and it needs no scene edit precisely
            // because it already exists.
            var razeSites = new Dictionary<string, string> {
                { WallRel, "Collapse" },
                { TowerRel, "Destructible" },
                { SpireRel, "Raze" }
            };
            foreach (var site in razeSites)
            {
                string src = ReadSource(site.Key);
                if (src == null) { note = "[C] " + site.Key + " is missing."; return false; }
                int at = src.IndexOf("RestoreOwnedTownCondition", StringComparison.Ordinal);
                if (at < 0)
                { note = "[C] " + site.Key + " has no RestoreOwnedTownCondition; the importer cannot replay its condition."; return false; }
                // Window the search to the method body rather than the whole file, so an unrelated
                // Collapse/Raze elsewhere cannot green this.
                string body = src.Substring(at, Math.Min(900, src.Length - at));
                if (body.IndexOf(site.Value, StringComparison.Ordinal) < 0)
                {
                    note = "[C] " + site.Key + ".RestoreOwnedTownCondition no longer reaches '" + site.Value +
                           "' at condition 0, so a razed record would restore as a STANDING body.";
                    return false;
                }
            }

            note = "[C] the importer replays condition and all three condition components still raze at 0";
            return true;
        }

        // =====================================================================
        //  CASE D - clearing a ruin pays floor(cost * pct) and removes it
        // =====================================================================

        private static bool CaseD_ClearingPaysFlooredSalvage(out string note)
        {
            note = null;

            // Deliberately ODD costs: 37 and 101 both have a remainder at 50%, so a rounding
            // implementation differs from a flooring one and this case can tell them apart.
            var cost = new ResourceCost { wood = 100, iron = 37, stone = 101, crystals = 3 };
            var salvage = CapturedTownStanddown.Salvage(cost, ExpectedSalvagePct);
            var expected = new ResourceCost { wood = 50, iron = 18, stone = 50, crystals = 1 };
            if (salvage.wood != expected.wood || salvage.iron != expected.iron ||
                salvage.stone != expected.stone || salvage.crystals != expected.crystals)
            {
                note = "[D] salvage of w100/i37/s101/c3 at " + ExpectedSalvagePct + "% paid w" + salvage.wood +
                       "/i" + salvage.iron + "/s" + salvage.stone + "/c" + salvage.crystals +
                       ", expected w50/i18/s50/c1 (FLOORED per resource). Rounding up would let a ruin pay " +
                       "more than the structure cost to build.";
                return false;
            }

            // Degenerate inputs pay NOTHING; they never pay a credit. A console typo must not become
            // a resource printer, which is why the percent is clamped rather than trusted.
            var negative = CapturedTownStanddown.Salvage(new ResourceCost { wood = -500 }, ExpectedSalvagePct);
            var overflowPct = CapturedTownStanddown.Salvage(cost, 100000);
            var underflowPct = CapturedTownStanddown.Salvage(cost, -100000);
            if (negative.wood != 0 || underflowPct.wood != 0 || overflowPct.wood != cost.wood)
            {
                note = "[D] degenerate salvage inputs are not safe: negative cost paid " + negative.wood +
                       " (expected 0), percent -100000 paid " + underflowPct.wood + " (expected 0), " +
                       "percent 100000 paid " + overflowPct.wood + " (expected the clamp at 100% = " + cost.wood + ").";
                return false;
            }

            // ...and clearing REMOVES it. The state edit is OwnedBaseConstruction.TryRetire, the same
            // transaction TrySell already proves; what this pins is that a ruin is clearable BEFORE
            // and not clearable AFTER, so the button cannot be pressed twice for two payouts.
            var property = Fixture();
            var ruin = property.structures.Find(s => s.instanceId == "owned:wall-1");
            if (!CapturedTownStanddown.IsClearableRubble(ruin))
            { note = "[D] the fixture ruin (inherited, condition 0, not retired) does not read as clearable rubble."; return false; }
            if (CapturedTownStanddown.IsClearableRubble(property.structures.Find(s => s.instanceId == "owned:heart")))
            { note = "[D] the fixture's standing Heart reads as clearable rubble."; return false; }

            if (!OwnedBaseConstruction.TryRetire(property, property.revision, "owned:wall-1", out var cleared, out string why))
            {
                note = "[D] clearing the fixture ruin was refused: " + why +
                       ". The captured perimeter must be CLEARABLE even though it stays UNSELLABLE - " +
                       "see the clearableWall carve-out in OwnedTownLayoutSnapshot.";
                return false;
            }
            var after = cleared.structures.Find(s => s.instanceId == "owned:wall-1");
            if (after == null || !after.retired || after.condition01 != 0f)
            { note = "[D] the cleared ruin is not retired at condition 0 after TryRetire."; return false; }
            if (CapturedTownStanddown.IsClearableRubble(after))
            { note = "[D] a cleared ruin is STILL clearable rubble - it could be cleared again for a second payout."; return false; }
            if (cleared.revision != property.revision + 1)
            { note = "[D] clearing did not advance the town revision; a concurrent edit could overwrite it."; return false; }

            note = "[D] salvage floors per resource (w50/i18/s50/c1 of w100/i37/s101/c3 at " +
                   ExpectedSalvagePct + "%), degenerate inputs pay nothing, and a cleared ruin is gone and not re-clearable";
            return true;
        }

        // =====================================================================
        //  CASE E - the fraction is a KNOB, not a literal
        // =====================================================================

        private static bool CaseE_FractionComesFromTheTunable(out string note)
        {
            note = null;
            var spec = RemoteTunables.SpecFor(RemoteTunables.KeyTownCaptureSalvagePct);
            if (spec == null)
            {
                note = "[E] '" + RemoteTunables.KeyTownCaptureSalvagePct + "' is not in RemoteTunables.Registry. " +
                       "An unregistered key has no default and RemoteTunables.Int answers 0 for it, so the " +
                       "owner could never move the salvage share and clearing would silently pay nothing.";
                return false;
            }
            if (spec.Kind != TunableKind.Int)
            { note = "[E] the salvage knob is registered as " + spec.Kind + "; this rail has no Float accessor, so it must be an int PERCENT."; return false; }
            if (spec.Default != ExpectedSalvagePct || RemoteTunables.TownCaptureSalvagePctDefault != ExpectedSalvagePct)
            {
                note = "[E] the salvage default disagrees across its three statements: Registry=" + spec.Default +
                       ", RemoteTunables.TownCaptureSalvagePctDefault=" + RemoteTunables.TownCaptureSalvagePctDefault +
                       ", this suite's literal=" + ExpectedSalvagePct + " (the owner's ruled 0.5). A default may be " +
                       "changed - but not in one place only.";
                return false;
            }

            string construction = ReadSource(ConstructionRel);
            if (construction == null) { note = "[E] " + ConstructionRel + " is missing."; return false; }
            if (construction.IndexOf("RemoteTunables.KeyTownCaptureSalvagePct", StringComparison.Ordinal) < 0)
            {
                note = "[E] OwnedTownConstructionService never reads the salvage knob (comment-stripped source). " +
                       "The share is hardcoded somewhere, and the owner's number cannot move without a build.";
                return false;
            }
            if (construction.IndexOf("CapturedTownStanddown.Salvage(entry.repo.cost, SalvagePct)", StringComparison.Ordinal) < 0)
            {
                note = "[E] the salvage is not computed as CapturedTownStanddown.Salvage(entry.repo.cost, SalvagePct). " +
                       "The ruling is a fraction of the structure's CATALOG BUILD COST, read through the knob - " +
                       "not of the sale refund and not of a literal.";
                return false;
            }
            // The clamp lives at the consumer, and it must: RemoteTunables.Int returns whatever the
            // console row says, and a row above 100 would pay more for a ruin than it cost to build.
            if (construction.IndexOf("pct > 100 ? 100", StringComparison.Ordinal) < 0)
            {
                note = "[E] OwnedTownConstructionService.SalvagePct does not clamp the knob at 100. " +
                       "A console row of 500 would turn the captured perimeter into a resource printer.";
                return false;
            }

            note = "[E] the share is RemoteTunables '" + RemoteTunables.KeyTownCaptureSalvagePct + "' (int, default " +
                   spec.Default + ", agreeing in Registry/const/oracle), read at the one consumer and clamped there";
            return true;
        }

        // =====================================================================
        //  FIXTURES + source reading
        // =====================================================================

        /// <summary>
        /// A minimal VALID owned base with one inherited wall ruin and one standing Heart. The
        /// milestone set stops at EssentialRepairCompleted because OwnedBaseConstruction.CanEdit
        /// requires it before any construction edit - which is exactly why
        /// OwnedBaseProgression.TryInspectPristineTown had to stop counting rubble as damage.
        /// </summary>
        private static OwnedBaseState Fixture()
        {
            var state = new OwnedBaseState {
                baseId = "captured-town-bare-fixture",
                sourceRaidId = OwnedBaseProgression.FinalRaidId,
                templateVersion = "iron-bastion-20260911",
                captureReceiptId = "capture:bare-fixture",
                suppliesReceiptId = "capture:bare-fixture",
                milestoneFlags = OwnedBaseMilestones.OwnershipRevealed | OwnedBaseMilestones.EssentialRepairCompleted
            };
            state.structures.Add(Ruin("owned:wall-1", "wall_stone", "iron_bastion.wall.0"));
            var heart = Ruin("owned:heart", "tower_arcane_spire", "iron_bastion.spire.0");
            heart.condition01 = CapturedTownStanddown.HeartCondition;
            state.structures.Add(heart);
            return state;
        }

        /// <summary>One inherited captured body at condition 0 - a ruin waiting to be cleared.</summary>
        private static OwnedBaseStructure Ruin(string instanceId, string itemId, string templateId)
        {
            return new OwnedBaseStructure {
                instanceId = instanceId,
                condition01 = CapturedTownStanddown.RazedCondition,
                placement = new PlacedStructureData(itemId, 0, 0, 0, 1),
                inheritedPose = new OwnedStructurePose {
                    sourceScene = "RaidBase_IronBastion",
                    sourcePath = "0/1/2",
                    sourceName = instanceId,
                    templateStructureId = templateId,
                    parentFrame = Identity16(),
                    qw = 1f, sx = 1f, sy = 1f, sz = 1f
                }
            };
        }

        /// <summary>A 4x4 identity, which OwnedBaseProgression.ValidatePose demands whenever a
        /// templateStructureId is present.</summary>
        private static float[] Identity16()
        {
            var frame = new float[16];
            frame[0] = frame[5] = frame[10] = frame[15] = 1f;
            return frame;
        }

        /// <summary>
        /// Comment-stripped, string-literal-preserving source. This repo records WHY a line is the
        /// way it is directly above that line, so a raw-text match matches the prose instead of the
        /// code - the false FAIL WO-1778 burned a combined-tree gate run on. Literal-aware, so a
        /// '//' inside a string cannot swallow the rest of a line (CLAUDE.md section 1's trap).
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
                if (ch == '"' || ch == '\'')
                {
                    char quote = ch;
                    sb.Append(ch);
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i]); i++; }
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
    }
}

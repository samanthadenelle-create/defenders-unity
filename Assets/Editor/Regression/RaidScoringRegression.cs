// =============================================================================
// RaidScoringRegression — headless gate for the LOCKED-V1 raid win/stars/loot/HUD
// slice (WO-771.6 + WO-771.11, teleport/deploy loop). Marker: RAID_SCORING_OK.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Wired into DeNelle.Editor.DataRegression.RunAll (one line — see the report).
//
// This closes the "win/stars are OUT" gap the raid spine flagged
// (RaidDeployController.cs:27, RaidVictoryController.cs:34). It proves, from data +
// source (NO PlayMode), that:
//   (A) RaidScoring exists and its PURE star math computes 0-3 from
//       cleared / boss-down / destruction% / the 180s clock (design B5), and its
//       loot math scales with stars + destruction.
//   (B) RaidVictoryController GRANTS loot on the OnCleared victory path (reusing the
//       village EconomyService / GameStateService — not a bespoke economy).
//   (C) a live raid HUD view exists, code-built (uGUI via ElarionUiKit) — NOT uxml.
//
// Mirrors the TowerPerkRegression contract: public static bool Run(out string
// reason); true = pass + a one-line summary, false = fail + the offending detail.
// Never throws (source-lint I/O is guarded).
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>
    /// Data + source regression for the V1 raid scoring/loot/HUD slice. Real static
    /// game code in (RaidScoring.ComputeStars/ComputeLoot), asserted out; plus a
    /// source-lint that the victory grant + the code-built HUD exist. Returns true
    /// (summary) / false (detail); never throws.
    /// </summary>
    public static class RaidScoringRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID SCORING/LOOT/HUD (WO-771.6 + 771.11 V1) ---");

            // =================================================================
            //  (A) PURE star + loot math — the deterministic-enough V1 formulas.
            // =================================================================

            // Stars 0-3 across the design B5 thresholds (cleared / boss / % / clock).
            // OWNER LADDER 2026-07-30: 1 = just cleared, 2 = cleared with high survival OR under
            // the clock, 3 = cleared with BOTH. Sub-clear credit (>=50% razed = 1) is unchanged.
            // The two-axis cases below are the point: a clear no longer implies 3 stars.
            AssertStars(failures, "retreat, <50% razed",              false, false, 0.20f,  10f, 180f, 1.00f, 0);
            AssertStars(failures, "retreat, >=50% razed",             false, false, 0.50f,  10f, 180f, 1.00f, 1);
            AssertStars(failures, "boss down (partial), floor is 1",  false, true,  0.60f,  10f, 180f, 1.00f, 1);
            AssertStars(failures, "clear, slow AND costly -> 1",      true,  true,  1.00f, 240f, 180f, 0.20f, 1);
            AssertStars(failures, "clear, under clock but costly",    true,  true,  1.00f, 120f, 180f, 0.20f, 2);
            AssertStars(failures, "clear, slow but high survival",    true,  true,  1.00f, 240f, 180f, 1.00f, 2);
            AssertStars(failures, "clear, fast AND high survival",    true,  true,  1.00f, 120f, 180f, 1.00f, 3);
            // Threshold boundary is inclusive at HighSurvivalPct, exclusive just under it.
            AssertStars(failures, "clear, fast, survival exactly at threshold",
                        true, true, 1.00f, 120f, 180f, RaidScoring.HighSurvivalPct, 3);
            AssertStars(failures, "clear, fast, survival a hair under threshold",
                        true, true, 1.00f, 120f, 180f, RaidScoring.HighSurvivalPct - 0.01f, 2);
            // A scout clear (no troops deployed) reads survival 1f and must not be punished.
            AssertStars(failures, "clear, fast, no troops deployed",   true,  true,  1.00f, 120f, 180f, 1.00f, 3);

            // =================================================================
            //  (F) WO-1526 — HERO DEATH CAPS THE RESULT AT 2 STARS.
            // -----------------------------------------------------------------
            //  Owner ruling 2026-09-06, verbatim: "Do not let hero death instantly
            //  terminate the raid... let the raid continue, but cap the result at 2
            //  stars if the hero dies. That makes hero survival matter without turning
            //  the hero into a giant red self-destruct button."
            //
            //  ⛔ RED PROOF, MEASURED NOT ASSUMED (2026-09-07, WO-1526 acceptance 1).
            //  The source-lints of (G) and (H) were replayed against the four files as
            //  they stood at HEAD 17e3c4f03, through this file's own StripComments and
            //  MethodBody logic, and returned ELEVEN failures:
            //      G: RaidDeathEndsRaid not false        G: missing NotifyHeroDied
            //      G: missing ApplyHeroDeathCap          G: missing HeroDeathStarCap
            //      G: missing _heroDied                  G: Finalize lacks the cap
            //      H: no liveRaidContinues guard         H: HeroHealth lacks NotifyHeroDied
            //      H: no NotifyHeroDown declaration      H: NotifyHeroDown body not found
            //      H: HeroDeathEndState does not stand down on a live raid
            //  Against the post-change tree the same replay returns ZERO. This block (F)
            //  could not even be replayed at HEAD: ApplyHeroDeathCap and HeroDeathStarCap
            //  did not exist, so it was uncompilable, not merely failing - `grep -n
            //  "HeroDied"` on RaidScoring.cs returned nothing and RaidDeathEndsRaid read
            //  `= true` at RaidScoring.cs:109.
            //
            //  And the behaviour those eleven pinned was the exact opposite of the ruling:
            //  raid-no-abilities-2026-09-06.log 12:59:47 shows "hero death settle: partial
            //  loot for 32% razed" 45s into a 180s raid, army still standing.
            // =================================================================

            // THE ACCEPTANCE CASE: a full clear + a dead hero settles EXACTLY 2, never 3.
            {
                int perfect = RaidScoring.ComputeStars(true, true, 1.00f, 120f, 180f, 1.00f);
                int withDeadHero = RaidScoring.ApplyHeroDeathCap(perfect, true);
                if (perfect != 3)
                    failures.Add($"[WO-1526] the perfect-clear baseline moved: ComputeStars now returns " +
                                 $"{perfect}, not 3, so the hero-death cap case below is no longer testing " +
                                 "a 3-star clear being pulled down to 2.");
                if (withDeadHero != 2)
                    failures.Add($"[WO-1526] a FULL CLEAR with the hero dead settled {withDeadHero} star(s), " +
                                 "owner ruled exactly 2. The clamp is the entire cost of dying - without it " +
                                 "the ruling ('hero survival matters') pays nothing.");
                if (RaidScoring.HeroDeathStarCap != 2)
                    failures.Add($"[WO-1526] RaidScoring.HeroDeathStarCap is {RaidScoring.HeroDeathStarCap}, " +
                                 "owner ruled 2. Change the ruling, not just the constant.");
            }

            // The cap only ever LOWERS: a live hero is returned untouched at every tier, and a
            // result already at or under the cap is not moved. A clamp that could raise a tier
            // would pay MORE for dying, which is the perverse incentive WO-1110 spent a ticket
            // removing from the death exit in the first place.
            for (int s = 0; s <= 3; s++)
            {
                if (RaidScoring.ApplyHeroDeathCap(s, false) != s)
                    failures.Add($"[WO-1526] ApplyHeroDeathCap moved a LIVING hero's {s}-star result to " +
                                 $"{RaidScoring.ApplyHeroDeathCap(s, false)}. It must be identity when the " +
                                 "hero never fell.");
                int capped = RaidScoring.ApplyHeroDeathCap(s, true);
                if (capped > RaidScoring.HeroDeathStarCap)
                    failures.Add($"[WO-1526] ApplyHeroDeathCap({s}, heroDied) returned {capped}, above the " +
                                 $"{RaidScoring.HeroDeathStarCap}-star ceiling.");
                if (capped > s)
                    failures.Add($"[WO-1526] ApplyHeroDeathCap RAISED a {s}-star result to {capped}. Dying " +
                                 "must never pay more than surviving.");
            }

            // The result is ALWAYS in 0..3 across a wide sweep (no out-of-range star).
            for (int di = 0; di <= 10; di++)
            {
                float d = di / 10f;
                foreach (var cleared in new[] { false, true })
                foreach (var boss in new[] { false, true })
                foreach (var t in new[] { 30f, 180f, 400f })
                foreach (var sv in new[] { 0f, 0.5f, RaidScoring.HighSurvivalPct, 1f })
                {
                    int s = RaidScoring.ComputeStars(cleared, boss, d, t, 180f, sv);
                    if (s < 0 || s > 3)
                        failures.Add($"ComputeStars out of range: {s} (cleared={cleared}, boss={boss}, d={d:0.0}, t={t}, surv={sv:0.00})");
                    // A 3-star tier must NEVER be reachable without clearing the base.
                    if (s == 3 && !cleared)
                        failures.Add($"ComputeStars awarded 3 stars WITHOUT a clear (d={d:0.0}, t={t}, surv={sv:0.00})");
                }
            }

            // Loot scales with stars + destruction, and a nothing-raid pays nothing.
            var lootNone = RaidScoring.ComputeLoot(0, 0f, 40, 60, 15, 20);
            var lootHalf = RaidScoring.ComputeLoot(1, 0.5f, 40, 60, 15, 20);
            var lootFull = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20);
            log.AppendLine($"  loot none=({lootNone.Crystals}c/{lootNone.Stone}f) " +
                           $"half=({lootHalf.Crystals}c/{lootHalf.Stone}f) full=({lootFull.Crystals}c/{lootFull.Stone}f)");
            if (!(lootNone.Crystals == 0 && lootNone.Stone == 0))
                failures.Add($"ComputeLoot(0,0) should be empty, got {lootNone.Crystals}c/{lootNone.Stone}f");
            if (!(lootFull.Crystals > lootHalf.Crystals && lootHalf.Crystals > lootNone.Crystals))
                failures.Add($"ComputeLoot crystals not monotonic: none {lootNone.Crystals} <= half {lootHalf.Crystals} <= full {lootFull.Crystals}");
            if (!(lootFull.Stone > lootHalf.Stone && lootHalf.Stone > lootNone.Stone))
                failures.Add($"ComputeLoot food not monotonic: none {lootNone.Stone} <= half {lootHalf.Stone} <= full {lootFull.Stone}");

            // Honesty: scene rewardMultiplier scales the FOOD payout (Hard x1.5 / Extreme x2.2).
            //
            // (!) CORRECTED 2026-09-04. This block used to assert the multiplier scaled
            // CRYSTALS, and that assertion is now the defect. The north-star map
            // (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md section 1) rules crystals OUT of the
            // camp multiplier: "Crystals are timer compression. If raids dump huge amounts
            // of crystals, you accidentally accelerate the already-too-short progression
            // curve." An escalating camp must pay more gold/wood/iron, never more
            // instant-finish. So the case is INVERTED rather than deleted - it now fails if
            // anyone re-applies the multiplier to crystals.
            var lootBase = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20, 1f);
            var lootHard = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20, 1.5f);
            if (lootHard.Stone < lootBase.Stone * 1.4f)
                failures.Add($"ComputeLoot x1.5 mult did not scale food: base {lootBase.Stone} hard {lootHard.Stone}");
            if (lootHard.Crystals != lootBase.Crystals)
                failures.Add($"ComputeLoot applied the camp multiplier to CRYSTALS (x1 {lootBase.Crystals} " +
                             $"vs x1.5 {lootHard.Crystals}). Crystals are timer compression and are ruled OUT " +
                             "of the camp multiplier by the north-star map - a harder camp pays more gold, " +
                             "wood and iron, never more instant-finish.");
            if (System.Math.Abs(RaidScoring.DefaultClockSeconds - 180f) > 0.01f)
                failures.Add($"DefaultClockSeconds is {RaidScoring.DefaultClockSeconds}, expected 180 (UI/scorer honesty).");

            // =================================================================
            //  (B)/(C) SOURCE-LINT — the victory grant + the code-built HUD.
            // =================================================================
            string modulesDir = null;
            try { modulesDir = Path.Combine(Application.dataPath, "_Modules"); } catch { }
            if (string.IsNullOrEmpty(modulesDir) || !Directory.Exists(modulesDir))
            {
                log.AppendLine("  (source-lint skipped — Assets/_Modules not found)");
            }
            else
            {
                // RaidScoring.cs carries the star + loot math + finalize + clock event.
                string scoringSrc = ReadFirst(modulesDir, "RaidScoring.cs");
                if (scoringSrc == null) failures.Add("RaidScoring.cs not found under Assets/_Modules");
                else
                {
                    RequireAll(failures, "RaidScoring.cs", scoringSrc,
                        "ComputeStars", "ComputeLoot", "Finalize", "OnTimeExpired");

                    // WO-853 sec.7 — the structures term must be REAL, not a constant that
                    // nothing reads. Checked against COMMENT-STRIPPED source: a token that only
                    // appears in a doc comment proves nothing, and the whole point of these four
                    // is that live code touches them.
                    string scoringCode = StripComments(scoringSrc);
                    RequireAll(failures, "RaidScoring.cs (code)", scoringCode,
                        "StructuresWeight",     // the 30% exists
                        "StructuresRazedPct",   // and something computes it
                        "CaptureStructureCensus",
                        "TowerAllegiance.PlayerOwned");   // and the census respects ownership

                    // The wall term must read the SHARED 0..1 abstraction. WallSegment stores an
                    // INVERTED 0-100 damage track, so hand-rolling the division is only correct
                    // while MaxHp happens to be 100 - it would silently skew every raid score the
                    // day that constant moves.
                    //
                    // STRIPPED source, and that is not incidental: on its first run this check
                    // failed against RaidScoring.cs's own doc comment, which quotes the forbidden
                    // expression while explaining why not to write it. An oracle that cannot tell
                    // code from prose punishes the author for documenting the trap.
                    if (scoringCode.Contains("Damage / 100") || scoringCode.Contains("Damage/100"))
                        failures.Add("RaidScoring.cs derives wall health from a hardcoded /100 instead of " +
                                     "WallSegment.HpFraction - that breaks silently if WallSegment.MaxHp changes");

                    // ---------------------------------------------------------
                    //  (G) WO-1526 - the ruling constant and the latch, IN CODE.
                    // ---------------------------------------------------------
                    // The constant is checked on STRIPPED source on purpose: RaidScoring.cs's own
                    // doc block quotes both the retired `true` and the ruled `false` while
                    // explaining the history, so an unstripped match would read the prose and pass
                    // whichever way the constant actually points. That is the identical trap the
                    // wall-health check above records hitting.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            scoringCode, @"RaidDeathEndsRaid\s*=\s*false"))
                        failures.Add("[WO-1526] RaidScoring.RaidDeathEndsRaid is not `= false` in live code. " +
                                     "The owner ruled 2026-09-06 that hero death must NOT terminate the raid " +
                                     "(\"let the raid continue, but cap the result at 2 stars\"). With it true, " +
                                     "HeroHealth settles partial loot and routes home the moment the hero falls - " +
                                     "captured at 12:59:47 in raid-no-abilities-2026-09-06.log, 45s into a 180s " +
                                     "raid with a full army still on the field.");
                    RequireAll(failures, "RaidScoring.cs (code)", scoringCode,
                        "NotifyHeroDied",       // the one hero-death latch (shared with WO-1594)
                        "ApplyHeroDeathCap",    // the pure ceiling
                        "HeroDeathStarCap",     // the ruled number, named once
                        "_heroDied");           // and something actually stores the death

                    // Finalize must APPLY the cap, not merely define it. A ceiling that no
                    // settlement calls is the "constant nothing reads" failure WO-853 sec.7
                    // already caught once on this same file.
                    // Matched on the DECLARATION (`RaidResult Finalize(bool cleared)`), never on a
                    // bare `Finalize(` - the file also contains `Finalize(false)` CALL sites, and
                    // a pattern that catches one of those would scan a caller's braces and pass or
                    // fail for reasons that have nothing to do with the settlement.
                    string finalizeBody = MethodBody(scoringCode, @"Finalize\s*\(\s*bool\s+cleared\s*\)");
                    if (string.IsNullOrEmpty(finalizeBody))
                        failures.Add("[WO-1526] could not locate RaidScoring.Finalize's body - the cap " +
                                     "assertion below is blind, so treat this as a FAIL, not a skip.");
                    else if (!finalizeBody.Contains("ApplyHeroDeathCap"))
                        failures.Add("[WO-1526] RaidScoring.Finalize does not route its star tier through " +
                                     "ApplyHeroDeathCap, so a raid where the hero died still settles 3 stars. " +
                                     "(WO-1594's `Mathf.Min(settleStars, honorStars)` is an acceptable " +
                                     "REPLACEMENT for this call - if you took that hunk, widen this check " +
                                     "rather than deleting the ceiling.)");
                }

                // (B) RaidVictoryController grants loot on the victory path via the
                //     village economy (EconomyService.Grant / GameStateService mutators).
                string victorySrc = ReadFirst(modulesDir, "RaidVictoryController.cs");
                if (victorySrc == null) failures.Add("RaidVictoryController.cs not found under Assets/_Modules");
                else
                {
                    // WO-1112 — THE PAYOUT ORACLE IS NOW FALSIFIABLE. Until today this block tested
                    // victorySrc RAW while the (A) block three lines up carefully stripped comments,
                    // with a comment explaining why. RaidVictoryController.cs names EconomyService
                    // FIVE times in the GrantLoot XML doc comment BEFORE the live grant, so deleting
                    // the entire raid payout left RAID_SCORING_OK printing. Half a fix is the worst
                    // outcome available: the suite is trusted precisely because the other half exists.
                    string victoryCode = StripComments(victorySrc);
                    RequireAll(failures, "RaidVictoryController.cs (code)", victoryCode,
                        "GrantLoot", "RaidScoring", "Finalize");
                    bool grantsToEconomy = victoryCode.Contains("EconomyService")
                                        || victoryCode.Contains("AddCrystals")
                                        || victoryCode.Contains("AddStone");
                    if (!grantsToEconomy)
                        failures.Add("RaidVictoryController.cs GrantLoot does not reach the village economy in LIVE CODE " +
                                     "(EconomyService/AddCrystals/AddStone appear nowhere outside comments) - the raid pays nothing");
                }

                // (C) a live raid HUD view exists, code-built (uGUI) — NOT uxml.
                string hudSrc = ReadFirst(modulesDir, "RaidHudController.cs");
                if (hudSrc == null) failures.Add("RaidHudController.cs not found under Assets/_Modules (no live raid HUD)");
                else
                {
                    // WO-1112: STRIPPED, same reason as the victory block above - RaidHudController.cs
                    // names RaidScoring in its file header comment, so the raw test could not tell a
                    // live binding from a sentence about one. (The uxml check below is stripped for the
                    // mirror-image reason: on RAW source a comment EXPLAINING the no-uxml rule reads as
                    // a violation of it.)
                    string hudCode = StripComments(hudSrc);
                    // Shows the four readouts + is built through the kit (code-built).
                    RequireAll(failures, "RaidHudController.cs (code)", hudCode,
                        "ElarionUiKit", "RaidScoring", "RemainingSeconds", "DestructionPct");
                    // §8: code-built uGUI, never uxml.
                    if (hudCode.Contains(".uxml") || hudCode.Contains("UIDocument") || hudCode.Contains("VisualElement"))
                        failures.Add("RaidHudController.cs references uxml/UIDocument/VisualElement — the raid HUD must be code-built uGUI (repo rule §8)");
                }

                // =============================================================
                //  (H) WO-1526 — HERO DEATH DOES NOT END THE RAID.
                // -------------------------------------------------------------
                //  The clamp in (F)/(G) is only half the ruling. The other half is
                //  BEHAVIOURAL and lives across three files, so it is pinned across
                //  three files: the hero-death path must reach a "raid continues"
                //  branch BEFORE the evac, that branch must settle nothing, and the
                //  fallen screen must stand down while the raid is still winnable.
                //
                //  ⛔ WHY THE ORDERING ASSERTION IS THE LOAD-BEARING ONE. Flipping
                //  RaidDeathEndsRaid to false does NOT on its own change behaviour:
                //  HeroHealth's evac test is `if (enemyOwnedScene || raidDeathExit)`
                //  and a LIVE raid base IS enemy-owned (RaidClaimService only flips
                //  ownership at the win), so the evac fired whatever the constant
                //  said. RaidScoring's own doc asserted the opposite ("flipping it is
                //  the whole change") and that assertion was false. A future edit that
                //  moves the new guard below the OR silently restores the bug while
                //  every constant still reads correctly - which is exactly what this
                //  case exists to catch.
                // =============================================================
                string heroSrc = ReadFirst(modulesDir, "HeroHealth.cs");
                if (heroSrc == null)
                    failures.Add("[WO-1526] HeroHealth.cs not found under Assets/_Modules - the hero-death " +
                                 "branch cannot be verified.");
                else
                {
                    string heroCode = StripComments(heroSrc);
                    int iGuard = heroCode.IndexOf("liveRaidContinues", System.StringComparison.Ordinal);
                    int iEvac  = heroCode.IndexOf("enemyOwnedScene || raidDeathExit", System.StringComparison.Ordinal);

                    if (iGuard < 0)
                        failures.Add("[WO-1526] HeroHealth has no live-raid guard (`liveRaidContinues`) in live " +
                                     "code, so the hero's death still takes the EVAC branch: partial loot settled, " +
                                     "army reconciled, routed home - 45s into a 180s raid, with the army still " +
                                     "standing. That is the exact behaviour the owner retired on 2026-09-06.");
                    if (iEvac < 0)
                        failures.Add("[WO-1526] HeroHealth's evac test `enemyOwnedScene || raidDeathExit` is gone, " +
                                     "so this case can no longer prove the live-raid guard precedes it. Re-point " +
                                     "the assertion at whatever replaced it; do not delete it.");
                    if (iGuard >= 0 && iEvac >= 0 && iGuard > iEvac)
                        failures.Add($"[WO-1526] HeroHealth evaluates the EVAC branch (at {iEvac}) BEFORE the " +
                                     $"live-raid guard (at {iGuard}). A live raid base is enemy-owned, so the evac " +
                                     "wins the OR and the raid ends on hero death again - with every constant in " +
                                     "the tree still reading `false`. The ORDER is the fix.");

                    // The guard's body must settle NOTHING. WO-1526 sec.3: "Do not settle loot at
                    // the moment of death. Loot settles when the raid actually ends." And sec.3
                    // again: "Do not let the hero respawn mid-raid."
                    if (iGuard >= 0 && iEvac >= 0 && iGuard < iEvac)
                    {
                        string guardBlock = heroCode.Substring(iGuard, iEvac - iGuard);
                        foreach (var banned in new[] { "SettlePartialLoot", "ReconcileRaidEnd",
                                                       "GoCastle", "Respawn(" })
                            if (guardBlock.Contains(banned))
                                failures.Add($"[WO-1526] HeroHealth's live-raid death branch calls `{banned}`. " +
                                             "The raid CONTINUES on this branch: loot settles when the raid " +
                                             "actually ends, the army is reconciled by whichever real exit " +
                                             "follows, and the hero must not stand back up (sec.3 - the 2-star " +
                                             "clamp IS the cost, and a respawn removes it).");
                        if (!guardBlock.Contains("NotifyHeroDown"))
                            failures.Add("[WO-1526] HeroHealth's live-raid death branch never calls " +
                                         "RaidDeployController.NotifyHeroDown, so nothing latches the 2-star cap " +
                                         "through the raid's own session owner and the player is never told " +
                                         "the army fights on.");
                    }

                    // The latch must be reached on EVERY death path, not just the live-raid one:
                    // a settled-raid death still has to cap what it settles.
                    if (!heroCode.Contains("NotifyHeroDied"))
                        failures.Add("[WO-1526] HeroHealth never calls RaidScoring.NotifyHeroDied, so no raid " +
                                     "records that the hero fell and every result settles uncapped.");
                }

                // The raid's session owner exposes the hero-down seam, and it settles nothing.
                string ctrlSrc = ReadFirst(modulesDir, "RaidDeployController.cs");
                if (ctrlSrc == null)
                    failures.Add("[WO-1526] RaidDeployController.cs not found under Assets/_Modules.");
                else
                {
                    string ctrlCode = StripComments(ctrlSrc);
                    if (!ctrlCode.Contains("public void NotifyHeroDown("))
                        failures.Add("[WO-1526] RaidDeployController no longer exposes " +
                                     "`public void NotifyHeroDown(` - raid session ownership has drifted back " +
                                     "out of the controller (the StrandingWatchdog's own rationale: a raid owns " +
                                     "its own lifecycle, a view never does).");
                    string downBody = MethodBody(ctrlCode, @"void\s+NotifyHeroDown\s*\(");
                    if (!string.IsNullOrEmpty(downBody))
                    {
                        foreach (var banned in new[] { "SettlePartialLoot", "ReconcileRaidEnd",
                                                       "GoCastle", "EndStateView.Show" })
                            if (downBody.Contains(banned))
                                failures.Add($"[WO-1526] RaidDeployController.NotifyHeroDown calls `{banned}` - " +
                                             "hero death is no longer an EXIT, so it must not settle, reconcile, " +
                                             "route home or show a result. The raid ends by objective, Retreat, " +
                                             "the clock or the stranding watchdog, exactly as before.");
                        if (!downBody.Contains("NotifyHeroDied"))
                            failures.Add("[WO-1526] RaidDeployController.NotifyHeroDown does not latch the death " +
                                         "on the scorer (NotifyHeroDied), so the raid settles uncapped.");
                        // Acceptance: the sentence is composed by the VM, not written into the view.
                        if (!downBody.Contains("EndStateVM.HeroDownArmyFightsOn"))
                            failures.Add("[WO-1526] the 'HERO DOWN - your army fights on' line is not taken from " +
                                         "EndStateVM.HeroDownArmyFightsOn. The acceptance criterion is that the VM " +
                                         "composes it: a literal at the call site means the words a player reads " +
                                         "and the words an oracle asserts can drift apart.");
                    }
                }

                // And the copy must match: no "the raid is lost" screen over a raid that is not.
                string deathScreenSrc = ReadFirst(modulesDir, "HeroDeathEndState.cs");
                if (deathScreenSrc == null)
                    failures.Add("[WO-1526] HeroDeathEndState.cs not found under Assets/_Modules.");
                else
                {
                    string deathCode = StripComments(deathScreenSrc);
                    if (!deathCode.Contains("RaidDeathEndsRaid"))
                        failures.Add("[WO-1526] HeroDeathEndState no longer reads RaidScoring.RaidDeathEndsRaid, " +
                                     "so the fallen screen has stopped tracking HeroHealth's branch - the WO-1437 " +
                                     "defect where the words and the behaviour answered the same question " +
                                     "differently.");
                    else if (!System.Text.RegularExpressions.Regex.IsMatch(
                                 deathCode, @"!\s*raidSettled\s*&&\s*!\s*RaidScoring\.RaidDeathEndsRaid"))
                        failures.Add("[WO-1526] HeroDeathEndState does not stand down on a LIVE raid. It shows " +
                                     "EndStateVM.FromHeroDeath, whose true branch prints \"The raid is lost. You " +
                                     "retreat to the castle to fight another day.\" - untrue while the army is " +
                                     "still fighting, and a full-screen panel over the deploy tray the player now " +
                                     "needs most.");
                }

                // Belt-and-braces: no RaidHud.uxml smuggled into the project.
                try
                {
                    var uxml = Directory.GetFiles(Application.dataPath, "RaidHud*.uxml", SearchOption.AllDirectories);
                    if (uxml != null && uxml.Length > 0)
                        failures.Add($"a RaidHud*.uxml exists ({uxml.Length}) — the raid HUD must be code-built uGUI, not uxml");
                }
                catch { /* enumeration best-effort */ }
            }

            // =================================================================
            //  (D) THE OWNER'S DESTRUCTION SPLIT — WO-853 sec.7, ruled 2026-08-07.
            //
            //  EXECUTED, not source-linted: this suite already references DeNelle.Village,
            //  so it reads the real constants. Before today NOTHING pinned these numbers -
            //  the 0.60f values elsewhere in this file are destructionPct ARGUMENTS to
            //  ComputeStars, not the weight - so the split could have drifted silently.
            //  That is exactly the class of miss the 4-day audit kept turning up.
            // =================================================================
            log.AppendLine($"  destruction split: spire {RaidScoring.SpireWeight:P0} / " +
                           $"structures {RaidScoring.StructuresWeight:P0} / " +
                           $"garrison {RaidScoring.GarrisonWeight:P0}");

            AssertWeight(failures, "spire", RaidScoring.SpireWeight, 0.50f);
            AssertWeight(failures, "structures", RaidScoring.StructuresWeight, 0.30f);
            AssertWeight(failures, "garrison (derived)", RaidScoring.GarrisonWeight, 0.20f);

            float weightSum = RaidScoring.SpireWeight + RaidScoring.StructuresWeight + RaidScoring.GarrisonWeight;
            if (Mathf.Abs(weightSum - 1f) > 0.0001f)
                failures.Add($"[split] the destruction weights sum to {weightSum:0.###}, not 1.0 - " +
                             "the razed bar can no longer reach 100% (or overshoots it)");

            // The regression that matters most: structures must carry REAL weight. The whole
            // defect WO-853 opened with was "a raid is a fight, never a demolition" - which is
            // what a 0% structures term means, whatever the seam underneath can damage.
            if (RaidScoring.StructuresWeight <= 0f)
                failures.Add("[split] StructuresWeight is 0 - breaking walls and turrets scores NOTHING again, " +
                             "which is the exact WO-853 defect. The owner ruled 50/30/20 on 2026-08-07.");

            // The spire must stay the single largest term or it stops reading as the objective.
            if (RaidScoring.SpireWeight < RaidScoring.StructuresWeight ||
                RaidScoring.SpireWeight < RaidScoring.GarrisonWeight)
                failures.Add($"[split] the spire ({RaidScoring.SpireWeight:P0}) is no longer the largest term " +
                             $"(structures {RaidScoring.StructuresWeight:P0}, garrison {RaidScoring.GarrisonWeight:P0}) - " +
                             "the objective must remain the primary objective");

            // =================================================================
            //  (E) THE ORACLE'S OWN FALSIFIABILITY — WO-1112.
            //
            //  Every source-lint above is only worth the strip that feeds it, and a
            //  future edit can silently un-strip one (that is EXACTLY how (B) shipped
            //  half-fixed for weeks: (A) stripped, (B) and (C) did not). So we prove the
            //  strip still does its job against a SYNTHETIC file whose only mention of
            //  the payout is prose. If this case ever passes-through, the whole
            //  source-lint section has quietly become unfalsifiable again.
            // =================================================================
            const string proseOnly =
                "/// <summary>Reuses <see cref=\"EconomyService\"/>; calls GrantLoot after Finalize.</summary>\n" +
                "// RaidScoring drives it. AddCrystals / AddStone are the fallback.\n" +
                "public sealed class Deleted { void Nothing() { } }\n";
            string proseStripped = StripComments(proseOnly);
            foreach (var ghost in new[] { "EconomyService", "GrantLoot", "RaidScoring", "AddCrystals", "AddStone", "Finalize" })
            {
                if (proseStripped.Contains(ghost))
                    failures.Add($"[falsifiability] StripComments left '{ghost}' behind on a comment-only sample - " +
                                 "the source-lints in (B)/(C) can be satisfied by a DOC COMMENT, so deleting the live " +
                                 "raid payout would leave RAID_SCORING_OK printing. Fix the strip, never the assertion.");
            }
            // And the strip must not eat live code with it (an over-eager strip would pass (E)
            // by blanking everything, then fail every real check for the wrong reason).
            if (!proseStripped.Contains("public sealed class Deleted"))
                failures.Add("[falsifiability] StripComments destroyed live code on the sample - it is stripping more " +
                             "than comments, so every source-lint above is now testing a mutilated file.");

            if (failures.Count == 0)
            {
                reason = null;
                Debug.Log(log.ToString() + "RAID_SCORING_OK");
                return true;
            }

            reason = "raid-scoring: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "RAID_SCORING_FAIL: " + reason);
            return false;
        }

        /// <summary>
        /// Blank out // line comments and block comments so a source-lint tests CODE, not prose.
        /// Deliberately replaces each comment with a space rather than deleting it, so a token
        /// cannot be forged by two identifiers on either side of a stripped comment.
        /// Not a C# parser: a "//" inside a string literal is also stripped. That is acceptable
        /// here (this file's checks look for identifiers, not string content) and is the same
        /// trade RegressionMarkerRegression.StripLineComments already makes.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src ?? string.Empty;

            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                // Block comment
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '*')
                {
                    int end = src.IndexOf("*/", i + 2);
                    if (end < 0) { sb.Append(' '); break; }
                    sb.Append(' ');
                    i = end + 1;
                    continue;
                }
                // Line comment (covers /// doc comments too)
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '/')
                {
                    int nl = src.IndexOf('\n', i);
                    sb.Append(' ');
                    if (nl < 0) break;
                    sb.Append('\n');
                    i = nl;
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Pin one destruction weight to the owner-ruled value. Tolerance is float-noise only -
        /// these are design numbers, so "close enough" is not a thing.
        /// </summary>
        private static void AssertWeight(List<string> failures, string label, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) > 0.0001f)
                failures.Add($"[split] {label} weight is {actual:0.###}, owner ruled {expected:0.###} " +
                             "(WO-853 sec.7, 2026-08-07). Change the ruling, not just the constant.");
        }

        private static void AssertStars(List<string> failures, string label,
            bool cleared, bool boss, float destruction, float elapsed, float clock,
            float survivalPct, int expected)
        {
            int got = RaidScoring.ComputeStars(cleared, boss, destruction, elapsed, clock, survivalPct);
            if (got != expected)
                failures.Add($"ComputeStars [{label}] expected {expected} star(s), got {got}");
        }

        /// <summary>
        /// WO-1526 — the brace-balanced body of the first method whose signature matches
        /// <paramref name="signature"/>, or an empty string. Shape borrowed verbatim from
        /// RaidExitParityRegression.Body so the two suites cannot drift in what "a method body"
        /// means. Feed it COMMENT-STRIPPED source: a doc comment containing a brace would end the
        /// scan early and quietly shorten every assertion made against the result.
        ///
        /// <para>Declared as a balanced PAIR of char literals on one line, matching
        /// RaidExitParityRegression's precedent - a lone brace char literal trips the
        /// CLAUDE.md rule-1 brace counter that gates every .cs edit in this repo.</para>
        /// </summary>
        private static string MethodBody(string src, string signature)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            const char open = '{', close = '}';
            var m = System.Text.RegularExpressions.Regex.Match(src, signature);
            if (!m.Success) return string.Empty;

            int i = src.IndexOf(open, m.Index + m.Length);
            if (i < 0) return string.Empty;

            int depth = 0;
            for (int j = i; j < src.Length; j++)
            {
                if (src[j] == open) depth++;
                else if (src[j] == close)
                {
                    depth--;
                    if (depth == 0) return src.Substring(i, j - i + 1);
                }
            }
            return string.Empty;
        }

        private static void RequireAll(List<string> failures, string file, string src, params string[] tokens)
        {
            foreach (var tok in tokens)
                if (!src.Contains(tok))
                    failures.Add($"{file} is missing expected token '{tok}'");
        }

        /// <summary>First matching file's text under <paramref name="root"/>, or null.</summary>
        private static string ReadFirst(string root, string fileName)
        {
            try
            {
                var hits = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
                if (hits == null || hits.Length == 0) return null;
                return File.ReadAllText(hits[0]);
            }
            catch { return null; }
        }
    }
}

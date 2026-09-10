// =============================================================================
// RaidHudThumbBandRegression [raid-thumb-band] — WO-1436, owner ruling 2026-09-06
// -----------------------------------------------------------------------------
// ⭐ THE INVARIANT: THE HERO'S ABILITY ROW OWNS THE THUMB POSITION AT THE BOTTOM
// OF THE SCREEN, AND NOTHING IS EVER DRAWN INTO THAT BAND.
//
// The raid deploy bar (FOOTMAN / ARCHER / Rally ON / RETREAT) stacks ABOVE it.
// Owner's reasoning, and the reason this is the right way round: CASTING IS
// CONSTANT, DEPLOYING IS OCCASIONAL — the constant action belongs under the thumb.
//
// WHAT WENT WRONG, MEASURED AT SOURCE (WO-1436 §7.3 — not inferred, not theorised)
// -------------------------------------------------------------------------------
//   surface                              Y band (viewport)   canvas sortingOrder
//   kit `actionBar` (holds combatDock)   0.015 – 0.150        4 000
//   raid deploy bar panel                0.010 – 0.160       30 000
// The same band, with the deploy bar 26 000 layers above it. The owner's device
// screenshot (mid-raid, 1:58 remaining, Razed 79%) shows the ability faces
// CAST · BLOCK · Arcane Bolt · Mend · EMPTY · ITEM rendering *underneath* the
// deploy strip. They existed, the log truthfully said they had been pushed, and
// the player still could not fight — which from the chair is indistinguishable
// from not having them. Then WO-1436's posture fix made a raid declare combat for
// its WHOLE duration (correct, and it stays), so combatDock went from
// intermittently buried to PERMANENTLY buried.
//
// ⛔ WHY NO EXISTING ORACLE COULD CATCH IT, AND WHAT THIS ONE DOES DIFFERENTLY
// ---------------------------------------------------------------------------
// Every HUD oracle in this folder asks "does surface X lay itself out correctly?"
// — and both surfaces did, perfectly, in isolation. Nobody asked whether the two
// of them, authored in two assemblies that CANNOT SEE EACH OTHER, could occupy the
// same pixels. This oracle asks exactly that, and asks it from the AUTHORED
// ANCHORS ON BOTH SIDES rather than from figures copied into this file.
//
// ⚠ WHY FRACTIONS FROM TWO DIFFERENT CANVASES ARE COMPARABLE. HudAreasHost builds
// a ScreenSpaceOverlay canvas (sortingOrder 4000); RaidDeployController builds a
// separate ScreenSpaceOverlay canvas (30000). On a ScreenSpaceOverlay canvas an
// anchor fraction is a fraction OF THE SCREEN whatever CanvasScaler the canvas
// carries — the scaler changes the size of a reference unit, never where
// anchorMin.y = 0.16 lands. So the band arithmetic below is valid across the two,
// and sortingOrder stops mattering the moment the bands are exclusive. ⛔ A future
// overlap must be fixed by separating the BANDS, never by re-ordering the canvases.
//
// ═══ RED PROOF — THIS ORACLE FAILS AGAINST THE BUILD AS IT SHIPPED ═══════════
// Run against the pre-WO-1436-layout tree, all four cases go red:
//   [1] deploy bar 0.010–0.160 vs ability row 0.015–0.150 →
//       HudLayoutBands.Intersects(...) == TRUE → FAIL "the raid deploy bar is
//       drawn INTO the ability row's band".
//   [2] DeployBarBand.yMin (0.010) < BottomOverlayFloorY (0.160) → FAIL.
//   [3] RaidDeployController.cs contained the literal
//       `new Vector2(0.98f, 0.16f)` — a hardcoded bottom-Y — → FAIL.
//   [4] HudAreasHost.cs's ActionBar mount contained the literal `0.150f` instead
//       of reading HudLayoutBands.ThumbActionRowMaxY → FAIL.
// Cases 3 and 4 are the ones that matter for the NEXT seat: they are not about
// today's numbers, they are about the numbers never being re-typed into two
// files again (CLAUDE.md §2/§5/§8/§16 — the duplicated-state failure, four times
// documented, every instance correct on the day it was written).
//
// === WO-1464 (2026-09-07) - THE SUITE GREW A MEASURED CASE ===================
// WO-1436 proved the deploy bar clears the ability row's Y band. It did not ask
// whether either raid surface cleared anything ELSE, and two of them did not:
// the tray slab sat on the movement stick (which reaches y 0.330, more than
// twice the ability row's 0.150 ceiling), and the raid readout painted its clock
// over the hero nameplate and its stars over the compass. Cases 5-7 close that:
//   [5] a FIXTURE case - a live HudAreasHost is instantiated and the three raid
//       bands are measured against the mounts the game actually builds. This is
//       the only case that can answer "do the pixels miss each other"; 3/4/6 only
//       keep the numbers from being retyped.
//   [6] neither raid surface may re-author its seat as a Village-local literal.
//   [7] HudAreasHost.ActionBarMinX and HudLayoutBands.MoveClusterMount.xMax must
//       AGREE. ActionBarMinX's source text is pinned verbatim by
//       HudDockLayoutRegression so it cannot be dissolved into the Core table; two
//       numbers that must agree in two files is exactly the failure above, so the
//       agreement is asserted rather than trusted.
// The red proof for [5] - every figure taken from the owner's device capture,
// Logs/device/screens/owner-screen-20260907-004502.png - sits on the method.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.HUD.Kit;
using DeNelle.Village;
using DeNelle.Village.Hero;   // RaidSelectionScreen.NeedPx - the one seat formula (WO-1464 [seat])

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Asserts the raid deploy bar and the hero ability row occupy EXCLUSIVE screen bands,
    /// measured from the authored anchors on both sides, and that neither side re-authors the
    /// shared band as a local literal.
    /// </summary>
    public static class RaidHudThumbBandRegression
    {
        public static bool Run(out string reason)
        {
            try
            {
                var failures = new List<string>();
                // Visible stand-downs (a headless editor that cannot instantiate a MonoBehaviour
                // host). They ride out on the PASS reason so an unmeasured case is never silent -
                // and they are NOT failures, which is what keeps a capability gap out of the red
                // column (CLAUDE.md §12; the three-way rule).
                var notes = new List<string>();

                // ── CASE 1 + 2: the numeric exclusion, from the authored anchors ──────────
                // The ability row's x edges come from HudAreasHost (their source text is pinned
                // by HudDockLayoutRegression, so they are read, never restated); its y edges come
                // from HudLayoutBands, the ONE copy. The deploy bar's band comes from
                // RaidDeployController's own public accessor, which BuildHud itself consumes —
                // so if the runtime seat and this assertion ever disagree, they cannot.
                Rect abilityRow = HudLayoutBands.ThumbActionRowBand(
                    HudAreasHost.ActionBarMinX, HudAreasHost.ActionBarMaxX);
                Rect deployBar = RaidDeployController.DeployBarBand;
                Rect deployStatus = RaidDeployController.DeployStatusBand;

                if (HudLayoutBands.Intersects(abilityRow, deployBar))
                    failures.Add("[buried-abilities] the raid deploy bar (y " +
                                 F(deployBar.yMin) + ".." + F(deployBar.yMax) +
                                 ") is drawn INTO the hero ability row's band (y " +
                                 F(abilityRow.yMin) + ".." + F(abilityRow.yMax) +
                                 "). The faces render and cannot be tapped — the WO-1436 defect. " +
                                 "Fix by moving the BAR, never by re-ordering the canvases.");

                if (HudLayoutBands.Intersects(abilityRow, deployStatus))
                    failures.Add("[buried-abilities] the raid deploy STATUS line (y " +
                                 F(deployStatus.yMin) + ".." + F(deployStatus.yMax) +
                                 ") is drawn INTO the hero ability row's band (y " +
                                 F(abilityRow.yMin) + ".." + F(abilityRow.yMax) + ").");

                if (deployBar.yMin < HudLayoutBands.BottomOverlayFloorY - HudLayoutBands.Epsilon)
                    failures.Add("[thumb-floor] the deploy bar bottoms out at y " +
                                 F(deployBar.yMin) + ", BELOW the reserved floor " +
                                 F(HudLayoutBands.BottomOverlayFloorY) +
                                 " (HudLayoutBands.BottomOverlayFloorY). The thumb band is the " +
                                 "ability row's, by owner ruling 2026-09-06.");

                if (HudLayoutBands.Intersects(deployBar, deployStatus))
                    failures.Add("[deploy-self] the deploy bar and its own status line overlap (bar y " +
                                 F(deployBar.yMin) + ".." + F(deployBar.yMax) + ", status y " +
                                 F(deployStatus.yMin) + ".." + F(deployStatus.yMax) + ").");

                // Stacking ORDER, stated as an assertion rather than assumed from the numbers:
                // the deploy bar must be ABOVE the ability row, not merely disjoint from it.
                if (deployBar.yMin < abilityRow.yMax)
                    failures.Add("[stack-order] the deploy bar starts at y " + F(deployBar.yMin) +
                                 ", which is not above the ability row's top edge " +
                                 F(abilityRow.yMax) + ". The owner's ruling is a STACK " +
                                 "(abilities under the thumb, deploy bar on top), not a swap.");

                // ── CASE 3 + 4: neither side may re-author the shared band as a literal ───
                // This is the assertion that outlives today's numbers. The whole defect exists
                // because DeNelle.Village cannot reference DeNelle.HUD (CLAUDE.md §5), so the
                // only way the two agreed was a human retyping a number — and this repo has four
                // separate documented cases of exactly that going stale.
                string raid = Read("Assets/_Modules/Village/Troops/RaidDeployController.cs");
                string host = Read("Assets/_Modules/HUD/Kit/HudAreasHost.cs");

                Require(raid, "HudLayoutBands.StackAboveThumbBand", failures,
                    "[shared-seam] RaidDeployController no longer derives its band from " +
                    "HudLayoutBands — a Village-local Y literal is exactly how the bar came to " +
                    "sit on the ability row");
                Forbid(raid, "new Vector2(0.98f, 0.16f)", failures,
                    "[shared-seam] RaidDeployController has a hardcoded bar top-Y again " +
                    "(`new Vector2(0.98f, 0.16f)`) — the literal that shipped the defect");
                Forbid(raid, "new Vector2(0.02f, 0.01f)", failures,
                    "[shared-seam] RaidDeployController has a hardcoded bar bottom-Y again " +
                    "(`new Vector2(0.02f, 0.01f)`) — it reached below the reserved thumb band");
                Forbid(raid, "new Vector2(0.02f, 0.165f)", failures,
                    "[shared-seam] the deploy status line is hardcoded again " +
                    "(`new Vector2(0.02f, 0.165f)`) instead of stacking off the shared gap");

                Require(host, "HudLayoutBands.ThumbActionRowMinY", failures,
                    "[shared-seam] HudAreasHost no longer reads the ability row's BOTTOM edge " +
                    "from HudLayoutBands — the Village side can no longer follow it");
                Require(host, "HudLayoutBands.ThumbActionRowMaxY", failures,
                    "[shared-seam] HudAreasHost no longer reads the ability row's TOP edge from " +
                    "HudLayoutBands — the Village side can no longer follow it");
                Forbid(host, "new Vector2(ActionBarMaxX, 0.150f)", failures,
                    "[shared-seam] the ActionBar mount authors its own top-Y literal (0.150f) " +
                    "again — DeNelle.Village cannot see it, so the deploy bar cannot follow it");

                // The Core seam must stay in DeNelle.Core, which is the ONLY assembly both sides
                // may reference. If it ever migrates into DeNelle.HUD the invariant is unbuildable.
                string bandsPath = "Assets/_Modules/Core/UI/HudLayoutBands.cs";
                if (!File.Exists(FullPath(bandsPath)))
                    failures.Add("[shared-seam] " + bandsPath + " is gone — the reserved thumb " +
                                 "band has no lawful home outside DeNelle.Core (CLAUDE.md §5)");

                // ── CASE 5: WO-1464 — the three raid bands vs the LIVE mount table ────────
                CheckRaidBandsAgainstMounts(deployBar, deployStatus, failures, notes);

                // ── CASE 6: WO-1464 — neither raid surface re-authors its seat ────────────
                string raidHud = Read("Assets/_Modules/Village/Troops/RaidHudController.cs");

                Require(raid, "HudLayoutBands.BottomOverlayLeftX", failures,
                    "[stick-seam] RaidDeployController no longer derives its LEFT edge from " +
                    "HudLayoutBands.BottomOverlayLeftX. WO-1436 lifted the bar clear of the " +
                    "ability row's Y band, but the movement stick reaches HIGHER than that row " +
                    "does (y 0.330 vs 0.150), so a strip that starts at the screen edge still " +
                    "covers the game's only locomotion control");
                Forbid(raid, "DeployBandMinX = 0.02f", failures,
                    "[stick-seam] RaidDeployController has a hardcoded tray LEFT edge again " +
                    "(`DeployBandMinX = 0.02f`) — the literal that put the slab over the stick in " +
                    "Logs/device/screens/owner-screen-20260907-004502.png");

                Require(raidHud, "HudLayoutBands.RaidReadoutBand", failures,
                    "[readout-seam] RaidHudController no longer derives its seat from " +
                    "HudLayoutBands.RaidReadoutBand — a Village-local rect cannot see the hero " +
                    "nameplate or the compass it lands on, which is the whole WO-1464 defect");
                Forbid(raidHud, "new Vector2(0.02f, 0.86f)", failures,
                    "[readout-seam] the raid readout is authored as a full-width top strip again " +
                    "(`new Vector2(0.02f, 0.86f)`). The top row is spoken for at every x: Vitals " +
                    "reaches y 0.983, Status 0.990, System 0.985 — a full-width raid strip cannot " +
                    "exist there without painting over one of them");

                // ── CASE 7: the two copies of the stick's right edge must AGREE ───────────
                // HudAreasHost.ActionBarMinX is documented as "also the MoveCluster's RIGHT edge"
                // and its SOURCE TEXT is pinned verbatim by HudDockLayoutRegression, so it cannot
                // be dissolved into HudLayoutBands. Two numbers that must agree, in two files, is
                // the failure CLAUDE.md documents four times over — so the agreement is asserted
                // here instead of trusted, and a drift is a red build.
                if (Math.Abs(HudAreasHost.ActionBarMinX - HudLayoutBands.MoveClusterMount.xMax) >
                    HudLayoutBands.Epsilon)
                    failures.Add("[stick-seam] HudAreasHost.ActionBarMinX (" +
                                 F(HudAreasHost.ActionBarMinX) + ") and " +
                                 "HudLayoutBands.MoveClusterMount.xMax (" +
                                 F(HudLayoutBands.MoveClusterMount.xMax) + ") have DRIFTED. " +
                                 "HudAreasHost documents its own const as 'also the MoveCluster's " +
                                 "RIGHT edge'; the raid tray derives its left edge from the Core " +
                                 "copy. While they disagree, either the ability dock or the deploy " +
                                 "tray is sitting on the movement stick.");

                Require(host, "HudLayoutBands.MoveClusterMount", failures,
                    "[stick-seam] HudAreasHost no longer mounts HudArea.MoveCluster from " +
                    "HudLayoutBands.MoveClusterMount — the Village side is measuring a band the " +
                    "game does not build");

                if (failures.Count > 0)
                {
                    reason = "raid-thumb-band: " + failures.Count + " failure(s): " +
                             string.Join(" | ", failures);
                    return false;
                }

                reason = "raid-thumb-band: the hero ability row owns y " +
                         F(abilityRow.yMin) + ".." + F(abilityRow.yMax) +
                         " (the thumb band); the raid deploy bar stacks above it at y " +
                         F(deployBar.yMin) + ".." + F(deployBar.yMax) + " with its status line at y " +
                         F(deployStatus.yMin) + ".." + F(deployStatus.yMax) +
                         "; the raid readout owns the reserved right column " +
                         R(RaidHudController.ReadoutBand) +
                         ", clear of the hero nameplate, the compass and the movement stick " +
                         "(WO-1464, measured against a live HudAreasHost mount table); both sides " +
                         "derive from HudLayoutBands (DeNelle.Core.UI), neither re-authors the " +
                         "band as a local literal" +
                         (notes.Count > 0 ? " | " + string.Join(" | ", notes) : "");
                return true;
            }
            catch (Exception ex)
            {
                reason = "raid-thumb-band: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // =====================================================================
        //  WO-1464 — THE MEASURED CASE: three raid bands vs the LIVE mount table
        // ---------------------------------------------------------------------
        // ⭐ THIS IS A FIXTURE CASE, NOT A TEXT LINT. Cases 3/4/6 keep the numbers from being
        // retyped; only this one can tell you whether the pixels actually miss each other. It
        // instantiates a real HudAreasHost (the pattern HudUiRegression check 9 uses) and reads
        // the anchors off the mounts the game builds, so the raid side is compared against the
        // HUD as MOUNTED rather than as described in a comment.
        //
        // ═══ RED PROOF — THIS CASE FAILS AGAINST THE BUILD THE OWNER PLAYED ═══════════
        // Build 358872, Logs/device/screens/owner-screen-20260907-004502.png (2670x1200, 1:13
        // remaining, Razed 72%, SPIRE 20%). Every figure below is the authored anchor pair as it
        // stood in that build:
        //   [tray-vs-stick]    deploy bar    x 0.020-0.980 y 0.160-0.310
        //                      MoveCluster   x 0.010-0.270 y 0.030-0.330   -> INTERSECTS
        //                      (the stick's ring is visible under the slab in the capture)
        //   [tray-vs-stick]    deploy status x 0.020-0.980 y 0.320-0.360   -> INTERSECTS
        //   [readout-vs-hud]   raid readout  x 0.020-0.980 y 0.860-0.990
        //                      hero plate    x 0.011-0.240 y 0.833-0.983   -> INTERSECTS
        //                      ("1:13" painted over "Th... Lv 7")
        //   [readout-vs-hud]   raid readout  vs Status (compass) 0.340-0.660 x, 0.845-0.990 y
        //                                                                   -> INTERSECTS
        //                      ("1/3" and "Troops 10/10" over the NE / E ticks and the bar)
        //
        // ⚠ ActionRail and QueueStatus are DELIBERATELY NOT asserted against the readout: the
        // readout TAKES that seat, and it is lawful to because hud-areas.json lists actionRail
        // with an empty widget array in both hostile postures and does not list queueStatus at
        // all, while WO-1436 makes a raid hostile for its whole duration. That conflict is
        // recorded in HudLayoutBands.RaidReadoutBand rather than asserted here — asserting it
        // would fail on a band nothing draws during a raid, which is the NightMarket
        // minimap-plate precedent.
        // =====================================================================
        private static void CheckRaidBandsAgainstMounts(Rect deployBar, Rect deployStatus,
                                                        List<string> failures, List<string> notes)
        {
            HudAreasHost host = null;
            try
            {
                try { host = HudAreasHost.Create(null); }
                catch (Exception ex)
                {
                    // HARNESS-CAPABILITY-ABSENT -> a visible stand-down, never a silent pass
                    // (the three-way rule; CLAUDE.md §12 - an unknown must not read as green).
                    notes.Add(RegressionOutcome.PartialSkip("RAID BANDS VS MOUNTS (WO-1464)",
                        "HudAreasHost could not be instantiated headlessly (" + ex.GetType().Name +
                        ": " + ex.Message + ") - the raid/HUD overlap was NOT measured this run"));
                    return;
                }
                if (host == null)
                {
                    // FIXTURE-ABSENT -> FAIL naming the path. HudAreasHost.Create news a
                    // GameObject, AddComponents the host, Builds and returns unconditionally, so
                    // a null is the mount factory returning nothing - not an editor limitation.
                    failures.Add("[raid-bands] HudAreasHost.Create(null) returned NULL without " +
                                 "throwing (Assets/_Modules/HUD/Kit/HudAreasHost.cs). The area mount " +
                                 "table does not exist this run, so every raid/HUD clearance below " +
                                 "is UNPROVEN - not clear, unmeasured.");
                    return;
                }

                Rect readout = RaidHudController.ReadoutBand;

                // The occupants a raid actually paints under, by name, from the live table.
                var stick = MountBand(host, HudArea.MoveCluster);
                var vitals = MountBand(host, HudArea.Vitals);
                var status = MountBand(host, HudArea.Status);
                var system = MountBand(host, HudArea.System);
                var targetInfo = MountBand(host, HudArea.TargetInfo);
                var heroPlate = HudLayoutBands.SubRect(vitals, HudLayoutBands.HeroPlateInVitals);

                if (IsZero(stick) || IsZero(vitals) || IsZero(status))
                {
                    failures.Add("[raid-bands] a required mount resolved to a ZERO band (stick " +
                                 R(stick) + ", vitals " + R(vitals) + ", status " + R(status) +
                                 ") - hud-areas.json declares all three in the hostile postures a " +
                                 "raid runs in, so the table did not build what it declares and the " +
                                 "raid clearances are unproven.");
                    return;
                }

                // ── the tray must clear the movement stick ───────────────────────────
                RequireClear(failures, "[tray-vs-stick]", "the raid deploy tray", deployBar,
                             "the movement stick (HudArea.MoveCluster)", stick,
                             "covering it does not degrade the HUD, it removes the player's only " +
                             "way to move on a phone");
                RequireClear(failures, "[tray-vs-stick]", "the raid deploy status line", deployStatus,
                             "the movement stick (HudArea.MoveCluster)", stick,
                             "the stick reaches y 0.330, higher than the reserved thumb band's " +
                             "0.150 floor - clearing the ability row is not the same as clearing " +
                             "the stick");

                // ── the readout must clear the town HUD's top row ────────────────────
                RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                             "the hero nameplate", heroPlate,
                             "the owner's capture shows the raid clock painted over the hero's " +
                             "name, level and health bars");
                RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                             "the compass band (HudArea.Status)", status,
                             "the owner's capture shows the star count and troop count painted " +
                             "over the compass ticks and the bar under them");
                RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                             "the System mount", system,
                             "hostile(activebattle) occupies System with fleeButton + " +
                             "settingsButton, so this band is NOT free during a raid");
                if (!IsZero(targetInfo))
                    RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                                 "the target frame (HudArea.TargetInfo)", targetInfo,
                                 "hostile postures occupy it with targetFrame / enemyBuffRow / " +
                                 "castBar - a raid is hostile end to end (WO-1436)");
                RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                             "the raid deploy tray", deployBar,
                             "the two raid surfaces have always been checked against each other; " +
                             "that check must keep passing while the readout moves");
                RequireClear(failures, "[readout-vs-hud]", "the raid readout", readout,
                             "the raid deploy status line", deployStatus, null);

                // ── [seat] a band that carries text must be able to SEAT it ─────────
                // ⛔ AN OVERLAP FIX THAT THINS A BAND IS NOT A FIX. TMP's Ellipsis overflow CULLS
                // a line it cannot seat, so a row under NeedPx(FontFloor) renders BLANK rather
                // than small - and the runtime relax guard does not run in the headless capture
                // the acceptance PNG comes from. This is the WO-1519 [seat] finding applied to
                // the surfaces WO-1464 moved, so the class cannot come back through this door.
                float needPx = RaidSelectionScreen.NeedPx((int)ElarionUiKit.FontFloor);
                float refH = HudLayoutBands.CanvasReferenceSize(
                    HudLayoutBands.DeviceWidth, HudLayoutBands.DeviceHeight).y;
                // ⚠ The status line is asserted against the HARD floor, not FontFloor, and that is
                // deliberate: its band (WO-1436's DeployStatusHeight, 0.040) resolves to 38.62 px
                // against NeedPx(30) = 38.58 - a 0.04 px margin, which is not an assertion, it is
                // a coin toss. So BuildHud fits that one label at ElarionUiKit.FontHardFloor and
                // this case asserts the same floor the code uses. Asserting a floor the code does
                // not honour would red a working line; asserting nothing would let the band be
                // thinned. NeedPx(20) = 26.8.
                RequireSeats(failures, "the raid deploy status line", deployStatus.height, refH,
                             RaidSelectionScreen.NeedPx((int)ElarionUiKit.FontHardFloor),
                             "it carries a full sentence of tap guidance and is fitted at the hard floor");
                // Five text rows stack in the readout column (timer / SPIRE / Razed / stars /
                // troops); the thinnest authored one is the Razed row at 0.120 of the panel.
                RequireSeats(failures, "the raid readout column", readout.height * 0.120f, refH,
                             needPx, "its thinnest authored row (Razed) is 0.120 of it");
                // ⛔ WO-1646 RE-POINT: THE TILE HEIGHT IS READ, NOT TYPED.
                // This asserted `deployBar.height * 0.64f * 0.48f`, and the 0.64 went STALE the
                // moment WO-1646 landed: the tray tiles' band is now DERIVED from
                // ElarionUiKit.MinTouchPx (the three bar faces and the tiles all resolved 92.7 ref
                // px tall at 2670x1200 against a 112 floor, so ClampMinTouch was silently growing
                // them into their neighbours every frame). The literal still PASSED - it
                // under-states the real height, so the assert was strictly conservative - which is
                // exactly what makes a stale copy dangerous: it measures something the code no
                // longer does and says nothing when the two diverge. Same duplicated-state failure
                // CLAUDE.md sec.2 / sec.5 / sec.16 each document, and the same reason this suite
                // already REQUIRES the readout seat to read HudLayoutBands.RaidReadoutBand rather
                // than a copied rect (:207-209).
                //
                // ⚠ THE 0.48f IS DELIBERATELY LEFT TYPED, and that is a judgement, not an
                // oversight. It is the count badge's own anchor span (RaidDeployController.cs
                // cr.anchorMin 0.26 / cr.anchorMax 0.74 = 0.48), and those anchors did NOT move -
                // so unlike the 0.64 it is still accurate. It is the same duplicated-state shape
                // and worth hoisting on the next touch of that badge; re-pointing it now would be
                // an unrequested change to a passing, correct assert.
                RequireSeats(failures, "the raid deploy tray",
                             deployBar.height * RaidDeployController.DeployFaceHeightFraction * 0.48f, refH,
                             needPx,
                             "the tile count badge is 0.48 of a tile whose height is DERIVED from MinTouchPx");
            }
            finally
            {
                if (host != null && host.gameObject != null)
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        /// <summary>A mount's band in SCREEN fractions. Every HudAreasHost mount is anchored
        /// inside a full-stretch canvas root, so its anchors ARE screen fractions.</summary>
        private static Rect MountBand(HudAreasHost host, HudArea area)
        {
            var rt = host == null ? null : host.Mount(area);
            if (rt == null) return default(Rect);
            return Rect.MinMaxRect(rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y);
        }

        private static bool IsZero(Rect r) { return r.width <= 0f || r.height <= 0f; }

        private static string R(Rect r)
        {
            return "(" + F(r.xMin) + ".." + F(r.xMax) + " x, " + F(r.yMin) + ".." + F(r.yMax) + " y)";
        }

        /// <summary>A band of <paramref name="heightFraction"/> of screen must resolve to at least
        /// <paramref name="needPx"/> reference px on the owner's device, or TMP culls the line.</summary>
        private static void RequireSeats(List<string> failures, string whoName, float heightFraction,
                                         float refH, float needPx, string why)
        {
            float havePx = heightFraction * refH;
            if (havePx >= needPx) return;
            failures.Add("[seat] " + whoName + " resolves to " + havePx.ToString("0.#") +
                         " reference px at the owner's " + HudLayoutBands.DeviceWidth.ToString("0") +
                         "x" + HudLayoutBands.DeviceHeight.ToString("0") + ", but a line at the " +
                         ElarionUiKit.FontFloor.ToString("0") + " px FontFloor needs " +
                         needPx.ToString("0.#") + " - " + why + ". TMP Ellipsis CULLS a line it " +
                         "cannot seat, so this row renders BLANK, not small. Grow the band; never " +
                         "answer an overlap by thinning one.");
        }

        private static void RequireClear(List<string> failures, string tag, string whoName, Rect who,
                                         string otherName, Rect other, string why)
        {
            if (IsZero(other) || !HudLayoutBands.Intersects(who, other)) return;
            failures.Add(tag + " " + whoName + " " + R(who) + " INTERSECTS " + otherName + " " +
                         R(other) + (string.IsNullOrEmpty(why) ? "." : ". " + why + ".") +
                         " Fix by separating the BANDS in HudLayoutBands - never by re-ordering " +
                         "the canvases, and never by nudging a coordinate (WO-1464 sec.3).");
        }

        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_THUMB_BAND_OK - " + reason);
            else Debug.LogError("RAID_THUMB_BAND_FAIL - " + reason);
        }

        private static string F(float v) { return v.ToString("F3"); }

        private static string FullPath(string relative)
        {
            return Path.Combine(Application.dataPath, "..", relative);
        }

        private static string Read(string relative)
        {
            return File.ReadAllText(FullPath(relative));
        }

        private static void Require(string source, string token, List<string> failures, string message)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) < 0) failures.Add(message);
        }

        private static void Forbid(string source, string token, List<string> failures, string message)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0) failures.Add(message);
        }
    }
}

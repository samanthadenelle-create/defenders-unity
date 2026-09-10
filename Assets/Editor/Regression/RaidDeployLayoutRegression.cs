// =============================================================================
// RaidDeployLayoutRegression - the RAID DEPLOY screen, MEASURED on a live canvas.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
//
// WO-1519 (owner ask 2026-09-06 20:14, on device frame
// Logs/device/screens/owner-screen-20260906-201443.png, build 358574: "screensshot,
// can we make this screen pop?") plus her 20:24 ruling "Remove it from the deploy
// screen" about the ECHO GUIDE block. Carries the DEPLOY-SCREEN half of WO-1464
// (the army-cap line printed on top of the hero portraits).
//
// =============================================================================
//  WHY THIS SUITE EXISTS, AND WHY IT IS NOT ANOTHER SOURCE LINT
// =============================================================================
// Two oracles already touched this screen and NEITHER could see its layout:
//
//   * RaidDeployUiRegression [deploy-bands-disjoint] read the band constants out of
//     RaidDeployScreen.cs WITH A REGULAR EXPRESSION
//     (@"private\s+const\s+float\s+GuideBandY0\s*=\s*([0-9.]+)f?\s*;"). Rename a
//     constant and that case stops judging anything - silently - while still
//     reporting OK. It also cannot know how many PIXELS a fraction becomes, which is
//     the only unit the defect lives in.
//   * RaidSelectionLayoutRegression S7 lints the deploy screen for one string
//     ("withBackdrop: false"). One layer, not a layout.
//
// So the screen's geometry has been judged by prose since WO-839. This suite builds
// the REAL bands on a REAL canvas at four surfaces and measures them, iterating
// RaidDeployScreen.BandsFor() / EnemyCardBands() / PartyRowBands() - the LIVE tables
// the builders themselves lay out from, never a copy of their numbers. Rename a
// constant here and the oracle moves with it.
//
// =============================================================================
//  THE RED PROOF - what this suite says about the tree it replaced
// =============================================================================
// Measured against the pre-WO-1519 geometry (an edit-only lane: these are computed
// from the shipped literals and the kit's own arithmetic, not from a Unity run):
//
//   R1  THE SEAT LAW, BROKEN IN THE FILE'S OWN COMMENT. The WO-1385/1403 band budget
//       claimed "every single-line row >= 36 px so the 30 px FontFloor seats WITHOUT
//       the runtime relax guard". It does not. ElarionUiKit.FitSingleLine floors auto-
//       sizing at ElarionUiKit.FontFloor (30), and TMP's Ellipsis overflow CULLS THE
//       WHOLE LINE when the floor's line height exceeds the rect
//       (ElarionUiKitObsidian.cs:3096-3110, and UiKitTextFitGuard - the relax net - is
//       a RUNTIME component that does not run in the edit-mode headless capture the
//       acceptance PNGs come from). A 30 pt line needs
//       RaidSelectionScreen.NeedPx(30) = 38.58 px. 36 < 38.58, on EVERY 36 px row of
//       the old right column. Case [seat] FAILS on that budget and PASSES on the new
//       one, where the thinnest text band is 38.6 px and most are 41.
//
//   R2  THE ARMY LINE HAD NO BAND. It was a bare label at body y 0.630-0.680 sitting
//       directly under a party row that ran to 0.885 - and on the owner's own frame it
//       printed "Army: 10 / 10 slots" ACROSS the Grom/Sylas portraits (WO-1464
//       evidence, seeker-357453-raid-deploy.png). It now owns body 0.548-0.648 with a
//       plate, and case [disjoint] measures the separation instead of trusting it.
//
//   R3  THE ECHO GUIDE BLOCK. Case [no-echo-guide] FAILS on any tree where the deploy
//       screen composes it - which is every tree before this one.
//
// MUTATIONS THIS SUITE CATCHES (named, so the RED is reproducible):
//   M1. Thin any text band back under NeedPx(FontFloor)          -> [seat].
//   M2. Overlap the army band with the party row again           -> [disjoint].
//   M3. Push any band above 1.0 or below 0.0 of the body         -> [inside].
//   M4. Re-add an ECHO GUIDE band / picker to the deploy screen  -> [no-echo-guide].
//   M5. Delete EchoGuideService or the NoteExpeditionTarget call -> [guide-intact].
//   M6. Restore a hue-only difficulty pill (DifficultyColor)     -> [no-hue-only].
//   M7. Make ArmyBandText stop saying FULL at the cap            -> [vm-army-band].
//   M8. Build the spoils chips from a second estimator           -> [vm-spoils-chips].
//   M9. Paint vm.ScoutReport (4 lines) in the 3-line well again  -> [vm-scout-intel].
//  M10. Type a sub-floor fontPx back into BuildSpoilsChips, or   -> [spoils-floor].
//       shrink the spoils row's share of its band
//
//   R4 (WO-1669, owner ruling 2026-09-10 12:16) THE ROW WAS DRAWN UNDER THE FLOOR AND
//      NOTHING COULD SEE IT. BuildSpoilsChips passed a bare literal `fontPx: 24f` while
//      ElarionUiKit.FontFloor is 30 - the one text site on this screen below the mobile-
//      legibility floor (WO-1640's RESULT checked the other three: 32 and 40). Case
//      [seat] passed throughout, because the BAND (40.3 ref px) seats a floor-height line
//      perfectly well; what it never judged was the size handed to CostRow.
//      ⚠ HOW TO REPRODUCE THE RED, stated precisely because "red on HEAD" would be a
//      claim no one can run: HEAD has neither SpoilsChipFontPx nor SpoilsRowAnchorMin/Max,
//      so this case does not go red against HEAD - it does not COMPILE against it. The
//      reproducible red is mutation M10: type HEAD's own values back into those two
//      constants (24f, and 0.10/0.90) and BOTH halves fail - the constant (24 < 30) and
//      the row's share of the band (0.80 x 40.3 = 32.2 px against NeedPx(30) = 38.58).
//
// REGISTRATION: WIRED. Read at source 2026-09-10 (WO-1669): DataRegression.cs:623
// runs this suite inside a Guard.Try as "raid-deploy-layout suite". The paragraph here
// used to read "NOT wired here" with the snippet to add - true the day WO-1519 authored
// it, stale ever since, and exactly the kind of line that makes a seat re-wire a suite
// that is already running. Every case below is a LIVE pin, not a proposal.
//
// Markers: RAID_DEPLOY_LAYOUT_OK / RAID_DEPLOY_LAYOUT_FAIL.
// Standalone: run-unity-method
//   -Method DeNelle.Editor.Regression.RaidDeployLayoutRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor.Regression
{
    public static class RaidDeployLayoutRegression
    {
        private const string ScreenRel = "_Modules/Village/Hero/RaidDeployScreen.cs";
        private const string VmRel     = "_Modules/Village/Hero/RaidDeployVM.cs";
        private const string GuideServiceRel = "_Modules/Village/World/Camps/EchoGuideService.cs";

        /// <summary>Sub-pixel slack. Two bands separated by less than this are TOUCHING,
        /// which on a real screen is a row printing into its neighbour's descenders.</summary>
        private const float Eps = 0.5f;

        private struct Surface { public string Name; public float W, H; }

        /// <summary>The surfaces this screen actually meets. The first is the owner's own
        /// Seeker frame (2670x1200, the one WO-1519 was written from); the rest bracket it so
        /// a fix cannot be tuned to one aspect - including PORTRAIT, where the panel is
        /// tallest and a fraction-authored band is at its most forgiving, and 1920x1080, the
        /// resolution the headless UI capture writes.</summary>
        private static readonly Surface[] Surfaces =
        {
            new Surface { Name = "2670x1200", W = 2670f, H = 1200f },
            new Surface { Name = "2340x1080", W = 2340f, H = 1080f },
            new Surface { Name = "1920x1080", W = 1920f, H = 1080f },
            new Surface { Name = "1080x1920", W = 1080f, H = 1920f },
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RAID_DEPLOY_LAYOUT_OK - " + reason);
            else Debug.LogError("RAID_DEPLOY_LAYOUT_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== RaidDeployLayoutRegression: WO-1519 deploy screen geometry ===");

            // -- FIXTURE. Absence is RED with the path, never a silent skip: an oracle that
            // quietly stops measuring a file that moved is how the deploy door went uncovered
            // for five WOs in the first place.
            string screenPath = Path.Combine(Application.dataPath, ScreenRel.Replace('/', Path.DirectorySeparatorChar));
            string vmPath     = Path.Combine(Application.dataPath, VmRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(screenPath))
            {
                reason = "raid-deploy-layout FAIL x1: [fixture] MISSING " + ScreenRel +
                         " - the screen this suite measures is not on disk.";
                return false;
            }
            if (!File.Exists(vmPath))
            {
                reason = "raid-deploy-layout FAIL x1: [fixture] MISSING " + VmRel +
                         " - the view model that composes this screen's words is not on disk.";
                return false;
            }

            // -- CAPABILITY. No canvas => a DECLARED stand-down, never a silent pass.
            GameObject probe = null;
            string canvasWhy = null;
            try { probe = NewCanvas("rdl-probe", 100f, 100f); }
            catch (Exception ex) { canvasWhy = ex.GetType().Name + ": " + ex.Message; }
            finally { if (probe != null) UnityEngine.Object.DestroyImmediate(probe); }
            if (canvasWhy != null)
            {
                return RegressionOutcome.Skip(out reason, "RAID DEPLOY LAYOUT",
                    "no UI canvas can be instantiated in this environment (" + canvasWhy +
                    ") - no band rect can be measured");
            }

            // LINT THE CODE, NOT THE PROSE. This file's own banner and the screen's own
            // comments NAME the removed symbols on purpose (that is what keeps them removed);
            // a raw Contains() would red on the very explanation that holds the ruling.
            string screenSrc = StripComments(File.ReadAllText(screenPath));
            string vmSrc     = StripComments(File.ReadAllText(vmPath));

            try
            {
                foreach (var s in Surfaces)
                {
                    var surface = s;
                    foreach (var sub in new[] { true, false })
                    {
                        bool hasSubHeader = sub;
                        string tag = surface.Name + ":" + (hasSubHeader ? "frame" : "procedural");
                        Case(failures, "bands:" + tag,
                             () => CaseBodyBands(surface, hasSubHeader, failures, notes, log));
                    }
                    Case(failures, "card:" + surface.Name,
                         () => CaseEnemyCard(surface, failures, log));
                    Case(failures, "party:" + surface.Name,
                         () => CasePartyRow(surface, failures, log));
                }
                Case(failures, "no-echo-guide",   () => CaseNoEchoGuide(screenSrc, failures, log));
                Case(failures, "guide-intact",    () => CaseGuideFeatureIntact(screenSrc, failures, log));
                Case(failures, "no-hue-only",     () => CaseNoHueOnlyDifficulty(screenSrc, failures, log));
                Case(failures, "vm-army-band",    () => CaseArmyBandWords(failures, log));
                Case(failures, "spoils-floor",    () => CaseSpoilsFloor(failures, log));
                Case(failures, "vm-spoils-chips", () => CaseSpoilsChips(vmSrc, failures, log));
                Case(failures, "vm-scout-intel",  () => CaseScoutIntel(failures, log));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : string.Empty;
            if (failures.Count == 0)
            {
                reason = "RAID DEPLOY LAYOUT OK - " + Surfaces.Length + " surfaces x 2 chrome paths " +
                         "MEASURED on a live canvas: every body band, every enemy-card row and every " +
                         "party-row band is disjoint from its column neighbours, sits wholly inside its " +
                         "host, and is at least NeedPx(FontFloor)=" +
                         RaidSelectionScreen.NeedPx(30).ToString("0.#") + " ref px tall so TMP cannot cull " +
                         "the line; the spoils chips draw at ElarionUiKit.FontFloor=" +
                         ElarionUiKit.FontFloor.ToString("0") + " in a row that can hold that line " +
                         "(WO-1669); the deploy screen composes NO Echo Guide block while EchoGuideService " +
                         "and the NoteExpeditionTarget seam survive; no hue-only difficulty pill; and the " +
                         "VM's army band, spoils chips and scout-intel projection all agree with the one " +
                         "producer behind them" + noteStr;
                Debug.Log("RAID_DEPLOY_LAYOUT_OK\n" + log);
                return true;
            }

            Debug.LogError("RAID_DEPLOY_LAYOUT_FAIL: " + failures.Count + " failure(s)\n" + log);
            reason = "raid-deploy-layout FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE [bands] - the body's two column stacks, MEASURED.
        // =====================================================================
        // Builds the real panel -> body -> one region per band, at the surface's real
        // reference box, then asserts three things in PIXELS:
        //   [inside]   every band sits wholly inside the body zone (0..1, no spill)
        //   [disjoint] no band overlaps another IN THE SAME COLUMN
        //   [seat]     every band carrying text is at least NeedPx(FontFloor) tall
        // On the procedural path it also asserts every band clears the legacy meta strip,
        // which is the row the badge/stars/clock keep when FrameCore's sub-header is absent.
        private static void CaseBodyBands(Surface s, bool hasSubHeader, List<string> failures,
                                          List<string> notes, StringBuilder log)
        {
            string tag = "[bands:" + s.Name + ":" + (hasSubHeader ? "frame" : "procedural") + "]";
            GameObject root = null;
            try
            {
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("rdl-" + s.Name + "-" + hasSubHeader, refW, refH);
                var rootRt = (RectTransform)root.transform;

                var panel = Region(rootRt, "Panel",
                    RaidDeployScreen.PanelAnchorMin, RaidDeployScreen.PanelAnchorMax);

                // THE BODY IS THE SCREEN'S OWN RECORDED FLOOR, and it is deliberately NOT
                // RaidSelectionScreen.ComputeWellBand. The sibling screen derives its well
                // straight from ElarionUiKit.ZonesFor; THIS panel additionally takes
                // BuildObsidianPanel's CLOSE-BAND RESERVATION, whose inputs (ZonesFor,
                // FrameZones, DefaultCloseZone) are private to the kit. Using the sibling's
                // function here would model a 534 ref px body on the owner's surface where
                // the screen really gets 411 - every band would clear the seat law in this
                // suite and some would render BLANK on her phone. An oracle measuring the
                // wrong thing and reporting a pass is worse than no oracle.
                // RaidDeployScreen.MinBodyFracOfPanel is the FLOOR (see its banner for the
                // provenance and the one-line grep that re-proves it); a bigger body only
                // makes every band taller, so passing at the floor passes everywhere.
                var body = Region(panel, "Body", new Vector2(0.055f, 0f),
                                  new Vector2(0.945f, RaidDeployScreen.MinBodyFracOfPanel));
                Settle(rootRt);

                float bodyPx = body.rect.height;
                if (bodyPx <= Eps)
                {
                    failures.Add(tag + " the body zone measured " + bodyPx.ToString("0.##") +
                                 " ref px - there is no band to judge; the panel anchors or the kit's " +
                                 "reserved zones have collapsed.");
                    return;
                }

                var bands = RaidDeployScreen.BandsFor(hasSubHeader);
                if (bands == null || bands.Length == 0)
                {
                    failures.Add(tag + " RaidDeployScreen.BandsFor returned no bands - the layout table " +
                                 "is empty and nothing on this screen has an authored home.");
                    return;
                }

                var rects = new Dictionary<string, RectTransform>();
                foreach (var b in bands)
                {
                    float x0 = b.Column == RaidDeployScreen.ColumnLeft ? 0.00f : 0.51f;
                    float x1 = b.Column == RaidDeployScreen.ColumnLeft ? 0.49f : 1.00f;
                    rects[b.Name] = Region(body, "Band_" + b.Name, new Vector2(x0, b.Y0), new Vector2(x1, b.Y1));
                }
                Settle(rootRt);

                var bodyRect = WorldRect(body);
                log.AppendLine(tag + " body " + bodyPx.ToString("0") + " ref px (floor " +
                               RaidDeployScreen.MinBodyFracOfPanel.ToString("0.###") + " of the panel)");

                for (int i = 0; i < bands.Length; i++)
                {
                    var b = bands[i];
                    var r = WorldRect(rects[b.Name]);

                    // [inside]
                    if (b.Y0 < -0.0001f || b.Y1 > 1.0001f || b.Y1 <= b.Y0)
                        failures.Add(tag + " band '" + b.Name + "' is " + b.Y0.ToString("0.###") + ".." +
                                     b.Y1.ToString("0.###") + " - not a positive band inside the body.");
                    if (!Contains(bodyRect, r))
                        failures.Add(tag + " band '" + b.Name + "' " + Fmt(r) + " is NOT inside the body " +
                                     Fmt(bodyRect) + " - it would paint over the footer, the shared Close " +
                                     "or the header.");

                    // [seat] - the whole point. A band under the floor's line renders NOTHING.
                    if (b.FontPt > 0 && r.height + 0.05f < b.NeedsPx)
                        failures.Add(tag + " band '" + b.Name + "' measured " + r.height.ToString("0.#") +
                                     " ref px but a line at the " + ElarionUiKit.FontFloor.ToString("0") +
                                     " px FontFloor needs " + b.NeedsPx.ToString("0.#") +
                                     " - TMP's Ellipsis overflow CULLS the whole line at that height, so this " +
                                     "row renders BLANK, not small. (UiKitTextFitGuard would relax it at " +
                                     "runtime; it does not run in the headless capture the acceptance PNG " +
                                     "comes from.)");

                    // [disjoint] - within the column only; the two columns are separate stacks.
                    for (int j = i + 1; j < bands.Length; j++)
                    {
                        var c = bands[j];
                        if (c.Column != b.Column) continue;
                        var rc = WorldRect(rects[c.Name]);
                        if (Overlaps(r, rc))
                            failures.Add(tag + " '" + b.Name + "' " + Fmt(r) + " OVERLAPS '" + c.Name + "' " +
                                         Fmt(rc) + " in the " + b.Column + " column - two rows print on the " +
                                         "same pixels. (This is the WO-1464 defect by name when the pair is " +
                                         "army/party: the cap line used to sit across the hero portraits.)");
                    }
                }

                // The procedural path keeps the legacy meta strip at the body top.
                if (!hasSubHeader)
                {
                    foreach (var b in bands)
                        if (b.Y1 > RaidDeployScreen.MetaStripY0 - 0.0001f)
                            failures.Add(tag + " band '" + b.Name + "' tops out at " + b.Y1.ToString("0.###") +
                                         ", into the legacy meta strip that starts at " +
                                         RaidDeployScreen.MetaStripY0.ToString("0.###") +
                                         " - on the procedural chrome the badge/stars/clock row lives there, " +
                                         "so FallbackShift is not clearing it.");
                }

                if (bodyPx < 380f) notes.Add(s.Name + " body is only " + bodyPx.ToString("0") + " ref px");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =====================================================================
        //  CASE [card] - the ENEMY BASE hero card's own stacked rows.
        // =====================================================================
        // WO-1519 section 2.2 makes this card the loudest thing on the screen, and the two BIG
        // numerals are the reason. A numeral row that gets thinned to fit "one more thing"
        // is the exact regression this case exists for: the stat value seats at FontHead,
        // shrinks to the floor, and at the floor it still needs a real band.
        private static void CaseEnemyCard(Surface s, List<string> failures, StringBuilder log)
        {
            string tag = "[card:" + s.Name + "]";
            MeasureNestedBands(tag, s, "enemy", RaidDeployScreen.EnemyCardBands(),
                               "the ENEMY BASE hero card", failures, log);
        }

        // =====================================================================
        //  CASE [party] - the medallion row's plate and name.
        // =====================================================================
        // WO-1385 found "Thrain" / "Grom" painted OVER the olive plates because the name
        // had 11 px under a full-height niche. WO-1519 grew the row and the plate; this
        // measures that the name still has a row of its own inside it.
        private static void CasePartyRow(Surface s, List<string> failures, StringBuilder log)
        {
            string tag = "[party:" + s.Name + "]";
            MeasureNestedBands(tag, s, "party", RaidDeployScreen.PartyRowBands(),
                               "the party medallion row", failures, log);
        }

        // Shared measurement for bands authored as fractions OF A HOST BAND (the hero card,
        // the party row) rather than of the body. Same three laws, one host deeper.
        private static void MeasureNestedBands(string tag, Surface s, string hostBandName,
                                               RaidDeployScreen.DeployBand[] bands, string what,
                                               List<string> failures, StringBuilder log)
        {
            GameObject root = null;
            try
            {
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("rdl-nested-" + hostBandName + "-" + s.Name, refW, refH);
                var rootRt = (RectTransform)root.transform;

                var panel = Region(rootRt, "Panel",
                    RaidDeployScreen.PanelAnchorMin, RaidDeployScreen.PanelAnchorMax);
                var body = Region(panel, "Body", new Vector2(0.055f, 0f),
                                  new Vector2(0.945f, RaidDeployScreen.MinBodyFracOfPanel));

                // The HOST band, taken off the same live table the builder uses.
                RaidDeployScreen.DeployBand host = default(RaidDeployScreen.DeployBand);
                bool found = false;
                foreach (var b in RaidDeployScreen.BandsFor(true))
                    if (b.Name == hostBandName) { host = b; found = true; break; }
                if (!found)
                {
                    failures.Add(tag + " no '" + hostBandName + "' band in RaidDeployScreen.BandsFor - " +
                                 what + " has no home to be measured in.");
                    return;
                }

                float hx0 = host.Column == RaidDeployScreen.ColumnLeft ? 0.00f : 0.51f;
                float hx1 = host.Column == RaidDeployScreen.ColumnLeft ? 0.49f : 1.00f;
                var hostRt = Region(body, "Host_" + hostBandName,
                                    new Vector2(hx0, host.Y0), new Vector2(hx1, host.Y1));

                var rects = new Dictionary<string, RectTransform>();
                foreach (var b in bands)
                    rects[b.Name] = Region(hostRt, "Sub_" + b.Name, new Vector2(0.02f, b.Y0), new Vector2(0.98f, b.Y1));
                Settle(rootRt);

                var hostRect = WorldRect(hostRt);
                log.AppendLine(tag + " " + what + " measured " + hostRect.height.ToString("0") + " ref px");

                for (int i = 0; i < bands.Length; i++)
                {
                    var b = bands[i];
                    var r = WorldRect(rects[b.Name]);
                    if (b.Y0 < -0.0001f || b.Y1 > 1.0001f || b.Y1 <= b.Y0)
                        failures.Add(tag + " '" + b.Name + "' is " + b.Y0.ToString("0.###") + ".." +
                                     b.Y1.ToString("0.###") + " - not a positive band inside " + what + ".");
                    if (!Contains(hostRect, r))
                        failures.Add(tag + " '" + b.Name + "' " + Fmt(r) + " hangs off " + what + " " +
                                     Fmt(hostRect) + ".");
                    if (b.FontPt > 0 && r.height + 0.05f < b.NeedsPx)
                        failures.Add(tag + " '" + b.Name + "' measured " + r.height.ToString("0.#") +
                                     " ref px, under the " + b.NeedsPx.ToString("0.#") +
                                     " a floored line needs - it renders BLANK, not small.");
                    for (int j = i + 1; j < bands.Length; j++)
                    {
                        var rc = WorldRect(rects[bands[j].Name]);
                        if (Overlaps(r, rc))
                            failures.Add(tag + " '" + b.Name + "' OVERLAPS '" + bands[j].Name + "' inside " +
                                         what + " - " + Fmt(r) + " vs " + Fmt(rc) + ".");
                    }
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =====================================================================
        //  CASE [no-echo-guide] - WO-1519 section 2B, the owner's 20:24 ruling.
        // =====================================================================
        // "Remove it from the deploy screen." A removal that is not pinned comes back the
        // next time someone re-reads WO-1380 and thinks the band is missing. Every symbol
        // below is checked in STRIPPED source, so the screen's own explanatory banner - which
        // names all of them on purpose - cannot fail this case.
        private static void CaseNoEchoGuide(string src, List<string> failures, StringBuilder log)
        {
            const string tag = "[no-echo-guide]";
            string[] banned =
            {
                "BuildGuideBand", "RefreshGuideBand", "OnCycleGuide",
                "_guideNameLabel", "_guideMemoryLabel",
                "EchoGuideService.MemoryLineFor", "EchoGuideService.SelectedGuide",
                "EchoGuideService.AvailableGuides", "EchoGuideService.SelectGuide",
            };
            int before = failures.Count;
            foreach (var token in banned)
                if (src.IndexOf(token, StringComparison.Ordinal) >= 0)
                    failures.Add(tag + " RaidDeployScreen composes the Echo Guide again ('" + token +
                                 "' is live code, not a comment). Owner ruling 2026-09-06 20:24, WO-1519 " +
                                 "section 2B: the block and its CHANGE button LEAVE this screen. The feature " +
                                 "is not cut - it belongs on the Echoes screen, not here.");
            // The band's own header string, which would survive a rename of every symbol above.
            if (src.IndexOf("\"ECHO GUIDE\"", StringComparison.Ordinal) >= 0)
                failures.Add(tag + " the literal \"ECHO GUIDE\" is back on the deploy screen.");
            if (failures.Count == before)
                log.AppendLine(tag + " no guide band, no picker, no header literal on the deploy screen.");
        }

        // =====================================================================
        //  CASE [guide-intact] - the OTHER half of the ruling, and it matters more.
        // =====================================================================
        // WO-1519 section 2B is "one surface removed, not a feature cut". A lane that
        // deleted EchoGuideService, or quietly dropped NoteExpeditionTarget from OnDeploy,
        // would satisfy [no-echo-guide] and SILENCE the Echo's world beat - the payoff
        // EchoWorldPresence.SpeakGuideMemory delivers after the battle. This case is why
        // that cannot pass as "done".
        private static void CaseGuideFeatureIntact(string screenSrc, List<string> failures, StringBuilder log)
        {
            const string tag = "[guide-intact]";
            int before = failures.Count;

            string svcPath = Path.Combine(Application.dataPath,
                GuideServiceRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(svcPath))
                failures.Add(tag + " EchoGuideService.cs is GONE (" + GuideServiceRel + "). WO-1519 " +
                             "section 2B removes a SURFACE; the service and its 24 memory lines STAY.");

            if (screenSrc.IndexOf("NoteExpeditionTarget", StringComparison.Ordinal) < 0)
                failures.Add(tag + " RaidDeployScreen no longer calls EchoGuideService.NoteExpeditionTarget. " +
                             "That call is the SEAM, not the band: it is how the Echo knows where it was " +
                             "taken, and without it EchoWorldPresence has nothing to say when it brings the " +
                             "Echo back after the battle.");

            if (failures.Count == before)
                log.AppendLine(tag + " EchoGuideService present and the NoteExpeditionTarget seam still fires " +
                               "from the deploy command.");
        }

        // =====================================================================
        //  CASE [no-hue-only] - WO-1519 section 2.5, and a standing project rule.
        // =====================================================================
        // The owner is red/green colourblind. The difficulty pill tinted its plate
        // Regular->green / Hard->amber / Extreme->red and put the same word on all three,
        // so the colour carried nothing she could read and the plate was the loudest thing
        // in the header. The WORD plus the diamonds is the whole signal now.
        private static void CaseNoHueOnlyDifficulty(string src, List<string> failures, StringBuilder log)
        {
            const string tag = "[no-hue-only]";
            if (src.IndexOf("DifficultyColor", StringComparison.Ordinal) >= 0)
            {
                failures.Add(tag + " DifficultyColor is back in RaidDeployScreen - a hue-only difficulty " +
                             "channel. The owner cannot read it; use shape, weight or another word.");
                return;
            }
            if (src.IndexOf("DifficultyLabel", StringComparison.Ordinal) < 0)
                failures.Add(tag + " DifficultyLabel is gone too - the difficulty now has NO channel at all. " +
                             "The colour was retired because the WORD was carrying the meaning; do not " +
                             "retire the word with it.");
            else
                log.AppendLine(tag + " difficulty reads as a word (+ the diamond row), never a hue.");
        }

        // =====================================================================
        //  CASE [vm-army-band] - WO-1517's word, on WO-1464's own band.
        // =====================================================================
        private static void CaseArmyBandWords(List<string> failures, StringBuilder log)
        {
            const string tag = "[vm-army-band]";
            int before = failures.Count;

            // No roster at all: absence of a number is NOT a full army.
            var none = new RaidDeployVM(Camp(), null, null, null, null);
            try
            {
                if (none.ArmyFull)
                    failures.Add(tag + " a VM with no army reports ArmyFull=true - the deploy screen would " +
                                 "tell a player with no roster that their army is full.");
                if (none.ArmyBandText != "ARMY -")
                    failures.Add(tag + " with no army the band must read 'ARMY -'; got '" + none.ArmyBandText + "'.");
            }
            finally { none.Dispose(); }

            // Below the cap: the numbers, no state word.
            var part = new RaidDeployVM(Camp(), ArmyOf(3), null, OneSlot, null);
            try
            {
                if (part.ArmyFull)
                    failures.Add(tag + " 3 of " + ArmyStorage.DefaultMaxArmySize + " slots reports ArmyFull=true.");
                string want = "ARMY 3 / " + ArmyStorage.DefaultMaxArmySize;
                if (part.ArmyBandText != want)
                    failures.Add(tag + " expected '" + want + "'; got '" + part.ArmyBandText + "'.");
            }
            finally { part.Dispose(); }

            // AT the cap: WO-1517's ruling word, or the player taps into a silent refusal.
            var full = new RaidDeployVM(Camp(), ArmyOf(ArmyStorage.DefaultMaxArmySize), null, OneSlot, null);
            try
            {
                if (!full.ArmyFull)
                    failures.Add(tag + " a roster at the cap reports ArmyFull=false.");
                if (full.ArmyBandText == null ||
                    full.ArmyBandText.IndexOf(RaidDeployVM.ArmyFullWord, StringComparison.Ordinal) < 0)
                    failures.Add(tag + " a full army's band does not carry the word '" +
                                 RaidDeployVM.ArmyFullWord + "' (WO-1517: the screens must SAY the army is " +
                                 "full). Got '" + full.ArmyBandText + "'.");
                // The legacy line and the band are one read, so they can never disagree.
                if (full.ArmyCapText == null || full.ArmyCapText.IndexOf(
                        ArmyStorage.DefaultMaxArmySize.ToString(), StringComparison.Ordinal) < 0)
                    failures.Add(tag + " ArmyCapText '" + full.ArmyCapText + "' does not carry the same cap " +
                                 "the band does - the two readouts have diverged.");
            }
            finally { full.Dispose(); }

            if (failures.Count == before)
                log.AppendLine(tag + " army band words hold at 0 / below-cap / at-cap.");
        }

        // =====================================================================
        //  CASE [spoils-floor] - WO-1669. THE SPOILS ROW IS DRAWN AT THE FLOOR, AND THE
        //  ROW GETS THE WHOLE BAND TO DRAW IT IN.
        // =====================================================================
        //  Owner ruling 2026-09-10 12:16, closing the open item WO-1640 declined to decide:
        //      "the raid staging screen's SPOILS resource band is seated at FontLabel 40
        //       (RaidDeployScreen.cs:219) but drawn at 24 px (:890), under the 30 px floor
        //       - raise the draw to the floor."
        //
        //  WHY NEITHER EXISTING CASE COULD SEE IT, which is the whole reason this one exists:
        //   * [seat] next door judges the BAND against NeedPx(FontFloor). The spoils band is
        //     40.3 ref px on the 411 px body floor against a 38.58 px demand - it PASSED,
        //     every run, while the row inside it was drawn at 24. A band that seats a floor-
        //     height line says nothing about the size actually handed to CostRow.
        //   * CostRowFitRegression's SPOILS cases measure the WRAP of the prefix word at
        //     whatever size they are given. SealPrefixCell is size-agnostic, so they are
        //     green at 24 and green at 30 - honest about the word, blind to the size.
        //  The sub-floor number was a BARE LITERAL at a call site with no oracle over it.
        //  That is the duplicated/unwatched-state shape CLAUDE.md warns about, and it is why
        //  this case reads RaidDeployScreen's LIVE constants rather than the source text -
        //  the regex-over-the-file approach this suite's banner retired (:16-23) would go
        //  quiet on a rename while still reporting OK.
        //
        //  ⚠ WHAT THIS CASE DOES NOT CLAIM. Do not read the [seat] cull law into it. The
        //  cost-row cells are NOT FitSingleLine labels: AddCostText (CostFormat.cs:133-155)
        //  leaves TMP's DEFAULT Overflow mode live and the HorizontalLayoutGroup clamps each
        //  cell to preferredHeight = max(24, fontPx + 4), so this row was never going to
        //  render BLANK - it was going to render SMALL, and then, once raised, to bleed past
        //  its own plate. Half 2 below is bleed containment, stated as such. An oracle that
        //  overclaims its own subject is how a suite starts being believed about things it
        //  never measured.
        private static void CaseSpoilsFloor(List<string> failures, StringBuilder log)
        {
            const string tag = "[spoils-floor]";

            // -- HALF 1: the DRAWN size is at or above the kit's mobile-legibility floor.
            float drawPx = RaidDeployScreen.SpoilsChipFontPx;
            if (drawPx + 0.001f < ElarionUiKit.FontFloor)
            {
                failures.Add(tag + " the spoils chips draw at " + drawPx.ToString("0.#") +
                             " px, under ElarionUiKit.FontFloor = " + ElarionUiKit.FontFloor.ToString("0") +
                             " (ElarionUiKitObsidian.cs). That is the WO-1669 defect by name: the row " +
                             "the owner had to lean in to read, while every neighbour on the screen sat " +
                             "at 32-40. RaidDeployScreen.SpoilsChipFontPx must stay pointed at the floor " +
                             "- never a literal typed back into BuildSpoilsChips.");
                return;
            }

            // -- HALF 2: the row's SHARE of its band can hold a line at that size.
            // The band is taken from the LIVE table, and the body from the screen's own
            // recorded floor (MinBodyFracOfPanel) - the tightest surface, so passing here
            // passes everywhere. Both are the same inputs case [bands] measures with.
            float bandFrac = 0f;
            foreach (var b in RaidDeployScreen.BandsFor(true))
                if (b.Name == "spoils") bandFrac = b.Y1 - b.Y0;
            if (bandFrac <= 0f)
            {
                failures.Add(tag + " RaidDeployScreen.BandsFor no longer contains a band named 'spoils' " +
                             "- this case cannot measure the row's share of a band that is not in the " +
                             "table, and a vacuous pass here is worse than no case. Fix the fixture.");
                return;
            }

            GameObject root = null;
            try
            {
                var s = Surfaces[0]; // the owner's own Seeker frame, and the tightest body
                float refW, refH;
                ReferenceBox(s, out refW, out refH);
                root = NewCanvas("rdl-spoils-floor", refW, refH);
                var panel = Region((RectTransform)root.transform, "Panel",
                                   RaidDeployScreen.PanelAnchorMin, RaidDeployScreen.PanelAnchorMax);
                var body = Region(panel, "Body", new Vector2(0.055f, 0f),
                                  new Vector2(0.945f, RaidDeployScreen.MinBodyFracOfPanel));
                Settle((RectTransform)root.transform);

                float bodyPx = body.rect.height;
                float bandPx = bandFrac * bodyPx;
                float rowFrac = RaidDeployScreen.SpoilsRowAnchorMax.y - RaidDeployScreen.SpoilsRowAnchorMin.y;
                float rowPx = rowFrac * bandPx;
                float needPx = RaidSelectionScreen.NeedPx(Mathf.RoundToInt(drawPx));

                if (rowPx + 0.05f < needPx)
                {
                    failures.Add(tag + " the spoils CostRow gets " + rowPx.ToString("0.#") +
                                 " ref px (" + rowFrac.ToString("0.##") + " of a " + bandPx.ToString("0.#") +
                                 " px band on the " + bodyPx.ToString("0") + " px body floor) but a line at " +
                                 drawPx.ToString("0") + " px needs " + needPx.ToString("0.#") +
                                 ". The row would overflow its own plate and print into the scout/enemy " +
                                 "gaps. Widen RaidDeployScreen.SpoilsRowAnchorMin/Max toward the whole " +
                                 "band - do NOT re-seat the band itself, which already clears the floor " +
                                 "and whose neighbours are 4.9 ref px away.");
                    return;
                }

                log.AppendLine(tag + " chips draw at " + drawPx.ToString("0") + " px (= FontFloor " +
                               ElarionUiKit.FontFloor.ToString("0") + "), row " + rowPx.ToString("0.#") +
                               " of " + bandPx.ToString("0.#") + " band ref px vs NeedPx(" +
                               drawPx.ToString("0") + ") = " + needPx.ToString("0.#") + " on the " +
                               s.Name + " body floor.");
            }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
        }

        // =====================================================================
        //  CASE [vm-spoils-chips] - ONE producer, three chips, no parsed string.
        // =====================================================================
        // The chips must be the SAME estimate the spoils sentence is, put through the SAME
        // rounding. A second estimator (a different star rung, a different rounding) would
        // quote one camp two ways on one screen - the drift RaidDeployZeroArmyRegression
        // already forbids in the sentence's direction, pinned here in the chips'.
        private static void CaseSpoilsChips(string vmSrc, List<string> failures, StringBuilder log)
        {
            const string tag = "[vm-spoils-chips]";
            int before = failures.Count;

            // The INVERTED source pin, same shape the sibling suite uses: the low-level
            // scorer must not be reached from here.
            if (vmSrc.IndexOf("RaidScoring.ComputeLoot(", StringComparison.Ordinal) >= 0 ||
                vmSrc.IndexOf("RaidScoring.ProjectLoot(", StringComparison.Ordinal) >= 0)
                failures.Add(tag + " RaidDeployVM reaches RaidScoring's low-level loot maths directly - the " +
                             "deploy screen DELEGATES to WO-1402's estimator, it does not compute.");
            if (vmSrc.IndexOf("RaidSelectionVM.Approx(", StringComparison.Ordinal) < 0)
                failures.Add(tag + " the chips do not use RaidSelectionVM.Approx - a second rounding would " +
                             "print a chip that disagrees with the sentence built from the same estimate.");

            foreach (var camp in AllCamps())
            {
                var vm = new RaidDeployVM(camp, null, null, null, null);
                try
                {
                    var chips = vm.SpoilsChips;
                    if (chips == null) { failures.Add(tag + " SpoilsChips is null for '" + camp.id + "'."); continue; }

                    var est = RaidSelectionVM.EstimateSpoils(camp);
                    var want = new List<KeyValuePair<string, int>>();
                    if (est.Wood  > 0) want.Add(new KeyValuePair<string, int>("wood", RaidSelectionVM.Approx(est.Wood)));
                    if (est.Iron  > 0) want.Add(new KeyValuePair<string, int>("iron", RaidSelectionVM.Approx(est.Iron)));
                    if (est.Coins > 0) want.Add(new KeyValuePair<string, int>("gold", RaidSelectionVM.Approx(est.Coins)));

                    if (chips.Count != want.Count)
                    {
                        failures.Add(tag + " '" + camp.id + "' has " + chips.Count + " chips but the estimate " +
                                     "pays " + want.Count + " currencies - a dropped or invented chip.");
                        continue;
                    }
                    for (int i = 0; i < want.Count; i++)
                    {
                        if (chips[i].ConceptId != want[i].Key)
                            failures.Add(tag + " '" + camp.id + "' chip " + i + " is '" + chips[i].ConceptId +
                                         "', expected '" + want[i].Key + "' (wood, iron, gold - the economy " +
                                         "map's order, the same order the sentence lists them in).");
                        if (chips[i].Amount != want[i].Value)
                            failures.Add(tag + " '" + camp.id + "' chip '" + chips[i].ConceptId + "' shows " +
                                         chips[i].Amount + " but the ONE estimate rounds to " + want[i].Value +
                                         " - the chip and the sentence quote the same camp differently.");
                        if (string.IsNullOrEmpty(chips[i].Word))
                            failures.Add(tag + " '" + camp.id + "' chip '" + chips[i].ConceptId + "' has no " +
                                         "WORD - it is the fallback when no icon art answers, and without it " +
                                         "a missing sprite leaves a bare number.");
                    }
                }
                finally { vm.Dispose(); }
            }

            if (failures.Count == before)
                log.AppendLine(tag + " chips equal the one estimate at every authored camp.");
        }

        // =====================================================================
        //  CASE [vm-scout-intel] - a PROJECTION of one report, not a second report.
        // =====================================================================
        // The well is budgeted for three lines now; ScoutIntel is ScoutReport minus its
        // spoils tail. ScoutReport itself is UNCHANGED and still pinned by
        // RaidDeployZeroArmyRegression [zero-army-spoils] - that pin was not weakened to
        // make this layout change easier, and this case proves the two stayed in step.
        private static void CaseScoutIntel(List<string> failures, StringBuilder log)
        {
            const string tag = "[vm-scout-intel]";
            int before = failures.Count;

            var vm = new RaidDeployVM(Camp(), null, null, null, null);
            try
            {
                var report = vm.ScoutReport;
                var intel  = vm.ScoutIntel;
                if (report == null || intel == null)
                {
                    failures.Add(tag + " ScoutReport/ScoutIntel is null.");
                    return;
                }
                string spoils = RaidDeployVM.SpoilsLine(Camp());
                if (!string.IsNullOrEmpty(spoils))
                {
                    if (intel.Count != report.Count - 1)
                        failures.Add(tag + " ScoutIntel has " + intel.Count + " lines and ScoutReport " +
                                     report.Count + " - the projection must drop exactly the spoils tail " +
                                     "(the chips carry it now), no more and no less.");
                    foreach (var line in intel)
                        if (line == spoils)
                            failures.Add(tag + " ScoutIntel still contains the spoils line '" + line +
                                         "' - it would be printed in the well AND on the chips, which is " +
                                         "the duplicated-state smell, not information.");
                    if (report[report.Count - 1] != spoils)
                        failures.Add(tag + " ScoutReport's LAST line is no longer the spoils estimate - " +
                                     "RaidDeployZeroArmyRegression [zero-army-spoils] pins that shape and it " +
                                     "must not be weakened for a layout change.");
                }
                if (intel.Count == 0)
                    failures.Add(tag + " ScoutIntel is EMPTY for a fully-authored camp - the well would " +
                                 "render as a bare plate.");
                // The well seats THREE lines. More than that wraps into a fourth it has no
                // room for, and TMP's Truncate would eat the tail silently.
                if (intel.Count > 3)
                    failures.Add(tag + " ScoutIntel is " + intel.Count + " lines; the WO-1519 well is " +
                                 "budgeted for THREE. A fourth line truncates with no exception and no " +
                                 "trace - re-budget the band before adding intel.");
            }
            finally { vm.Dispose(); }

            if (failures.Count == before)
                log.AppendLine(tag + " ScoutIntel is ScoutReport minus its spoils tail, and fits the well.");
        }

        // -- fixtures ---------------------------------------------------------

        private static Func<string, RaidDeployVM.TroopInfo> OneSlot =>
            id => new RaidDeployVM.TroopInfo("Footman", 10f, false, 1);

        private static SceneConfigDef Camp()
        {
            return new SceneConfigDef
            {
                id = "regression_raid_deploy_layout",
                displayName = "Regression Raid",
                sceneName = "RaidBase_regression",
                wallTier = "ReinforcedSteel",
                entranceCount = 2,
                garrison = new GarrisonDef
                {
                    composition = new List<GarrisonUnitDef>
                    {
                        new GarrisonUnitDef { enemyId = "orc-berserker", count = 4 },
                        new GarrisonUnitDef { enemyId = "shaman", count = 2 },
                    },
                    boss = "orc-necromancer",
                },
                rewardMultiplier = 2.2f,
            };
        }

        /// <summary>The four shipped camps at their authored multipliers (scene-configs.json,
        /// read 2026-09-06) plus the fixture. Only id + multiplier reach the estimate.</summary>
        private static IEnumerable<SceneConfigDef> AllCamps()
        {
            yield return Camp();
            yield return new SceneConfigDef { id = "raider_camp_small",  rewardMultiplier = 1.0f };
            yield return new SceneConfigDef { id = "fortified_garrison", rewardMultiplier = 1.5f };
            yield return new SceneConfigDef { id = "mage_enclave",       rewardMultiplier = 2.2f };
            yield return new SceneConfigDef { id = "iron_bastion",       rewardMultiplier = 2.2f };
        }

        private static ArmyStorage ArmyOf(int n)
        {
            var a = new ArmyStorage { Owned = new List<PlayerTroop>() };
            for (int i = 1; i <= n; i++)
                a.Owned.Add(new PlayerTroop { Id = "troop-" + i, TroopDefId = "troop-footman" });
            return a;
        }

        // -- geometry helpers -------------------------------------------------
        // These mirror RaidSelectionLayoutRegression's (and ArmyMusterLayoutRegression's, and
        // NightMarketRuntimeLayoutRegression's) private copies. THREE copies of NewCanvas /
        // Region / WorldRect / Settle now exist in this folder and this is the fourth; a
        // shared UI-fixture helper beside RegressionOutcome is the right home for them, and
        // that refactor is deliberately NOT smuggled into a player-facing lane
        // (ARCHITECTURE_PRINCIPLES: never bury a structural change inside feature work).
        // Reported as a finding instead.

        /// <summary>The reference box a surface resolves to under the kit's CanvasScaler
        /// (1080x1920, MatchWidthOrHeight 0.5) - derived, never tabulated.</summary>
        private static void ReferenceBox(Surface s, out float refW, out float refH)
        {
            float scale = Mathf.Pow(2f, Mathf.Lerp(
                Mathf.Log(s.W / 1080f, 2f), Mathf.Log(s.H / 1920f, 2f), 0.5f));
            refW = s.W / scale;
            refH = s.H / scale;
        }

        private static GameObject NewCanvas(string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.position = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return go;
        }

        private static RectTransform Region(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Settle(RectTransform root)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect WorldRect(RectTransform rt)
        {
            if (rt == null) return new Rect();
            rt.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x);
            float x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y);
            float y1 = Mathf.Max(_corners[0].y, _corners[2].y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static bool Overlaps(Rect a, Rect b)
        {
            if (a.width <= Eps || a.height <= Eps || b.width <= Eps || b.height <= Eps) return false;
            return a.xMin + Eps < b.xMax && b.xMin + Eps < a.xMax &&
                   a.yMin + Eps < b.yMax && b.yMin + Eps < a.yMax;
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Eps && inner.xMax <= outer.xMax + Eps &&
                   inner.yMin >= outer.yMin - Eps && inner.yMax <= outer.yMax + Eps;
        }

        private static string Fmt(Rect r)
        {
            return "[x " + r.xMin.ToString("0") + ".." + r.xMax.ToString("0") +
                   " y " + r.yMin.ToString("0") + ".." + r.yMax.ToString("0") + "]";
        }

        // Comment stripper - string-literal CONTENTS are preserved because this suite pins
        // one ("ECHO GUIDE"). Same reader shape the sibling raid suite uses.
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inStr = false, inChar = false, verbatim = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';
                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } continue; }
                if (inStr)
                {
                    sb.Append(c);
                    if (verbatim)
                    {
                        if (c == '"' && n == '"') { sb.Append(n); i++; }
                        else if (c == '"') { inStr = false; verbatim = false; }
                    }
                    else
                    {
                        if (c == '\\' && n != '\0') { sb.Append(n); i++; }
                        else if (c == '"') inStr = false;
                    }
                    continue;
                }
                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && n != '\0') { sb.Append(n); i++; }
                    else if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '/' && n == '/') { inLine = true; continue; }
                if (c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '@' && n == '"') { sb.Append(c); sb.Append(n); i++; inStr = true; verbatim = true; continue; }
                if (c == '"') { sb.Append(c); inStr = true; continue; }
                if (c == '\'') { sb.Append(c); inChar = true; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

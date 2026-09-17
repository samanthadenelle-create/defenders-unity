// =============================================================================
// RaidDeployUiRegression — WO-839 contract pins (Raid Deploy screen cleanup).
// -----------------------------------------------------------------------------
// Source-lint + pure-VM oracle (DataRegression family: headless, never throws).
// Pins the three contracts WO-839 established:
//   1. KIT ZONE CONTRACT — ElarionUiKit.ZonesFor's FrameCore case declares an
//      EXPLICIT footer band (z.footer) and a sub-header band (z.subHeader).
//      ROOT CAUSE pinned: FrameCore previously INHERITED the thin default footer;
//      the sweep-9413 relocation kept its ~0.065 height, too thin for the
//      MinTouchPx=112 floor, so ClampMinTouch grew footer CTAs past the band into
//      the shared Close (the owner's Raid Deploy bottom-row overlap).
//   2. DEV-GUARD CONTRACT — BreakCaptureHarness's IMGUI note-entry box ("What
//      looks wrong?") and its freeze entry (FlagHere note flow) sit INSIDE
//      #if UNITY_EDITOR || DEVELOPMENT_BUILD, so the capture field can never
//      render (nor timeScale-0 softlock) on a non-development player build.
//   3. VM CONTRACT — RaidDeployVM.ScoutReport is never null/empty, reports honest
//      config facts (walls/gates/garrison/boss), and NEVER surfaces
//      rewardMultiplier / shardDropChance (cosmetic-only fields the loot math
//      ignores — RAID_BATTLEFIELD_ANATOMY_2026-08-02: showing them would lie).
//   4. BAND CONTRACT [deploy-bands-disjoint] - every band in
//      RaidDeployScreen.BandsFor() stacks strictly, with a gap, never intersecting
//      a neighbour IN ITS OWN COLUMN, on BOTH chrome paths; and no Echo Guide band
//      is in the table (owner ruling 2026-09-06 20:24, WO-1519 section 2B).
//      (!) REWRITTEN 2026-09-06. WO-1385 authored this case as a REGEX over the
//      source text ("private const float GuideBandY0 = <literal>f;"). That reads a
//      constant NAME, so a rename blinds it silently while it still prints OK -
//      and WO-1519 then DELETED two of the six constants it named. It now reads
//      the LIVE table the builders lay out from, which cannot go stale that way.
//      The PIXEL law (a band tall enough that TMP does not cull its line) is
//      measured on a live canvas by RaidDeployLayoutRegression, not here.
//   5. WO-1385 (2026-09-04) DEPLOY-BAR CONTRACT [deploy-bar-kit-button] - BEGIN
//      ASSAULT is the kit's primary button (BuildObsidianButton, Yellow) and the
//      raw flat "DeployGlow" AddImage slab is gone. Owner, verbatim: "yuck".
//
// REGISTRATION: not yet wired into DataRegression.RunAll (that file is the
// sole-committer's lane). Wire there as:
//   if (!RaidDeployUiRegression.Run(out var raidDeployUiReason))
//       failures.Add(raidDeployUiReason);
//   else log.AppendLine("[raid-deploy-ui] " + raidDeployUiReason);
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor
{
    public static class RaidDeployUiRegression
    {
        /// <summary>Runs all WO-839 contract pins. True when green; reason always says why.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CheckFrameCoreZones(failures, notes);
                CheckCaptureFieldDevGuard(failures, notes);
                CheckScoutReportContract(failures, notes);
                CheckDeployBandsDisjoint(failures, notes);
                CheckDeployBarKitButton(failures, notes);
                CheckInWorldTroopControls(failures, notes);
                CheckDeployToastAboveModal(failures, notes);
                CheckRaidUiGuardBand(failures, notes);
                CheckRaidUiGuardIsComponentLevel(failures, notes);
                CheckBreachDiagnosticIsTruthful(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("RAID-DEPLOY-UI oracle threw: " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "RAID-DEPLOY-UI OK — " + string.Join("; ", notes.ToArray());
                return true;
            }
            reason = "RAID-DEPLOY-UI VIOLATION x" + failures.Count + " — " + string.Join(" | ", failures.ToArray());
            return false;
        }

        // ── 1. KIT ZONE CONTRACT (source-lint of ElarionUiKit.ZonesFor) ─────────
        static void CheckFrameCoreZones(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, "_Modules/Core/UI/ElarionUiKit.cs");
            if (!File.Exists(path))
            {
                failures.Add("ElarionUiKit.cs not found at " + path);
                return;
            }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add("ElarionUiKit.cs unreadable (" + ex.Message + ")"); return; }

            int start = text.IndexOf("case RpgUiCatalog.FrameCore:", StringComparison.Ordinal);
            if (start < 0)
            {
                failures.Add("ElarionUiKit.ZonesFor has no FrameCore case (frame renamed? update this pin)");
                return;
            }
            int end = text.IndexOf("break;", start, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(start + 4000, text.Length);
            string block = text.Substring(start, end - start);

            if (block.IndexOf("z.footer", StringComparison.Ordinal) < 0)
                failures.Add("FrameCore case no longer sets an EXPLICIT z.footer — WO-839 root cause regressed " +
                             "(inherited thin default footer + ClampMinTouch spills footer CTAs over the shared Close)");
            if (block.IndexOf("z.subHeader", StringComparison.Ordinal) < 0)
                failures.Add("FrameCore case no longer sets z.subHeader — WO-839 #1 regressed " +
                             "(badge/stars/target meta row stacks back into the body top)");

            if (failures.Count == before)
                notes.Add("FrameCore declares explicit footer + subHeader zones");
        }

        // ── 2. DEV-GUARD CONTRACT (source-lint of BreakCaptureHarness) ──────────
        const string DevGuard = "#if UNITY_EDITOR || DEVELOPMENT_BUILD";

        static void CheckCaptureFieldDevGuard(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, "_Modules/Core/Diagnostics/BreakCaptureHarness.cs");
            if (!File.Exists(path))
            {
                failures.Add("BreakCaptureHarness.cs not found at " + path);
                return;
            }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add("BreakCaptureHarness.cs unreadable (" + ex.Message + ")"); return; }

            // The IMGUI note box must sit inside the dev guard within OnGUI.
            // Search the phrase FROM OnGUI onward — the harness quotes "What looks
            // wrong?" in explanatory comments near the file top, and a plain first-
            // IndexOf matched those, flagging a guarded box as unguarded (the false
            // positive this suite fired on 2026-08-02).
            int onGui = text.IndexOf("void OnGUI()", StringComparison.Ordinal);
            int noteBox = onGui < 0 ? -1 : text.IndexOf("What looks wrong?", onGui, StringComparison.Ordinal);
            if (onGui < 0)
            {
                failures.Add("BreakCaptureHarness has no OnGUI() (harness rewritten? update this pin)");
            }
            else if (noteBox < 0)
            {
                // Note flow removed entirely = also safe; record it.
                notes.Add("note-entry box absent from harness (removed — trivially guarded)");
            }
            else
            {
                int guard = text.IndexOf(DevGuard, onGui, StringComparison.Ordinal);
                if (guard < 0 || guard > noteBox)
                    failures.Add("the 'What looks wrong?' note-entry box is OUTSIDE " + DevGuard +
                                 " in OnGUI — the dev capture field would render on a RELEASE player build (WO-839 §3)");
            }

            // The freeze ENTRY must be guarded too, or a release F8 would softlock at
            // timeScale 0 with the note box compiled out.
            int flagHere = text.IndexOf("void FlagHere()", StringComparison.Ordinal);
            int noteModeOn = flagHere < 0 ? -1 : text.IndexOf("_noteMode = true", flagHere, StringComparison.Ordinal);
            if (flagHere >= 0 && noteModeOn >= 0)
            {
                int guard = text.IndexOf(DevGuard, flagHere, StringComparison.Ordinal);
                if (guard < 0 || guard > noteModeOn)
                    failures.Add("FlagHere's note-mode/freeze entry is not dev-guarded — a release F8 would " +
                                 "freeze at timeScale 0 with no note box to commit out (WO-839 §3)");
            }

            if (failures.Count == before && noteBox >= 0)
                notes.Add("capture note field + freeze entry are dev-guarded");
        }

        // ── 3. VM CONTRACT (pure C# — RaidDeployVM.ScoutReport) ─────────────────
        static void CheckScoutReportContract(List<string> failures, List<string> notes)
        {
            int before = failures.Count;

            var def = new SceneConfigDef
            {
                id = "regression_raid",
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
                    boss = "necromancer",
                },
                rewardMultiplier = 2.2f,
                shardDropChance = 0.2f,
            };

            var vm = new RaidDeployVM(def, null, null, null, null);
            try
            {
                var report = vm.ScoutReport;
                if (report == null || report.Count == 0)
                {
                    failures.Add("ScoutReport is null/empty for a fully-specified def");
                }
                else
                {
                    bool walls = false, garrison = false, boss = false;
                    foreach (var line in report)
                    {
                        if (line == null) { failures.Add("ScoutReport contains a null line"); continue; }
                        if (line.Contains("Reinforced Steel") && line.Contains("2 gates")) walls = true;
                        // WO-1389 (2026-09-05): the garrison line is now the COMPARE form
                        // "Garrison: 6 defenders - you field N" (owner: the scout report must
                        // compare the camp to YOUR army). Pin the prefix + the compare tail;
                        // the exact equality it replaced would red on the feature working.
                        if (line.StartsWith("Garrison: 6 defenders") && line.Contains(" - you field ")) garrison = true;
                        if (line == "Boss: Necromancer") boss = true;
                        string lower = line.ToLowerInvariant();
                        // The anatomy-doc lie guard: these config fields are COSMETIC (the
                        // loot math never applies them) — surfacing them on screen is a lie.
                        if (lower.Contains("reward") || lower.Contains("shard"))
                            failures.Add("ScoutReport surfaces a cosmetic-only reward field (lie on screen): '" + line + "'");
                    }
                    if (!walls) failures.Add("ScoutReport missing the walls line ('Reinforced Steel', '2 gates'); got: " + Join(report));
                    if (!garrison) failures.Add("ScoutReport missing 'Garrison: 6 defenders - you field N' (WO-1389 compare form); got: " + Join(report));
                    if (!boss) failures.Add("ScoutReport missing 'Boss: Necromancer'; got: " + Join(report));
                }
            }
            finally { vm.Dispose(); }

            var emptyVm = new RaidDeployVM(null, null, null, null, null);
            try
            {
                var report = emptyVm.ScoutReport;
                if (report == null || report.Count != 1 || report[0] != "No scout intel available.")
                    failures.Add("null-def ScoutReport must be exactly ['No scout intel available.']; got: " +
                                 (report == null ? "<null>" : Join(report)));
                if (emptyVm.CanDeploy)
                    failures.Add("null-def VM reports CanDeploy=true (deploy contract regressed)");
            }
            finally { emptyVm.Dispose(); }

            if (failures.Count == before)
                notes.Add("ScoutReport contract holds (walls/garrison/boss honest, no cosmetic reward fields, safe null-def fallback)");
        }

        // -- 4. BAND CONTRACT [deploy-bands-disjoint] ---------------------------
        // WO-1385 (2026-09-04) wrote this case as a REGEX over RaidDeployScreen.cs, reading
        // "private const float GuideBandY0 = <literal>f;" out of the source text. WO-1519
        // (2026-09-06) rewrites it against the LIVE band table, and the reason is the same
        // duplicated-state lesson CLAUDE.md sec.2 / sec.5 / sec.16 each tell in their own
        // words: a regex on a constant NAME stops judging anything the moment the constant is
        // renamed, SILENTLY, while this suite goes on printing OK. That is not hypothetical -
        // WO-1519 deleted GuideBandY0/Y1 outright (the owner's 20:24 ruling took the ECHO
        // GUIDE block off this screen), and the old case would have failed with "could not
        // read the literal" rather than telling anyone whether the layout was sound.
        //
        // RaidDeployScreen.BandsFor(bool) is now the authority - the same table the builders
        // lay out from - so this case judges what actually ships and moves with a rename.
        // It asserts the geometry RULE (in-range, ordered, gapped, one stack per column) on
        // BOTH chrome paths. The PIXEL law (a band tall enough that TMP does not cull its
        // line) is measured on a live canvas by RaidDeployLayoutRegression; this stays the
        // cheap source-level pin that needs no canvas.
        const string DeployScreenRel = "_Modules/Village/Hero/RaidDeployScreen.cs";

        static void CheckDeployBandsDisjoint(List<string> failures, List<string> notes)
        {
            const string Tag = "[deploy-bands-disjoint]";
            int before = failures.Count;

            foreach (var hasSubHeader in new[] { true, false })
            {
                string path = hasSubHeader ? "frame" : "procedural";
                var bands = RaidDeployScreen.BandsFor(hasSubHeader);
                if (bands == null || bands.Length == 0)
                {
                    failures.Add(Tag + " RaidDeployScreen.BandsFor(" + hasSubHeader + ") returned no bands - " +
                                 "the deploy body has no authored layout at all on the " + path + " chrome");
                    return;
                }

                for (int i = 0; i < bands.Length; i++)
                {
                    var b = bands[i];
                    if (b.Y0 < 0f)
                        failures.Add(Tag + " (" + path + ") band '" + b.Name + "' starts at " + b.Y0 +
                                     " - below the body bottom");
                    if (b.Y1 > 1f)
                        failures.Add(Tag + " (" + path + ") band '" + b.Name + "' tops out at " + b.Y1 +
                                     " - above the body top");
                    if (b.Y1 <= b.Y0)
                        failures.Add(Tag + " (" + path + ") band '" + b.Name + "' is inverted or zero-height (" +
                                     b.Y0 + ".." + b.Y1 + ")");

                    for (int j = i + 1; j < bands.Length; j++)
                    {
                        var c = bands[j];
                        // The two columns are INDEPENDENT stacks (WO-1519): a left band and a
                        // right band at the same height is the layout, not a collision.
                        if (c.Column != b.Column) continue;
                        bool intersects = b.Y0 < c.Y1 - 0.0001f && c.Y0 < b.Y1 - 0.0001f;
                        if (intersects)
                            failures.Add(Tag + " (" + path + ") '" + b.Name + "' (" + b.Y0 + ".." + b.Y1 +
                                         ") and '" + c.Name + "' (" + c.Y0 + ".." + c.Y1 + ") INTERSECT in the " +
                                         b.Column + " column - two elements own the same pixels. (WO-1385: the " +
                                         "owner's screenshot had three right-column elements in one band. " +
                                         "WO-1464: the army line printed across the hero portraits.)");
                        else
                        {
                            // Neighbours must be SEPARATED, not merely non-overlapping: a shared
                            // edge puts one row's descenders in the next row's ascenders.
                            float gap = c.Y0 >= b.Y1 ? c.Y0 - b.Y1 : b.Y0 - c.Y1;
                            bool adjacent = Mathf.Abs(gap) < 0.030f;
                            if (adjacent && gap < 0.004f)
                                failures.Add(Tag + " (" + path + ") '" + b.Name + "' and '" + c.Name +
                                             "' are adjacent with a gap of " + gap.ToString("0.####") +
                                             " - neighbouring bands need a real gap, not a shared edge");
                        }
                    }
                }
            }

            // The ruling this lane landed, pinned where the old GUIDE band constant used to be
            // read: there is no guide band in the table any more, and re-adding one would have
            // to be a deliberate edit to BandsFor rather than a quiet const.
            foreach (var b in RaidDeployScreen.BandsFor(true))
                if (b.Name.IndexOf("guide", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add(Tag + " an Echo Guide band is back in RaidDeployScreen.BandsFor ('" + b.Name +
                                 "') - owner ruling 2026-09-06 20:24 (WO-1519 section 2B) took it off this screen");

            if (failures.Count == before)
            {
                var names = new List<string>();
                foreach (var b in RaidDeployScreen.BandsFor(true)) names.Add(b.Name);
                notes.Add("body bands disjoint per column on both chrome paths (" + string.Join("/", names.ToArray()) + ")");
            }
        }

        // -- 5. WO-1385 DEPLOY-BAR CONTRACT [deploy-bar-kit-button] (source-lint) -
        // Owner 2026-09-04 (Seeker build 355905, on the BEGIN ASSAULT row): "yuck". The
        // button sat on a flat yellow AddImage slab ("DeployGlow", the WO-839 halo) beside
        // ARMY READY?'s framed kit button - two visual languages on one row. Contract:
        // BuildDeployBar builds BEGIN ASSAULT through BuildObsidianButton with the Yellow
        // primary face, wires OnDeploy, and contains NO AddImage / "DeployGlow" at all.
        // RED if the slab literal returns or the CTA goes back to ElarionUiKit.Button(Confirm).
        static void CheckDeployBarKitButton(List<string> failures, List<string> notes)
        {
            const string Tag = "[deploy-bar-kit-button]";
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, DeployScreenRel);
            if (!File.Exists(path)) { failures.Add(Tag + " RaidDeployScreen.cs not found at " + path); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(Tag + " RaidDeployScreen.cs unreadable (" + ex.Message + ")"); return; }

            int start = text.IndexOf("private void BuildDeployBar(", StringComparison.Ordinal);
            if (start < 0) { failures.Add(Tag + " RaidDeployScreen.BuildDeployBar not found - the CTA builder moved"); return; }
            // The next method after the bar is the seating helper; bound the body there.
            int end = text.IndexOf("private static void SeatFooterCtaAtCanonicalHeight(", start, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(start + 6000, text.Length);
            string bar = text.Substring(start, end - start);

            if (bar.IndexOf("\"DeployGlow\"", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " the \"DeployGlow\" slab literal is back in BuildDeployBar (owner: \"yuck\")");
            if (bar.IndexOf("AddImage(", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " BuildDeployBar paints an AddImage behind a CTA - the row must be kit buttons only");

            int obs = bar.IndexOf("BuildObsidianButton(", StringComparison.Ordinal);
            if (obs < 0)
                failures.Add(Tag + " BEGIN ASSAULT is no longer built by ElarionUiKit.BuildObsidianButton");
            else
            {
                int callEnd = bar.IndexOf("OnDeploy", obs, StringComparison.Ordinal);
                string call = callEnd > obs ? bar.Substring(obs, callEnd - obs) : bar.Substring(obs);
                if (call.IndexOf("ObsidianButtonColor.Yellow", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the BEGIN ASSAULT BuildObsidianButton call is not the Yellow primary face");
                if (call.IndexOf("\"BEGIN ASSAULT\"", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the BuildObsidianButton call in BuildDeployBar is not labelled \"BEGIN ASSAULT\"");
                if (callEnd < 0)
                    failures.Add(Tag + " the BEGIN ASSAULT button no longer wires OnDeploy");
            }
            // Both CTAs on the row share the seating call (same row geometry).
            int seats = 0, at = 0;
            while ((at = bar.IndexOf("SeatFooterCtaAtCanonicalHeight(", at, StringComparison.Ordinal)) >= 0) { seats++; at++; }
            if (seats < 2)
                failures.Add(Tag + " BuildDeployBar seats " + seats + " CTA(s) at the canonical height; ARMY READY? and " +
                             "BEGIN ASSAULT must share the row geometry (2 expected)");

            if (failures.Count == before)
                notes.Add("BEGIN ASSAULT = BuildObsidianButton Yellow, no DeployGlow slab, both CTAs seated on one row");
        }

        // -- 6. IN-WORLD TROOP CONTROLS -----------------------------------------
        // The compact raid control must identify troops by the same round portrait
        // language used elsewhere, and provide one deliberate all-army action.
        static void CheckInWorldTroopControls(List<string> failures, List<string> notes)
        {
            const string Tag = "[in-world-troop-controls]";
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath,
                "_Modules/Village/Troops/RaidDeployController.cs");
            if (!File.Exists(path)) { failures.Add(Tag + " RaidDeployController.cs not found at " + path); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(Tag + " RaidDeployController.cs unreadable (" + ex.Message + ")"); return; }

            if (text.IndexOf("Resources.Load<Sprite>(\"RpgUi/troop/\" + icon)", StringComparison.Ordinal) < 0 ||
                text.IndexOf("ElarionUiKit.Portrait(square.transform", StringComparison.Ordinal) < 0 ||
                text.IndexOf("AspectRatioFitter.AspectMode.FitInParent", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " troop-type buttons no longer use the canonical round troop portraits");
            if (text.IndexOf("private void DeployAll()", StringComparison.Ordinal) < 0 ||
                text.IndexOf("TroopDeployer.SpawnFromArmy", StringComparison.Ordinal) < 0 ||
                text.IndexOf("\"Deploy All\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the one-tap Deploy All route is incomplete");

            if (failures.Count == before)
                notes.Add("in-world troop controls use round portraits and a wired Deploy All action");
        }

        // =====================================================================
        //  7. WO-1640 ITEM B [deploy-toast-above-modal] -- A TOAST FIRED FROM THIS
        //     PANEL MUST DRAW ABOVE IT.
        // ---------------------------------------------------------------------
        //  The defect, from the device (Builds/device-frames/2026-09-10_raid_logcat_stream.txt
        //  :25546-25547 + 2026-09-10_0607_arena_01_entry.png): the WO-1542 outmatch confirm
        //  fired and logged, and the player saw nothing. RaidDeployScreen's canvas is a
        //  ScreenSpaceOverlay modal at 31050 with overrideSorting and a 0.94-alpha kit
        //  Backdrop; ElarionUiKit.ShowToast builds its own ScreenSpaceOverlay canvas at the
        //  DEFAULT sortingOrder 720. 720 under 31050, behind an opaque plate. The first
        //  BEGIN ASSAULT tap therefore looked dead.
        //
        //  WHY A SOURCE LINT AND NOT A BUILT PROBE: ShowToast early-outs on
        //  !Application.isPlaying (ElarionUiKitConformance.cs:398), so an edit-mode oracle
        //  can never build a toast to measure. The relation between the two numbers IS the
        //  contract, and both numbers live in one file -- so read them both, out of that
        //  file, and require the relation. It cannot go stale the way a copied literal can.
        //
        //  RED BY CONSTRUCTION: against the pre-WO-1640 file there is no ToastSortingOrder
        //  const at all and not one ShowToast in OnDeploy carries the argument, so every
        //  branch below fires. It also fails LOUDLY (never silently green) if the panel's
        //  BuildModalCanvas call, the const, or OnDeploy itself is renamed or removed.
        // =====================================================================
        static void CheckDeployToastAboveModal(List<string> failures, List<string> notes)
        {
            const string Tag = "[deploy-toast-above-modal]";
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, DeployScreenRel);
            if (!File.Exists(path)) { failures.Add(Tag + " RaidDeployScreen.cs not found at " + path); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(Tag + " RaidDeployScreen.cs unreadable (" + ex.Message + ")"); return; }

            // (a) The panel's own modal band, read off the live call. ⛔ The ASSIGNMENT form,
            //     not the bare call: the WO-1640 comment block a few lines above OnDeploy
            //     quotes the canvas name and the band, and a bare-call search would find the
            //     COMMENT if the real call were ever removed -- reading a number out of prose
            //     and passing. This suite exists because a copy went stale; it may not become
            //     one itself.
            const string CanvasCall = "_ui = ElarionUiKit.BuildModalCanvas(\"RaidDeployScreenUI\"";
            int mc = text.IndexOf(CanvasCall, StringComparison.Ordinal);
            if (mc < 0)
            {
                failures.Add(Tag + " no `_ui = ElarionUiKit.BuildModalCanvas(\"RaidDeployScreenUI\", ...)` assignment " +
                             "found -- this pin " +
                             "reads the panel's sorting band out of that call, so it cannot judge anything. " +
                             "Re-point it at whatever built the deploy canvas instead of deleting it.");
                return;
            }
            int mcEnd = text.IndexOf(')', mc);
            int mcComma = text.IndexOf(',', mc);
            int modalOrder;
            if (mcEnd < 0 || mcComma < 0 || mcComma > mcEnd ||
                !int.TryParse(text.Substring(mcComma + 1, mcEnd - mcComma - 1).Trim(), out modalOrder))
            {
                failures.Add(Tag + " the BuildModalCanvas(\"RaidDeployScreenUI\", ...) sorting argument is no " +
                             "longer a plain integer literal -- this pin can no longer read the panel's band, " +
                             "so update it in the same change that made the argument dynamic.");
                return;
            }

            // (b) The toast band this screen hands ShowToast.
            const string ConstDecl = "private const int ToastSortingOrder";
            int cd = text.IndexOf(ConstDecl, StringComparison.Ordinal);
            int cdEq = cd >= 0 ? text.IndexOf('=', cd) : -1;
            int cdEnd = cdEq >= 0 ? text.IndexOf(';', cdEq) : -1;
            int toastOrder;
            if (cd < 0 || cdEnd < 0 ||
                !int.TryParse(text.Substring(cdEq + 1, cdEnd - cdEq - 1).Trim(), out toastOrder))
            {
                failures.Add(Tag + " RaidDeployScreen has no readable \"" + ConstDecl + " = <n>;\" -- WO-1640 " +
                             "raised this screen's toasts above its own modal band, and without that constant " +
                             "the kit default (720) applies and every toast this panel fires is painted UNDER " +
                             "its 0.94-alpha backdrop, exactly as on the 2026-09-10 device frames.");
                return;
            }

            if (toastOrder <= modalOrder)
                failures.Add(Tag + " ToastSortingOrder is " + toastOrder + " but the panel's own canvas is " +
                             modalOrder + " -- the relation is INVERTED and the confirm sentence draws behind " +
                             "the panel that asked for it (WO-1640 item B). The toast band must exceed the " +
                             "modal band.");

            // (c) Every toast fired while this panel is up carries it. OpenTroopsDoor's toast
            //     is deliberately NOT in scope: it fires after Close(), with no panel to hide
            //     behind. OnDeploy's are the ones the player taps into.
            int start = text.IndexOf("private void OnDeploy()", StringComparison.Ordinal);
            if (start < 0)
            {
                failures.Add(Tag + " RaidDeployScreen.OnDeploy not found -- the BEGIN ASSAULT handler moved, " +
                             "and this pin was reading its toasts. Re-point it.");
                return;
            }
            int end = text.IndexOf("// ── Data helpers", start, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(start + 8000, text.Length);
            string body = text.Substring(start, end - start);

            // Non-vacuous: a body with no toasts at all would pass every check below.
            var spans = new List<int>();
            int at = 0;
            while ((at = body.IndexOf("ShowToast(", at, StringComparison.Ordinal)) >= 0) { spans.Add(at); at++; }
            if (spans.Count == 0)
            {
                failures.Add(Tag + " OnDeploy fires no ShowToast at all, so this check proves nothing -- and " +
                             "the WO-1542 outmatch confirm plus the WO-932 under-construction refusal both " +
                             "spoke through one. A refused or confirmed tap with no word is a dead tap.");
                return;
            }
            for (int i = 0; i < spans.Count; i++)
            {
                int s = spans[i];
                int e = i + 1 < spans.Count ? spans[i + 1] : body.Length;
                string call = body.Substring(s, e - s);
                if (call.IndexOf("sortingOrder: ToastSortingOrder", StringComparison.Ordinal) < 0)
                {
                    int line = 1;
                    for (int k = 0; k < start + s && k < text.Length; k++) if (text[k] == '\n') line++;
                    failures.Add(Tag + " the ShowToast at RaidDeployScreen.cs:" + line + " does not pass " +
                                 "sortingOrder: ToastSortingOrder, so it takes the kit default (720) and draws " +
                                 "UNDER this panel's " + modalOrder + " band and its 0.94-alpha backdrop. That " +
                                 "is the WO-1640 defect: the player taps, the log records a toast, and nothing " +
                                 "appears on screen.");
                }
            }

            // (d) §5.1 -- arrival is traced BEFORE any branch reports. Without it "tap not
            //     received" and "tap received and swallowed" are indistinguishable.
            int firstTrace = body.IndexOf("FlowTrace.", StringComparison.Ordinal);
            if (firstTrace < 0 || firstTrace > spans[0])
                failures.Add(Tag + " OnDeploy's first FlowTrace call does not precede its first ShowToast -- the " +
                             "BEGIN ASSAULT tap arrives untraced (WO-1640 §5.1), which is the silent-arrival " +
                             "hole CLAUDE.md §12 forbids.");

            if (failures.Count == before)
                notes.Add("deploy toasts ride sortingOrder " + toastOrder + " above the panel's " + modalOrder +
                          " modal band, and the BEGIN ASSAULT tap is traced on arrival");
        }

        // =====================================================================
        //  WO-1777 / WO-1790 — the raid world-tap UI guard, and a truthful breach diagnostic
        // ---------------------------------------------------------------------
        //  Three cases, all headless:
        //    6. [raid-ui-guard-band]      PURE ORACLE over the real decision function
        //       (RaidDeployController.IsInReservedThumbBand), driven with the ACTUAL screen
        //       points from the owner's Bastion capture. Not a source-lint: it calls the
        //       shipped code. A live EventSystem raycast is deliberately NOT built here —
        //       overlay-canvas raycasting depends on Screen.* and display state and is flaky
        //       in EditMode, which is why this file's own header settles on pure oracles.
        //    7. [raid-ui-guard-component] SOURCE-LINT that the guard is component-level and
        //       crosses canvases WITHOUT a DeNelle.HUD reference (CLAUDE.md §5).
        //    8. [breach-diag-truthful]    SOURCE-LINT that the breach diagnostic reports what
        //       it measured and no longer picks its "nearest" wall by camera distance nor
        //       prints an includes-inactive renderer union (WO-1790).
        // =====================================================================

        const string GuardTag = "[raid-ui-guard]";

        // ⭐ THE MEASURED EVIDENCE, kept as data so the case cannot drift from the capture it
        // was written against. Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt,
        // screen 2670x1200: 40 failed breach taps inside x 774-876 / y 78-130, and the ONE
        // success at (1333, 573) on 'Wall_Keep1_SS_0'.
        const float CaptureW = 2670f;
        const float CaptureH = 1200f;

        static void CheckRaidUiGuardBand(List<string> failures, List<string> notes)
        {
            int before = failures.Count;

            // The four corners of the measured 100x50 px failed-tap box. Every one must be
            // consumed by the guard, or WO-1777's forty taps deploy a troop again.
            var failedTapCorners = new[]
            {
                new Vector2(774f, 78f), new Vector2(876f, 78f),
                new Vector2(774f, 130f), new Vector2(876f, 130f),
                new Vector2(820f, 105f),   // the centre of the box — the face whose identity is UNPROVEN
            };
            for (int i = 0; i < failedTapCorners.Length; i++)
            {
                Vector2 p = failedTapCorners[i];
                float nx = p.x / CaptureW;
                float ny = p.y / CaptureH;
                if (!RaidDeployController.IsInReservedThumbBand(nx, ny))
                    failures.Add(GuardTag + " the measured failed-tap point (" + p.x + ", " + p.y + ") of " +
                                 CaptureW + "x" + CaptureH + " = norm (" + nx.ToString("0.000") + ", " +
                                 ny.ToString("0.000") + ") is NOT consumed by the reserved ability-row band. " +
                                 "That is one of the forty presses in the owner's Bastion capture that also " +
                                 "deployed a troop (WO-1777) — the band clause is what catches them WITHOUT " +
                                 "knowing which ability face sits there, which was never established.");
            }

            // The genuine mid-screen wall tap, and open ground, must still reach the world.
            var worldTaps = new[]
            {
                new Vector2(1333f, 573f),    // the ONE success in the capture: 'Wall_Keep1_SS_0'
                new Vector2(1335f, 600f),    // open ground, mid-screen
                new Vector2(1335f, 1080f),   // open ground, high on the screen
            };
            for (int i = 0; i < worldTaps.Length; i++)
            {
                Vector2 p = worldTaps[i];
                float nx = p.x / CaptureW;
                float ny = p.y / CaptureH;
                if (RaidDeployController.IsInReservedThumbBand(nx, ny))
                    failures.Add(GuardTag + " the world tap (" + p.x + ", " + p.y + ") = norm (" +
                                 nx.ToString("0.000") + ", " + ny.ToString("0.000") + ") is being consumed as " +
                                 "UI. The guard is OVER-rejecting: a mid-screen wall tap must still order a " +
                                 "breach and open ground must still deploy (WO-1777 acceptance 3).");
            }

            // The band is x-bounded, so the movement stick's corner is left exactly as it was —
            // the lead's instruction was to keep the joystick exclusion unchanged, and the stick
            // is raycast-transparent (VirtualJoystick.cs:219), so no clause here may claim it.
            if (RaidDeployController.IsInReservedThumbBand(0.05f, 0.08f))
                failures.Add(GuardTag + " the band now claims the bottom-LEFT corner (norm 0.05, 0.08), which is " +
                             "the movement stick's mount (HudLayoutBands.MoveClusterMount), not the ability row. " +
                             "The band must start at MoveClusterMount.xMax.");

            // Non-vacuous: a function that always returned false would pass every negative above.
            if (!RaidDeployController.IsInReservedThumbBand(0.5f, 0.08f))
                failures.Add(GuardTag + " IsInReservedThumbBand consumes NOTHING at norm (0.5, 0.08), dead centre " +
                             "of the reserved ability row. The band clause is inert and this case proves nothing.");

            if (failures.Count == before)
                notes.Add("world-tap guard consumes all 5 measured ability-row points and none of the 3 world " +
                          "taps; the stick corner is untouched");
        }

        static void CheckRaidUiGuardIsComponentLevel(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, "_Modules/Village/Troops/RaidDeployController.cs");
            if (!File.Exists(path)) { failures.Add(GuardTag + " RaidDeployController.cs not found at " + path); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(GuardTag + " RaidDeployController.cs unreadable (" + ex.Message + ")"); return; }

            // (a) The guard must reach FOREIGN canvases by COMPONENT identity.
            if (text.IndexOf("GetComponentInParent<Selectable>", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the guard no longer resolves a foreign canvas' Selectable. WO-1777's " +
                             "forty presses were on kit ability faces (Image + Button, built by " +
                             "ElarionUiKitObsidian.BuildActionSlot) on a canvas this assembly cannot name.");
            if (text.IndexOf("IEventSystemHandler", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the guard no longer tests IEventSystemHandler, so a foreign canvas' " +
                             "pointer handler that carries no Selectable falls through to a world tap.");
            if (text.IndexOf("GraphicRaycaster", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the guard has no GraphicRaycaster fallback, so with no EventSystem " +
                             "present (headless / a scene that never built one) it measures nothing at all.");

            // (b) The OWN-canvas condition must survive: any hit on this HUD consumes, including a
            //     backing plate between two tray tiles.
            if (text.IndexOf("IsChildOf(_ui.transform)", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the own-canvas condition (IsChildOf(_ui.transform)) is gone. It was not " +
                             "the WO-1777 bug — being the ONLY condition was. Dropping it lets a press on this " +
                             "HUD's own non-interactable backing plate deploy a troop behind it.");

            // (c) ⛔ CLAUDE.md §5: DeNelle.Village may not reference DeNelle.HUD, in code or asmdef.
            // ⚠ Match a REFERENCE, not the word: this file's comments legitimately NAME
            // DeNelle.HUD four times to record why the boundary exists. A using-directive or a
            // qualified type is the violation; prose about the rule is not.
            if (text.IndexOf("using DeNelle.HUD", StringComparison.Ordinal) >= 0
                || text.IndexOf("DeNelle.HUD.", StringComparison.Ordinal) >= 0
                || text.IndexOf("HudKitController", StringComparison.Ordinal) >= 0)
                failures.Add(GuardTag + " RaidDeployController now names DeNelle.HUD / HudKitController. That is " +
                             "the assembly boundary CLAUDE.md §5 forbids, and it is exactly why the guard has to " +
                             "be component-level in the first place (WO-1777 §3b).");

            // (d) The band comes from SHARED DATA, never a literal typed into this file.
            if (text.IndexOf("HudLayoutBands.ThumbActionRowBand", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the reserved band is no longer read from " +
                             "HudLayoutBands.ThumbActionRowBand. A matching literal on each side is the " +
                             "duplicated-state failure CLAUDE.md §2/§5/§16 each describe.");

            // (e) The rejection must be TRACED, and throttled — the Mage casts constantly, and the
            //     logcat ring evicting the boot window is a recorded memory.
            if (text.IndexOf("world-tap-rejected-ui", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " the guard rejects silently. CLAUDE.md §12 forbids a silent skip: the " +
                             "trace has to name WHICH element consumed the press, or the next reader is back to " +
                             "guessing which is what produced WO-1777's retracted collider theory.");
            int rejectAt = text.IndexOf("world-tap-rejected-ui", StringComparison.Ordinal);
            if (rejectAt >= 0)
            {
                int lineStart = text.LastIndexOf('\n', rejectAt) + 1;
                string callLine = text.Substring(lineStart, Math.Min(240, text.Length - lineStart));
                if (callLine.IndexOf("Throttle", StringComparison.Ordinal) < 0)
                    failures.Add(GuardTag + " the world-tap rejection trace is not Throttled. Every ability press " +
                                 "during a raid hits this path (the capture shows 54 Mage casts in ~100 s), and an " +
                                 "un-throttled line evicts the boot window out of the logcat ring.");
            }

            // (f) The deploy path must trace its arrival — WO-1777 acceptance 1 greps this prefix,
            //     and without the line the acceptance passes vacuously.
            if (text.IndexOf("HandleDeployTap IN", StringComparison.Ordinal) < 0)
                failures.Add(GuardTag + " HandleDeployTap emits no 'HandleDeployTap IN' arrival trace, so " +
                             "WO-1777 acceptance 1 (zero deploy taps with y < 200) cannot distinguish 'the guard " +
                             "works' from 'the line does not exist'.");

            if (failures.Count == before)
                notes.Add("world-tap guard is component-level (Selectable / IEventSystemHandler / " +
                          "GraphicRaycaster + shared band data), keeps the own-canvas condition, names no " +
                          "DeNelle.HUD type, and traces its rejection under a throttle");
        }

        const string DiagTag = "[breach-diag-truthful]";

        static void CheckBreachDiagnosticIsTruthful(List<string> failures, List<string> notes)
        {
            int before = failures.Count;
            string path = Path.Combine(Application.dataPath, "_Modules/Village/Troops/RaidDeployController.cs");
            if (!File.Exists(path)) { failures.Add(DiagTag + " RaidDeployController.cs not found at " + path); return; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(DiagTag + " RaidDeployController.cs unreadable (" + ex.Message + ")"); return; }

            int start = text.IndexOf("private void LogBreachTapDiagnostics(", StringComparison.Ordinal);
            if (start < 0)
            {
                failures.Add(DiagTag + " LogBreachTapDiagnostics is gone. Instrumentation is PERMANENT " +
                             "(CLAUDE.md §12, owner ruling 2026-08-09) — it may be flagged off, never stripped. " +
                             "WO-1790's fix was to make it truthful.");
                return;
            }
            int end = text.IndexOf("private static float DistanceFromRay(", start, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(start + 9000, text.Length);
            string block = text.Substring(start, end - start);

            // (a) WO-1790 §3.1 — the "nearest" wall must be nearest THE RAY, not the camera.
            if (block.IndexOf("- ray.origin).sqrMagnitude", StringComparison.Ordinal) >= 0)
                failures.Add(DiagTag + " the diagnostic still picks its nearest WallSegment by distance to " +
                             "ray.origin — the CAMERA. For a tap that never went near a wall that is an arbitrary " +
                             "wall, and its two IntersectsRay=False values are noise the message then read as a " +
                             "diagnosis (WO-1790 §2b). Pick by distance from the ray.");
            if (block.IndexOf("DistanceFromRay(ray", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " the diagnostic does not call DistanceFromRay, so nothing establishes that " +
                             "the reported wall is anywhere near the tap.");
            if (block.IndexOf("nearestWallDistToRay=", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " the trace does not print nearestWallDistToRay. Without it the reader " +
                             "cannot tell a relevant wall from an arbitrary one — which is the whole of WO-1790 §2b.");

            // (b) WO-1790 §3.2 — no includes-inactive renderer union under a name that means
            //     "what the player sees". WO-1723 §6 had already retired that reading once.
            if (block.IndexOf("GetComponentsInChildren<Renderer>(true)", StringComparison.Ordinal) >= 0)
                failures.Add(DiagTag + " the diagnostic still unions INCLUDES-INACTIVE renderers. On an " +
                             "IronBastion segment that swallows the inactive placeholder twin and the Ruin_* " +
                             "tiles, which is how a 2.00 m collider read as half of an 8.24 m wall and produced a " +
                             "P0 that had to be retracted (WO-1790 §2a; WO-1723 §6 retired it nine days earlier).");
            if (block.IndexOf("activeRendererBounds=", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " the renderer union is not reported as activeRendererBounds. The name has " +
                             "to say what was measured.");
            if (block.IndexOf("activeRenderersCounted=", StringComparison.Ordinal) < 0
                || block.IndexOf("renderersSkippedInactiveOrDisabled=", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " the trace does not report how many renderers it counted and skipped, so a " +
                             "reader cannot tell an empty union from a full one.");

            // (c) WO-1790 §3.4 — the screen point's normalised position and the band flag. This is
            //     the single field that would have named WO-1777's real cause on first read.
            if (block.IndexOf("screenNorm=", StringComparison.Ordinal) < 0
                || block.IndexOf("inReservedThumbBand=", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " the diagnostic does not report screenNorm and inReservedThumbBand. Those " +
                             "two fields are what turn 'the tap resolved nothing' into 'the finger was on the " +
                             "ability row' (WO-1790 §3.4).");

            // (d) WO-1790 §3.3 / §4 — the block states measurements, not causes.
            if (block.IndexOf("MEASURED ONLY", StringComparison.Ordinal) < 0)
                failures.Add(DiagTag + " no line in the block marks itself MEASURED ONLY. CLAUDE.md §11B: an " +
                             "instrument that states an unproven cause as fact converts 'we do not know' into " +
                             "'we know, wrongly', and the reader cannot tell which they are holding.");
            if (block.IndexOf("Splits the remaining causes", StringComparison.Ordinal) >= 0)
                failures.Add(DiagTag + " the retired 'Splits the remaining causes' inference is back. That comment " +
                             "is the one that told a reader two expected Falses meant the ray was wrong.");

            // (e) The sibling OUT line must carry the same measured fields and claim nothing.
            int outAt = text.IndexOf("outcome=not_wall_segment", StringComparison.Ordinal);
            if (outAt < 0)
                failures.Add(DiagTag + " the not_wall_segment OUT line is gone — WO-1777's evidence grep keys on " +
                             "'HandleBreachTap OUT: outcome=<token>' and on that token.");
            else
            {
                string outBlock = text.Substring(outAt, Math.Min(1400, text.Length - outAt));
                if (outBlock.IndexOf("screenNorm=", StringComparison.Ordinal) < 0
                    || outBlock.IndexOf("hitLayer=", StringComparison.Ordinal) < 0)
                    failures.Add(DiagTag + " the not_wall_segment OUT line does not report screenNorm and hitLayer. " +
                                 "It is the line a reader greps first, and in the owner's capture it already " +
                                 "carried the coordinates that named the real cause — unread (WO-1790 §1).");
                if (outBlock.IndexOf("invites", StringComparison.Ordinal) >= 0)
                    failures.Add(DiagTag + " the not_wall_segment OUT line still editorialises. It may state what " +
                                 "was measured and nothing more (WO-1790 §3.3).");
            }

            if (failures.Count == before)
                notes.Add("breach diagnostic picks its nearest wall by ray distance, reports " +
                          "activeRendererBounds with counts, prints screenNorm + inReservedThumbBand, and " +
                          "asserts no unmeasured cause");
        }

        static string Join(IReadOnlyList<string> lines)
        {
            var arr = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) arr[i] = lines[i] ?? "<null>";
            return "[" + string.Join(" / ", arr) + "]";
        }
    }
}

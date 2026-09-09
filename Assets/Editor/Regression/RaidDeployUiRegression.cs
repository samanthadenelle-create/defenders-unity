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
                text.IndexOf("ElarionUiKit.Portrait(portraitSeat.transform", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " troop-type buttons no longer use the canonical round troop portraits");
            if (text.IndexOf("private void DeployAll()", StringComparison.Ordinal) < 0 ||
                text.IndexOf("TroopDeployer.SpawnFromArmy", StringComparison.Ordinal) < 0 ||
                text.IndexOf("\"Deploy All\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the one-tap Deploy All route is incomplete");

            if (failures.Count == before)
                notes.Add("in-world troop controls use round portraits and a wired Deploy All action");
        }

        static string Join(IReadOnlyList<string> lines)
        {
            var arr = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) arr[i] = lines[i] ?? "<null>";
            return "[" + string.Join(" / ", arr) + "]";
        }
    }
}

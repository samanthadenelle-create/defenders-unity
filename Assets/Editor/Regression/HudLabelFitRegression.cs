// =============================================================================
// HudLabelFitRegression [hud-label-fit] (WO-1144) - a town HUD label can never
// again be CUT, ellipsised, or painted through the widget next to it.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Markers: HUD_LABEL_FIT_OK / HUD_LABEL_FIT_FAIL
//
// WHY A DISTINCT MARKER (canon section 8): a shared REGRESSION_OK is how a 22-case
// suite's pass once read as the whole suite's pass. This one says its own name.
//
// WHAT WAS CAPTURED (2026-08-22 headed fleet, autopilot-runs/*/break_24_error.png at
// 2670x1200, IDENTICAL in all 8 runs - the screenshot IS the data):
//   1  "Tap to collec"      the Collectors rail chip, cut mid-word.
//   2  "Manag..."           the Manage bar face, ellipsised while every sibling fit.
//   3  "TIER UP! Initiate"  painted across the world tree at screen centre.
//   4  "Wave 1" / "Next wave in 45s" painted THROUGH the compass strip, with
//                           "Start Now" jammed against its bottom edge.
// Every layout NUMBER in that frame was legal. That is precisely why this oracle
// exists and why it MEASURES.
//
// ==========================  HOW THIS SUITE IS HONEST  =======================
// The banned shape is an oracle that recomputes a label's fit from the same
// constants the layout used - it cannot fail, and this repo found three of those
// in 24 hours. So:
//
//   * THE WIDTH IS MEASURED, NOT COUNTED. ElarionUiKit.MeasureLineWidthPx sums the
//     real TMP font asset's per-glyph HORIZONTAL ADVANCES - the same numbers TMP
//     steps the pen by - for the string as AUTHORED IN canon-strings.json. Add a
//     word to a canon string, or regenerate a role font with a wider face, and the
//     number moves and this suite fails. Nothing here counts characters.
//   * THE BOX IS PART MEASURED, PART SOURCE LINT - and Case 0 says which is which
//     per box, because "measured" is a claim, not a mood.
//       - MEASURED off a live object: the ActionBar and Status zone fractions are
//         read back from a real HudAreasHost (Create -> Mount(area) -> anchors),
//         so a mount that moves reds this suite even though every source literal
//         that authored it is untouched. Un-instantiable => NAMED PartialSkip.
//       - TYPED PIN (a real cross-assembly read, not text): HudKitController's
//         public WaveBandHeightPx and ElarionUiKit's MinTouchPx/FontFloor. Rename
//         or retype one and this file does not compile.
//       - SOURCE LINT: RailChipWidthPx / RailChipHeightPx / BarGap. Those three
//         are declared `private const` in HudKitController (grep the names; no line
//         numbers here on purpose — copied ones rot) and there
//         is no InternalsVisibleTo for DeNelle.EditorRegression, so a typed read
//         is impossible from here. Narrow a chip and the lint fails rather than
//         the oracle silently following it down. Making these measured needs a
//         probe hook in HudKitController - that is a HudKitController lane, and
//         until it exists this file says LINT and does not pretend otherwise.
//     ⛔ CORRECTED 2026-09-07 (WO-1494). This paragraph used to justify the lint
//     with "HudKitController is DeNelle.HUD, which this assembly does NOT
//     reference". THAT IS FALSE: DeNelle.EditorRegression.asmdef lists DeNelle.HUD
//     (second row), which is why the mount read above is possible at all. The same
//     false sentence sat in HudUiRegression and in Case 0 and is corrected in both.
//     The REAL limits are member ACCESSIBILITY (above) and the fact that batchmode
//     runs no layout pass, so anything needing a laid-out rect must force one or
//     degrade to a named skip - never to a pass.
//   * TWO ASPECTS, BOTH LANDSCAPE. 2670x1200 (the capture) and 1920x1080. A label
//     that fits at one width can still cut at another, which is the whole reason
//     the WO demanded it.
//   * THE FLOOR IS THE FLOOR. Fit is asserted at ElarionUiKit.FontFloor (30 ref
//     px), the smallest LEGIBLE size auto-sizing may reach. "It would fit at 18px"
//     is not a pass - past the floor TMP ellipsises, which is defect 2 exactly.
//
// WHAT THIS SUITE CANNOT DO: prove two live rects do not overlap on a real canvas.
// That stays the job of RunCaptureHeadless plus eyes-on (canon: verify UI changes
// by SCREENSHOT). What it CAN do is make the four captured defects unreachable by
// construction, headlessly, on every gate run.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class HudLabelFitRegression
    {
        // ── the canonical artefacts ──────────────────────────────────────────
        private const string EnglishRes = "Assets/Resources/Data/Canonical/en.json";
        private const string EnglishStr = "Assets/StreamingAssets/Data/Canonical/en.json";
        private const string HudSrc   = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string TierSrc  = "Assets/_Modules/Village/Progression/TierSystem.cs";
        private const string AreasRes = "Assets/Resources/Data/Canonical/hud-areas.json";
        /// <summary>The Hero/Realm/Journey card deck - the file under test in Case 6 (WO-1341).</summary>
        private const string DeckSrc  = "Assets/_Modules/HUD/PlayerDeckWorkspace.cs";
        /// <summary>THE REFERENCE IMPLEMENTATION for a kit card face. Owner ruling 2026-09-03:
        /// "should match font and format of Manage screen". Case 6 reads its numbers OUT of this
        /// file rather than restating them, so Manage stays the standard by construction: restyle
        /// Manage and the deck must follow or the suite fails.</summary>
        private const string ManageSrc = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";

        /// <summary>
        /// Card art that has a TITLE AND A TAGLINE PAINTED INTO THE PNG, verified by eye on
        /// 2026-09-03 under Assets/Resources/UI/ElarionMedieval/cards/. These four are the only
        /// ones in the kit that do: buildings.png (Manage), quests.png (Journey) and
        /// realm-store.png (Realm) are all illustration-left with an EMPTY text plate right,
        /// which is the standard ManageScreenPanel.cs:606 states outright ("the approved kit
        /// cards are text-safe layered faces: illustration and border are art, while title,
        /// purpose, count and interaction remain live").
        /// <para>Mounting one of these as a card face gives that card TWO producers for one
        /// string. On device build 2026.09.03.353742 that printed every Hero label twice, in two
        /// fonts, with two different wordings ("Manage your items" over "Browse every carried
        /// item by category", and "LOAD OUT" over "Loadout"). Re-authoring them text-free is the
        /// owner's call; until then no card may mount them.</para>
        /// </summary>
        private static readonly string[] BakedLabelCardArt = { "bag", "equipment", "skills", "loadout" };

        // ── WO-1359: the action-bar faces ────────────────────────────────────
        /// <summary>The calm dock's five faces, IN THE ORDER THE PLAYER SEES THEM. The first sheet
        /// the owner authored read BUILD/TALK/HERO/MANAGE across the top with JOURNEY beneath - the
        /// sheet's order and the bar's order were already NOT the same, on the very first delivery.
        /// That is exactly why a face's icon is keyed by that face's own caption and never by a
        /// slot index: the next sheet is free to disagree again.</summary>
        private static readonly string[] BarFaceIconKeys = { "build", "talk", "hero", "journey", "manage" };
        private static readonly string[] BarFaceLabelSymbols =
            { "KeyNavBuild", "KeyNavTalk", "KeyNavHero", "KeyNavJourney", "KeyNavManage" };

        /// <summary>Where authored face art lives.</summary>
        private const string ActionBarArtDir = "Assets/Resources/UI/ElarionMedieval/actionbar/";
        /// <summary>The owner-authored emblem sheet and the slice manifest derived from its alpha.</summary>
        private const string EmblemSheetPng = ActionBarArtDir + "actionbar-emblems.png";
        private const string EmblemSheetJson = ActionBarArtDir + "actionbar-emblems.json";
        /// <summary>The Resources address of that sheet - the left half of every face's icon address.</summary>
        private const string EmblemSheetRes = "UI/ElarionMedieval/actionbar/actionbar-emblems";

        /// <summary>
        /// Action-bar art with WORDS PAINTED INTO THE PNG. Same hazard as
        /// <see cref="BakedLabelCardArt"/> and the same precedent (WO-1341): the dock draws its
        /// caption as live TMP text, so mounting a plate that already says "MANAGE" gives that face
        /// TWO producers for one word, in two fonts, at two sizes - which on device build
        /// 2026.09.03.353742 printed every Hero deck label twice.
        /// <para>actionbar-icons-sheet-2026-09-03 was the FIRST sheet the owner supplied: five
        /// emblems, each with an engraved name plate under it. She re-generated the set WITHOUT the
        /// words and that sheet is deleted, so this entry normally just adds a note - it stays as a
        /// permanent guard, because the failure it describes is one this repo has already shipped
        /// once and the art most likely to come back with a word on it is action-bar art.</para>
        /// </summary>
        private static readonly string[] BakedWordFaceArt = { "actionbar-icons-sheet-2026-09-03" };

        /// <summary>The concept-&gt;icon table, in both its shipped copies.</summary>
        private const string ConceptIconsRes = "Assets/Resources/Data/Canonical/concept-icons.json";
        private const string ConceptIconsStr = "Assets/StreamingAssets/Data/Canonical/concept-icons.json";

        // ── the boxes, each pinned by a source lint in Case 0 ────────────────
        /// <summary>Collectors/Echoes/Resources rail chip, fixed reference px (HudKitController
        /// RailChipWidthPx == EchoUnlockFeedback.EchoChipWidthPx - three chips, one right edge).</summary>
        private const float RailChipWidthPx = 220f;
        /// <summary>Rail chip height - authored AT the tap floor.</summary>
        private const float RailChipHeightPx = 112f;
        /// <summary>ElarionUiKit.BuildObsidianButton insets its label to x 0.04..0.96.</summary>
        private const float ButtonLabelInset = 0.92f;
        /// <summary>HudAreasHost: ActionBar spans x 0.270..0.730 of the canvas.
        /// ⭐ WO-1494: VERIFIED AGAINST A LIVE MOUNT — Case 0's MeasureZoneMounts instantiates a
        /// real HudAreasHost and reds if the built HudArea.ActionBar disagrees with this. It is a
        /// declared expectation checked against an object, no longer an unchecked copy.</summary>
        private const float ActionBarZoneFrac = 0.730f - 0.270f;
        /// <summary>HudKitController.BarGap. SOURCE LINT ONLY — `private const` over there
        /// in HudKitController with no InternalsVisibleTo, so it cannot be read typed.</summary>
        private const float BarGap = 0.01f;
        /// <summary>HudAreasHost: Status spans x 0.340..0.660 (the wave band inherits its width).
        /// ⭐ WO-1494: VERIFIED AGAINST A LIVE MOUNT, same as ActionBarZoneFrac above.</summary>
        private const float StatusZoneFrac = 0.660f - 0.340f;
        /// <summary>HudKitController.WaveBandHeightPx. TYPED PIN — the member is `public const`,
        /// so Case 0 compares this against the real one across the assembly boundary.</summary>
        private const float WaveBandHeightPx = 128f;
        /// <summary>The wave CTA's band inside it (y 0.03..0.93).</summary>
        private const float WaveCtaBandFrac = 0.93f - 0.03f;
        /// <summary>The wave labels' x band inside it (0.02..0.60).</summary>
        private const float WaveLabelBandFrac = 0.60f - 0.02f;
        /// <summary>TMP line advance as a multiple of font size (conservative; TMP's own line
        /// height for these faces is below this, so a pass here is a pass there).</summary>
        private const float LineHeightFactor = 1.2f;

        private struct Aspect { public string Name; public float W, H; }
        private static readonly Aspect[] Aspects =
        {
            new Aspect { Name = "2670x1200 (the capture)", W = 2670f, H = 1200f },
            new Aspect { Name = "1920x1080",               W = 1920f, H = 1080f },
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HUD_LABEL_FIT_OK - " + reason);
            else Debug.LogError("HUD_LABEL_FIT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "boxes-pinned",     () => Case0_BoxesStillAuthored(failures, notes));
                Case(failures, "canon-parity",     () => Case1_CanonParity(failures, notes));
                Case(failures, "collector-chip",   () => Case2_CollectorChip(failures, notes));
                Case(failures, "manage-face",      () => Case3_ManageFace(failures, notes));
                Case(failures, "wave-band",        () => Case4_WaveBand(failures, notes));
                Case(failures, "tier-stamp",       () => Case5_TierStamp(failures, notes));
                Case(failures, "deck-card-labels", () => Case6_DeckCardSingleProducer(failures, notes));
                Case(failures, "deck-card-packaging", () => Case7_DeckCardPackagingMargin(failures, notes));
                Case(failures, "bar-face-icons", () => Case8_BarFaceIcons(failures, notes));
                Case(failures, "raids-locked-face", () => Case9_RaidsLockedFace(failures, notes));
                Case(failures, "heartfire-inside-plate", () => Case10_HeartfireInsidePlate(failures, notes));
                Case(failures, "night-market-standout", () => Case11_NightMarketStandout(failures, notes));
                Case(failures, "night-market-aurora", () => Case12_NightMarketAurora(failures, notes));
                Case(failures, "heart-objective-state", () => Case13_HeartObjectiveState(failures, notes));
                Case(failures, "countdown-minutes", () => Case14_CountdownMinutes(failures, notes));
                Case(failures, "builders-chip-idle", () => Case15_BuildersChipIdle(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes.ToArray()) + "]" : "";
            if (failures.Count == 0)
            {
                // ⚠ The verdict states WHAT WAS MEASURED and WHAT WAS ONLY LINTED (WO-1494).
                // "OK" over an unqualified claim of measurement is how a text match came to read
                // as a rendered pass; a reader of a green log must be able to tell them apart
                // without opening this file.
                reason = "HUD LABEL FIT OK - MEASURED: every authored chip/face line's width from the real " +
                         "TMP glyph advances, inside its box at the 30px legibility floor at both landscape " +
                         "aspects (not a character count), and the ActionBar/Status zone fractions read back " +
                         "off a live HudAreasHost. TYPED PINS: HudKitController.WaveBandHeightPx, " +
                         "ElarionUiKit.MinTouchPx/FontFloor. SOURCE LINT ONLY (not measured): the rail-chip " +
                         "220x112 box and BarGap, which are private consts unreadable from this assembly. " +
                         "NOT CLAIMED: no laid-out rect, and no two live rects proven disjoint on a canvas - " +
                         "that stays RunCaptureHeadless plus eyes-on" + noteStr;
                return true;
            }
            reason = "hud-label-fit FAIL x" + failures.Count + ": " +
                     string.Join(" | ", failures.ToArray()) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>The parsed Resources copy, filled by Case 1 - the AUTHORED artefact, used as
        /// the fallback so a headless loader hiccup can never turn into a bogus width failure.</summary>
        private static Dictionary<string, string> _authored;

        /// <summary>
        /// The string this suite measures: the RUNTIME resolution (HudStrings, so the loader the
        /// game actually uses is exercised) with the AUTHORED file as the fallback. If the loader
        /// hands back the visible "[[missing:key]]" marker we measure the authored text instead
        /// and SAY SO - measuring the marker would fail on its length and blame the wrong thing.
        /// </summary>
        private static string Copy(List<string> failures, List<string> notes, string key, params object[] args)
        {
            string live = args == null || args.Length == 0 ? HudStrings.Get(key) : HudStrings.Format(key, args);
            if (live != null && live.IndexOf("[[missing:", StringComparison.Ordinal) < 0) return live;

            string raw;
            if (_authored != null && _authored.TryGetValue(key, out raw))
            {
                notes.Add("'" + key + "' did not resolve through the runtime loader headlessly - measured the " +
                          "authored English localization text instead");
                try { return args == null || args.Length == 0 ? raw : string.Format(raw, args); }
                catch (FormatException) { return raw; }
            }
            failures.Add("[copy] localization key '" + key + "' resolves to nothing at runtime AND is absent from " +
                         EnglishRes + " - the HUD would paint a placeholder marker");
            return "";
        }

        // =====================================================================
        //  CASE 0 - the boxes this suite measures against are STILL the boxes
        // =====================================================================
        // Without this case the suite would be measuring against numbers it made up.
        // The pin is a source lint: if someone narrows a chip, re-divides the bar, or
        // drops the wave band, this fails LOUDLY instead of the oracle quietly following
        // the layout down.
        // ⭐ WO-1494 (2026-09-07) - THE TWO ZONE FRACTIONS ARE NOW MEASURED, NOT COPIED.
        // ActionBarZoneFrac and StatusZoneFrac used to be literals typed into this file from
        // HudAreasHost. MeasureZoneMounts below instantiates a REAL HudAreasHost and reads the
        // mounts back, so moving a mount reds this suite with every source literal intact - which
        // is exactly what a lint cannot do. The three HudKitController box literals stay lint and
        // the header says so: they are `private const` and there is no InternalsVisibleTo here.
        // ⛔ CORRECTED 2026-09-06 (WO-1465/1466/1468). The sentence that used to justify the
        // lint — "DeNelle.EditorRegression cannot reference DeNelle.HUD" — IS FALSE:
        // Assets/Editor/Regression/DeNelle.EditorRegression.asmdef lists "DeNelle.HUD" in its
        // references array (second row), and HudUiRegression's [gear-drawer-clearance] and
        // [stack-badge-inside-medallion] cases now instantiate HUD types to prove it. The same
        // false claim sat in HudUiRegression and is corrected there too. The lint stays — a
        // source pin is still the right tool for "this literal must not move" — but nothing in
        // this file is limited to source reading by an assembly boundary that does not exist.
        // The REAL limit is that batchmode runs no layout pass, so a laid-out measurement must
        // force one and degrade to a NAMED SKIP when it cannot.
        private static void Case0_BoxesStillAuthored(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(HudSrc);
            if (src == null)
            {
                failures.Add("[boxes-pinned] cannot read " + HudSrc + " - every measurement below would be " +
                             "against invented numbers");
                return;
            }

            RequireLiteral(failures, src, "RailChipWidthPx = 220f",
                "the rail chip width this suite measures the Collectors lines against");
            RequireLiteral(failures, src, "RailChipHeightPx = ElarionUiKit.MinTouchPx",
                "the rail chip height (three text lines have to seat inside it)");
            RequireLiteral(failures, src, "BarGap = 0.01f",
                "the action-bar gap the per-face slot width is derived from");
            RequireLiteral(failures, src, "WaveBandHeightPx = 128f",
                "the wave block's own fixed-pixel band");

            // TYPED PIN, not a lint: WaveBandHeightPx is `public const` on HudKitController, so
            // this reads the REAL member across the assembly boundary. Rename it and this file
            // stops compiling; retune it and this fails naming both numbers. (The three literals
            // above cannot be read this way - they are private, see the header.)
            if (Mathf.Abs(DeNelle.HUD.Kit.HudKitController.WaveBandHeightPx - WaveBandHeightPx) > 0.01f)
                failures.Add("[boxes-pinned] HudKitController.WaveBandHeightPx is " +
                             DeNelle.HUD.Kit.HudKitController.WaveBandHeightPx + ", but Case 4 measures the " +
                             "wave lines against " + WaveBandHeightPx + " - the band moved and this suite " +
                             "would have kept fitting labels into a box that no longer exists");

            MeasureZoneMounts(failures, notes);

            // The kit's own inset + floors, read from Core (a real reference, not a lint).
            if (Mathf.Abs(ElarionUiKit.MinTouchPx - 112f) > 0.01f)
                failures.Add("[boxes-pinned] ElarionUiKit.MinTouchPx is " + ElarionUiKit.MinTouchPx +
                             ", not 112 - the wave CTA band (0.03..0.93 of " + WaveBandHeightPx +
                             "px) was sized to clear that floor and must be re-derived");
            if (Mathf.Abs(ElarionUiKit.FontFloor - 30f) > 0.01f)
                notes.Add("FontFloor is " + ElarionUiKit.FontFloor + " (was 30 when these boxes were fitted)");

            // ⚠ WO-1467 - THE MaxVisibleFaces PIN THAT SAT HERE IS RETIRED. It failed unless the
            // constant equalled a literal, and its message claimed the constant described "the
            // adaptive peaceful HUD". It never did: HudKitController.BindActionBar returns early
            // whenever the adaptive peaceful dock exists, so HudActionBarModel is not subscribed on
            // the shipping path and does not size a single medallion the player touches. A pin on
            // an unbound path is worse than no pin - it reads as coverage.
            // The dock is now MEASURED: HudActionBarRegression.CheckMeasuredPeacefulDock builds it
            // live and asserts the faces it finds. Do not re-add a literal here.

            // The two widgets that collided are STILL both authored into one area, which is why the
            // wave block has to buy its own band rather than trust two fraction stacks to agree.
            string areas = ReadSrc(AreasRes);
            if (areas != null)
            {
                int status = areas.IndexOf("\"status\"", StringComparison.Ordinal);
                int bar = status >= 0 ? areas.IndexOf("\"actionBar\"", status, StringComparison.Ordinal) : -1;
                string window = (status >= 0 && bar > status) ? areas.Substring(status, bar - status) : "";
                bool shared = window.IndexOf("\"compass\"", StringComparison.Ordinal) >= 0 &&
                              window.IndexOf("\"waveBlock\"", StringComparison.Ordinal) >= 0;
                notes.Add(shared
                    ? "compass + waveBlock still share the calm(town) status area - the wave band's " +
                      "fixed-pixel hang below the mount is what keeps them disjoint"
                    : "compass + waveBlock no longer share the status area (the WO-1144 hang may now be " +
                      "belt-and-braces)");
            }
        }

        // =====================================================================
        //  MEASURED: the zone fractions come off a LIVE HudAreasHost (WO-1494)
        // =====================================================================
        // WHY THIS IS NOT THE BANNED SHAPE. The banned shape recomputes a fit from the same
        // constants the layout used. This does the opposite: it builds the real mount table the
        // HUD builds (HudAreasHost.Create -> Build() -> Add(HudArea.*, ...)) and reads the anchors
        // back off the instantiated RectTransforms, then compares them with the fractions Cases 3
        // and 4 divide their boxes by. Move HudArea.ActionBar or HudArea.Status - in HudAreasHost,
        // or in HudLayoutBands, or by any future route - and this REDS while every literal in
        // HudKitController.cs and in this file stays byte-identical. That is the class of defect
        // a source lint structurally cannot see, and it is the WO-1494 conversion for this suite.
        //
        // ANCHORS, NOT LAID-OUT RECTS - deliberate. Batchmode runs no layout pass, so sizeDelta /
        // rect would be noise; anchorMin/anchorMax are authored screen fractions and are correct
        // the moment Build() returns. (HudUiRegression's [gear-drawer-clearance] reads the same
        // mounts the same way and is the precedent.)
        //
        // RED, one line: in HudAreasHost.Build, change
        //     Add(HudArea.Status, new Vector2(0.340f, 0.845f), new Vector2(0.660f, 0.990f));
        // to 0.400f/0.600f. Every RequireLiteral in this file stays green; this fails with
        // "the LIVE HudArea.Status mount spans x 0.400..0.600 (width 0.200), but Case 4 ...".
        private static void MeasureZoneMounts(List<string> failures, List<string> notes)
        {
            const string Section = "boxes-pinned zone mounts";
            DeNelle.HUD.Kit.HudAreasHost host = null;
            try
            {
                try { host = DeNelle.HUD.Kit.HudAreasHost.Create(null); }
                catch (Exception ex)
                {
                    // HARNESS-CAPABILITY-ABSENT. A named PartialSkip, never a silent pass: the
                    // caller has to be able to subtract what was not proven this run.
                    notes.Add(RegressionOutcome.PartialSkip(Section,
                        "HudAreasHost could not be instantiated headlessly (" + ex.GetType().Name +
                        ": " + ex.Message + ") - the ActionBar/Status zone fractions were NOT " +
                        "measured this run and remain unverified copies"));
                    return;
                }
                if (host == null)
                {
                    // NOT a capability gap: Create news a GameObject, Builds and returns
                    // unconditionally (HudAreasHost.Create), and the throwing path is already
                    // caught above (see HudAreasHost.Create — read the method, not a line number).
                    // A null here means the HUD's own mount factory produced nothing, and every
                    // box below it is unproven.
                    failures.Add("[boxes-pinned] HudAreasHost.Create(null) returned NULL without throwing - " +
                                 "there is no legitimate shape for this, and the ActionBar/Status zone " +
                                 "fractions this suite divides its boxes by are UNPROVEN, not correct");
                    return;
                }

                RequireMountWidth(failures, notes, host, DeNelle.HUD.Kit.HudArea.ActionBar,
                    ActionBarZoneFrac, "Case 3 divides the action bar into per-face slots across it");
                RequireMountWidth(failures, notes, host, DeNelle.HUD.Kit.HudArea.Status,
                    StatusZoneFrac, "Case 4 fits the wave lines and the Start Now CTA inside it");

                // The one authority HudAreasHost publishes as a const is checked typed as well, so
                // the mount read and the const cannot drift apart unnoticed.
                float published = DeNelle.HUD.Kit.HudAreasHost.ActionBarMaxX -
                                  DeNelle.HUD.Kit.HudAreasHost.ActionBarMinX;
                if (Mathf.Abs(published - ActionBarZoneFrac) > 0.0005f)
                    failures.Add("[boxes-pinned] HudAreasHost.ActionBarMinX..MaxX publishes a width of " +
                                 published.ToString("0.000") + ", but this suite measures faces against " +
                                 ActionBarZoneFrac.ToString("0.000"));
            }
            catch (Exception ex)
            {
                // Reading anchors off a built host is plain object access. A throw here is the
                // product defect this exists to catch, not a stand-down.
                failures.Add("[boxes-pinned] measuring the live HUD area mounts THREW " +
                             ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        private static void RequireMountWidth(List<string> failures, List<string> notes,
            DeNelle.HUD.Kit.HudAreasHost host, DeNelle.HUD.Kit.HudArea area,
            float expected, string whatUsesIt)
        {
            var rt = host.Mount(area);
            if (rt == null)
            {
                failures.Add("[boxes-pinned] the HUD has no live mount for HudArea." + area +
                             " - " + whatUsesIt + ", against a zone that does not exist");
                return;
            }
            float x0 = rt.anchorMin.x, x1 = rt.anchorMax.x;
            float width = x1 - x0;
            if (width <= 0f)
            {
                failures.Add("[boxes-pinned] the LIVE HudArea." + area + " mount spans x " +
                             x0.ToString("0.000") + ".." + x1.ToString("0.000") +
                             " - a zero/inverted width, so " + whatUsesIt + " against nothing");
                return;
            }
            if (Mathf.Abs(width - expected) > 0.0005f)
            {
                failures.Add("[boxes-pinned] the LIVE HudArea." + area + " mount spans x " +
                             x0.ToString("0.000") + ".." + x1.ToString("0.000") + " (width " +
                             width.ToString("0.000") + "), but this suite measures against " +
                             expected.ToString("0.000") + ". " + whatUsesIt + ", so every fit " +
                             "verdict for that widget is against a box the HUD no longer builds. " +
                             "MEASURED off the instantiated mount - no source literal moved.");
                return;
            }
            notes.Add("measured HudArea." + area + " mount x " + x0.ToString("0.000") + ".." +
                      x1.ToString("0.000") + " off a live HudAreasHost");
        }

        // =====================================================================
        //  CASE 1 - the words live in the English localization catalog, in BOTH copies, in ASCII
        // =====================================================================
        private static void Case1_CanonParity(List<string> failures, List<string> notes)
        {
            var res = ReadCanon(EnglishRes, failures);
            var str = ReadCanon(EnglishStr, failures);
            _authored = res;
            if (res == null || str == null) return;

            foreach (string key in HudStrings.AllKeys)
            {
                string a, b;
                if (!res.TryGetValue(key, out a))
                {
                    failures.Add("[canon-parity] " + EnglishRes + " has no '" + key + "' - the HUD would " +
                                 "render the [[missing:key]] marker where a word belongs");
                    continue;
                }
                if (!str.TryGetValue(key, out b))
                {
                    failures.Add("[canon-parity] " + EnglishStr + " has no '" + key + "' - a device build " +
                                 "reading StreamingAssets would lose this line");
                    continue;
                }
                if (!string.Equals(a, b, StringComparison.Ordinal))
                    failures.Add("[canon-parity] '" + key + "' DIFFERS between the copies: Resources '" + a +
                                 "' vs StreamingAssets '" + b + "'");
                foreach (char c in a)
                    if (c > 127)
                    {
                        failures.Add("[canon-parity] '" + key + "' carries the non-ASCII character U+" +
                                     ((int)c).ToString("X4") + " - TMP renders it as tofu");
                        break;
                    }
            }
            notes.Add(HudStrings.AllKeys.Length + " HUD copy keys present in both English localization copies");
        }

        // =====================================================================
        //  CASE 2 - the Collectors chip: "Tap to collec" can never come back
        // =====================================================================
        // The chip is 220 x 112 ref px with a 0.92 label inset, so the label rect is
        // ~202 x 112. Line 1 ("Collectors N/M full") WRAPS - which is fine, the chip is
        // two lines tall by design - so the real assertions are:
        //   (a) every ACTION line fits on ONE line of ~202 px at the floor  <- defect 1
        //   (b) the whole block still seats in 112 px of height once line 1 has wrapped
        private static void Case2_CollectorChip(List<string> failures, List<string> notes)
        {
            float boxW = RailChipWidthPx * ButtonLabelInset;
            float boxH = RailChipHeightPx;
            float floor = ElarionUiKit.FontFloor;

            // Worst realistic counts: two digits each side, and a two-digit percentage.
            string count = Copy(failures, notes, HudStrings.KeyCollectorsCount, 12, 12);
            string[] actionLines =
            {
                Copy(failures, notes, HudStrings.KeyCollectorsFullLine),
                Copy(failures, notes, HudStrings.KeyCollectorsNearlyLine, 99),
                Copy(failures, notes, HudStrings.KeyCollectorsWaitingLine, 99),
            };

            foreach (string action in actionLines)
            {
                string detail;
                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, action, floor, out detail);
                if (w < 0f)
                {
                    failures.Add("[collector-chip] cannot measure '" + action + "': " + detail);
                    continue;
                }
                if (w > boxW)
                    failures.Add("[collector-chip] the action line '" + action + "' MEASURES " +
                                 w.ToString("0.0") + " ref px at the " + floor + "px legibility floor but the " +
                                 "chip label rect is only " + boxW.ToString("0.0") + " px (" + detail +
                                 "). There is no legible size at which it fits, so TMP cuts it - that is the " +
                                 "captured 'Tap to collec' exactly. Shorten the WORDS in canon-strings.json; " +
                                 "do NOT drop the font and do NOT widen the chip (three rail chips share one edge)");

                // (b) height: line 1 wraps, then the action line. Measured wrap, not assumed.
                int lines = WrappedLineCount(count, boxW, floor) + 1;
                float needed = lines * floor * LineHeightFactor;
                if (needed > boxH)
                    failures.Add("[collector-chip] '" + count + "' + '" + action + "' needs " + lines +
                                 " wrapped lines = " + needed.ToString("0.0") + " ref px of height at the floor, " +
                                 "but the chip is only " + boxH.ToString("0.0") + " px tall - the bottom line " +
                                 "would be truncated away");
            }

            string d2;
            string title = Copy(failures, notes, HudStrings.KeyCollectorsTitle);
            float cw = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, title, floor, out d2);
            if (cw > boxW)
                failures.Add("[collector-chip] even the bare title '" + title + "' MEASURES " + cw.ToString("0.0") +
                             " px against a " + boxW.ToString("0.0") + " px rect (" + d2 + ")");
            notes.Add("collector chip rect " + boxW.ToString("0") + "x" + boxH.ToString("0") +
                      " ref px; longest action line " + LongestOf(actionLines, floor).ToString("0.0") + " px");
        }

        // =====================================================================
        //  CASE 3 - the Manage bar face: "Manag..." can never come back
        // =====================================================================
        // A face is one MaxVisibleFaces-th of the ActionBar zone, which is a fraction of
        // the CANVAS - so it is aspect-dependent and has to be measured at both. The
        // captured sentence "Manage - 2 of 3 idle" was roughly four times its box; the
        // face now paints ManageBaseLabel plus a canon BADGE on a second line.
        private static void Case3_ManageFace(List<string> failures, List<string> notes)
        {
            float floor = ElarionUiKit.FontFloor;
            float slotFrac = (1f - BarGap * (HudActionBarModel.MaxVisibleFaces - 1)) /
                             HudActionBarModel.MaxVisibleFaces;

            string[] faceLines =
            {
                HudActionBarModel.ManageBaseLabel,
                Copy(failures, notes, HudStrings.KeyManageIdleAll, 3),
                Copy(failures, notes, HudStrings.KeyManageIdleSome, 2, 3),
            };

            foreach (var a in Aspects)
            {
                float canvasW = a.W / ScaleFactor(a.W, a.H);
                float faceW = canvasW * ActionBarZoneFrac * slotFrac;
                float boxW = faceW * ButtonLabelInset;

                foreach (string line in faceLines)
                {
                    string detail;
                    float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, line, floor, out detail);
                    if (w < 0f) { failures.Add("[manage-face] cannot measure '" + line + "': " + detail); continue; }
                    if (w > boxW)
                        failures.Add("[manage-face] at " + a.Name + " the face line '" + line + "' MEASURES " +
                                     w.ToString("0.0") + " ref px at the " + floor + "px floor but a bar face's " +
                                     "label rect is only " + boxW.ToString("0.0") + " px (" + detail +
                                     "). TMP ellipsises past the floor - that is the captured 'Manag...'. Put " +
                                     "FEWER WORDS on the face (HudStrings/ManageFaceBadge); the one-line " +
                                     "sentence belongs in HudActionBarModel.ManageFaceLabel, which nothing paints");
                }

                // Two lines have to seat in the face's height as well.
                float barZoneH = (a.H / ScaleFactor(a.W, a.H)) * (0.150f - 0.015f);
                float faceH = barZoneH * (0.95f - 0.10f);
                float needed = 2f * floor * LineHeightFactor;
                if (needed > faceH)
                    failures.Add("[manage-face] at " + a.Name + " a two-line face needs " +
                                 needed.ToString("0.0") + " ref px but the face is only " + faceH.ToString("0.0") +
                                 " px tall - the badge line would be culled, which is worse than the ellipsis " +
                                 "(a culled line says nothing at all)");
                notes.Add("manage face at " + a.Name + ": " + boxW.ToString("0") + "x" + faceH.ToString("0") +
                          " ref px of label rect");
            }

            // The View must still paint the BADGE, not the sentence, or the box math above is moot.
            string src = ReadSrc(HudSrc);
            if (src != null)
            {
                if (src.IndexOf("ManageFaceBadge", StringComparison.Ordinal) < 0)
                    failures.Add("[manage-face] HudKitController no longer reads HudActionBarModel." +
                                 "ManageFaceBadge - if it went back to painting ManageFaceLabel, the face is " +
                                 "carrying a sentence again and every measurement above is measuring the wrong " +
                                 "string");
                if (src.IndexOf("ElarionUiKit.FitBlock(_manageButtonLabel)", StringComparison.Ordinal) < 0)
                    failures.Add("[manage-face] the Manage face label is no longer re-armed with FitBlock - " +
                                 "BuildObsidianButton arms FitSingleLine (no-wrap + ellipsis), so a second line " +
                                 "cannot render and the badge is silently ellipsised away");
            }
        }

        // =====================================================================
        //  CASE 4 - the wave block owns a band the compass cannot enter
        // =====================================================================
        private static void Case4_WaveBand(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add("[wave-band] cannot read " + HudSrc); return; }

            // (a) It must NOT stretch the shared Status mount any more. That single line is what
            //     put the wave labels inside the compass strip (which owns y 0.34..1.00 of it).
            int bwb = src.IndexOf("private void BuildWaveBlock", StringComparison.Ordinal);
            int end = bwb >= 0 ? src.IndexOf("Register(\"waveBlock\"", bwb, StringComparison.Ordinal) : -1;
            string body = (bwb >= 0 && end > bwb) ? src.Substring(bwb, end - bwb) : "";
            if (body.Length == 0)
            {
                failures.Add("[wave-band] BuildWaveBlock not found in " + HudSrc);
            }
            else
            {
                if (body.IndexOf("wbrt.anchorMax = Vector2.one", StringComparison.Ordinal) >= 0)
                    failures.Add("[wave-band] the wave block stretches the whole Status mount again " +
                                 "(anchorMax = Vector2.one). hud-areas.json puts the compass in that SAME " +
                                 "mount and HudCompassWidget's strip owns y 0.34-1.00 of it, so the wave " +
                                 "labels are being painted through the compass - the captured defect 4");
                if (body.IndexOf("WaveBandHeightPx", StringComparison.Ordinal) < 0)
                    failures.Add("[wave-band] the wave block no longer sizes itself in FIXED reference px. " +
                                 "It must not go back to a height FRACTION: HudArea.Status is 0.845-0.990, " +
                                 "which is 278 ref px in portrait but collapses to ~140 in landscape, and a " +
                                 "compass + two labels + a bar + a 112px CTA has never fitted in 140");
                if (body.IndexOf("pivot = new Vector2(0.5f, 1f)", StringComparison.Ordinal) < 0)
                    failures.Add("[wave-band] the wave band no longer hangs from its mount's BOTTOM edge " +
                                 "(pivot 0.5,1) - it is back inside the crown with the compass");
            }

            // (b) The CTA must be authored AT the touch floor. ClampMinTouch cannot rescue it: the
            //     rect is still 0 when the button is built, which is how it shipped at ~46 ref px.
            float ctaH = WaveBandHeightPx * WaveCtaBandFrac;
            if (ctaH < ElarionUiKit.MinTouchPx - 0.5f)
                failures.Add("[wave-band] the Start Wave CTA resolves to " + ctaH.ToString("0.0") +
                             " ref px tall, under the " + ElarionUiKit.MinTouchPx + "px touch floor");

            // (c) The countdown - the longest wave string - must fit the label column it now has.
            //     MEASURED at the floor, at both aspects.
            //     WO-1407 RE-POINT: the string used to be "Next wave in 45s"; the countdown now
            //     prints minutes ("Next wave in 14m 15s" - ElarionUi.Duration), which is the
            //     WIDER form, so this is the one that has to fit the column.
            string countdown = "Next wave in " + ElarionUi.Duration(855);
            foreach (var a in Aspects)
            {
                float canvasW = a.W / ScaleFactor(a.W, a.H);
                float boxW = canvasW * StatusZoneFrac * WaveLabelBandFrac;
                string detail;
                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, countdown,
                                                          ElarionUiKit.FontFloor, out detail);
                if (w < 0f) { failures.Add("[wave-band] cannot measure the countdown: " + detail); break; }
                if (w > boxW)
                    failures.Add("[wave-band] at " + a.Name + " the countdown '" + countdown + "' MEASURES " +
                                 w.ToString("0.0") + " ref px but its label column is only " +
                                 boxW.ToString("0.0") + " px - it would ellipsize the seconds away, which is " +
                                 "the only number on that line that changes");
                else
                    notes.Add("wave countdown at " + a.Name + ": " + w.ToString("0") + " px in a " +
                              boxW.ToString("0") + " px column");
            }
        }

        // =====================================================================
        //  CASE 5 - TIER UP is a CAPPED SCREEN STAMP, never a world-space label
        // =====================================================================
        private static void Case5_TierStamp(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(TierSrc);
            if (src == null) { failures.Add("[tier-stamp] cannot read " + TierSrc); return; }

            if (src.IndexOf("DamageNumberSpawner.SpawnLabel(", StringComparison.Ordinal) >= 0)
                failures.Add("[tier-stamp] TierSystem is back on DamageNumberSpawner.SpawnLabel. That is a " +
                             "WORLD-SPACE TextMesh whose on-screen size is a function of camera distance, and " +
                             "it spawns at the HERO - who in town stands metres from the camera. At the 1.6 " +
                             "scale it shipped with, the 2026-08-22 fleet captured 'TIER UP!  Initiate' " +
                             "painted across the entire world tree. Use CombatText (screen space, font capped " +
                             "at 44 ref px, dark outline, pooled and deduped)");
            if (src.IndexOf("CombatText.Show(", StringComparison.Ordinal) < 0)
                failures.Add("[tier-stamp] TierSystem no longer announces the milestone through CombatText - " +
                             "a tier-up that says nothing is worse than one that says it too loudly");
            notes.Add("TIER UP routed through the CombatText stamp layer");
        }

        // =====================================================================
        //  CASE 6 - ONE STRING, ONE PRODUCER, IN MANAGE'S FORMAT  (WO-1341)
        // =====================================================================
        // WHAT WAS CAPTURED (owner device screenshot, build 2026.09.03.353742, the HERO deck):
        // every label on the screen drawn TWICE, overlapping, in two fonts, disagreeing on the
        // words - "BAG / Manage your items" (serif gold, baked into cards/bag.png) under
        // "BAG / Browse every carried item by category" (the live TMP text), and "LOAD OUT"
        // against "Loadout". The live purpose was additionally ellipsised mid-word.
        //
        // NOT A DOUBLED MOUNT. ObsidianNavigationWorkspace.RenderCurrent destroys every content
        // child before it renders and BuildShell is idempotent, so nothing builds twice. The
        // second producer was the TEXTURE. That is why this case lints an ART REFERENCE and a
        // FORMAT, and does not go looking for a duplicate Build call.
        //
        // HOW THIS CASE STAYS HONEST: the format numbers are not restated here, they are READ
        // OUT OF ManageScreenPanel.cs and the deck is required to equal them. Manage is the
        // standard per the owner's ruling, so if Manage is restyled this fails until the deck
        // follows - it cannot pass by recomputing the deck's own constants back at itself.
        private static void Case6_DeckCardSingleProducer(List<string> failures, List<string> notes)
        {
            string deck = ReadSrc(DeckSrc);
            if (deck == null) { failures.Add("[deck-card-labels] cannot read " + DeckSrc); return; }
            string manage = ReadSrc(ManageSrc);
            if (manage == null) { failures.Add("[deck-card-labels] cannot read " + ManageSrc); return; }

            // ---- 6a  exactly ONE producer per Hero card label ------------------------------
            // Anchor inside CardsFor FIRST. PlayerDeckWorkspace.SubtitleFor switches on the same
            // enum earlier in the file, so slicing from the first 'case PlayerDeckKind.Hero:'
            // lands on a block with no Route( in it and this case would pass on nothing. That is
            // the silent-pass shape this suite's header calls banned - it was caught by running
            // the oracle RED against HEAD before shipping it.
            int cardsFor = deck.IndexOf("List<Card> CardsFor(", StringComparison.Ordinal);
            if (cardsFor < 0)
            {
                failures.Add("[deck-card-labels] no 'List<Card> CardsFor(' in " + DeckSrc +
                             " - the deck card table was renamed and 6a is measuring nothing");
                return;
            }
            string hero = SliceCase(deck.Substring(cardsFor), "case PlayerDeckKind.Hero:");
            if (hero == null)
            {
                failures.Add("[deck-card-labels] no 'case PlayerDeckKind.Hero:' block in " + DeckSrc +
                             " - the Hero deck is the screen the FTUE teaches (Hero -> Skills); this " +
                             "case cannot silently pass because the block was renamed");
                return;
            }

            int routes = 0;
            // WO-1410 (2026-09-05): the Bag / Skills / Loadout titles now come from the ONE canon source
            // (HudStrings.HeroFaceLabel(HudStrings.KeyHero*, "deck")) and each Route( call wraps onto a
            // second line. The literal budget is unchanged - "deck" + purpose + concept = three - but the
            // count must be taken over the WHOLE call, not one physical line, or a wrapped call reads as
            // one literal. Accumulate from "Route(" until its parentheses balance.
            string[] heroLines = hero.Split('\n');
            for (int li = 0; li < heroLines.Length; li++)
            {
                string line = heroLines[li].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.IndexOf("Route(", StringComparison.Ordinal) < 0) continue;
                routes++;
                int depth = 0;
                var call = new System.Text.StringBuilder(line);
                for (int i = 0; i < line.Length; i++) { if (line[i] == '(') depth++; else if (line[i] == ')') depth--; }
                while (depth > 0 && li + 1 < heroLines.Length)
                {
                    string next = heroLines[++li].Trim();
                    call.Append(' ').Append(next);
                    for (int i = 0; i < next.Length; i++) { if (next[i] == '(') depth++; else if (next[i] == ')') depth--; }
                }
                line = call.ToString();
                // Route(title, purpose, concept, panelId[, artKey]) - three quoted literals is a
                // text-free card whose ONLY label producer is the live TMP text. A fourth is an
                // art key, i.e. a second producer painted into the same plate.
                int quotes = 0;
                for (int i = 0; i < line.Length; i++) if (line[i] == '"') quotes++;
                if (quotes != 6)
                    failures.Add("[deck-card-labels] Hero route has " + (quotes / 2) + " string literals, " +
                                 "expected 3 (title, purpose, concept): '" + line + "'. A 4th is an ART KEY, " +
                                 "and a Hero card that mounts illustrated art gets its title and tagline a " +
                                 "SECOND time from the PNG - that is the WO-1341 defect exactly");
            }
            // WO-1397 re-pointed this from 4 to 5: Wardrobe (PanelId.CosmeticShop) joined the deck
            // as a text-free card - no PNG exists for it, so it cannot re-create the WO-1341
            // double-label; the quotes==6 check above still holds it to three literals.
            if (routes != 5)
                failures.Add("[deck-card-labels] found " + routes + " Hero deck routes, expected 5 " +
                             "(Bag, Equipment, Skills, Loadout, Wardrobe)");

            // ---- 6b  the label-baked PNGs are mounted by NOTHING in the deck ---------------
            foreach (string key in BakedLabelCardArt)
            {
                string art = CardArtDir + key + ".png";
                if (!File.Exists(art))
                    notes.Add("card art '" + key + "' is gone from disk - if it was re-authored " +
                              "text-free, drop it from BakedLabelCardArt and the art key may return");
                if (deck.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0)
                    failures.Add("[deck-card-labels] " + DeckSrc + " references card art key \"" + key +
                                 "\" as a string literal. " + art + " has a TITLE AND TAGLINE BAKED INTO " +
                                 "THE PNG, so mounting it puts a second producer under the live text. " +
                                 "Re-author the art text-free (owner's call) before restoring this key");
            }

            // ---- 6c  FORMAT PARITY, with the expectation read out of Manage ----------------
            Parity(failures, "card title size",  deck, manage, "face.fontSize", ";");
            Parity(failures, "card title align", deck, manage, "face.alignment", ";");
            Parity(failures, "card title fit",   deck, manage, "FitSingleLine(face,", ")");
            // ⛔ RE-POINTED 2026-09-07 (WO-1567 panel row 1), WITH THE RETIRED ANCHORS KEPT SO
            // THEY ARE NOT MOVED BACK. They read:
            //     ArgsOf(manage, "FitSingleLine(description,", ")")
            //     ArgsOf(deck,   "FitSingleLine(purpose,",     ")")
            // Manage's card description moved to FitBlock, so the Manage read returned null and
            // this case reported "cannot read the card purpose fit call" - i.e. it stopped
            // measuring rather than failing on a real drift.
            //
            // ⭐ THE FITTER CHANGED FOR A MEASURED REASON AND THE PARITY IS UNCHANGED IN FORCE.
            // Owner's device, Logs/device/screens/owner-screen-20260907-004724.png: all three hub
            // cards read "Construct and upgrade yo...", "Train and manage your tr...", "Unlock
            // powerful advance..." - the one sentence that says what the card DOES was ellipsised
            // off every card. FitSingleLine can only shrink to a floor and then ellipsise; FitBlock
            // WRAPS, and the band is two lines tall. Case 6d below already argues for wrapping over
            // an inserted U+2026 on that exact label.
            // ⚠ CORRECTED 2026-09-10 (WO-1636). This sentence used to read "The deck card's purpose
            // band is 0.26-0.52 of its plate - taller still". It was NOT taller: 0.26 of the deck
            // card resolved to 45-53 measured ref px against a 68.0 px two-line requirement, which
            // is why the glyph oracle found sixteen truncations on DeckCardPurpose_* alone. The
            // band is now 70 ref px hung off PlayerDeckWorkspace.PurposeFloorFrac; the FIT CALL
            // this case compares is unchanged and still must equal Manage's.
            // ⚠ THE FLOOR IS NOT WEAKENED. This still demands EQUALITY, and Manage's floor ROSE
            // (24f -> ElarionUi.FontFloorMobile, 30f), so the deck had to follow UP, not down.
            // Manage remains the standard the deck is read against; this case cannot pass by
            // recomputing the deck's own constants back at itself.
            string manageFit = ArgsOf(manage, "FitBlock(description,", ")");
            string deckFit   = ArgsOf(deck,   "FitBlock(purpose,", ")");
            if (manageFit == null || deckFit == null)
                failures.Add("[deck-card-labels] cannot read the card purpose fit call from " +
                             (manageFit == null ? ManageSrc : DeckSrc));
            else if (!string.Equals(manageFit, deckFit, StringComparison.Ordinal))
                failures.Add("[deck-card-labels] card purpose fit floors drifted from the Manage " +
                             "reference: Manage fits '" + manageFit + "', the deck fits '" + deckFit + "'");
            if (deck.IndexOf("(int)ElarionUi.FontMicro, TextAlignmentOptions.Center", StringComparison.Ordinal) < 0)
                failures.Add("[deck-card-labels] the deck card purpose is no longer FontMicro/Centred - " +
                             "Manage authors its card description at (int)ElarionUi.FontMicro centred " +
                             "(ManageScreenPanel.cs:632-634) and the owner ruled Manage is the standard");

            // ---- 6d  the truncation may not come back --------------------------------------
            // "Choose the abilities equipped for bat..." in the capture was TMP inserting U+2026
            // at RENDER time because the purpose label hard-set TextOverflowModes.Ellipsis with
            // wrapping off. Manage sets neither. The one legitimate ellipsis on a card is the
            // fixed-width "[ LOCKED ]" badge, which cannot truncate.
            // Terminator moved with the fitter (see 6c). An anchor that no longer occurs makes
            // Between return null, which this case reports as "cannot find the block" - a case
            // that cannot find its scope must fail loudly, not measure nothing.
            string purposeBlock = Between(deck, "available ? spec.Purpose", "FitBlock(purpose,");
            if (purposeBlock == null)
                failures.Add("[deck-card-labels] cannot find the deck card purpose label block in " + DeckSrc);
            else
            {
                if (purposeBlock.IndexOf("TextOverflowModes.Ellipsis", StringComparison.Ordinal) >= 0)
                    failures.Add("[deck-card-labels] the deck card purpose hard-sets " +
                                 "TextOverflowModes.Ellipsis again. That is what printed 'Choose the " +
                                 "abilities equipped for bat...' on device - and the inserted glyph is " +
                                 "U+2026, which the tofu oracle will not forgive. Manage lets it wrap");
                if (purposeBlock.IndexOf("enableWordWrapping = false", StringComparison.Ordinal) >= 0)
                    failures.Add("[deck-card-labels] the deck card purpose disables word wrapping again; " +
                                 "Manage does not, and a card plate is too narrow to guarantee one line");
            }

            // ---- 6e  ASCII ONLY in the authored Hero copy -----------------------------------
            for (int i = 0; i < hero.Length; i++)
            {
                if (hero[i] <= 126) continue;
                failures.Add("[deck-card-labels] non-ASCII U+" + ((int)hero[i]).ToString("X4") +
                             " in the Hero deck block - player-facing copy is ASCII-only (no em dash, " +
                             "ellipsis glyph or smart quotes) or the tofu oracle fails on it");
                break;
            }

            notes.Add("Hero deck: 5 text-free cards, one live producer per label, format read from Manage");
        }

        // =====================================================================
        // CASE 7  [deck-card-packaging]  A DECK CARD MAY NEVER DRAW ITS OWN
        //         PACKAGING MARGIN (owner F8 2026-09-03, "the journey raids
        //         button is wrong").
        // ---------------------------------------------------------------------
        // WHAT WAS CAPTURED: the owner's phone photo of the JOURNEY panel. The
        // RAIDS card carried a pale near-white FILLED band right around its ornate
        // frame, spilling to the cell edge, while the QUESTS card beside it - the
        // same art family, the same 1774x887 - was clean. That band is the
        // authoring tool's CHECKERBOARD: cards/raids.png was flattened onto it
        // instead of being exported with alpha, so its border pixels are OPAQUE.
        //
        // WHY THE EXISTING FIX DID NOT COVER IT: WO-1311 derives each card's
        // packaging margin from the sprite's alpha-built TIGHT MESH. That can only
        // see a margin that is TRANSPARENT. For raids.png the mesh honestly reports
        // "no margin at all", the card renders 1:1, and the checkerboard is drawn.
        // Every one of that ticket's own pins stayed green through the defect.
        //
        // HOW THIS CASE IS HONEST - IT OPENS THE PNG. It does not read a constant
        // back at itself and it does not trust the fit code's arithmetic. For every
        // card art key the deck actually mounts it decodes the file, finds the
        // alpha bounds and the INK bounds, and asks the one question the source
        // cannot answer: does this image have a pale border that alpha cannot see?
        //   * a transparent margin  -> the alpha route owns it, no table row wanted
        //   * an opaque pale margin -> PlayerDeckWorkspace.OpaqueMargins MUST carry
        //                              a row for it, with the measured numbers
        //   * a row for a card that no longer has one -> the art was re-exported
        //                              and the row is now a stale crop; delete it
        // Re-export raids.png properly and this case turns RED until the row goes,
        // which is the only way a hardcoded margin is made self-retiring.
        //
        // PROVEN RED: with the OpaqueMargins row for "raids" removed, this case
        // fails with "card art 'raids' has an OPAQUE packaging margin L49 T63 R48
        // B78 ... and NO row" - it names the exact card in the photo, from pixels.
        private static void Case7_DeckCardPackagingMargin(List<string> failures, List<string> notes)
        {
            string deck = ReadSrc(DeckSrc);
            if (deck == null) { failures.Add("[deck-card-packaging] cannot read " + DeckSrc); return; }

            // The plumbing must still be wired, or every measurement below is moot.
            if (deck.IndexOf("TryOpaqueMargin(key, rect", StringComparison.Ordinal) < 0)
                failures.Add("[deck-card-packaging] " + DeckSrc + " no longer calls TryOpaqueMargin " +
                             "from the fit measurement - an opaque packaging margin is invisible to " +
                             "the alpha route, so with that call gone the RAIDS defect is back");
            if (deck.IndexOf("alphaSawNoMargin && !opaqueMargin", StringComparison.Ordinal) < 0)
                failures.Add("[deck-card-packaging] " + DeckSrc + " no longer guards its 'render 1:1' " +
                             "early-return on !opaqueMargin, so an authored margin is measured and " +
                             "then thrown away");

            if (!Directory.Exists(CardArtDir))
            { failures.Add("[deck-card-packaging] card art directory is gone: " + CardArtDir); return; }

            int checkedCards = 0, corrected = 0;
            foreach (string file in Directory.GetFiles(CardArtDir, "*.png"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                // Only cards the deck actually mounts. An unused PNG's packaging is nobody's bug.
                if (deck.IndexOf("\"" + key + "\"", StringComparison.Ordinal) < 0) continue;
                checkedCards++;

                int w, h, aL, aT, aR, aB, iL, iT, iR, iB;
                if (!MeasureCardPng(file, out w, out h, out aL, out aT, out aR, out aB,
                                    out iL, out iT, out iR, out iB))
                { failures.Add("[deck-card-packaging] cannot decode " + file); continue; }

                bool transparentMargin = aL > 0 || aT > 0 || aR > 0 || aB > 0;
                // A pale border that alpha cannot see. 8px sits well under the ~50px these
                // deliveries carry and well over any single-pixel authoring slop.
                bool opaqueMargin = !transparentMargin && (iL >= 8 || iT >= 8 || iR >= 8 || iB >= 8);
                bool hasRow = HasOpaqueMarginRow(deck, key);

                if (opaqueMargin && !hasRow)
                {
                    failures.Add("[deck-card-packaging] card art '" + key + "' has an OPAQUE packaging " +
                        "margin L" + iL + " T" + iT + " R" + iR + " B" + iB + " (" + w + "x" + h + ") and " +
                        "NO row in PlayerDeckWorkspace.OpaqueMargins. Its border pixels are not " +
                        "transparent, so the WO-1311 alpha route cannot see them and the card draws that " +
                        "pale band around its own frame - the Journey RAIDS defect exactly. Add the row " +
                        "with these measured numbers, or have the PNG re-exported with alpha");
                }
                else if (!opaqueMargin && hasRow)
                {
                    failures.Add("[deck-card-packaging] PlayerDeckWorkspace.OpaqueMargins still carries a " +
                        "row for '" + key + "', but that PNG no longer has an opaque packaging margin " +
                        "(alpha margin L" + aL + " T" + aT + " R" + aR + " B" + aB + "). The art was " +
                        "re-exported; the row is now a hardcoded crop of real artwork. DELETE the row");
                }
                else if (opaqueMargin)
                {
                    corrected++;
                    RequireOpaqueMarginNumbers(failures, deck, key, w, h, iL, iT, iR, iB);
                }
            }

            if (checkedCards == 0)
                failures.Add("[deck-card-packaging] matched NO card art to " + DeckSrc + " - the deck's " +
                             "art keys were renamed and this case is measuring nothing");
            else
                notes.Add("deck card packaging: " + checkedCards + " mounted card PNGs opened, " +
                          corrected + " carry an opaque margin corrected by an authored row");
        }

        /// <summary>Decode a PNG off disk and return its size plus TWO margins, both in pixels off
        /// each edge in IMAGE space (top-left origin): the ALPHA margin (fully transparent border)
        /// and the INK margin (border that is opaque but pale - what the alpha route cannot see).
        /// Reads the file bytes, so the importer's isReadable setting is irrelevant.</summary>
        private static bool MeasureCardPng(string path, out int w, out int h,
                                           out int aL, out int aT, out int aR, out int aB,
                                           out int iL, out int iT, out int iR, out int iB)
        {
            w = h = 0; aL = aT = aR = aB = 0; iL = iT = iR = iB = 0;
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return false;
                w = tex.width; h = tex.height;
                if (w < 8 || h < 8) return false;
                var px = tex.GetPixels32();

                int axMin = w, axMax = -1, ayMin = h, ayMax = -1;   // any non-transparent pixel
                int ixMin = w, ixMax = -1, iyMin = h, iyMax = -1;   // non-transparent AND not pale
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[row + x];
                        if (c.a <= 8) continue;
                        if (x < axMin) axMin = x;
                        if (x > axMax) axMax = x;
                        if (y < ayMin) ayMin = y;
                        if (y > ayMax) ayMax = y;
                        // 170/255 sits above every authored parchment tone in these cards and
                        // below the ~200+ checkerboard - that is what separates ink from packaging.
                        if ((c.r + c.g + c.b) / 3 >= 170) continue;
                        if (x < ixMin) ixMin = x;
                        if (x > ixMax) ixMax = x;
                        if (y < iyMin) iyMin = y;
                        if (y > iyMax) iyMax = y;
                    }
                }
                if (axMax < 0 || ixMax < 0) return false;

                // GetPixels32 is bottom-left origin; report TOP-LEFT origin margins.
                aL = axMin; aR = w - 1 - axMax; aT = h - 1 - ayMax; aB = ayMin;
                iL = ixMin; iR = w - 1 - ixMax; iT = h - 1 - iyMax; iB = iyMin;
                return true;
            }
            catch { return false; }
            finally { if (tex != null) UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>True when PlayerDeckWorkspace.OpaqueMargins declares a row for this art key.
        /// The anchor carries the row's NEXT field on purpose: the card table one screen away
        /// writes 'ArtKey = "raids"', which contains 'Key = "raids"' as a substring and made the
        /// first draft of this case report a row that was not there.</summary>
        private static bool HasOpaqueMarginRow(string deck, string key)
        {
            return deck.IndexOf(RowAnchor(key), StringComparison.Ordinal) >= 0;
        }

        private static string RowAnchor(string key)
        {
            return "Key = \"" + key + "\", Width";
        }

        /// <summary>The authored row must equal what the PNG actually measures - within 2px, which
        /// is authoring slop, not a licence to drift.</summary>
        private static void RequireOpaqueMarginNumbers(List<string> failures, string deck, string key,
                                                       int w, int h, int l, int t, int r, int b)
        {
            int at = deck.IndexOf(RowAnchor(key), StringComparison.Ordinal);
            if (at < 0) return;
            // Bound the row at the NEXT row's constructor rather than at a closing brace: a bare
            // brace literal in this file would unbalance the repo's C# brace-count gate.
            int next = deck.IndexOf("new OpaqueMargin", at, StringComparison.Ordinal);
            int end = next > at ? next : Math.Min(deck.Length, at + 400);
            string row = deck.Substring(at, end - at);
            MarginField(failures, key, row, "Width", w);
            MarginField(failures, key, row, "Height", h);
            MarginField(failures, key, row, "Left", l);
            MarginField(failures, key, row, "Top", t);
            MarginField(failures, key, row, "Right", r);
            MarginField(failures, key, row, "Bottom", b);
        }

        private static void MarginField(List<string> failures, string key, string row,
                                        string name, int measured)
        {
            int at = row.IndexOf(name + " = ", StringComparison.Ordinal);
            if (at < 0)
            { failures.Add("[deck-card-packaging] OpaqueMargins row '" + key + "' has no " + name); return; }
            int i = at + name.Length + 3;
            var digits = new StringBuilder();
            while (i < row.Length && char.IsDigit(row[i])) digits.Append(row[i++]);
            int authored;
            if (digits.Length == 0 || !int.TryParse(digits.ToString(), out authored))
            { failures.Add("[deck-card-packaging] OpaqueMargins row '" + key + "' has an unreadable " + name); return; }
            if (Math.Abs(authored - measured) > 2)
                failures.Add("[deck-card-packaging] OpaqueMargins row '" + key + "' declares " + name + " = " +
                             authored + " but the PNG measures " + measured + ". The authored margin is a " +
                             "claim about that file's pixels - re-measure it, or the card is cropped wrong");
        }

        private const string CardArtDir = "Assets/Resources/UI/ElarionMedieval/cards/";

        /// <summary>Assert the deck's literal for <paramref name="anchor"/> equals the MANAGE
        /// reference's - so the reference file, not this suite, owns the number.</summary>
        private static void Parity(List<string> failures, string what, string deck, string manage,
                                   string anchor, string terminator)
        {
            string want = ArgsOf(manage, anchor, terminator);
            string got  = ArgsOf(deck, anchor, terminator);
            if (want == null) { failures.Add("[deck-card-labels] '" + anchor + "' is gone from the Manage reference " + ManageSrc); return; }
            if (got == null)  { failures.Add("[deck-card-labels] '" + anchor + "' is gone from " + DeckSrc); return; }
            if (!string.Equals(want, got, StringComparison.Ordinal))
                failures.Add("[deck-card-labels] " + what + " drifted from the Manage reference: " +
                             "Manage has '" + anchor + " " + want + "', the deck has '" + got + "'");
        }

        /// <summary>Text between the first <paramref name="anchor"/> and the next
        /// <paramref name="terminator"/>, trimmed. Null when either is absent.</summary>
        private static string ArgsOf(string src, string anchor, string terminator)
        {
            int a = src.IndexOf(anchor, StringComparison.Ordinal);
            if (a < 0) return null;
            a += anchor.Length;
            int b = src.IndexOf(terminator, a, StringComparison.Ordinal);
            if (b < 0) return null;
            return src.Substring(a, b - a).Replace("=", "").Trim();
        }

        private static string Between(string src, string from, string to)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(to, a, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }

        /// <summary>The body of one switch case, up to the next 'case ' / 'default:' label.</summary>
        private static string SliceCase(string src, string label)
        {
            int a = src.IndexOf(label, StringComparison.Ordinal);
            if (a < 0) return null;
            a += label.Length;
            int next = src.IndexOf("case PlayerDeckKind.", a, StringComparison.Ordinal);
            int def  = src.IndexOf("default:", a, StringComparison.Ordinal);
            int end  = next < 0 ? def : def < 0 ? next : Math.Min(next, def);
            return end < 0 ? src.Substring(a) : src.Substring(a, end - a);
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        /// <summary>CanvasScaler ScaleWithScreenSize, reference 1080x1920, MatchWidthOrHeight 0.5 -
        /// the HudAreasHost canvas verbatim. This is what turns a 2670x1200 device frame into the
        /// 2148x965 REFERENCE canvas every fraction above is a fraction OF.</summary>
        private static float ScaleFactor(float screenW, float screenH)
        {
            return Mathf.Pow(screenW / 1080f, 0.5f) * Mathf.Pow(screenH / 1920f, 0.5f);
        }

        /// <summary>Greedy word wrap using the SAME measured advances, so the line count this
        /// suite asserts against is the line count TMP would produce - not a guess at one.</summary>
        private static int WrappedLineCount(string text, float boxW, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string[] words = text.Split(' ');
            int lines = 1;
            string current = "";
            foreach (string word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                string detail;
                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, candidate, fontSize, out detail);
                if (w > boxW && current.Length > 0) { lines++; current = word; }
                else current = candidate;
            }
            return lines;
        }

        private static float LongestOf(string[] lines, float fontSize)
        {
            float max = 0f;
            foreach (string s in lines)
            {
                string detail;
                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, s, fontSize, out detail);
                if (w > max) max = w;
            }
            return max;
        }

        // =====================================================================
        //  CASE 8 - the action bar's five faces (WO-1359)
        // =====================================================================
        // Two defects are made unreachable here, and only one of them is about pixels.
        //
        //   (i)  A FACE DRAWING A BAKED WORD UNDER ITS LIVE LABEL. The dock paints its caption
        //        with live TMP text (SetCaption) - which localises, fits, and is already styled to
        //        the kit. Art that already says the word gives the face two producers for one
        //        string. That is not hypothetical: WO-1341 shipped it on the Hero deck cards and
        //        the device printed every label twice, in two fonts, with two wordings.
        //   (ii) A FACE WEARING ANOTHER FACE'S EMBLEM. The authored sheet's reading order is not
        //        the bar's order (JOURNEY and MANAGE are transposed between them), so a slice
        //        keyed by POSITION swaps two faces - and both still look plausible, so it ships.
        //        The defence is structural: a face's icon key IS its caption, lower-cased.
        //
        // Everything below is a source/data lint because DeNelle.EditorRegression cannot reference
        // DeNelle.HUD (the Case 0 note), and because the art may legitimately not be on disk yet -
        // a missing icon is the EXPECTED state until the owner drops her files in, and must never
        // read as a failure.
        private static void Case8_BarFaceIcons(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add("[bar-face-icons] cannot read " + HudSrc); return; }

            // ---- 8a  the icon key is stable data, separate from translated display copy ------
            if (src.IndexOf("BuildPeacefulDockSlot(int index, string iconKey, string labelKey,",
                            StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] BuildPeacefulDockSlot must receive separate stable icon " +
                             "and localized label keys; deriving art from translated copy breaks icons");
            if (src.IndexOf("string iconKey = (caption ?? string.Empty).ToLowerInvariant();",
                            StringComparison.Ordinal) >= 0)
                failures.Add("[bar-face-icons] translated caption is still used as an icon identity");
            if (src.IndexOf("UiStyle.Icon(iconKey,", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] BuildPeacefulDockSlot no longer resolves its own icon via " +
                             "UiStyle.Icon(iconKey, ...) - the caption has stopped being the icon key");

            if (src.IndexOf("if (authored != null) ElarionUiKit.PresentAuthoredEmblem(slot);",
                            StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] the calm dock no longer hands authored emblems to " +
                             "PresentAuthoredEmblem. Her emblems carry their own ring and four diamond " +
                             "points: inside the kit medallion they get a second ring drawn round them, " +
                             "their points clipped at the round stencil, and a 386x411 emblem squared off");
            if (src.IndexOf("ElarionUiKit.ClampMinTouch(slot.button)", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] the calm dock no longer clamps its faces to the touch " +
                             "floor. Art must never cost the most-tapped surface in the game a tap target");

            // ---- 8b  five faces, right captions, right order, no icon passed positionally ----
            int found = 0;
            for (int i = 0; i < BarFaceIconKeys.Length; i++)
            {
                string want = "BuildPeacefulDockSlot(" + i + ", \"" + BarFaceIconKeys[i] +
                              "\", HudStrings." + BarFaceLabelSymbols[i] + ",";
                int at = src.IndexOf(want, StringComparison.Ordinal);
                if (at < 0)
                {
                    failures.Add("[bar-face-icons] no '" + want + "' in " + HudSrc + " - the calm dock's " +
                                 "face order changed. Slot " + i + " is expected to be " + BarFaceIconKeys[i] +
                                 "; if the bar genuinely re-ordered, re-order BarFaceIconKeys WITH it so the " +
                                 "art keys move too");
                    continue;
                }
                found++;
                int eol = src.IndexOf('\n', at);
                string line = eol < 0 ? src.Substring(at) : src.Substring(at, eol - at);
                if (line.IndexOf("UiStyle.Icon(", StringComparison.Ordinal) >= 0)
                    failures.Add("[bar-face-icons] slot " + i + " (" + BarFaceIconKeys[i] + ") is handed a " +
                                 "UiStyle.Icon(...) at the CALL SITE: '" + line.Trim() + "'. That re-opens the " +
                                 "hand-paired key - the slot must resolve its art from its own caption");
            }
            if (found != BarFaceIconKeys.Length)
                notes.Add("only " + found + " of " + BarFaceIconKeys.Length + " calm-dock faces matched");

            // ---- 8c  the caption is STILL live text -----------------------------------------
            // If this ever stops being true the art becomes the only producer of the word, and
            // then a baked word is not a duplicate - it is the whole label, un-localisable.
            if (src.IndexOf("slot.SetCaption(caption);", StringComparison.Ordinal) < 0 ||
                src.IndexOf("string caption = HudStrings.Get(labelKey);", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] BuildPeacefulDockSlot no longer resolves and paints its label key - " +
                             "the face's word must stay LIVE text; art must never become its only producer");
            if (src.IndexOf("LocalText.Changed += RefreshLocalizedHudCopy;", StringComparison.Ordinal) < 0 ||
                src.IndexOf("LocalText.Changed -= RefreshLocalizedHudCopy", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] calm-dock labels do not repaint on locale changes with teardown");
            if (src.IndexOf("HudStrings.Get(HudStrings.KeyCollectorsTitle)", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] Collectors startup seed bypasses its localized HUD key");

            // ---- 8d  every face has a named row, in BOTH shipped copies of the table ---------
            string res = ReadSrc(ConceptIconsRes);
            string str = ReadSrc(ConceptIconsStr);
            if (res == null) { failures.Add("[bar-face-icons] cannot read " + ConceptIconsRes); return; }
            if (str == null) { failures.Add("[bar-face-icons] cannot read " + ConceptIconsStr); return; }

            // The owner's art and the manifest derived from its alpha both have to BE there. This
            // is the half of the oracle that says "the faces really do come from her sheet" - the
            // rest only says they come from the RIGHT part of it.
            if (!File.Exists(EmblemSheetPng))
            {
                failures.Add("[bar-face-icons] the emblem sheet " + EmblemSheetPng + " is gone. Every " +
                             "face would fall back to a pack icon - which is a silent regression, because " +
                             "the bar still renders and nothing errors");
                return;
            }
            string sheetManifest = ReadSrc(EmblemSheetJson);
            if (sheetManifest == null)
            {
                failures.Add("[bar-face-icons] no slice manifest at " + EmblemSheetJson + ". The sheet is " +
                             "one image; without the derived rects nothing knows where a face ends. Run " +
                             "Elarion/UI/Re-slice Action Bar Emblems");
                return;
            }
            if (sheetManifest.IndexOf("\"" + EmblemSheetRes + "\"", StringComparison.Ordinal) < 0)
                failures.Add("[bar-face-icons] " + EmblemSheetJson + " does not name the sheet '" +
                             EmblemSheetRes + "' - the manifest and the art have drifted apart");

            for (int i = 0; i < BarFaceIconKeys.Length; i++)
            {
                string key = BarFaceIconKeys[i];
                string blockRes = JsonBlock(res, key);
                string blockStr = JsonBlock(str, key);
                if (blockRes == null)
                {
                    failures.Add("[bar-face-icons] concept-icons.json has no '" + key + "' row, so the " +
                                 BarFaceIconKeys[i] + " face has nowhere to name its authored art");
                    continue;
                }
                if (blockStr == null)
                {
                    failures.Add("[bar-face-icons] '" + key + "' is in the Resources copy of concept-icons.json " +
                                 "but not the StreamingAssets copy - the two ship apart and would disagree");
                    continue;
                }
                string pathRes = JsonField(blockRes, "path");
                string pathStr = JsonField(blockStr, "path");
                if (string.IsNullOrEmpty(pathRes))
                {
                    failures.Add("[bar-face-icons] concept row '" + key + "' names no \"path\" - that field IS " +
                                 "the drop-in seam; without it adopting new art needs a code edit");
                    continue;
                }
                if (!string.Equals(pathRes, pathStr, StringComparison.Ordinal))
                    failures.Add("[bar-face-icons] concept row '" + key + "' points at '" + pathRes +
                                 "' in Resources but '" + pathStr + "' in StreamingAssets");

                string expect = EmblemSheetRes + "#" + key;
                if (!string.Equals(pathRes, expect, StringComparison.Ordinal))
                    failures.Add("[bar-face-icons] concept row '" + key + "' points at '" + pathRes +
                                 "', expected '" + expect + "'. The NAME after the '#' is the mapping - a face " +
                                 "addressed by anything positional can be handed another face's emblem the day " +
                                 "the sheet is re-ordered, and both faces still look plausible");

                // ---- 8e  the baked-word denylist ------------------------------------------
                for (int b = 0; b < BakedWordFaceArt.Length; b++)
                    if (pathRes.IndexOf(BakedWordFaceArt[b], StringComparison.OrdinalIgnoreCase) >= 0)
                        failures.Add("[bar-face-icons] the " + BarFaceIconKeys[i] + " face points at '" +
                                     pathRes + "', which has the word PAINTED INTO THE PNG. The dock already " +
                                     "draws that word as live text, so the face would print it twice in two " +
                                     "fonts - the WO-1341 defect exactly. Use text-free emblem art");

                // ---- 8f  that named slice actually EXISTS in the derived manifest ----------
                string sliceBlock = JsonBlock(sheetManifest, key);
                if (sliceBlock == null)
                {
                    failures.Add("[bar-face-icons] the slice manifest " + EmblemSheetJson + " has no face " +
                                 "named '" + key + "', so the " + BarFaceIconKeys[i] + " face would resolve " +
                                 "NOTHING from the owner's sheet and silently keep a pack icon. Re-run " +
                                 "Elarion/UI/Re-slice Action Bar Emblems");
                    continue;
                }
                float rx = JsonNumber(sliceBlock, "x"), ry = JsonNumber(sliceBlock, "y");
                float rw = JsonNumber(sliceBlock, "width"), rh = JsonNumber(sliceBlock, "height");
                if (rw <= 0f || rh <= 0f || rx < 0f || ry < 0f || rx + rw > 1.001f || ry + rh > 1.001f)
                    failures.Add("[bar-face-icons] face '" + key + "' has rect x=" + rx + " y=" + ry +
                                 " w=" + rw + " h=" + rh + " in " + EmblemSheetJson + ". Rects are NORMALIZED " +
                                 "0..1 (that is what makes them survive a maxTextureSize downscale) - this one " +
                                 "is outside the texture, so the slice would be empty or clipped");
            }

            // ---- 8g  no word-bearing art is wired to anything --------------------------------
            for (int b = 0; b < BakedWordFaceArt.Length; b++)
            {
                string art = ActionBarArtDir + BakedWordFaceArt[b] + ".png";
                if (!File.Exists(art))
                {
                    notes.Add("baked-word art '" + BakedWordFaceArt[b] + "' is gone from disk - if it was " +
                              "re-authored text-free, drop it from BakedWordFaceArt");
                    continue;
                }
                if (res.IndexOf(BakedWordFaceArt[b], StringComparison.OrdinalIgnoreCase) >= 0 ||
                    str.IndexOf(BakedWordFaceArt[b], StringComparison.OrdinalIgnoreCase) >= 0 ||
                    src.IndexOf(BakedWordFaceArt[b], StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[bar-face-icons] '" + BakedWordFaceArt[b] + "' is referenced by the HUD or " +
                                 "the concept table. It carries five engraved NAME PLATES; it is superseded " +
                                 "art kept only as a record and must be wired to nothing");
            }
        }

        /// <summary>The brace-balanced object body for a <c>"key": { ... }</c> row in a
        /// pretty-printed canonical JSON file, or null when the key is absent. Deliberately not a
        /// JSON parser: this suite must not gain a dependency to read two fields.</summary>
        // The two brace characters live in these consts, never as char literals in the body:
        // the repo's C# quality gate counts raw braces per file to catch a truncated write, and a
        // lone open-brace char literal in a comparison reads to it as an unclosed block.
        private const string BraceOpen = "{";
        private const string BraceClose = "}";

        private static string JsonBlock(string src, string key)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int at = src.IndexOf("\"" + key + "\":", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int open = src.IndexOf(BraceOpen, at, StringComparison.Ordinal);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == BraceOpen[0]) depth++;
                else if (src[i] == BraceClose[0])
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        /// <summary>The numeric value of <paramref name="field"/> inside a JSON object body, or
        /// -1 when it is absent or unreadable (which every caller treats as out of range).</summary>
        private static float JsonNumber(string block, string field)
        {
            if (string.IsNullOrEmpty(block)) return -1f;
            int at = block.IndexOf("\"" + field + "\":", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1f;
            int i = at + field.Length + 3;
            var num = new StringBuilder();
            while (i < block.Length && (block[i] == ' ' || block[i] == '\t')) i++;
            while (i < block.Length &&
                   (char.IsDigit(block[i]) || block[i] == '.' || block[i] == '-' ||
                    block[i] == 'e' || block[i] == 'E' || block[i] == '+'))
                num.Append(block[i++]);
            float v;
            return float.TryParse(num.ToString(), System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out v) ? v : -1f;
        }

        /// <summary>The string value of <paramref name="field"/> inside a JSON object body, or
        /// null when the field is absent.</summary>
        private static string JsonField(string block, string field)
        {
            if (string.IsNullOrEmpty(block)) return null;
            int at = block.IndexOf("\"" + field + "\":", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int q1 = block.IndexOf('"', at + field.Length + 3);
            if (q1 < 0) return null;
            int q2 = block.IndexOf('"', q1 + 1);
            return q2 < 0 ? null : block.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static void RequireLiteral(List<string> failures, string src, string literal, string why)
        {
            if (src.IndexOf(literal, StringComparison.Ordinal) < 0)
                failures.Add("[boxes-pinned] " + HudSrc + " no longer contains '" + literal + "' (" + why +
                             "). This suite measures against that number - re-measure the labels and update " +
                             "this pin together, or the oracle is asserting against a box that is gone");
        }

        private static string ReadSrc(string relPath)
        {
            try { return File.Exists(relPath) ? File.ReadAllText(relPath) : null; }
            catch { return null; }
        }

        /// <summary>Flat string->string read of a canonical locale file,
        /// without pulling a JSON dependency into this suite: the canonical copies are one
        /// "key": "value" pair per line.</summary>
        private static Dictionary<string, string> ReadCanon(string path, List<string> failures)
        {
            string raw = ReadSrc(path);
            if (raw == null) { failures.Add("[canon-parity] cannot read " + path); return null; }
            var map = new Dictionary<string, string>();
            foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length < 5 || t[0] != '"') continue;
                int keyEnd = t.IndexOf('"', 1);
                if (keyEnd <= 1) continue;
                int colon = t.IndexOf(':', keyEnd);
                if (colon < 0) continue;
                string rest = t.Substring(colon + 1).Trim();
                if (rest.Length < 2 || rest[0] != '"') continue;
                int valEnd = rest.LastIndexOf('"');
                if (valEnd <= 0) continue;
                map[t.Substring(1, keyEnd - 1)] = Unescape(rest.Substring(1, valEnd - 1));
            }
            return map;
        }

        // =====================================================================
        // CASE 9  [raids-locked-face]  THE LOCKED JOURNEY RAIDS CARD MOUNTS THE
        //         OWNER'S LOCKED ART, AND THE WORDS ON IT STAY LIVE.
        // ---------------------------------------------------------------------
        // WO-1357 shipped the locked state; the owner then supplied the face for
        // it (cards/raids-locked.png, 2026-09-03): a war camp gone dark behind a
        // stone-and-steel padlock, with the right ~45% left as an EMPTY plate.
        //
        // THE PLATE IS EMPTY ON PURPOSE AND THAT IS THE WHOLE POINT OF THIS CASE.
        // The reason this card is shut is DYNAMIC - "Build a Barracks to raid" /
        // "Rebuild your lost Barracks to raid" / "Raids are turned off in this
        // build" - so it can only be live TMP text. Two of her earlier exports
        // carried a generic line baked into exactly that plate; she was offered
        // the choice and chose the wordless re-generate. Bake a line back in and
        // this is WO-1341 again: one string, two producers, in two fonts, saying
        // two different things - the defect that printed every Hero deck label
        // twice on device build 2026.09.03.353742.
        //
        // HOW IT IS HONEST: 9b and 9c OPEN THE PNG. Case 6 can only lint source
        // (it bans art keys it already knows are bad); this one measures the
        // delivered pixels, so a face re-delivered WITH words fails without
        // anybody having to remember to add its key to a list. 9c then asks the
        // question the source cannot answer at all - this plate is near-black, so
        // does the live text still READ on it - as a WCAG contrast ratio.
        //
        // PROVEN RED: with 'LockedArtKey = "raids-locked"' changed to ArtKey's
        // own value (i.e. the locked state falling back to the unlocked camp),
        // 9a fails with "the Journey RAIDS card declares no LockedArtKey".
        private const string RaidsLockedKey = "raids-locked";
        /// <summary>The illustrated card's text plate, copied from
        /// PlayerDeckWorkspace.TextPlateX0(true) and the title/purpose anchors around it.
        /// <para>⚠ PlateY0 RE-POINTED 0.20 -> 0.06 (WO-1636, 2026-09-10), AND THE OLD VALUE IS
        /// NAMED SO IT IS NOT PUT BACK. It was the purpose band's old 0.26 bottom minus a 0.06
        /// margin. That band is no longer a share of the card at all: PlayerDeckWorkspace hangs
        /// it in reference px off PurposeTopFrac - read PurposeBandPx / PurposeTwoLineReqPx
        /// THERE, never a copy of them here - because 0.26 of this card resolved to 45-53 px
        /// against a two-line requirement the font asset puts at 68.0, which is the sixteen
        /// DeckCardPurpose_* truncations the glyph oracle recorded. Leaving PlateY0 at 0.20
        /// would have left 9c checking contrast where the live copy no longer lands, which reads
        /// GREEN while covering nothing.</para>
        /// <para>⛔ THESE FRACTIONS ARE PNG FRACTIONS, AND THE LABEL'S ARE BUTTON FRACTIONS - the
        /// two frames are NOT the same. PlayerDeckWorkspace.MeasureArtFit seats a sprite's OPAQUE
        /// region onto the button, so PNG y p renders at button y (p - fy0)/(fy1 - fy0). This
        /// case happens to be safe because raids-locked.png's alpha bbox is (3,7)-(1410,736) of
        /// 1416x742, i.e. fy0..fy1 = 0.008..0.991 and the two frames coincide to within 1%. A
        /// face with a real packaging margin would NOT, and this assumption predates WO-1636 - it
        /// is recorded here, not fixed here.</para>
        /// <para>⭐ RE-MEASURED WO-1642 (2026-09-10), because raids.png was given a real alpha
        /// channel that day: this sentence used to quote its crop as 0.088..0.929 and it now
        /// measures <b>~0.086..0.930</b> (alpha bbox (49,62)-(1725,810) of 1774x887, threshold
        /// a &gt; 8, same rule as MeasureCardPng below).
        /// ⛔ THE RE-EXPORT DOES NOT MAKE THAT CROP THE IDENTITY, and any note claiming it does is
        /// wrong: PlayerDeckWorkspace.MeasureArtFit returns Corrected = true for ANY margin it can
        /// see, and only the alphaSawNoMargin branch renders 1:1. quests.png (alpha margin
        /// L47 T62 R47 B74, spans 0.947 x 0.847) has always taken that same corrected route - so
        /// the two cards were never at different aspects, and the white corner patches were the
        /// ONLY rendered difference between them.</para>
        /// <para>MEASURED BEFORE IT WAS CHANGED (replica of MeasurePlate/Contrast run over the
        /// PNG bytes, 2026-09-10): raids-locked.png over the enlarged 0.06-0.86 plate reads ink
        /// 0.0014 (ceiling 0.006) and 9.10 / 10.25 / 15.98 : 1 for Gold / ParchmentDim /
        /// Parchment - versus 0.0017 and 9.05 / 10.21 / 15.91 at the old 0.20. The replica
        /// reproduces this suite's own documented 0.0017 baseline exactly, and the enlarged
        /// region is if anything CLEANER, so this re-point does not buy its coverage with a
        /// relaxed assert.</para></summary>
        private const float PlateX0 = 0.49f, PlateX1 = 0.96f, PlateY0 = 0.06f, PlateY1 = 0.86f;
        /// <summary>Fraction of plate pixels allowed to be light enough to be a glyph.
        /// <para>CALIBRATED, not guessed. The delivered face measures 0.0017 - the padlock rim
        /// clipping the plate's left edge, and nothing else. Baking one parchment-toned line
        /// across the same plate measures 0.0077 at 28px, 0.0139 at 40px and 0.0278 at 72px on
        /// this 1416px-wide card, and a title+subtitle pair measures 0.0255. 0.006 sits 3.5x
        /// above the clean face and below the SMALLEST line anyone would author, so it catches a
        /// re-delivery with words on it without tripping on the art that is there.</para></summary>
        private const float PlateInkCeiling = 0.006f;
        /// <summary>WCAG AA for large text. The title is 36px and the reason is FontMicro, so
        /// this is the floor for BOTH, deliberately not the 3.0 large-text relaxation.</summary>
        private const float ContrastFloor = 4.5f;

        private static void Case9_RaidsLockedFace(List<string> failures, List<string> notes)
        {
            string deck = ReadSrc(DeckSrc);
            if (deck == null) { failures.Add("[raids-locked-face] cannot read " + DeckSrc); return; }

            // ---- 9a  the wiring: locked face selected, unlocked face untouched --------------
            if (deck.IndexOf("LockedArtKey = \"" + RaidsLockedKey + "\"", StringComparison.Ordinal) < 0)
                failures.Add("[raids-locked-face] the Journey RAIDS card declares no LockedArtKey = \"" +
                             RaidsLockedKey + "\" in " + DeckSrc + " - the owner's locked face is not " +
                             "mounted and the card falls back to the inviting camp behind a [ LOCKED ] badge");
            if (deck.IndexOf("ArtKey = \"raids\"", StringComparison.Ordinal) < 0)
                failures.Add("[raids-locked-face] the Journey RAIDS card lost 'ArtKey = \"raids\"' - the " +
                             "UNLOCKED face is the path that already works and this ticket may not touch it");
            if (deck.IndexOf("!available && !string.IsNullOrEmpty(spec.LockedArtKey)", StringComparison.Ordinal) < 0)
                failures.Add("[raids-locked-face] " + DeckSrc + " no longer gates the locked face on " +
                             "!available, so a locked-state PNG can reach an UNLOCKED card");
            if (deck.IndexOf("(available || authoredLockFace) ? Color.white", StringComparison.Ordinal) < 0)
                failures.Add("[raids-locked-face] the authored locked face is being gray-washed again by " +
                             "the IllustratedCardSurface tint. That art is ALREADY the darkened locked " +
                             "scene; washing it costs the live text its contrast, and locked-ness is " +
                             "carried by the padlock, the word badge and the remedy line - never by hue " +
                             "(the owner is red/green colourblind)");
            if (deck.IndexOf("colors.disabledColor = authoredLockFace", StringComparison.Ordinal) < 0)
                failures.Add("[raids-locked-face] colors.disabledColor no longer spares the authored " +
                             "locked face. button.interactable is false on a locked card, so the " +
                             "Selectable multiplies the art by disabledColor - that is the SECOND wash " +
                             "and it undoes the first one being dropped");

            // ---- 9b/9c  the delivered pixels: empty plate, and text that reads on it --------
            string png = CardArtDir + RaidsLockedKey + ".png";
            if (!File.Exists(png))
            { failures.Add("[raids-locked-face] the owner's locked face is missing from disk: " + png); return; }
            if (!File.Exists(png + ".meta"))
                failures.Add("[raids-locked-face] " + png + " has no .meta, so Unity will import it with " +
                             "DEFAULT settings - not a Sprite, no tight mesh - and Resources.Load<Sprite> " +
                             "returns null at runtime. Clone quests.png.meta (textureType 8, spriteMode 1, " +
                             "spriteMeshType 1, alphaIsTransparency 1) with a fresh guid");

            float ink, lum;
            if (!MeasurePlate(png, out ink, out lum))
            { failures.Add("[raids-locked-face] cannot decode " + png); return; }

            if (ink > PlateInkCeiling)
                failures.Add("[raids-locked-face] the text plate of " + RaidsLockedKey + ".png is " +
                             (ink * 100f).ToString("F1") + "% light pixels (ceiling " +
                             (PlateInkCeiling * 100f).ToString("F1") + "%) - that plate is supposed to be " +
                             "EMPTY. Words baked there collide with the LIVE title and lock reason " +
                             "PlayerDeckWorkspace draws over it, which is the WO-1341 double-label defect. " +
                             "Re-generate the art without the words; the reason line is dynamic and can " +
                             "never be baked");

            Contrast(failures, "card title", lum, 0.831f, 0.686f, 0.216f);      // ElarionUi.Gold
            Contrast(failures, "lock reason", lum, 0.78f, 0.74f, 0.66f);        // ElarionUi.ParchmentDim
            Contrast(failures, "locked badge", lum, 0.953f, 0.918f, 0.827f);    // ElarionUi.Parchment

            notes.Add("raids locked face: plate " + (ink * 100f).ToString("F2") + "% ink, mean luminance " +
                      lum.ToString("F4") + " - live title and reason render over authored art");
        }

        // =====================================================================
        // CASE 10  [heartfire-inside-plate]  THE HEARTFIRE ROW SITS INSIDE THE HEART
        //          PLATE, AT THE PLATE'S NAME SIZE, ON ITS OWN ROW.        (WO-1384, 2026-09-04)
        // ---------------------------------------------------------------------
        // Owner felt-test (Seeker, build 355905): "there is something under the Heart of
        // Elarion, but i cannot read it its too small on screen". The capture showed
        // "[*] [*] [*]  Heartfire" drawn ACROSS the plate's bottom edge at the smallest size on
        // the plate. Cause: two lines forced into a 0.04..0.32 band of an 83-unit plate.
        //
        // DeNelle.EditorRegression cannot reference DeNelle.HUD, so this is the suite's honest
        // shape: the bands and font floors are read as LITERALS out of HudKitController.cs
        // (the same constants the code lays out with), the plate height comes from the REAL
        // HudLayoutBands.HeartMount in Core at both aspects, and the marks row's WIDTH is
        // measured from real glyph advances. What it pins:
        //   10a  every Heart row band lies inside the plate's visible frame (y 0.06..0.97) and
        //        no two rows overlap - the row cannot straddle the plate edge again;
        //   10b  the Heartfire row is fitted as ONE line (FitSingleLine, never FitBlock) with a
        //        floor >= the objective line's ceiling - it cannot be the plate's smallest text;
        //   10c  at both aspects every row's band seats its floor line (floor x 1.2), so the
        //        post-layout guard has no reason to relax a font below its floor;
        //   10d  three fixed-size flame Images fit left of the unchanged PlateLabel, and the
        //        PlateLabel measures inside its own right-hand band at the floor.
        //        (!) WO-1415 WIDENED THAT ROW. The owner ruled the plate must name what a
        //        charge buys - HeartfireCharges.PlateLabel, "Heartfire 3/3 (raids)" - so the
        //        measured string is now the PlateLabel form, 12 characters longer than the
        //        bare Name it used to be. It is composed from the SAME Core methods the View
        //        paints with, so a reworded plate is re-measured here rather than silently
        //        ellipsised on the device.
        // RED, one line each: put HeartfireBandY0/Y1 back to 0.04f/0.32f (10a: overlaps the
        // rekindle band and leaves the frame); set HeartfireFontMin = 16f (10b); or set
        // HudLayoutBands.HeartMount back to y 0.700 (10c: the four rows no longer seat).
        // =====================================================================
        private const float HeartPlateInsetY0 = 0.06f;
        private const float HeartPlateInsetY1 = 0.97f;
        /// <summary>BuildHeartStatus places the plate at y 0.02..0.98 of the cluster root.</summary>
        private const float HeartPlateOfMount = 0.96f;

        private static void Case10_HeartfireInsidePlate(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add("[heartfire-inside-plate] cannot read " + HudSrc); return; }

            string[] rows = { "HeartName", "HeartObjective", "Heartfire", "HeartfireRekindle" };
            var y0 = new float[rows.Length];
            var y1 = new float[rows.Length];
            bool parsed = true;
            for (int i = 0; i < rows.Length; i++)
            {
                parsed &= TryFloatConst(src, rows[i] + "BandY0", out y0[i]);
                parsed &= TryFloatConst(src, rows[i] + "BandY1", out y1[i]);
            }
            float fireMin, fireMax, objMin, objMax, nameMin;
            float iconPx, iconGapPx, flameX1, labelX0;
            parsed &= TryFloatConst(src, "HeartfireFontMin", out fireMin);
            parsed &= TryFloatConst(src, "HeartfireFontMax", out fireMax);
            parsed &= TryFloatConst(src, "HeartObjectiveFontMin", out objMin);
            parsed &= TryFloatConst(src, "HeartObjectiveFontMax", out objMax);
            parsed &= TryFloatConst(src, "HeartNameFontMin", out nameMin);
            parsed &= TryFloatConst(src, "HeartfireFlameIconPx", out iconPx);
            parsed &= TryFloatConst(src, "HeartfireFlameGapPx", out iconGapPx);
            parsed &= TryFloatConst(src, "HeartfireFlameX1", out flameX1);
            parsed &= TryFloatConst(src, "HeartfireLabelX0", out labelX0);
            if (!parsed)
            {
                failures.Add("[heartfire-inside-plate] " + HudSrc + " no longer declares the Heart plate row " +
                             "constants (Heart{Name,Objective}BandY0/Y1, Heartfire{,Rekindle}BandY0/Y1, " +
                             "HeartfireFontMin/Max, HeartObjectiveFontMin/Max, HeartNameFontMin, " +
                             "HeartfireFlameIconPx/GapPx/X1 and HeartfireLabelX0) as float literals - this pin reads " +
                             "the icon/label composition off them");
                return;
            }

            // 10a - inside the frame, and disjoint.
            for (int i = 0; i < rows.Length; i++)
            {
                if (y0[i] < HeartPlateInsetY0 || y1[i] > HeartPlateInsetY1 || y0[i] >= y1[i])
                    failures.Add("[heartfire-inside-plate] the " + rows[i] + " row band " + y0[i].ToString("0.00") +
                                 ".." + y1[i].ToString("0.00") + " leaves the Heart plate's visible frame (" +
                                 HeartPlateInsetY0 + ".." + HeartPlateInsetY1 + " of _heartPlate.Root) - that " +
                                 "is the captured row straddling the plate edge");
                for (int j = i + 1; j < rows.Length; j++)
                    if (y0[i] < y1[j] && y0[j] < y1[i])
                        failures.Add("[heartfire-inside-plate] the " + rows[i] + " and " + rows[j] +
                                     " rows overlap (" + y0[i] + ".." + y1[i] + " vs " + y0[j] + ".." + y1[j] +
                                     ") - two rows in one band is how the marks became unreadable");
            }

            // 10b - one line, at the name size, never the plate's smallest text.
            RequirePin(failures, "[heartfire-inside-plate]", src,
                "FitSingleLine(_heartfireLabel, HeartfireFontMin, HeartfireFontMax)",
                "the Heartfire row must be fitted as ONE line at its own floor; FitBlock with two lines " +
                "in a one-line band is the exact defect");
            if (src.IndexOf("FitBlock(_heartfireLabel", StringComparison.Ordinal) >= 0)
                failures.Add("[heartfire-inside-plate] _heartfireLabel is FitBlock'd again - a wrapped block " +
                             "in a single row shrinks to whatever seats two lines");
            RequirePin(failures, "[heartfire-inside-plate]", src,
                "_heartfireLabel = ElarionUiKit.Label(_heartPlate.Root.transform,",
                "the Heartfire row must be a child of _heartPlate.Root so its band is a fraction of the plate");
            RequirePin(failures, "[heartfire-inside-plate]", src,
                "_heartfireRekindleLabel = ElarionUiKit.Label(_heartPlate.Root.transform,",
                "the rekindle line must have its OWN row on the plate, not ride as line two of the marks row");
            if (fireMin < objMax)
                failures.Add("[heartfire-inside-plate] HeartfireFontMin " + fireMin + " is under the objective " +
                             "line's ceiling " + objMax + " - the owner's ruling is the marks row reads at the " +
                             "plate's NAME size (20..26), never smaller than the objective");
            if (fireMin < nameMin || fireMin < ElarionUiKit.FontHardFloor)
                failures.Add("[heartfire-inside-plate] HeartfireFontMin " + fireMin + " is under the name floor " +
                             nameMin + " / the kit hard floor " + ElarionUiKit.FontHardFloor);
            if (fireMax < fireMin)
                failures.Add("[heartfire-inside-plate] HeartfireFontMax " + fireMax + " < HeartfireFontMin " + fireMin);

            // 10c - every row seats its floor line at both aspects (the real HeartMount, from Core).
            // The EFFECTIVE floor is the kit's: FitSingleLine clamps min up to FontHardFloor and
            // then down to max, so an authored 16..18 resolves to a fixed 18.
            float objFloor = Math.Min(Math.Max(objMin, ElarionUiKit.FontHardFloor), objMax);
            float fireFloor = Math.Min(Math.Max(fireMin, ElarionUiKit.FontHardFloor), fireMax);
            float[] floors = { nameMin, objFloor, fireFloor, objFloor };
            foreach (var a in Aspects)
            {
                var refSize = HudLayoutBands.CanvasReferenceSize(a.W, a.H);
                float plateH = HudLayoutBands.HeartMount.height * refSize.y * HeartPlateOfMount;
                for (int i = 0; i < rows.Length; i++)
                {
                    float bandH = (y1[i] - y0[i]) * plateH;
                    float need = floors[i] * LineHeightFactor;
                    if (bandH < need)
                        failures.Add("[heartfire-inside-plate] at " + a.Name + " the " + rows[i] + " row is " +
                                     bandH.ToString("0.0") + " ref px tall but its " + floors[i] + "px floor line " +
                                     "needs " + need.ToString("0.0") + " - the fit guard would relax the font " +
                                     "under its floor (the captured 'too small'). Grow HudLayoutBands.HeartMount, " +
                                     "never the font down");
                }
                notes.Add("Heart plate " + plateH.ToString("0") + " ref px at " + a.Name);
            }

            // 10d - WO-1419 splits the row into three flame Images plus unchanged PlateLabel.
            string marks = DeNelle.Core.State.HeartfireCharges.PlateLabel(0, 3);
            float rowX0, rowX1;
            if (!TryFloatConst(src, "HeartRowX0", out rowX0)) rowX0 = 0.05f;
            if (!TryFloatConst(src, "HeartRowX1", out rowX1)) rowX1 = 0.95f;
            string detail;
            float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, marks, fireMin, out detail);
            if (w < 0f) notes.Add("marks row not measurable headlessly: " + detail);
            else
            {
                foreach (var a in Aspects)
                {
                    var refSize = HudLayoutBands.CanvasReferenceSize(a.W, a.H);
                    float plateW = HudLayoutBands.HeartMount.width * refSize.x * 0.97f;
                    float labelW = (rowX1 - labelX0) * plateW;
                    int slots = DeNelle.Core.State.HeartfireCharges.FlameStates(0, 3).Length;
                    float flamesW = slots * iconPx + Math.Max(0, slots - 1) * iconGapPx;
                    float flameBandW = (flameX1 - rowX0) * plateW;
                    if (flamesW > flameBandW)
                        failures.Add("[heartfire-inside-plate] at " + a.Name + " the " + slots + " flame Images need " +
                                     flamesW.ToString("0.0") + " ref px but their band is " +
                                     flameBandW.ToString("0.0") + " px wide");
                    if (w > labelW)
                        failures.Add("[heartfire-inside-plate] at " + a.Name + " the PlateLabel '" + marks +
                                     "' MEASURES " + w.ToString("0.0") + " ref px at its " + fireMin + "px floor " +
                                     "but its label band is " + labelW.ToString("0.0") +
                                     " px wide (" + detail + ") - it would ellipsise");
                }
                notes.Add("Heartfire PlateLabel '" + marks + "' " + w.ToString("0.0") +
                          " px beside " + DeNelle.Core.State.HeartfireCharges.FlameStates(0, 3).Length + " icons");
            }
        }

        // =====================================================================
        // CASE 11  [night-market-standout]  THE NIGHT MARKET CARD IS THE LARGEST,
        //          FRAMED, LIT CONTROL IN THE LEFT COLUMN, WITH ITS WHOLE WORD.
        //                                                          (WO-1384, 2026-09-04)
        // ---------------------------------------------------------------------
        // Owner: "night market ... needs to be the shining gem, it should draw attention to it
        // so it above all stands out". Standout by SIZE, FRAME and LIGHT - never hue alone (the
        // owner is red/green colourblind). The card's band is REAL (HudLayoutBands, Core); the
        // frame and aura are source pins; the word is measured.
        //   11a  the card's resolved area exceeds every other left-column band that is drawn
        //        (gear) and the FLAG capture chip (its size parsed from FlagCaptureButton.cs);
        //   11b  HudKitController mounts the soft ring and the kit's RadialGlowSprite aura
        //        (WO-1384b: the ring REPLACED the flat gold frame - Case 12 pins the shape);
        //   11c  the canon storeWordmark (the word the card renders since WO-1398; it was the
        //        literal "NIGHT MARKET") measures inside the label plate at the 20 px hard floor.
        // RED, one line each: HudLayoutBands.NightMarketCardWidthPx = 112f (11a); delete the
        // "NightMarketCardRing" AddImage (11b); NightMarketLabelPlateX0 = 0.60f (11c).
        // =====================================================================
        private const string FlagSrc = "Assets/_Modules/Core/Dev/FlagCaptureButton.cs";

        private static void Case11_NightMarketStandout(List<string> failures, List<string> notes)
        {
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add("[night-market-standout] cannot read " + HudSrc); return; }

            // 11a - size, from the real table at both aspects.
            float cardArea = HudLayoutBands.NightMarketCardWidthPx * HudLayoutBands.NightMarketCardHeightPx;
            float gearArea = HudLayoutBands.DockControlPx * HudLayoutBands.DockControlPx;
            if (cardArea <= gearArea)
                failures.Add("[night-market-standout] the Night Market card (" + HudLayoutBands.NightMarketCardWidthPx +
                             " x " + HudLayoutBands.NightMarketCardHeightPx + ") is not larger than the gear (" +
                             HudLayoutBands.DockControlPx + " sq) - it is not the column's standout by size");
            string flag = ReadSrc(FlagSrc);
            var flagSize = flag == null ? null
                : System.Text.RegularExpressions.Regex.Match(flag,
                    @"sizeDelta\s*=\s*new\s+Vector2\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*\)");
            if (flagSize != null && flagSize.Success)
            {
                float fw = float.Parse(flagSize.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float fh = float.Parse(flagSize.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (cardArea <= fw * fh)
                    failures.Add("[night-market-standout] the Night Market card is not larger than the FLAG chip (" +
                                 fw + " x " + fh + ") it was captured indistinguishable from");
                notes.Add("card " + cardArea.ToString("0") + " vs gear " + gearArea.ToString("0") + " vs FLAG " +
                          (fw * fh).ToString("0") + " ref px^2");
            }
            else notes.Add("FLAG chip size not parsed from " + FlagSrc + " - compared against the gear only");

            foreach (var a in Aspects)
            {
                var bands = HudLayoutBands.ResolveLeftColumn(a.W, a.H);
                var names = HudLayoutBands.LeftColumnNames;
                Rect card = default; bool haveCard = false;
                for (int i = 0; i < bands.Length && i < names.Length; i++)
                    if (names[i] == "Night Market card") { card = bands[i]; haveCard = true; }
                if (!haveCard) { failures.Add("[night-market-standout] ResolveLeftColumn has no 'Night Market card' band"); break; }
                for (int i = 0; i < bands.Length && i < names.Length; i++)
                {
                    if (names[i] != "gear") continue;   // the one other band that is actually drawn
                    if (card.width * card.height <= bands[i].width * bands[i].height)
                        failures.Add("[night-market-standout] at " + a.Name + " the card's resolved band is not " +
                                     "larger than the gear's");
                }
            }

            // 11b - frame + light, by source pin.
            RequirePin(failures, "[night-market-standout]", src, "\"NightMarketCardRing\"",
                "the card lost its ring - standout by FRAME is one of the three legs (WO-1384b: a soft " +
                "rounded ring, never the flat gold box again)");
            RequirePin(failures, "[night-market-standout]", src, "\"NightMarketCardAura\"",
                "the card lost its aura - standout by LIGHT is one of the three legs");
            RequirePin(failures, "[night-market-standout]", src, "ElarionUiKit.RadialGlowSprite",
                "the aura must be the kit's one bloom primitive, not a second glow texture");
            string cardMethod = Between(src, "private void BuildNightMarketCard(", "private void OpenNightMarket(");
            if (cardMethod == null)
                notes.Add("BuildNightMarketCard..OpenNightMarket slice not found - the ElarionUi.Gold law was not sliced");
            else if (cardMethod.IndexOf("ElarionUi.Gold", StringComparison.Ordinal) < 0)
                failures.Add("[night-market-standout] BuildNightMarketCard no longer uses ElarionUi.Gold - the " +
                             "frame must be the kit gold, the same tone as the card's title");

            // 11c - the whole word, one line, at the hard floor. WO-1398: the word is no longer
            // a literal - it is the canon storeWordmark the card now renders (HudStrings.
            // StoreFaceLabel), so the string MEASURED is the string AUTHORED in canon-strings.
            // The obsidian button sets `canonical.text = label` with no case transform
            // (ElarionUiKitObsidian.CanonicalizeButtonLabels), so the authored casing is what
            // TMP steps the pen by; the upper-case width is reported as a note only.
            float plateX0;
            if (!TryFloatConst(src, "NightMarketLabelPlateX0", out plateX0))
            { failures.Add("[night-market-standout] NightMarketLabelPlateX0 is no longer a float literal in " + HudSrc); return; }
            float plateW = (0.97f - plateX0) * HudLayoutBands.NightMarketCardWidthPx * ButtonLabelInset;
            string word = Copy(failures, notes, HudStrings.KeyStoreWordmark);
            if (string.IsNullOrEmpty(word)) return;
            // ⛔ WO-1466 (2026-09-06) — MEASURE THE GLYPHS THAT ARE DRAWN, NOT THE ONES AUTHORED.
            // This block used to measure the MIXED-CASE storeWordmark and report the upper-case
            // width as a NOTE, on the reasoning quoted above ("no case transform ... the authored
            // casing is what TMP steps the pen by"). The capture disproves it:
            // Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png paints "THE NIGHT MA..." while
            // canon-strings authors "The Night Market" — and the same frame shows every other
            // obsidian face upper-cased too (AddDockTab passes "Music"/"Settings"/"Realm"/"Pause";
            // the HUD paints MUSIC / SETTINGS / REALM / PAUSE). So the oracle was measuring a
            // narrower string than the player sees, which is exactly how a cut caption survived
            // two builds, a felt-test and this very case. UPPER CASE IS NOW THE BINDING
            // MEASUREMENT; the authored casing is kept as the note.
            string dUpper;
            string upper = word.ToUpperInvariant();
            float ww = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, upper,
                                                        ElarionUiKit.FontHardFloor, out dUpper);
            if (ww < 0f) notes.Add("'" + upper + "' not measurable headlessly: " + dUpper);
            else if (ww > plateW)
                failures.Add("[night-market-standout] '" + upper + "' (canon storeWordmark, AS DRAWN - the " +
                             "obsidian faces render upper case) MEASURES " + ww.ToString("0.0") +
                             " ref px at the " + ElarionUiKit.FontHardFloor + "px hard floor but the label plate is " +
                             plateW.ToString("0.0") + " px wide (" + dUpper + ") - that is the captured 'THE NIGHT " +
                             "MA...' shape. The words get shorter or the plate gets wider; the font does not shrink");
            else notes.Add("'" + upper + "' " + ww.ToString("0.0") + " px in a " + plateW.ToString("0.0") + " px plate");
            string d;
            float wa = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, word,
                                                        ElarionUiKit.FontHardFloor, out d);
            if (wa >= 0f) notes.Add("as authored, '" + word + "' would measure " + wa.ToString("0.0") + " px");
        }

        // =====================================================================
        // CASE 12  [night-market-aurora]  THE NIGHT MARKET CARD IS ROUNDED AND WEARS A
        //          CHASING, COLOUR-DRIFTING RIM LIGHT - NOT A FLAT GOLD BOX.
        //                                                         (WO-1384b, 2026-09-05)
        // ---------------------------------------------------------------------
        // Owner, after seeing build 355952: "instead of just dropping a yellow box around the
        // store on the left of UI can we round the edges and have a chasing soft color changing
        // vfx, subtle but inviting?" Headless capture cannot see motion - the device
        // screenrecord is the felt proof, and "[Flow:Store] aurora cost" is the perf proof. What
        // THIS case pins is the SHAPE of the implementation, from source:
        //   12a  the flat "NightMarketCardFrame" AddImage is GONE and the card is rounded: the
        //        BuildNightMarketCard slice applies ElarionUiKit.ApplyRounded at
        //        NightMarketCornerRadiusPx and mounts a Mask so the art is clipped to it;
        //   12b  the ring and the comets exist under the card root ("NightMarketCardRing",
        //        "NightMarketCardComet"), the comets ride the kit's RadialGlowSprite (no second
        //        glow texture, no ParticleSystem anywhere in the slice);
        //   12c  the motion is driven from THIS class's existing Update (AnimateNightMarketGlow
        //        is called inside the Update slice - no second Update owner) and the cost line
        //        "aurora cost" is traced;
        //   12d  the three knobs exist as literals in NightMarketGlowKnobs with sane defaults
        //        (lap 3..8 s, alpha 15..60 %) and the palette default names all three stops;
        //   12e  the label stays ONE FULL LINE: NoWrap + FitSingleLine in the slice, and
        //        the canon storeWordmark (WO-1398) still measures inside the plate at the hard floor.
        // RED, one line each: delete `button.gameObject.AddComponent<Mask>()` (12a); rename
        // "NightMarketCardRing" (12b); delete the `AnimateNightMarketGlow();` call in Update
        // (12c); NightMarketGlowLapSecDefault = 30f (12d); drop `TextWrappingModes.NoWrap` (12e).
        // =====================================================================
        private static void Case12_NightMarketAurora(List<string> failures, List<string> notes)
        {
            const string Tag = "[night-market-aurora]";
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + HudSrc); return; }
            string card = Between(src, "private void BuildNightMarketCard(", "private void OpenNightMarket(");
            if (card == null) { failures.Add(Tag + " BuildNightMarketCard..OpenNightMarket slice not found in " + HudSrc); return; }

            // 12a - rounded, masked, and the flat box gone.
            if (card.IndexOf("\"NightMarketCardFrame\"", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " the flat rectangular \"NightMarketCardFrame\" AddImage is back - the owner " +
                             "retired the yellow box (WO-1384b); the standout is rounding + motion");
            if (card.IndexOf("ElarionUiKit.ApplyRounded(cardImage, NightMarketCornerRadiusPx)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the card's button Image is no longer the kit RoundedSprite at " +
                             "NightMarketCornerRadiusPx - the card lost its rounded corners");
            if (card.IndexOf("AddComponent<Mask>()", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the card no longer mounts a Mask - without it the opaque art paints " +
                             "square over the rounded stencil");
            float radius;
            if (!TryFloatConst(src, "NightMarketCornerRadiusPx", out radius))
                failures.Add(Tag + " NightMarketCornerRadiusPx is no longer a float literal in " + HudSrc);
            else if (radius < 8f || radius > 40f)
                failures.Add(Tag + " NightMarketCornerRadiusPx = " + radius + " is outside 8..40 - under 8 reads as " +
                             "square, over 40 eats the art on a 156-unit card");

            // 12b - the ring + comets, on the kit's one bloom sprite, no particles.
            if (card.IndexOf("\"NightMarketCardRing\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " \"NightMarketCardRing\" is not built under the card root");
            if (card.IndexOf("\"NightMarketCardComet\"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the \"NightMarketCardComet\" chase blobs are not built under the card root");
            if (card.IndexOf("cimg.sprite = auraSprite", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the comets no longer ride ElarionUiKit.RadialGlowSprite (auraSprite) - " +
                             "one bloom primitive, never a second glow texture");
            if (card.IndexOf("ParticleSystem", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " a ParticleSystem appeared in BuildNightMarketCard - no particles on the HUD canvas");

            // 12c - driven from the existing Update, cost traced.
            string update = Between(src, "private void Update()", "private void RepaintHeartfire(");
            if (update == null) failures.Add(Tag + " the HudKitController Update() slice was not found");
            else if (update.IndexOf("AnimateNightMarketGlow();", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " Update() no longer calls AnimateNightMarketGlow() - the rim light is static " +
                             "(or a second Update owner was added, which is forbidden)");
            if (src.IndexOf("\"aurora cost \"", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the \"[Flow:Store] aurora cost\" trace line is gone - that line is the perf pin");
            int updateOwners = CountOccurrences(src, "private void Update()");
            if (updateOwners != 1)
                failures.Add(Tag + " HudKitController.cs declares " + updateOwners + " Update() methods - exactly one " +
                             "owner drives every HUD animation");

            // 12d - the knobs, with sane shipping defaults.
            float lap, alphaPct;
            if (!TryFloatConst(src, "NightMarketGlowLapSecDefault", out lap))
                failures.Add(Tag + " NightMarketGlowLapSecDefault is not a float literal (hud.nightMarketGlowLapSec)");
            else if (lap < 3f || lap > 8f)
                failures.Add(Tag + " NightMarketGlowLapSecDefault = " + lap + "s is outside the owner's 'slow, a lap " +
                             "every ~4-6 s' window (3..8)");
            if (!TryFloatConst(src, "NightMarketGlowAlphaPctDefault", out alphaPct))
                failures.Add(Tag + " NightMarketGlowAlphaPctDefault is not a float literal (hud.nightMarketGlowAlphaPct)");
            else if (alphaPct < 15f || alphaPct > 60f)
                failures.Add(Tag + " NightMarketGlowAlphaPctDefault = " + alphaPct + "% is outside 15..60 - 'subtle " +
                             "but inviting', neither invisible nor a beacon");
            if (src.IndexOf("NightMarketGlowPaletteMaskDefault = PaletteGold | PaletteAmber | PaletteRose", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the palette default no longer names gold|amber|rose (hud.nightMarketGlowPaletteMask)");
            foreach (var knob in new[] { "hud.nightMarketGlowLapSec", "hud.nightMarketGlowAlphaPct", "hud.nightMarketGlowPaletteMask" })
                if (src.IndexOf(knob, StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the tunable key '" + knob + "' is no longer named at the knob holder - the rail " +
                                 "lane finds the knobs by that name");

            // 12e - one full line.
            if (card.IndexOf("TextWrappingModes.NoWrap", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the label lost TextWrappingModes.NoWrap - 'NIGHT MARKET' may wrap to two lines");
            if (card.IndexOf("ElarionUiKit.FitSingleLine(face, 20f, 26f)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the label is no longer fitted as ONE line at the 20 px hard floor");
            float plateX0;
            if (TryFloatConst(src, "NightMarketLabelPlateX0", out plateX0))
            {
                // WO-1398: measure the canon storeWordmark the card renders, not a literal.
                // WO-1466: and measure it UPPER CASE - the obsidian faces draw upper case (see
                // the long note in Case 11c). Measuring the authored casing here was the second
                // copy of the same blind spot.
                float plateW = (0.97f - plateX0) * HudLayoutBands.NightMarketCardWidthPx * ButtonLabelInset;
                string word = Copy(failures, notes, HudStrings.KeyStoreWordmark);
                string upper = string.IsNullOrEmpty(word) ? "" : word.ToUpperInvariant();
                string d;
                float ww = string.IsNullOrEmpty(upper) ? -1f
                    : ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, upper, ElarionUiKit.FontHardFloor, out d);
                if (ww >= 0f && ww > plateW)
                    failures.Add(Tag + " '" + upper + "' (canon storeWordmark, as drawn) MEASURES " + ww.ToString("0.0") +
                                 " ref px at the hard floor but the plate is " + plateW.ToString("0.0") +
                                 " px - it would truncate again");
            }
            notes.Add("night market aurora: r=" + radius + " lap=" + lap + "s alpha=" + alphaPct + "% updateOwners=" + updateOwners);
        }

        private static int CountOccurrences(string src, string literal)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(literal, i, StringComparison.Ordinal)) >= 0) { n++; i += literal.Length; }
            return n;
        }

        /// <summary>Read a <c>private const float NAME = 1.23f;</c> literal out of a source file.</summary>
        private static bool TryFloatConst(string src, string name, out float value)
        {
            value = 0f;
            var m = System.Text.RegularExpressions.Regex.Match(src,
                @"const\s+float\s+" + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*=\s*(-?[0-9]*\.?[0-9]+)f?\s*;");
            if (!m.Success) return false;
            return float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        // =====================================================================
        // CASE 13  [heart-objective-state]  THE HEART PLATE'S LINE 2 CARRIES STATE
        //                                                          (WO-1407, 2026-09-05)
        // ---------------------------------------------------------------------
        // Merged UI review row 6: "Prepare the realm for the next wave." was a STATIC sentence
        // on every device frame, and nothing on the town HUD told a non-raid-capable player how
        // to become one. The words are now resolved in Core (HeartObjectiveCopy.Resolve) from
        // the posture rail + the army seam, and the View only paints them.
        //   13a  the View no longer authors the sentence: the literal is gone from HudSrc and
        //        the Resolve call is present; the row is still ONE fitted line in its band;
        //   13b  Resolve, driven by fixtures, returns the three state strings: a Barracks hint
        //        for NoBarracks AND BarracksLost, a "Train N" line whose N is RequiredSlots minus
        //        (deployable + queued) when a Barracks stands and the army is short, the wave
        //        line when ready, and "Defend" in a hostile posture; all ASCII;
        //   13c  every candidate string MEASURES inside the objective row at its effective floor
        //        at both aspects (the WO-1384 geometry: HudLayoutBands.HeartMount x 0.96 plate,
        //        HeartRowX0..X1 of it) - no state may ellipsise, because the state IS the word.
        // RED, one line each: paste "Prepare the realm for the next wave." back into
        // BuildHeartStatus (13a); make Resolve return PrepareWave for NoBarracks (13b); set
        // BuildBarracks to a 70-character sentence (13c).
        // =====================================================================
        private static void Case13_HeartObjectiveState(List<string> failures, List<string> notes)
        {
            const string tag = "[heart-objective-state]";
            string src = ReadSrc(HudSrc);
            if (src == null) { failures.Add(tag + " cannot read " + HudSrc); return; }

            // 13a - the View relays; it does not author.
            if (src.IndexOf("\"" + HeartObjectiveCopy.PrepareWave + "\"", StringComparison.Ordinal) >= 0)
                failures.Add(tag + " " + HudSrc + " still authors the literal '" + HeartObjectiveCopy.PrepareWave +
                             "' - the plate line is a STATIC sentence again (the captured defect). The View must " +
                             "paint HeartObjectiveCopy.Resolve and nothing else");
            RequirePin(failures, tag, src, "HeartObjectiveCopy.Resolve(",
                "the Heart plate's line 2 must come from the Core resolver so the words carry state");
            RequirePin(failures, tag, src, "RepaintHeartObjective(force: false)",
                "the objective line must be re-resolved on the cheap poll / posture flip, not painted once");
            RequirePin(failures, tag, src,
                "FitSingleLine(_heartObjectiveLabel, HeartObjectiveFontMin, HeartObjectiveFontMax)",
                "the objective row is a ONE-line band (WO-1384); a FitBlock wrap here Truncates the tail silently");

            // 13b - the resolver, driven.
            var ready = new RaidEntryGate.RaidArmyStatus
                { Ready = true, DeployableSlots = 3, QueuedSlots = 0, CapSlots = 10, RequiredSlots = 3 };
            var empty = new RaidEntryGate.RaidArmyStatus
                { Ready = false, DeployableSlots = 0, QueuedSlots = 0, CapSlots = 10, RequiredSlots = 3 };
            var oneShort = new RaidEntryGate.RaidArmyStatus
                { Ready = false, DeployableSlots = 1, QueuedSlots = 1, CapSlots = 10, RequiredSlots = 3 };
            var legacy = new RaidEntryGate.RaidArmyStatus   // pre-WO-1407 publish: RequiredSlots 0 -> cap
                { Ready = false, DeployableSlots = 4, QueuedSlots = 0, CapSlots = 10, RequiredSlots = 0 };
            // WO-1641 - THE FIXTURE THAT WAS MISSING, and its absence is why this shipped. Nothing
            // here drove Resolve with the bar AT the cap on a save that has already raided, which
            // is the ordinary state of every post-first-raid player. Device frame
            // Builds/device-frames/2026-09-10_0625_back_in_town.png, 5 minutes after a completed
            // raid, read "Train 2 troops to unlock Raids" at a player who had just raided.
            var raided = new RaidEntryGate.RaidArmyStatus
                { Ready = false, DeployableSlots = 8, QueuedSlots = 0, CapSlots = 10, RequiredSlots = 10,
                  PastFirstRaid = true };
            // The pre-first-raid EDGE the soft-gate bit alone cannot see: a cap at or below the
            // softened floor makes required == cap, so "required == cap" is NOT a stand-in for
            // "has raided". This save has never raided and must keep the unlock wording.
            var smallCapFirstRaid = new RaidEntryGate.RaidArmyStatus
                { Ready = false, DeployableSlots = 1, QueuedSlots = 0, CapSlots = 3, RequiredSlots = 3,
                  PastFirstRaid = false };
            int n;

            string noBarracks = HeartObjectiveCopy.Resolve(false, false, PostureSignals.RaidLockReason.NoBarracks, empty, out n);
            if (noBarracks.IndexOf("Barracks", StringComparison.Ordinal) < 0)
                failures.Add(tag + " with NO Barracks the plate reads '" + noBarracks + "' - it must name the " +
                             "Barracks and the door (Build > Realm); this is the player the review row is about");
            string lost = HeartObjectiveCopy.Resolve(false, false, PostureSignals.RaidLockReason.BarracksLost, empty, out n);
            if (lost.IndexOf("Barracks", StringComparison.Ordinal) < 0)
                failures.Add(tag + " with a LOST Barracks the plate reads '" + lost + "' - it must name the Barracks");
            string train = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None, empty, out n);
            if (!train.StartsWith("Train 3 ", StringComparison.Ordinal) || train.IndexOf("unlock Raids", StringComparison.Ordinal) < 0)
                failures.Add(tag + " a Barracks with an empty army on a first-raid save reads '" + train +
                             "' - expected 'Train 3 troops to unlock Raids' (RequiredSlots 3 - 0 fielded)");
            if (n != 3)
                failures.Add(tag + " troopsNeeded for an empty army against a bar of 3 is " + n + ", not 3");
            string one = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None, oneShort, out n);
            if (!one.StartsWith("Train 1 ", StringComparison.Ordinal) || n != 1)
                failures.Add(tag + " 1 deployable + 1 queued against a bar of 3 reads '" + one + "' (n=" + n +
                             ") - queued troops must count, exactly as the raid door counts them");
            string leg = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None, legacy, out n);
            if (!leg.StartsWith("Train 6 ", StringComparison.Ordinal))
                failures.Add(tag + " a publish without RequiredSlots must fall back to the CAP (10 - 4 = 6); read '" +
                             leg + "'");
            // WO-1641 RED-FIRST: against HEAD before the fix this reads "Train 2 troops to unlock
            // Raids" and both asserts below fire. RED recipe to re-prove it: drop the
            // army.PastFirstRaid branch in HeartObjectiveCopy.Resolve.
            string raidedShort = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None, raided, out n);
            if (raidedShort.IndexOf("unlock", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add(tag + " a save that has ALREADY finished a raid reads '" + raidedShort +
                             "' - the plate is claiming an unlock the player has already earned. " +
                             "After the first raid the bar is the full cap and falling short of it " +
                             "means the raid PARTY is short, not that Raids are locked");
            if (!raidedShort.StartsWith("Train 2 ", StringComparison.Ordinal) || n != 2)
                failures.Add(tag + " 8 deployable against a post-first-raid bar of 10 reads '" + raidedShort +
                             "' (n=" + n + ") - expected the same arithmetic as before, 10 - 8 = 2");
            string smallCap = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None,
                                                        smallCapFirstRaid, out n);
            if (smallCap.IndexOf("unlock Raids", StringComparison.Ordinal) < 0)
                failures.Add(tag + " a save that has NEVER raided whose cap equals the softened bar reads '" +
                             smallCap + "' - it must still read the unlock line. Reading 'has raided' off " +
                             "RequiredSlots == CapSlots is exactly the derivation this case forbids");

            string wave = HeartObjectiveCopy.Resolve(false, true, PostureSignals.RaidLockReason.None, ready, out n);
            if (wave.IndexOf("wave", StringComparison.OrdinalIgnoreCase) < 0 || n != 0)
                failures.Add(tag + " a raid-capable, army-ready save reads '" + wave + "' - expected the wave line");
            string flagOff = HeartObjectiveCopy.Resolve(false, false, PostureSignals.RaidLockReason.FlagOff, ready, out n);
            if (flagOff.IndexOf("Barracks", StringComparison.Ordinal) >= 0)
                failures.Add(tag + " with the raid FLAG off the plate sends the player to build a Barracks ('" +
                             flagOff + "') - there is no player action that opens that door");
            string defend = HeartObjectiveCopy.Resolve(true, false, PostureSignals.RaidLockReason.NoBarracks, empty, out n);
            if (defend != HeartObjectiveCopy.Defend)
                failures.Add(tag + " in a hostile posture the plate reads '" + defend + "', not '" +
                             HeartObjectiveCopy.Defend + "' - a wave in progress outranks every unlock hint");
            if (!string.Equals(HeartObjectiveCopy.TrainTroops(1), "Train 1 troop to unlock Raids",
                    StringComparison.Ordinal))
                failures.Add(tag + " 'Train 1 troops' - the singular must read 'troop'");
            if (!string.Equals(HeartObjectiveCopy.TrainNextRaid(1), "Train 1 troop for the next raid",
                    StringComparison.Ordinal))
                failures.Add(tag + " the post-first-raid singular reads '" + HeartObjectiveCopy.TrainNextRaid(1) +
                             "' - expected 'Train 1 troop for the next raid'");

            // 13c - every state string fits the row, measured, at both aspects.
            string[] candidates =
            {
                HeartObjectiveCopy.BuildBarracks, HeartObjectiveCopy.TrainTroops(10),
                HeartObjectiveCopy.TrainNextRaid(10),   // WO-1641 - the post-first-raid twin
                HeartObjectiveCopy.PrepareWave, HeartObjectiveCopy.Defend,
            };
            float objMin, objMax, rowX0, rowX1;
            if (!TryFloatConst(src, "HeartObjectiveFontMin", out objMin)) objMin = 16f;
            if (!TryFloatConst(src, "HeartObjectiveFontMax", out objMax)) objMax = 18f;
            if (!TryFloatConst(src, "HeartRowX0", out rowX0)) rowX0 = 0.05f;
            if (!TryFloatConst(src, "HeartRowX1", out rowX1)) rowX1 = 0.95f;
            float objFloor = Math.Min(Math.Max(objMin, ElarionUiKit.FontHardFloor), objMax);
            foreach (string s in candidates)
            {
                string detail;
                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, s, objFloor, out detail);
                if (w < 0f) { notes.Add("objective '" + s + "' not measurable headlessly: " + detail); continue; }
                foreach (var a in Aspects)
                {
                    var refSize = HudLayoutBands.CanvasReferenceSize(a.W, a.H);
                    float plateW = HudLayoutBands.HeartMount.width * refSize.x * 0.97f;
                    float rowW = (rowX1 - rowX0) * plateW;
                    if (w > rowW)
                        failures.Add(tag + " at " + a.Name + " the objective '" + s + "' MEASURES " + w.ToString("0.0") +
                                     " ref px at its " + objFloor + "px floor but the row is " + rowW.ToString("0.0") +
                                     " px wide (" + detail + ") - it would ellipsise, and the state is in the words. " +
                                     "Shorten the sentence in HeartObjectiveCopy or grow HudLayoutBands.HeartMount; " +
                                     "never the font down");
                }
                notes.Add("objective '" + s + "' " + w.ToString("0.0") + " px at " + objFloor + "px");
            }
        }

        // =====================================================================
        // CASE 14  [countdown-minutes]  A PLAYER-FACING COUNTDOWN NEVER PRINTS BARE SECONDS
        //                                                          (WO-1407, 2026-09-05)
        // ---------------------------------------------------------------------
        // "Next wave in 855s" on every device frame (review row 6). ElarionUi.Duration is the
        // ONE formatter; both wave call sites (HudKitController.OnWave, WaveCountdownUI) and
        // the queue rail (QueueRailView.FormatTime) print through it.
        //   14a  Duration(855) == "14m 15s"; small values stay seconds; hours drop seconds;
        //   14b  a sweep of 61..7200 never yields a bare "<n>s";
        //   14c  neither wave call site builds seconds by hand any more, and both call Duration.
        // RED, one line each: return seconds + "s" from Duration (14a/14b); put
        // `Mathf.CeilToInt(w.CountdownRemaining) + "s"` back in OnWave (14c).
        // =====================================================================
        private const string WaveOverlaySrc = "Assets/_Modules/Village/Waves/WaveCountdownUI.cs";
        private const string QueueRailSrc = "Assets/_Modules/Core/UI/QueueRailView.cs";

        private static void Case14_CountdownMinutes(List<string> failures, List<string> notes)
        {
            const string tag = "[countdown-minutes]";

            // 14a
            Expect(failures, tag, ElarionUi.Duration(855), "14m 15s", "the review frame's 855 s");
            Expect(failures, tag, ElarionUi.Duration(45), "45s", "under a minute stays seconds");
            Expect(failures, tag, ElarionUi.Duration(60), "1m 0s", "exactly one minute");
            Expect(failures, tag, ElarionUi.Duration(3661), "1h 1m", "an hour drops the seconds");
            Expect(failures, tag, ElarionUi.Duration(-5), "0s", "negative clamps to zero");
            Expect(failures, tag, QueueRailView.FormatTime(855), ElarionUi.Duration(855),
                   "the queue rail prints the same words as the wave line (one formatter)");

            // 14b - never a bare seconds count above a minute.
            var bare = new System.Text.RegularExpressions.Regex(@"^\d+s$");
            for (int s = 61; s <= 7200; s++)
            {
                string d = ElarionUi.Duration(s);
                if (bare.IsMatch(d))
                {
                    failures.Add(tag + " Duration(" + s + ") = '" + d + "' - a bare seconds count above 60 s; " +
                                 "the player has to do the division the HUD exists to do");
                    break;
                }
                for (int i = 0; i < d.Length; i++)
                    if (d[i] > 126) { failures.Add(tag + " Duration(" + s + ") is not ASCII"); s = 7201; break; }
            }

            // 14c - both wave call sites go through the formatter.
            string hud = ReadSrc(HudSrc);
            if (hud == null) failures.Add(tag + " cannot read " + HudSrc);
            else
            {
                if (hud.IndexOf("Mathf.CeilToInt(w.CountdownRemaining) + \"s\"", StringComparison.Ordinal) >= 0)
                    failures.Add(tag + " " + HudSrc + " builds the wave countdown as raw seconds again " +
                                 "(CeilToInt(...) + \"s\") - the captured 'Next wave in 855s'");
                RequirePin(failures, tag, hud, "\"Next wave in \" + ElarionUi.Duration(",
                    "the HudKit wave line must print through the one formatter");
            }
            string overlay = ReadSrc(WaveOverlaySrc);
            if (overlay == null) failures.Add(tag + " cannot read " + WaveOverlaySrc);
            else
            {
                if (overlay.IndexOf("{whole}s", StringComparison.Ordinal) >= 0)
                    failures.Add(tag + " " + WaveOverlaySrc + " interpolates raw seconds ('{whole}s') - the " +
                                 "UIElements overlay is the second wave call site and must use the same formatter");
                if (overlay.IndexOf("ElarionUi.Duration(", StringComparison.Ordinal) < 0)
                    failures.Add(tag + " " + WaveOverlaySrc + " does not call ElarionUi.Duration");
            }
            string rail = ReadSrc(QueueRailSrc);
            if (rail != null && rail.IndexOf("ElarionUi.Duration(", StringComparison.Ordinal) < 0)
                failures.Add(tag + " " + QueueRailSrc + " no longer delegates FormatTime to ElarionUi.Duration - " +
                             "two duration grammars will drift");
            notes.Add("Duration(855)='" + ElarionUi.Duration(855) + "'");
        }

        private static void Expect(List<string> failures, string tag, string got, string want, string what)
        {
            if (!string.Equals(got, want, StringComparison.Ordinal))
                failures.Add(tag + " " + what + ": got '" + got + "', want '" + want + "'");
        }

        // =====================================================================
        // CASE 15  [builders-chip-idle]  THE BUILDERS CHIP SAYS IDLE, AND IS THERE TO SAY IT
        //                                                          (WO-1407, 2026-09-05)
        // ---------------------------------------------------------------------
        // Review row 6: "no idle-builders surface". The chip is a STATUS GLANCE (CLAUDE.md s7 -
        // the Manage face is the one Queues door), so at 0 active it must still carry a word.
        //   15a  BuildersChipCopy.Format at 0 busy / 2 slots reads "Builders idle 2"; busy keeps
        //        the "N/M" count; the Train line still rides as line two; unpublished is still
        //        the bare word (never empty);
        //   15b  the View relays the Core words (BuildersChipCopy.Format in HudSrc) and the chip
        //        is not hidden on an idle queue (no SetActive keyed on BuilderBusy);
        //   15c  "Builders idle 2" MEASURES inside the chip's label rect at the chip's own floor
        //        (BuildRailChip fits 22..30).
        // RED, one line each: return "Builders 0/2" for the idle case (15a); gate the chip root
        // on s.BuilderBusy > 0 (15b); make the idle word "Builders standing idle: 2" (15c).
        // =====================================================================
        private static void Case15_BuildersChipIdle(List<string> failures, List<string> notes)
        {
            const string tag = "[builders-chip-idle]";

            // 15a
            var idle = new ObsidianQueueGate.WorkQueueStatus { Available = true, BuilderBusy = 0, BuilderSlots = 2 };
            string idleText = BuildersChipCopy.Format(idle);
            Expect(failures, tag, idleText, "Builders idle 2", "0 active of 2 builders");
            var busy = new ObsidianQueueGate.WorkQueueStatus { Available = true, BuilderBusy = 1, BuilderSlots = 2 };
            Expect(failures, tag, BuildersChipCopy.Format(busy), "Builders 1/2", "1 active of 2 builders");
            var training = new ObsidianQueueGate.WorkQueueStatus
                { Available = true, BuilderBusy = 0, BuilderSlots = 2, TrainBusy = 1 };
            Expect(failures, tag, BuildersChipCopy.Format(training), "Builders idle 2\nTrain 1",
                   "idle builders with one troop training (two lines, never a pipe)");
            var unpublished = new ObsidianQueueGate.WorkQueueStatus { Available = false };
            if (string.IsNullOrEmpty(BuildersChipCopy.Format(unpublished)))
                failures.Add(tag + " an unpublished queue status renders an EMPTY chip - a blank face is a missing chip");

            // 15b
            string hud = ReadSrc(HudSrc);
            if (hud == null) failures.Add(tag + " cannot read " + HudSrc);
            else
            {
                RequirePin(failures, tag, hud, "BuildersChipCopy.Format(",
                    "the chip's words come from Core so this suite can drive them");
                string chipPoll = Between(hud, "var qs = ObsidianQueueGate.Status;", "RepaintHeartfire(force: false);");
                if (chipPoll != null && chipPoll.IndexOf("SetActive(", StringComparison.Ordinal) >= 0 &&
                    chipPoll.IndexOf("BuilderBusy", StringComparison.Ordinal) >= 0)
                    failures.Add(tag + " the Builders chip poll toggles a SetActive on BuilderBusy - the chip hides " +
                                 "itself when idle, which is the review's 'no idle-builders surface'");
            }

            // 15c - the idle word fits the chip at the chip's floor.
            float boxW = RailChipWidthPx * ButtonLabelInset;
            const float chipFloor = 22f;   // BuildRailChip: FitSingleLine(lbl, 22f, 30f)
            string detail;
            float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, idleText, chipFloor, out detail);
            if (w < 0f) notes.Add("builders idle word not measurable headlessly: " + detail);
            else if (w > boxW)
                failures.Add(tag + " '" + idleText + "' MEASURES " + w.ToString("0.0") + " ref px at the chip's " +
                             chipFloor + "px floor but the label rect is " + boxW.ToString("0.0") + " px (" + detail +
                             ") - it would ellipsise the count, the one number that carries the state");
            else notes.Add("builders idle chip '" + idleText + "' " + w.ToString("0.0") + " px in " + boxW.ToString("0") + " px");
        }

        private static void RequirePin(List<string> failures, string tag, string src, string literal, string why)
        {
            if (src.IndexOf(literal, StringComparison.Ordinal) < 0)
                failures.Add(tag + " " + HudSrc + " no longer contains '" + literal + "' - " + why);
        }

        /// <summary>WCAG 2.x contrast of one authored sRGB text colour against the measured plate.</summary>
        private static void Contrast(List<string> failures, string what, float plateLum,
                                     float r, float g, float b)
        {
            float textLum = 0.2126f * Linear(r) + 0.7152f * Linear(g) + 0.0722f * Linear(b);
            float hi = Math.Max(textLum, plateLum), lo = Math.Min(textLum, plateLum);
            float ratio = (hi + 0.05f) / (lo + 0.05f);
            if (ratio < ContrastFloor)
                failures.Add("[raids-locked-face] the " + what + " reads at only " + ratio.ToString("F2") +
                             ":1 against the locked card's plate (floor " + ContrastFloor.ToString("F1") +
                             ":1). The plate is near-black art, so this is the check that the live words " +
                             "are still legible on it - raise the text tone or lighten the plate, do not " +
                             "lower this floor");
        }

        private static float Linear(float c)
        {
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);
        }

        /// <summary>Decode the card PNG and measure its TEXT PLATE only: the fraction of pixels
        /// light enough to be a glyph, and the mean relative luminance the live text sits on.
        /// Reads the file bytes, so the importer's isReadable setting is irrelevant.</summary>
        private static bool MeasurePlate(string path, out float inkFraction, out float meanLuminance)
        {
            inkFraction = 0f; meanLuminance = 0f;
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return false;
                int w = tex.width, h = tex.height;
                if (w < 8 || h < 8) return false;
                var px = tex.GetPixels32();

                // GetPixels32 is bottom-left origin and the plate fractions are UV, so they map
                // straight across with no flip.
                int x0 = Mathf.Clamp(Mathf.RoundToInt(PlateX0 * w), 0, w - 1);
                int x1 = Mathf.Clamp(Mathf.RoundToInt(PlateX1 * w), x0 + 1, w);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(PlateY0 * h), 0, h - 1);
                int y1 = Mathf.Clamp(Mathf.RoundToInt(PlateY1 * h), y0 + 1, h);

                long total = 0, light = 0;
                double acc = 0.0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        var c = px[row + x];
                        total++;
                        float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        // 90/255: above every shadow tone in this plate, below any legible glyph
                        // the authoring tool would have painted onto it.
                        if (l >= 90f) light++;
                        acc += 0.2126f * Linear(c.r / 255f) + 0.7152f * Linear(c.g / 255f) +
                               0.0722f * Linear(c.b / 255f);
                    }
                }
                if (total == 0) return false;
                inkFraction = (float)((double)light / total);
                meanLuminance = (float)(acc / total);
                return true;
            }
            catch { return false; }
            finally { if (tex != null) UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    if (s[i] == 'n') sb.Append('\n');
                    else if (s[i] == 't') sb.Append('\t');
                    else sb.Append(s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}

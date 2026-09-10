// =============================================================================
// TouchFloorAuthoringRegression — WO-1664. The bands the CLAMP was rescuing.
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS SUITE IS, IN THE HONEST WORDS InventoryArmoryRailRegression's header
// had to be corrected into (WO-1494): it is ARITHMETIC ON SOURCE-PARSED CONSTANTS
// plus two SOURCE LINTS. It never enters PlayMode and never builds a visual tree, so
// it runs inside the headless DataRegression batch. It can prove that an AUTHORED
// anchor band resolves under the touch floor; it cannot prove that the band lays out
// the way its anchors say. That last mile belongs to LayoutOracle.Audit on the
// capture path — which is exactly why case [oracles-are-read] below exists.
//
// ⛔ WHY IT DOES NOT RECOMPUTE THE LAYOUT FROM THE LAYOUT'S OWN NUMBERS. The rule
// InventoryArmoryRailRegression states at its :42-44 ("DO NOT ASSERT GEOMETRY BY
// RECOMPUTING IT FROM THE SAME CONSTANTS THE LAYOUT USES — that yields a suite
// structurally incapable of failing, and this repo found three of them in twenty-four
// hours") applies here, so every case pairs an INDEPENDENT authority against the
// parsed anchors:
//
//   [title-row]        ElarionUiKit.MinTouchPx + the CanvasScaler model  vs  the row
//                      anchors and the inner face fraction parsed out of TitleController
//   [title-tagline]    the tagline band parsed out of BuildTitleTextBlock  vs  the row's top
//   [repair-all]       ElarionUiKit.MinTouchPx + the scaler model  vs  HudLayoutBands.ToastZone
//   [collect-band]     ElarionUiKit.MinTouchPx + the scaler model  vs  WelcomeBackPopup's
//                      ActionBandY0/Y1, BodyY0 and DoorRowH
//   [one-band]         the two call sites in WelcomeBackPopup  vs  each other
//   [oracles-are-read] the DEVICE's captured CLAMP FIRED lines  vs  the capture entry
//                      points still reading the tally that would have caught them
//
// THE FLOOR ITSELF IS READ LIVE, NOT PARSED: ElarionUiKit.MinTouchPx is referenced as a
// symbol, so lowering it to "fix" a failure here (WO-1664 §6's exact inversion) moves the
// assertion with it and is caught by the six other regressions that name it.
//
// -----------------------------------------------------------------------------
// THE REFERENCE HEIGHT, AND WHY IT IS DERIVED HERE RATHER THAN TYPED
// -----------------------------------------------------------------------------
// MinTouchPx is a REFERENCE-pixel floor. A band authored as a fraction resolves against
// the CanvasScaler's post-scale height, and the kit scaler is referenceResolution
// (1080,1920) with ScreenMatchMode.MatchWidthOrHeight at match 0.5 (ElarionUiKit.cs:109-111):
//
//     scale     = (W / refW)^(1-match) x (H / refH)^match
//     refHeight = H / scale
//
// which at the three captured aspects gives
//
//     1920x1080 -> 1080.0     2340x1080 -> 978.4     2670x1200 -> 965.4  (the Seeker)
//
// 965.4 is the SMALLEST, so every band is asserted against it — clear the floor at the
// worst aspect and it is clear at all three. The scaler's three numbers are PARSED out of
// ElarionUiKit.cs rather than hardcoded, so re-tuning the scaler re-tunes this suite
// instead of silently invalidating it.
//
// ⚠ THE UNIT IS THE WHOLE BUG THIS SUITE EXISTS TO CATCH. WelcomeBackPopup's DoorRowH
// carried a doc block that computed its own plate at "~127px" and concluded it cleared
// 112 — in DEVICE pixels, at 1200 tall. In the reference pixels the floor is actually
// measured in, the same band was 102.2. A band can pass its own comment and fail the
// oracle, and did.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class TouchFloorAuthoringRegression
    {
        // ── the artefacts ────────────────────────────────────────────────────
        private const string TitleSrc   = "Assets/_Modules/Onboarding/TitleController.cs";
        private const string BandsSrc   = "Assets/_Modules/Core/UI/HudLayoutBands.cs";
        private const string RepairSrc  = "Assets/_Modules/Village/Walls/HubRepairAffordance.cs";
        private const string PopupSrc   = "Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs";
        private const string KitSrc     = "Assets/_Modules/Core/UI/ElarionUiKit.cs";
        private const string CaptureSrc = "Assets/Editor/UICaptureLaunch.cs";

        /// <summary>The captured landscape aspects, matching UICaptureLaunch's LandscapeTargets.
        /// The suite asserts against the SMALLEST resolved reference height across them.</summary>
        private static readonly (int W, int H)[] Aspects =
        {
            (1920, 1080), (2340, 1080), (2670, 1200),
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("TOUCH_FLOOR_AUTHORING_OK - " + reason);
            else Debug.LogError("TOUCH_FLOOR_AUTHORING_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                float refH = SmallestReferenceHeight(failures, notes);
                CaseTitleRow(failures, notes, refH);
                CaseTitleClearsTagline(failures, notes);
                CaseRepairAll(failures, notes, refH);
                CaseCollectBand(failures, notes, refH);
                CaseOneBand(failures, notes);
                CaseOraclesAreRead(failures, notes);
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
            reason = "WO-1664 touch-floor authoring ok (arithmetic on source-parsed anchors against " +
                     "ElarionUiKit.MinTouchPx=" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                     " at the smallest captured reference height; no built rect was measured " +
                     "here -- LayoutOracle.Audit on the capture path is that authority) - " +
                     string.Join("; ", notes);
            return true;
        }

        // =====================================================================
        //  THE SCALER MODEL — parsed, not typed
        // =====================================================================

        private static float SmallestReferenceHeight(List<string> failures, List<string> notes)
        {
            const float Fallback = 965.4f;   // 2670x1200 under the shipped scaler; see the header
            string kit = ReadOrNull(KitSrc);
            if (kit == null)
            {
                failures.Add("[scaler] cannot read " + KitSrc + " -- the reference height could not " +
                             "be derived, so every band below would be asserted against a number " +
                             "nobody measured this run");
                return Fallback;
            }

            var res = Regex.Match(kit,
                @"referenceResolution\s*=\s*new\s+Vector2\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)");
            var match = Regex.Match(kit, @"matchWidthOrHeight\s*=\s*(-?\d+(?:\.\d+)?)f?\s*;");
            if (!res.Success || !match.Success)
            {
                failures.Add("[scaler] could not parse referenceResolution / matchWidthOrHeight out of " +
                             KitSrc + " -- the kit's CanvasScaler setup moved, and this suite's " +
                             "reference-pixel arithmetic is only as good as that model");
                return Fallback;
            }
            if (kit.IndexOf("ScreenMatchMode.MatchWidthOrHeight", StringComparison.Ordinal) < 0)
            {
                failures.Add("[scaler] the kit no longer sets ScreenMatchMode.MatchWidthOrHeight -- the " +
                             "geometric-mean formula this suite derives refHeight with is wrong for " +
                             "Expand/Shrink, and every band below must be re-derived before it is trusted");
                return Fallback;
            }

            float refW = ParseF(res.Groups[1].Value, 1080f);
            float refHRes = ParseF(res.Groups[2].Value, 1920f);
            float m = Mathf.Clamp01(ParseF(match.Groups[1].Value, 0.5f));

            float smallest = float.MaxValue;
            string smallestAt = "?";
            for (int i = 0; i < Aspects.Length; i++)
            {
                float w = Aspects[i].W, h = Aspects[i].H;
                float scale = Mathf.Pow(w / Mathf.Max(1f, refW), 1f - m) *
                              Mathf.Pow(h / Mathf.Max(1f, refHRes), m);
                if (scale <= 0.0001f) continue;
                float rh = h / scale;
                if (rh < smallest) { smallest = rh; smallestAt = Aspects[i].W + "x" + Aspects[i].H; }
            }
            if (smallest >= float.MaxValue) { failures.Add("[scaler] no aspect resolved a usable scale"); return Fallback; }

            notes.Add("[scaler] refResolution " + refW.ToString("0") + "x" + refHRes.ToString("0") +
                      ", match " + m.ToString("0.##") + " -> smallest reference height " +
                      smallest.ToString("0.#") + " px at " + smallestAt);
            return smallest;
        }

        // =====================================================================
        //  CASE — the title row seats the floor (WO-1664 §5 pin 2)
        // =====================================================================
        //
        //  RED BEFORE THE FIX: TitleController authored the row at 0.045..0.135 (0.090) with
        //  each face filling 0.10..0.90 of it, i.e. 0.090 x 965.4 x 0.80 = 69.5 ref px. The
        //  Seeker printed that number back three times over, once per face:
        //    CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Continue:  authored 399.5x69.5 -> 112
        //    CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Start New: authored 399.5x69.5 -> 112
        //    CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Play Intro:authored 399.5x69.5 -> 112
        //  (Builds/device-frames/2026-09-10_1137_363866_logcat.txt, byte-identical on the 363786
        //  log before it.) THE ROW IS THE DRIVER, NOT THE FACE: three faces share one band, so
        //  the height is a row-level number and this case asserts it there.
        private static void CaseTitleRow(List<string> failures, List<string> notes, float refH)
        {
            const string Tag = "[title-row-seats-the-touch-floor]";
            string src = ReadOrNull(TitleSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + TitleSrc); return; }

            string method = MethodBody(src, "BuildButtonColumn");
            if (method == null)
            {
                failures.Add(Tag + " BuildButtonColumn not found in " + TitleSrc +
                             " -- the title row's one authoring site moved and this pin is blind");
                return;
            }

            if (!TryAnchorY(method, "anchorMin", out float y0) ||
                !TryAnchorY(method, "anchorMax", out float y1))
            {
                failures.Add(Tag + " could not parse the row's anchorMin/anchorMax out of " +
                             "BuildButtonColumn");
                return;
            }

            // The inner slot every face is built at: new Vector2(x0, 0.10f), new Vector2(x1, 0.90f)
            var face = Regex.Match(method,
                @"new\s+Vector2\(\s*x0\s*,\s*(-?\d+(?:\.\d+)?)f?\s*\)\s*,\s*new\s+Vector2\(\s*x1\s*,\s*(-?\d+(?:\.\d+)?)f?\s*\)");
            if (!face.Success)
            {
                failures.Add(Tag + " could not parse the per-face inner slot (new Vector2(x0, ..), " +
                             "new Vector2(x1, ..)) -- the face is what the touch floor applies to, " +
                             "so without it the row height alone proves nothing");
                return;
            }
            float f0 = ParseF(face.Groups[1].Value, 0f);
            float f1 = ParseF(face.Groups[2].Value, 1f);

            float rowFrac = y1 - y0;
            float faceFrac = f1 - f0;
            float px = rowFrac * refH * faceFrac;
            if (px + 0.5f < ElarionUiKit.MinTouchPx)
            {
                failures.Add(Tag + " the title row is authored " + y0.ToString("0.###") + ".." +
                             y1.ToString("0.###") + " (" + rowFrac.ToString("0.###") + " of screen) and " +
                             "each face fills " + faceFrac.ToString("0.##") + " of it, so a face resolves " +
                             px.ToString("0.#") + " ref px -- " + (ElarionUiKit.MinTouchPx - px).ToString("0.#") +
                             " UNDER ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                             ") at reference height " + refH.ToString("0.#") + ". ClampMinTouch will grow it " +
                             "symmetrically about its centre and spill it into the row's own chrome. The row " +
                             "needs at least " + (ElarionUiKit.MinTouchPx / (refH * Mathf.Max(0.01f, faceFrac)))
                                 .ToString("0.####") + " of screen height; keep anchorMin.y at " +
                             y0.ToString("0.###") + " and raise anchorMax.y.");
                return;
            }
            notes.Add("[title-row] row " + rowFrac.ToString("0.###") + " x face " + faceFrac.ToString("0.##") +
                      " x " + refH.ToString("0.#") + " = " + px.ToString("0.#") + " ref px >= " +
                      ElarionUiKit.MinTouchPx.ToString("0.#"));
        }

        // =====================================================================
        //  CASE — the title row does not eat the tagline (WO-1664 §5 pin 3)
        // =====================================================================
        //
        //  ⛔ THE PIN THAT STOPS pin 2 FROM BEING FIXED INTO A NEW DEFECT. The row grows
        //  UPWARD, so the copy block above it is what it can collide with. Both are parented
        //  to _canvas.transform (TitleController :204-206), i.e. the SAME full-canvas space,
        //  so the comparison is a straight fraction-vs-fraction one at every aspect.
        //  The tagline is the LOWEST of the three copy lines, so it is the one that matters.
        private static void CaseTitleClearsTagline(List<string> failures, List<string> notes)
        {
            const string Tag = "[title-row-clears-the-tagline]";
            string src = ReadOrNull(TitleSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + TitleSrc); return; }

            string row = MethodBody(src, "BuildButtonColumn");
            string text = MethodBody(src, "BuildTitleTextBlock");
            if (row == null || text == null)
            {
                failures.Add(Tag + " BuildButtonColumn and/or BuildTitleTextBlock not found in " + TitleSrc);
                return;
            }
            if (!TryAnchorY(row, "anchorMax", out float rowTop))
            {
                failures.Add(Tag + " could not parse the row's anchorMax out of BuildButtonColumn");
                return;
            }

            // ElarionUiKit.Label(parent, text, y0, y1, ...) -- the tagline is the LAST of the three
            // and has the lowest y0. Take the minimum y0 across the block rather than trusting order.
            var labels = Regex.Matches(text,
                @"ElarionUiKit\.Label\(\s*parent\s*,\s*[^,]+,\s*(-?\d+(?:\.\d+)?)f\s*,\s*(-?\d+(?:\.\d+)?)f");
            if (labels.Count == 0)
            {
                failures.Add(Tag + " could not parse any ElarionUiKit.Label band out of " +
                             "BuildTitleTextBlock -- the copy block this row must clear is unreadable, " +
                             "so a clearance claim would be a guess");
                return;
            }
            float lowestCopy = float.MaxValue;
            for (int i = 0; i < labels.Count; i++)
            {
                float y0 = ParseF(labels[i].Groups[1].Value, 1f);
                if (y0 < lowestCopy) lowestCopy = y0;
            }

            if (rowTop >= lowestCopy)
            {
                failures.Add(Tag + " the button row's top is " + rowTop.ToString("0.###") +
                             " of screen and the LOWEST title-copy line starts at " +
                             lowestCopy.ToString("0.###") + " -- the row has grown into the copy block. " +
                             "Growing the row to seat MinTouchPx must not be paid for by the tagline; " +
                             "re-seat the copy block or take the height off the bottom margin instead.");
                return;
            }
            notes.Add("[title-tagline] row top " + rowTop.ToString("0.###") + " clears the lowest copy " +
                      "line at " + lowestCopy.ToString("0.###") + " by " +
                      (lowestCopy - rowTop).ToString("0.###") + " of screen height");
        }

        // =====================================================================
        //  CASE — REPAIR ALL seats the floor (WO-1664 §5 pin 4a)
        // =====================================================================
        //
        //  RED BEFORE THE FIX: ToastZone was 0.203..0.308, i.e. 0.105 x 965.4 = 101.3 ref px, and
        //  the Seeker printed
        //    CLAMP FIRED HubRepairAffordance/HubRepairCanvas/ObsBtn_REPAIR ALL: authored
        //    386.6x101.4 -> grown 386.6x112
        //
        //  ⛔ THE DRIVER IS THE ZONE, NOT THE CALL SITE, AND WO-1664 §2B/§4B SAID OTHERWISE.
        //  The ticket read HubRepairAffordance's ToastZoneSlice(0f, 0.72f) as a HEIGHT fraction
        //  and prescribed raising 0.72 to ~0.80. ToastZoneSlice slices X ONLY -- it lerps
        //  xMin/xMax and passes yMin/yMax straight through, and its own doc says from/to are
        //  "0..1 across the zone's WIDTH" -- so that change
        //  would have made the card wider and left the clamp firing. This case therefore asserts
        //  the ZONE, and asserts SEPARATELY that the call site has not gone back to hand-authoring
        //  a rect -- which HubRepairAffordance :528-534 forbids in the strongest terms it has,
        //  having already been in two wrong seats.
        private static void CaseRepairAll(List<string> failures, List<string> notes, float refH)
        {
            const string Tag = "[repair-all-seats-the-touch-floor]";
            string bands = ReadOrNull(BandsSrc);
            string repair = ReadOrNull(RepairSrc);
            if (bands == null || repair == null)
            {
                failures.Add(Tag + " cannot read " + BandsSrc + " and/or " + RepairSrc);
                return;
            }

            var zone = Regex.Match(bands,
                @"ToastZone\s*=\s*Rect\.MinMaxRect\(\s*(-?\d+(?:\.\d+)?)f\s*,\s*(-?\d+(?:\.\d+)?)f\s*,\s*(-?\d+(?:\.\d+)?)f\s*,\s*(-?\d+(?:\.\d+)?)f\s*\)");
            if (!zone.Success)
            {
                failures.Add(Tag + " could not parse HudLayoutBands.ToastZone -- the ONE seat every " +
                             "toast on this HUD inherits its height from is unreadable");
                return;
            }
            float yMin = ParseF(zone.Groups[2].Value, 0f);
            float yMax = ParseF(zone.Groups[4].Value, 0f);
            float px = (yMax - yMin) * refH;

            if (px + 0.5f < ElarionUiKit.MinTouchPx)
            {
                failures.Add(Tag + " HudLayoutBands.ToastZone is " + yMin.ToString("0.###") + ".." +
                             yMax.ToString("0.###") + " of screen height, so EVERY control seated in it " +
                             "resolves " + px.ToString("0.#") + " ref px -- " +
                             (ElarionUiKit.MinTouchPx - px).ToString("0.#") + " under ElarionUiKit.MinTouchPx (" +
                             ElarionUiKit.MinTouchPx.ToString("0.#") + ") at reference height " +
                             refH.ToString("0.#") + ". ToastZoneSlice slices X only, so NO call-site " +
                             "fraction can rescue this: the zone needs at least " +
                             (ElarionUiKit.MinTouchPx / refH).ToString("0.####") + " of screen height. " +
                             "It grows UPWARD (ActionBar tops out at 0.150 below it; TargetInfo does not " +
                             "begin until 0.660 above).");
            }
            else
            {
                notes.Add("[repair-all] ToastZone " + (yMax - yMin).ToString("0.###") + " x " +
                          refH.ToString("0.#") + " = " + px.ToString("0.#") + " ref px >= " +
                          ElarionUiKit.MinTouchPx.ToString("0.#"));
            }

            // The seat must still come from the shared zone. HubRepairAffordance :528-534:
            // "Do NOT author a rect here again; if the zone is wrong, it is wrong for everybody
            // and it moves in ONE place."
            if (repair.IndexOf("ToastZoneMin(", StringComparison.Ordinal) < 0 ||
                repair.IndexOf("ToastZoneMax(", StringComparison.Ordinal) < 0)
            {
                failures.Add(Tag + " HubRepairAffordance no longer seats REPAIR ALL through " +
                             "ToastZoneMin/ToastZoneMax -- a THIRD hand-picked seat is exactly what " +
                             "that file's :528-534 block forbids, after the first landed on the minimap " +
                             "and the second was re-derived from rects this module does not own.");
            }
        }

        // =====================================================================
        //  CASE — COLLECT, the raid door and the WO-1408 door row (WO-1664 §5 pin 4b)
        // =====================================================================
        //
        //  RED BEFORE THE FIX: COLLECT was authored 0.045..0.155 of a modal content 0.84 x 965.4 =
        //  810.9 ref px tall, i.e. 89.2 px -- and the Seeker printed
        //    CLAMP FIRED WelcomeBackUI/ObsidianPanel/PanelContent/ObsBtn_COLLECT:
        //    authored 357.4x89.2 -> grown 357.4x112
        //  ⭐ 89.2 / 0.110 = 810.9 CORROBORATES the content height from the device rather than
        //  deriving it, which is why the modal's own 0.08..0.92 anchors are parsed below AND the
        //  product checked against that measurement.
        //
        //  THE DOOR ROW IS ASSERTED IN THE SAME CASE ON PURPOSE. It was never on the device log
        //  (that session's report carried no door row) and never on the gate (the capture path did
        //  not read the touch tally), yet at DoorRowH 0.21 it resolved 102.2 ref px. Two blind
        //  spots and a doc block that computed in device pixels is how a control stays sub-floor
        //  for a month; splitting it into its own optional case would rebuild the first blind spot.
        private static void CaseCollectBand(List<string> failures, List<string> notes, float refH)
        {
            const string Tag = "[collect-seats-the-touch-floor]";
            string src = ReadOrNull(PopupSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + PopupSrc); return; }

            // The modal shell: BuildObsidianModal(..., new Vector2(0.18f, 0.08f), new Vector2(0.82f, 0.92f), ...)
            var shell = Regex.Match(src,
                @"BuildObsidianModal\([^;]*?new\s+Vector2\(\s*-?\d+(?:\.\d+)?f\s*,\s*(-?\d+(?:\.\d+)?)f\s*\)\s*,\s*new\s+Vector2\(\s*-?\d+(?:\.\d+)?f\s*,\s*(-?\d+(?:\.\d+)?)f\s*\)",
                RegexOptions.Singleline);
            if (!shell.Success)
            {
                failures.Add(Tag + " could not parse the WelcomeBack modal's shell anchors -- the " +
                             "content height every band below is a fraction OF is unknown, and a " +
                             "floor claim without it would be the inference CLAUDE.md 11B forbids");
                return;
            }
            float contentFrac = ParseF(shell.Groups[2].Value, 0f) - ParseF(shell.Groups[1].Value, 0f);
            float contentPx = contentFrac * refH;

            if (!TryConst(src, "ActionBandY0", out float a0) ||
                !TryConst(src, "ActionBandY1", out float a1) ||
                !TryConst(src, "BodyY0", out float bodyY0) ||
                !TryConst(src, "DoorRowH", out float doorH))
            {
                failures.Add(Tag + " could not parse ActionBandY0 / ActionBandY1 / BodyY0 / DoorRowH " +
                             "out of " + PopupSrc + " -- WO-1664 named these constants precisely so " +
                             "this pin could read them instead of chasing inline literals");
                return;
            }

            float actionPx = (a1 - a0) * contentPx;
            if (actionPx + 0.5f < ElarionUiKit.MinTouchPx)
                failures.Add(Tag + " the bottom action band is ActionBandY0..ActionBandY1 = " +
                             a0.ToString("0.###") + ".." + a1.ToString("0.###") + " of a " +
                             contentPx.ToString("0.#") + " ref px content, so COLLECT and the raid door " +
                             "each resolve " + actionPx.ToString("0.#") + " ref px -- " +
                             (ElarionUiKit.MinTouchPx - actionPx).ToString("0.#") + " under " +
                             "ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                             "). The band needs at least " +
                             (ElarionUiKit.MinTouchPx / Mathf.Max(1f, contentPx)).ToString("0.####") +
                             " of content height.");
            else
                notes.Add("[collect-band] " + (a1 - a0).ToString("0.###") + " x " + contentPx.ToString("0.#") +
                          " = " + actionPx.ToString("0.#") + " ref px >= " + ElarionUiKit.MinTouchPx.ToString("0.#"));

            // The WO-1408 door row: DoorRowH of the body, and the door face fills the plate 0..1.
            // The body's CEILING is parsed from its one authoring line rather than typed, because
            // the door row's height is a fraction of the body and a moved ceiling silently
            // re-opens the sub-floor door this case closes.
            var bodyTop = Regex.Match(src,
                @"bodyRect\.anchorMax\s*=\s*new\s+Vector2\(\s*bodyRect\.anchorMax\.x\s*,\s*(-?\d+(?:\.\d+)?)f\s*\)");
            if (!bodyTop.Success)
            {
                failures.Add(Tag + " could not parse the report body's ceiling (bodyRect.anchorMax) " +
                             "out of " + PopupSrc + " -- the door row is a fraction of that band, so " +
                             "its floor clearance cannot be asserted without it");
                return;
            }
            float BodyY1 = ParseF(bodyTop.Groups[1].Value, 0f);
            float doorPx = doorH * (BodyY1 - bodyY0) * contentPx;
            if (doorPx + 0.5f < ElarionUiKit.MinTouchPx)
                failures.Add(Tag + " the WO-1408 door row is DoorRowH " + doorH.ToString("0.###") +
                             " of a body spanning " + bodyY0.ToString("0.###") + ".." +
                             BodyY1.ToString("0.##") + " of a " + contentPx.ToString("0.#") +
                             " ref px content, so the door face (which fills its plate 0..1) resolves " +
                             doorPx.ToString("0.#") + " ref px -- " +
                             (ElarionUiKit.MinTouchPx - doorPx).ToString("0.#") + " under " +
                             "ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                             "). ⚠ Check the UNITS before re-deriving this: the retired doc block " +
                             "computed the same plate at ~127 px and cleared the floor, in DEVICE " +
                             "pixels at 1200 tall. MinTouchPx is a REFERENCE-pixel floor.");
            else
                notes.Add("[door-row] DoorRowH " + doorH.ToString("0.###") + " x body " +
                          (BodyY1 - bodyY0).ToString("0.###") + " x " + contentPx.ToString("0.#") + " = " +
                          doorPx.ToString("0.#") + " ref px >= " + ElarionUiKit.MinTouchPx.ToString("0.#"));
        }

        // =====================================================================
        //  CASE — COLLECT and the raid door share ONE band (WO-1664 §5 pin 5)
        // =====================================================================
        //
        //  ⛔ THE HALF-LANDING PIN. AddReadyBand seats the raid door on the SAME y band as
        //  COLLECT and has said so in a comment since WO-1408. A comment is not an assert:
        //  moving one and not the other leaves two faces of different heights side by side,
        //  one of them clamp-rescued. WO-1664 replaced both pairs of literals with the shared
        //  ActionBandY0/ActionBandY1 constants; this case pins that they stay shared, which is
        //  a stronger statement than "the numbers currently match".
        private static void CaseOneBand(List<string> failures, List<string> notes)
        {
            const string Tag = "[welcomeback-collect-and-raid-door-share-one-band]";
            string src = ReadOrNull(PopupSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + PopupSrc); return; }

            int uses = CountOf(src, "ActionBandY0") + CountOf(src, "ActionBandY1");
            // 2 declarations + 2 per call site (COLLECT and the raid door) = 6.
            if (uses < 6)
            {
                failures.Add(Tag + " ActionBandY0/ActionBandY1 appear " + uses + " times in " + PopupSrc +
                             " (expected at least 6: two declarations plus both faces of the bottom " +
                             "band). One of COLLECT or the WO-1408 raid door has gone back to a " +
                             "hand-typed band and the pair can now desynchronise.");
                return;
            }
            if (src.IndexOf("ActionBandY1), CollectAndDismiss", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " COLLECT is no longer seated on the shared band (the " +
                             "'ActionBandY1), CollectAndDismiss' pairing is gone) -- " +
                             "AwaySummaryReportRegression case5 pins the same string for the VERB; " +
                             "this case pins it for the BAND.");
            // Whitespace-tolerant on purpose: the raid door's arguments wrap across lines and a
            // literal-string pin would fail on a re-indent, which is a pin that cries wolf.
            if (!Regex.IsMatch(src, @"ActionBandY1\s*\)\s*,\s*CollectThenRouteReady"))
                failures.Add(Tag + " the WO-1408 raid door is no longer seated on the shared band " +
                             "(no 'ActionBandY1)' immediately before CollectThenRouteReady) -- it has " +
                             "drifted off COLLECT's band, which is the half-landing this case exists for.");
            if (failures.Count == 0 || !failures[failures.Count - 1].StartsWith(Tag, StringComparison.Ordinal))
                notes.Add("[one-band] COLLECT and the raid door both seat on ActionBandY0..ActionBandY1");
        }

        // =====================================================================
        //  CASE — the capture paths READ their touch oracle (WO-1664 §5 pin 1)
        // =====================================================================
        //
        //  ⛔ THE PIN THAT MAKES EVERY OTHER PIN IN THIS FILE REDUNDANT ONE DAY, AND THE ONLY
        //  ONE THAT EXPLAINS HOW FIVE SUB-FLOOR CONTROLS SHIPPED GREEN. LayoutOracle.Audit has
        //  raised FindingKind.SubTouchFloorBand since WO-1060 and UICaptureLaunch has routed the
        //  "SUB-TOUCH-FLOOR BAND" prefix into the touch tally since then too. The oracle was never
        //  missing. Two entry points computed the tally on every run and never reported it:
        //    RunFrontDoorCaptureHeadless    -- Title + Login (three sub-floor faces)
        //    RunWelcomeBackCaptureHeadless  -- the offline-haul modal (COLLECT, and the door row)
        //  WO-1644 fixed exactly this for the GLYPH oracle on the front-door path and left the
        //  touch half thrown away, which is why its own comment there ("this path was the odd one
        //  out") was still true after it landed.
        //
        //  ⚠ SOURCE LINT, AND IT SAYS SO. It proves the CALL is present in the method body; it
        //  does not prove the run went green. The marker on a fresh log is that authority.
        private static void CaseOraclesAreRead(List<string> failures, List<string> notes)
        {
            const string Tag = "[oracles-are-read]";
            string src = ReadOrNull(CaptureSrc);
            if (src == null) { failures.Add(Tag + " cannot read " + CaptureSrc); return; }

            string[] entries = { "RunFrontDoorCaptureHeadless", "RunWelcomeBackCaptureHeadless" };
            for (int i = 0; i < entries.Length; i++)
            {
                string body = MethodBody(src, entries[i]);
                if (body == null)
                {
                    failures.Add(Tag + " " + entries[i] + " not found in " + CaptureSrc);
                    continue;
                }
                if (body.IndexOf("ReportTouchOracle()", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " " + entries[i] + " does not call ReportTouchOracle() -- it " +
                                 "builds and MEASURES its panels through AuditGeometry and then throws " +
                                 "the clamp/overlap verdict away, which is how the title row's three " +
                                 "sub-floor faces reached a device (WO-1664).");
                if (body.IndexOf("_touchPanelsChecked = 0", StringComparison.Ordinal) < 0 ||
                    body.IndexOf("_touchFailures.Clear()", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " " + entries[i] + " does not reset the touch tally before " +
                                 "capturing (_touchFailures.Clear() + _touchPanelsChecked = 0) -- a " +
                                 "report over a tally another entry point left behind is not this " +
                                 "run's verdict.");
                if (body.IndexOf("_touchFailures.Count == 0", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " " + entries[i] + "'s own OK/FAIL verdict does not fold in the " +
                                 "touch tally -- a capture that renders clean PNGs of a panel whose " +
                                 "faces are under the touch floor is not a pass, and a green capture " +
                                 "marker printed beside UI_TOUCH_FAIL is the split verdict CLAUDE.md 8 " +
                                 "warns about.");
            }

            // WO-1664 §6: TouchBaseline is shrink-only. None of this ticket's panels may be added.
            string[] forbidden = { "\"Title\"", "\"WelcomeBack\"", "\"HubRepair\"" };
            int baselineAt = src.IndexOf("string[] TouchBaseline", StringComparison.Ordinal);
            if (baselineAt >= 0)
            {
                int end = src.IndexOf("};", baselineAt, StringComparison.Ordinal);
                string list = end > baselineAt ? src.Substring(baselineAt, end - baselineAt) : string.Empty;
                for (int i = 0; i < forbidden.Length; i++)
                    if (list.IndexOf(forbidden[i], StringComparison.Ordinal) >= 0)
                        failures.Add(Tag + " TouchBaseline now contains " + forbidden[i] + " -- WO-1664 " +
                                     "fixed these bands at their driver; suppressing them instead " +
                                     "inverts the ticket, and that list is shrink-only by owner ruling.");
            }

            notes.Add("[oracles-are-read] front-door and welcome-back capture paths reset, report and " +
                      "fold in the WO-1060 touch tally (source lint -- the marker on a fresh log is " +
                      "what proves the run)");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static float ParseF(string s, float fallback)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static bool TryConst(string src, string name, out float value)
        {
            value = 0f;
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*(-?\d+(?:\.\d+)?)f?\s*[;,]");
            if (!m.Success) return false;
            return float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>The FIRST `rt.anchorMin/anchorMax = new Vector2(x, Y)` in a method body,
        /// returning Y. Deliberately not a general parser: it reads the one authoring shape
        /// these two methods use, and returns false rather than guessing when that shape moves.</summary>
        private static bool TryAnchorY(string methodBody, string which, out float y)
        {
            y = 0f;
            var m = Regex.Match(methodBody,
                @"\b" + Regex.Escape(which) + @"\s*=\s*new\s+Vector2\(\s*-?\d+(?:\.\d+)?f\s*,\s*(-?\d+(?:\.\d+)?)f\s*\)");
            if (!m.Success) return false;
            y = ParseF(m.Groups[1].Value, 0f);
            return true;
        }

        private static int CountOf(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>Brace-matched body of the named method. Returns null when the method is not
        /// found or its braces do not close — a pin that silently matched the WHOLE FILE would
        /// pass on a call sitting in a different method, which is the failure mode this suite is
        /// literally about.</summary>
        private static string MethodBody(string src, string methodName)
        {
            var m = Regex.Match(src, @"\b" + Regex.Escape(methodName) + @"\s*\([^)]*\)\s*\{");
            if (!m.Success) return null;
            int open = src.IndexOf('{', m.Index);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                char ch = src[i];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }
    }
}

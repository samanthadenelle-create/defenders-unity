// =============================================================================
// BuildMenuLayoutRegression [buildmenu-layout] (WO-878) - the build menu can never
// stack a control on top of another one again.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// WHAT BROKE (captures: UI_REVIEW/16_Build Menu/delivered.png @2670x1200 and
// docs/ui-review/screens-2026-08-04/BuildMenuUpgradeTower_2340x1080.png). One defect
// class - a band sized as a FRACTION OF ITS PARENT - reproducible on paper:
//
//   THE GEOMETRY (derived here so every number below is checkable).
//     The kit canvas is 1080x1920 with MatchWidthOrHeight 0.5, so at screen w x h the
//     canvas resolves to sqrt(1080*1920*h/w) REFERENCE px tall:
//         2340x1080 -> 978.3     2670x1200 (the Seeker's real surface) -> 965.4
//         1920x1080 (the headless capture aspect) -> 1080.0
//     The modal takes ModalHeightFrac of that, and ElarionUiKit's close-band
//     reservation then raises the FrameCore body floor:
//         closeBandTop = 0.050 + CanonCtaHeight/panelH
//         footer  re-seated to [closeBandTop+0.015, +0.130]   (FrameCore's authored height)
//         body    floor = footer top + 0.015 = closeBandTop + 0.160,  body top = 0.835
//     so the usable body is
//         bodyPx = (0.835 - 0.210)*panelH - CanonCtaHeight = 0.625*panelH - CanonCtaHeight
//
//     At the SHIPPED anchors (0.10-0.90 => panelH 782.6 @2340x1080) that is 357 px.
//     Against 357 px the shipped fractions resolved to:
//         root verb row 0.115 -> 41 px      "< Back" 0.14  -> 50 px
//         upgrade CTA   0.15  -> 54 px      info row 0.095 -> 34 px
//     ALL of them under ElarionUiKit.MinTouchPx (112). ClampMinTouch grows a sub-floor
//     button SYMMETRICALLY ABOUT ITS CENTRE after layout, so each gained 30-36 px on
//     EACH side: the five root verbs (stride 48.9 px, grown height 112 px) overlapped
//     by ~63 px and sliced one another's labels (the "Build T... / U... T... / R... W..."
//     in the Build Menu capture), "< Back" grew through the "UPGRADE TOWER" title, and
//     the Upgrade CTA grew up into the cost/preview text.
//
// THE FIX IS A FIXED-REFERENCE-PIXEL LADDER (DeNelle.Village.BuildMenuLayout). This
// oracle is the cheap structural guard on it - all headlessly decidable:
//
//   1 [touch-floor]  every band that carries a button is >= the kit touch floor, so
//                    ClampMinTouch is provably a NO-OP and cannot grow into a neighbour.
//   2 [line-box]     every text band is at least one TMP line box at the font it renders.
//   3 [body-fits]    THE assertion the shipped layout would have failed: the whole ladder
//                    fits the DERIVED body height at all three capture aspects - the root
//                    grid with every verb visible, and the sub-screen ladder with at least
//                    one full list row left between the nav and action bands.
//   4 [disjoint]     the two horizontal splits (Back|title, info|CTA) do not cross, and
//                    the source still lays every band with the fixed-px helpers - the
//                    retired fraction constants may not come back.
//   5 [vm-strings]   the cost / preview / CTA-label strings are assembled in BuildMenuVM,
//                    not in the View (the View lays out and renders; it computes nothing).
//   6 [cost-line]    BEHAVIOURAL: the VM's cost line, driven over an injected ledger,
//                    states the on-hand amount per axis with an ASCII +/- mark - state is
//                    TEXT-encoded, never carried by colour alone (the owner is red/green
//                    colourblind).
//
// A live "no two rects overlap" assertion needs a canvas at both aspects; that stays the
// job of RunCaptureHeadless + eyes-on. This oracle catches the REGRESSION (someone
// re-authors a band as a fraction, drops one under the touch floor, or moves a string
// back into the View), which is the failure mode that recurs.
//
// Markers: BUILDMENU_LAYOUT_OK / BUILDMENU_LAYOUT_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.BuildMenuLayoutRegression.RunAll
// Registered in DataRegression.RunAll as the "buildmenu-layout suite".
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.UI;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Editor.Regression
{
    public static class BuildMenuLayoutRegression
    {
        private const string ViewSrc = "Assets/_Modules/Village/Buildings/UI/BuildMenu.cs";
        private const string VmSrc   = "Assets/_Modules/Village/Buildings/UI/BuildMenuVM.cs";

        // Read by REFLECTION (the RumorBoardLayoutRegression precedent) so this oracle needs
        // no UnityEngine.UI / TextMeshPro asmdef reference: the kit + style types carry those
        // in their member signatures. BuildMenuLayout itself is referenced DIRECTLY - it is
        // pure constants, so a rename breaks the compile loudly instead of silently skipping.
        private const string KitType = "DeNelle.Core.UI.ElarionUiKit";
        private const string UiType  = "DeNelle.Core.UI.ElarionUi";

        /// <summary>The TMP line box multiplier the bands are budgeted from (~1.25em) - the same
        /// figure the sibling layout oracles use.</summary>
        private const float LineBoxMul = 1.25f;

        // ── The kit's FrameCore + close-band reservation constants (private to the kit,
        //    restated here with the derivation in the header so the body height is COMPUTED
        //    rather than copied). If the kit changes these, case 3 is the tripwire.
        private const float CloseBandY     = 0.050f;   // DefaultCloseZone.y
        private const float ZoneGap        = 0.015f;   // reservation gap above close / above footer
        private const float FooterHeight   = 0.130f;   // FrameCore's authored footer band height
        private const float BodyTopFrac    = 0.835f;   // FrameCore body zone top

        /// <summary>The capture aspects this ladder must hold at: the two review aspects plus the
        /// headless capture aspect (1920x1080 - the one that hid WO-866 because it is TALLER).</summary>
        private static readonly (string Name, float W, float H)[] Aspects =
        {
            ("2340x1080", 2340f, 1080f),
            ("2670x1200 (Seeker)", 2670f, 1200f),
            ("1920x1080 (headless capture)", 1920f, 1080f),
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("BUILDMENU_LAYOUT_OK - " + reason);
            else Debug.LogError("BUILDMENU_LAYOUT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "touch-floor", () => CaseTouchFloor(failures, notes));
                Case(failures, "line-box",    () => CaseLineBox(failures, notes));
                Case(failures, "body-fits",   () => CaseBodyFits(failures, notes));
                Case(failures, "disjoint",    () => CaseDisjoint(failures, notes));
                Case(failures, "vm-strings",  () => CaseVmOwnsStrings(failures, notes));
                Case(failures, "cost-line",   () => CaseCostLine(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "BUILDMENU LAYOUT OK - every band that carries a button is at the kit touch " +
                         "floor (ClampMinTouch is a no-op, so nothing can grow into a neighbour), every " +
                         "text band is a whole line box, the root grid and the sub-screen ladder both fit " +
                         "the derived body at all " + Aspects.Length + " capture aspects, the Back|title and " +
                         "info|CTA splits do not cross, and the cost/preview/CTA strings are the VM's" + noteStr;
                return true;
            }
            reason = "buildmenu-layout FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 [touch-floor] - the growth that caused the overlap cannot fire
        // =====================================================================
        private static void CaseTouchFloor(List<string> failures, List<string> notes)
        {
            float minTouch = KitFloat("MinTouchPx", failures, "[touch-floor]");
            if (minTouch <= 0f) return;

            AssertFloor(failures, "[touch-floor]", "NavBandPx",   BuildMenuLayout.NavBandPx,   minTouch,
                "the '< Back' button lives in it; at 0.14 of the body it resolved to 50 px and grew 31 px " +
                "past EACH edge, straight through the screen title");
            AssertFloor(failures, "[touch-floor]", "ActionBandPx", BuildMenuLayout.ActionBandPx, minTouch,
                "the primary CTA lives in it; at 0.15 of the body it resolved to 54 px and grew up into the " +
                "cost/preview text - the WO-878 defect verbatim");
            AssertFloor(failures, "[touch-floor]", "RowPx",        BuildMenuLayout.RowPx,        minTouch,
                "a list row IS the tap target; at 96 px it grew 8 px each side and consumed the inter-row spacing");
            AssertFloor(failures, "[touch-floor]", "RootCellPx",   BuildMenuLayout.RootCellPx,   minTouch,
                "the five root verbs each carry a button; at 0.115 of the body they resolved to 41 px on a " +
                "48.9 px stride and overlapped by ~63 px, which is the sliced labels in the capture");

            if (BuildMenuLayout.BandGapPx <= 0f)
                failures.Add("[touch-floor] BuildMenuLayout.BandGapPx=" + BuildMenuLayout.BandGapPx +
                             " - two bands with no gap are one band; a positive fixed gap is what makes " +
                             "them provably disjoint");
            if (BuildMenuLayout.RowGapPx <= 0f)
                failures.Add("[touch-floor] BuildMenuLayout.RowGapPx=" + BuildMenuLayout.RowGapPx + " - rows would abut");
            if (BuildMenuLayout.RootColumns < 2)
                failures.Add("[touch-floor] BuildMenuLayout.RootColumns=" + BuildMenuLayout.RootColumns +
                             " - " + BuildMenuLayout.RootVerbCount + " verbs at the " + minTouch +
                             " px floor need " + (BuildMenuLayout.RootVerbCount * minTouch) + " px stacked in a " +
                             "body that is ~423 px tall; a single column cannot show them all");

            notes.Add("touch floor " + minTouch + " px");
        }

        private static void AssertFloor(List<string> failures, string tag, string name, float value, float floor, string why)
        {
            if (value < floor)
                failures.Add(tag + " BuildMenuLayout." + name + "=" + value + " is BELOW the kit touch floor " +
                             floor + " - ClampMinTouch would grow it symmetrically about its centre and it " +
                             "would overlap its neighbours (" + why + ")");
        }

        // =====================================================================
        //  CASE 2 [line-box] - no band is shorter than the text it renders
        // =====================================================================
        private static void CaseLineBox(List<string> failures, List<string> notes)
        {
            float fontHead  = UiFloat("FontHead",  failures, "[line-box]");
            float fontLabel = UiFloat("FontLabel", failures, "[line-box]");
            float fontMicro = UiFloat("FontMicro", failures, "[line-box]");
            if (fontHead <= 0f || fontLabel <= 0f || fontMicro <= 0f) return;

            float headLine  = fontHead * LineBoxMul;
            float labelLine = fontLabel * LineBoxMul;
            float microLine = fontMicro * LineBoxMul;

            if (BuildMenuLayout.NavBandPx < headLine)
                failures.Add("[line-box] NavBandPx=" + BuildMenuLayout.NavBandPx + " is shorter than one FontHead " +
                             "line box (" + headLine + ") - the screen title would be culled");
            if (BuildMenuLayout.InfoLinePx < labelLine)
                failures.Add("[line-box] InfoLinePx=" + BuildMenuLayout.InfoLinePx + " is shorter than one FontLabel " +
                             "line box (" + labelLine + ") - the cost line would clip or spill into the line below it");
            if (BuildMenuLayout.InfoLinePx < microLine)
                failures.Add("[line-box] InfoLinePx=" + BuildMenuLayout.InfoLinePx + " is shorter than one FontMicro " +
                             "line box (" + microLine + ") - the preview line would clip");
            // ── WO-1636: THE ROWS MUST SEAT **TWO** LINE BOXES, NOT ONE ─────────────────────
            // The three asserts above only ever demanded ONE line box per info row, and that was
            // exactly the shipped defect: at ActionBandPx 112 each row was 56 px = one 30 px line
            // box, so the preview SENTENCE the VM emits could only ellipsise. Measured on chain 17
            // (BuildMenuUpgradeTower_1920x1080): "Lvl 1 to 2:  dmg 23.8 to 46.8,  range 18m to 22m"
            // drew 33 of 35 printable glyphs in a 604.1 x 56.0 px rect at font 30. The old asserts
            // were GREEN throughout - a pin that passes while the screen cuts its own copy is not
            // covering the thing it names, so the contract is stated here in full.
            // The floor is the MOBILE font floor, not FontLabel/FontMicro: FitBlock is free to
            // autosize DOWN to ElarionUi.FontFloorMobile, and the two-line requirement has to hold
            // at the size the text will actually resolve to when it is long.
            float floorMobile = UiFloat("FontFloorMobile", failures, "[line-box]");
            if (floorMobile > 0f)
            {
                float twoFloorLines = 2f * floorMobile * LineBoxMul;
                if (BuildMenuLayout.InfoLinePx < twoFloorLines)
                    failures.Add("[line-box] InfoLinePx=" + BuildMenuLayout.InfoLinePx + " cannot seat TWO " +
                                 "line boxes at the mobile font floor (" + twoFloorLines + ") - the cost and " +
                                 "preview lines are SENTENCES (BuildMenuVM.UpgradeCostLineFor / " +
                                 "UpgradeStatLineFor), the longest measure ~731 px against a ~538 px lane, and " +
                                 "a one-line row can only ellipsise them. Raise ActionBandPx (its ceiling is " +
                                 "the [body-fits] case below); NEVER shorten the copy to fit one line and " +
                                 "NEVER lower the floor");
            }
            if (Mathf.Abs(BuildMenuLayout.InfoLinePx * 2f - BuildMenuLayout.ActionBandPx) > 0.01f)
                failures.Add("[line-box] InfoLinePx(" + BuildMenuLayout.InfoLinePx + ") x2 != ActionBandPx(" +
                             BuildMenuLayout.ActionBandPx + ") - the two preview lines must exactly tile the " +
                             "action band, or one of them overhangs it");

            notes.Add("info line " + BuildMenuLayout.InfoLinePx + "px vs label line box " + labelLine);
        }

        // =====================================================================
        //  CASE 3 [body-fits] - THE assertion the shipped layout would have failed
        // =====================================================================
        private static void CaseBodyFits(List<string> failures, List<string> notes)
        {
            float ctaH = KitFloat("CanonCtaHeight", failures, "[body-fits]");
            if (ctaH <= 0f) return;

            foreach (var a in Aspects)
            {
                float canvasH = Mathf.Sqrt(1080f * 1920f * a.H / a.W);
                float panelH  = BuildMenuLayout.ModalHeightFrac * canvasH;
                // closeBandTop + ZoneGap + FooterHeight + ZoneGap = the reserved floor.
                float reservedFloor = CloseBandY + ctaH / panelH + ZoneGap + FooterHeight + ZoneGap;
                float bodyPx = (BodyTopFrac - reservedFloor) * panelH;

                if (bodyPx < BuildMenuLayout.RootGridHeightPx)
                    failures.Add("[body-fits] the root verb grid needs " + BuildMenuLayout.RootGridHeightPx +
                                 " px but the body resolves to " + bodyPx.ToString("0.#") + " px at " + a.Name +
                                 " - a verb would be cut off. Widen the grid (RootColumns) or raise " +
                                 "ModalHeightFrac; NEVER shrink a cell under the touch floor.");

                float need = BuildMenuLayout.SubScreenFixedPx + BuildMenuLayout.RowPx;
                if (bodyPx < need)
                    failures.Add("[body-fits] the sub-screen ladder (nav " + BuildMenuLayout.NavBandPx + " + gap + " +
                                 "action " + BuildMenuLayout.ActionBandPx + " + gap = " + BuildMenuLayout.SubScreenFixedPx +
                                 " px) leaves less than ONE " + BuildMenuLayout.RowPx + " px list row in a " +
                                 bodyPx.ToString("0.#") + " px body at " + a.Name + " - the scroll well would " +
                                 "render no row at all (this is the WO-866 negative-band class)");

                notes.Add(a.Name + ": body " + bodyPx.ToString("0.#") + "px, list well " +
                          (bodyPx - BuildMenuLayout.SubScreenFixedPx).ToString("0.#") + "px");
            }
        }

        // =====================================================================
        //  CASE 4 [disjoint] - the horizontal splits + the source laws
        // =====================================================================
        private static void CaseDisjoint(List<string> failures, List<string> notes)
        {
            if (BuildMenuLayout.BackWidthFrac > BuildMenuLayout.TitleLeftFrac)
                failures.Add("[disjoint] BackWidthFrac(" + BuildMenuLayout.BackWidthFrac + ") reaches past " +
                             "TitleLeftFrac(" + BuildMenuLayout.TitleLeftFrac + ") - the screen title would sit " +
                             "on the Back button, which is the '\"UPGRADE TOWER\" clips Back' half of WO-878");
            if (BuildMenuLayout.InfoWidthFrac > BuildMenuLayout.CtaLeftFrac)
                failures.Add("[disjoint] InfoWidthFrac(" + BuildMenuLayout.InfoWidthFrac + ") reaches past " +
                             "CtaLeftFrac(" + BuildMenuLayout.CtaLeftFrac + ") - the cost/preview text would be " +
                             "drawn over the primary CTA, which is WO-878 verbatim");
            if (BuildMenuLayout.CtaLeftFrac >= 1f || BuildMenuLayout.TitleLeftFrac >= 1f)
                failures.Add("[disjoint] a right-hand split starts at or past the band's right edge");
            if (BuildMenuLayout.RootCellPadFrac <= 0f)
                failures.Add("[disjoint] RootCellPadFrac=" + BuildMenuLayout.RootCellPadFrac +
                             " - adjacent grid cells would abut with no seam");

            string raw = ReadSource(ViewSrc, failures, "[disjoint]");
            if (raw == null) return;
            string src = StripComments(raw);

            // The ladder must actually be used, through the fixed-px helpers.
            foreach (string helper in new[] { "PxBandFromTop", "PxBandFromBottom", "PxStretchBand" })
                if (src.IndexOf(helper, StringComparison.Ordinal) < 0)
                    failures.Add("[disjoint] BuildMenu.cs no longer uses " + helper + " - a band authored as a " +
                                 "fraction of the body is the WO-841/852/865/878 failure class");
            if (src.IndexOf("BuildMenuLayout.", StringComparison.Ordinal) < 0)
                failures.Add("[disjoint] BuildMenu.cs no longer reads BuildMenuLayout - the band ladder this " +
                             "oracle pins is not the one the screen draws");

            // The retired FRACTION constants must stay retired.
            foreach (string retired in new[] { "BackButtonHeight", "UpgradeInfoTop", "UpgradeInfoRowHeight",
                                               "UpgradeCtaTop", "UpgradeCtaBottom", "UpgradeRowPixelH" })
                if (Regex.IsMatch(src, @"\b" + retired + @"\b"))
                    failures.Add("[disjoint] BuildMenu.cs declares " + retired + " again - that is a " +
                                 "fraction-of-parent band, and every one of them resolved under the touch floor");
            if (Regex.IsMatch(src, @"const\s+float\s+rowH\s*=\s*0\."))
                failures.Add("[disjoint] BuildMenu.cs re-introduces a fractional row height (const float rowH = 0.x) - " +
                             "row heights are FIXED PIXELS at the touch floor");

            // Style-everything-obsidian (the UiObsidianConformanceRegression law).
            if (src.IndexOf("ElarionUiKit", StringComparison.Ordinal) < 0)
                failures.Add("[disjoint] BuildMenu.cs does not go through ElarionUiKit - the hand-rolled-uGUI law");

            // ASCII-only where it MATTERS: the code (string literals included). Prose comments in
            // this file carry em-dashes from earlier work orders and are not rendered by TMP.
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] > 127)
                {
                    int line = 1;
                    for (int j = 0; j < i; j++) if (src[j] == '\n') line++;
                    failures.Add("[disjoint] BuildMenu.cs carries a NON-ASCII character (U+" +
                                 ((int)src[i]).ToString("X4") + ") in CODE at line " + line +
                                 " - a non-ASCII glyph renders as tofu on the shipped TMP font");
                    break;
                }
            }
            if (raw.IndexOf('\0') >= 0)
                failures.Add("[disjoint] BuildMenu.cs contains an embedded NUL byte (mount-garble, CLAUDE.md Sec.0)");

            notes.Add("source laws checked on " + ViewSrc);
        }

        // =====================================================================
        //  CASE 5 [vm-strings] - presentation renders, the VM computes
        // =====================================================================
        private static readonly string[] VmLineMembers =
        {
            "CostSummaryFor", "BuildDetailLineFor", "BuildCtaLabelFor",
            "UpgradeCostLineFor", "UpgradeStatLineFor", "UpgradeCtaLabelFor",
        };

        private static void CaseVmOwnsStrings(List<string> failures, List<string> notes)
        {
            string vmRaw = ReadSource(VmSrc, failures, "[vm-strings]");
            string viewRaw = ReadSource(ViewSrc, failures, "[vm-strings]");
            if (vmRaw == null || viewRaw == null) return;
            string vm = StripComments(vmRaw);
            string view = StripComments(viewRaw);

            foreach (string member in VmLineMembers)
            {
                if (vm.IndexOf(member, StringComparison.Ordinal) < 0)
                    failures.Add("[vm-strings] BuildMenuVM no longer exposes " + member + " - the line it owns " +
                                 "has moved back into presentation");
                if (view.IndexOf("_vm." + member, StringComparison.Ordinal) < 0)
                    failures.Add("[vm-strings] BuildMenu.cs no longer renders _vm." + member + " - the View is " +
                                 "assembling that string itself again");
            }

            // The tells of a View that computes: a hand-built cost line, or the five-way upgrade
            // availability switch that used to live in BuildUpgradeInfoBlock.
            if (Regex.IsMatch(view, "\"Cost: \"") || Regex.IsMatch(view, "\" wood, \"") ||
                Regex.IsMatch(view, "\" crystals\""))
                failures.Add("[vm-strings] BuildMenu.cs assembles a cost string itself - cost/affordability text " +
                             "is BuildMenuVM.CostSummaryFor (WO-878: the View computes nothing)");
            if (view.IndexOf("UpgradeAvailability.", StringComparison.Ordinal) >= 0)
                failures.Add("[vm-strings] BuildMenu.cs switches on BuildMenuVM.UpgradeAvailability - deciding " +
                             "which sentence a state deserves is the VM's job; the View renders the sentence");

            // Strict MVVM ([ui-mvvm] ratchet): the View reads the VM, never the services.
            foreach (string forbidden in new[] { "EconomyService", "GameStateService", "CatalogRegistry" })
                if (Regex.IsMatch(view, @"\b" + forbidden + @"\s*\."))
                    failures.Add("[vm-strings] BuildMenu.cs touches " + forbidden + " directly - the View is a " +
                                 "read-only consumer of BuildMenuVM (strict MVVM, [ui-mvvm] ratchet armed)");

            notes.Add(VmLineMembers.Length + " VM-owned lines rendered by the View");
        }

        // =====================================================================
        //  CASE 6 [cost-line] - BEHAVIOURAL: the moved string still reads the ledger
        // =====================================================================
        private sealed class FakeLedger : IEconomy
        {
            public int Coins { get; set; }
            public int Wood { get; set; }
            public int Iron { get; set; }
            public int Food { get; set; }
            public int Crystals { get; set; }

            public event Action<ResourceSnapshot> OnChanged;

            public bool CanAfford(DeNelle.Village.ResourceCost cost)
                => Wood >= cost.Wood && Food >= cost.Food && Iron >= cost.Iron
                   && Crystals >= cost.Crystals && Coins >= cost.Coins;

            public bool TrySpend(DeNelle.Village.ResourceCost cost)
            {
                if (!CanAfford(cost)) return false;
                Wood -= cost.Wood; Food -= cost.Food; Iron -= cost.Iron;
                Crystals -= cost.Crystals; Coins -= cost.Coins;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Food, Iron, Crystals));
                return true;
            }

            public DeNelle.Village.ResourceCost Grant(DeNelle.Village.ResourceCost amount)
            {
                Wood += amount.Wood; Food += amount.Food; Iron += amount.Iron;
                Crystals += amount.Crystals; Coins += amount.Coins;
                OnChanged?.Invoke(new ResourceSnapshot(Wood, Food, Iron, Crystals));
                // Uncapped fake ledger: every requested unit lands, so applied == requested.
                return amount;
            }
        }

        private static void CaseCostLine(List<string> failures, List<string> notes)
        {
            var ledger = new FakeLedger { Wood = 250, Iron = 12, Food = 0, Crystals = 0 };
            var vm = new BuildMenuVM(ledger, new PlacedTowerListVM(() => new Tower[0]), null, 0, null);
            var cost = new CoreCost { wood = 70, iron = 40 };

            string line = vm.CostSummaryFor(cost) ?? string.Empty;

            if (line.IndexOf("Wood: 70 (+250)", StringComparison.Ordinal) < 0)
                failures.Add("[cost-line] CostSummaryFor did not state the covered axis as 'Wood: 70 (+250)' - got '" +
                             line + "'. The mark is the TEXT encoding of affordability; the owner is red/green " +
                             "colourblind, so a colour may only ever reinforce a state already spelled out.");
            if (line.IndexOf("Iron: 40 (-12)", StringComparison.Ordinal) < 0)
                failures.Add("[cost-line] CostSummaryFor did not state the SHORT axis as 'Iron: 40 (-12)' - got '" +
                             line + "'. The on-hand number must come from the live ledger (the WO-861 fake-wallet " +
                             "literals were 20/5).");
            if (line.IndexOf("Food", StringComparison.Ordinal) >= 0 || line.IndexOf("Crystals", StringComparison.Ordinal) >= 0)
                failures.Add("[cost-line] CostSummaryFor listed an axis the tower does not cost: '" + line + "'");
            for (int i = 0; i < line.Length; i++)
                if (line[i] > 127)
                {
                    failures.Add("[cost-line] CostSummaryFor emitted a NON-ASCII character (U+" +
                                 ((int)line[i]).ToString("X4") + ") - it renders as tofu on the shipped TMP font");
                    break;
                }

            // The CTA label must STATE the blocked state rather than relying on a grey face.
            var option = new BuildMenuVM.TowerBuildOption("tower_test", "Test Tower", cost, 10f, 5f);
            string cta = vm.BuildCtaLabelFor(option) ?? string.Empty;
            if (cta == "Build")
                failures.Add("[cost-line] BuildCtaLabelFor returned the bare verb 'Build' for an UNAFFORDABLE " +
                             "tower (iron 12 of 40) - the button must state why it is dead");
            if (cta.Length == 0)
                failures.Add("[cost-line] BuildCtaLabelFor returned an EMPTY label - a blank button is a button " +
                             "with no state at all");

            ledger.Iron = 40;
            if (vm.BuildCtaLabelFor(option) != "Build")
                failures.Add("[cost-line] BuildCtaLabelFor still refuses after the ledger was funded ('" +
                             vm.BuildCtaLabelFor(option) + "') - the label is not re-read from the live ledger");

            notes.Add("cost line '" + line + "'");
            vm.Dispose();
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static float KitFloat(string name, List<string> failures, string tag)
            => ConstFloat(KitType, name, failures, tag);

        private static float UiFloat(string name, List<string> failures, string tag)
            => ConstFloat(UiType, name, failures, tag);

        /// <summary>Read a public const by reflection (no UnityEngine.UI / TMP asmdef reference needed).</summary>
        private static float ConstFloat(string typeName, string name, List<string> failures, string tag)
        {
            Type t = FindType(typeName);
            if (t == null)
            {
                failures.Add(tag + " " + typeName + " not found - cannot read " + name);
                return 0f;
            }
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (f == null)
            {
                failures.Add(tag + " " + t.Name + "." + name + " does not exist - the constant this oracle pins " +
                             "was renamed or removed; re-point it rather than deleting the guard");
                return 0f;
            }
            object v = f.GetValue(null);
            if (v is float fv) return fv;
            if (v is int iv) return iv;
            if (v is double dv) return (float)dv;
            failures.Add(tag + " " + t.Name + "." + name + " is not a numeric constant");
            return 0f;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); }
                catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static string ReadSource(string path, List<string> failures, string tag)
        {
            try
            {
                string abs = path;
                if (!Path.IsPathRooted(abs))
                {
                    var parent = Directory.GetParent(Application.dataPath);
                    string root = parent != null ? parent.FullName : Directory.GetCurrentDirectory();
                    abs = Path.Combine(root, path);
                }
                if (!File.Exists(abs))
                {
                    failures.Add(tag + " source not found: " + path);
                    return null;
                }
                return File.ReadAllText(abs);
            }
            catch (Exception ex)
            {
                failures.Add(tag + " could not read " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Blank out // and block comments so a lesson written in prose (which quotes the
        /// retired fractions) can never fail a source law.</summary>
        private static string StripComments(string src)
        {
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\n]*", " ");
        }
    }
}

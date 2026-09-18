// UI-001: independent source oracle for the landscape Night Market and its persistent HUD door.
// Presentation only. It deliberately does not inspect or alter PurchaseGate/payment transport.
//
// =============================================================================
//  ⚠ RE-POINTED 2026-08-23 — READ THIS BEFORE "FIXING" A RED FROM THIS FILE.
// -----------------------------------------------------------------------------
//  This suite was written against the PRE-§R Night Market and then never
//  REGISTERED, so it had NEVER RUN. When it was registered it went red with 21
//  failures — and 20 of them were this ORACLE being stale, not the screen being
//  broken: the §R rebuild (2026-08-22) replaced a fraction-anchored two-column
//  layout with an authored REFERENCE-PIXEL three-band / three-column budget, and
//  replaced four inline MakeText calls per card with the one StorePackCard
//  template. The old assertions looked for `SpotlightMin`, `CardHeightPx`,
//  `pack.Name, 24` — identifiers the rebuild legitimately retired. An oracle that
//  names a vanished identifier does not detect a defect; it reports its own
//  staleness in the defect's voice, which is worse than silence.
//
//  ⛔ SO THE RULE FOR THIS FILE: assert the INTENT, read the CURRENT names, and
//  keep the THRESHOLDS independent. Every bound below is an acceptance bound
//  owned by this oracle — shrinking the implementation toward it still goes red.
//  What is NOT duplicated here are the kit's canon numbers (MinTouchPx,
//  CanonCtaWidth/Height, FontFloorMobile): those are PARSED OUT OF ElarionUiKit /
//  ElarionUi at run time, because ~25 files derive from them and a 26th copy in a
//  test is exactly the duplicated-state drift CLAUDE.md §2/§5/§16 keep recording.
//
//  ⛔ AND THE TEXT SCAN IS COMMENT/STRING-BLIND ON PURPOSE. The old
//  "more than one Realm Store routing authority" red was a FALSE POSITIVE: it
//  counted raw occurrences of `PanelRouter.Open(PanelId.RealmStore)` in
//  HudKitController, and three matched — one real call site, one explanatory
//  COMMENT, and one FlowTrace.Fail STRING that quotes the call it is reporting on.
//  Counting authority by substring punishes a file for documenting itself. The
//  count now runs over Code(), which strips comments and string literals first.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using DeNelle.Wallet;   // WO-1162: the composition + card template are the live authority now
using DeNelle.Core.UI;  // WO-1335: HudLayoutBands is the left column's ONE authority - resolved, not retyped

namespace DeNelle.Editor.Regression
{
    public static class NightMarketUiRegression
    {
        private const string StoreRel  = "/_Modules/Wallet/PackStore.cs";
        private const string CardRel   = "/_Modules/Wallet/StorePackCard.cs";
        private const string FooterRel = "/_Modules/Wallet/StoreLegalFooter.cs";
        private const string HudRel    = "/_Modules/HUD/Kit/HudKitController.cs";
        private const string AreasRel  = "/_Modules/HUD/Kit/HudAreasHost.cs";
        private const string DeckRel   = "/_Modules/HUD/PlayerDeckWorkspace.cs";
        private const string CompositionRel = "/_Modules/Wallet/NightMarketComposition.cs";
        private const string KitRel    = "/_Modules/Core/UI/ElarionUiKit.cs";
        private const string UiRel     = "/_Modules/Core/UI/ElarionUi.cs";

        // ── Independent acceptance bounds. Owned here, not imported. ──────────
        private const float MinPanelWidthShare   = 0.80f;  // the store is the screen (owner ruling 1)
        private const float MinBodyHeightShare   = 0.65f;  // vertical must not be given away to chrome
        private const float MinShelfWidthShare   = 0.40f;  // the shelf is the widest of the three columns
        private const float MinStandardCardPx    = 240f;   // readability floor for a priced card
        private const int   RequiredCardsPerRow  = 2;      // device-verified readability ruling

        [MenuItem("Tools/Regression/UI/Night Market Landscape")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("--- NIGHT MARKET UI-001 ---\n");

            string store  = Read(Application.dataPath + StoreRel,  failures);
            string card   = Read(Application.dataPath + CardRel,   failures);
            string footer = Read(Application.dataPath + FooterRel, failures);
            string hud    = Read(Application.dataPath + HudRel,    failures);
            string areas  = Read(Application.dataPath + AreasRel,  failures);
            string deck   = Read(Application.dataPath + DeckRel,   failures);
            string kit    = Read(Application.dataPath + KitRel,    failures);
            string ui     = Read(Application.dataPath + UiRel,     failures);

            // The kit's canon numbers, parsed — never re-declared (see header).
            float minTouch  = Scalar(kit, "MinTouchPx",       failures);
            float ctaWidth  = Scalar(kit, "CanonCtaWidth",    failures);
            float ctaHeight = Scalar(kit, "CanonCtaHeight",   failures);
            float fontFloor = Scalar(ui,  "FontFloorMobile",  failures);

            CheckLandscapeBudget(store, ctaHeight, failures);
            CheckCard(card, minTouch, fontFloor, failures);
            CheckFreeBand(store, minTouch, failures);
            CheckCommerceCta(store, minTouch, ctaWidth, failures);
            CheckOneTitleAndOneLegalOwner(store, footer, ctaWidth, failures);
            CheckHudDoor(hud, deck, failures);
            CheckWalletChip(store, failures);
            CheckHudStoreCard(hud, areas, minTouch, failures);
            CheckGapPacksAlwaysBuyable(store, failures);
            CheckStorewideSale(store, card, failures);

            if (failures.Count > 0)
            {
                reason = "NIGHT_MARKET_UI_FAIL\n - " + string.Join("\n - ", failures);
                return false;
            }

            log.Append("NIGHT_MARKET_UI_OK — landscape body, one visible title, readable cards, " +
                       "one legal owner, persistent single-authority HUD door");
            reason = log.ToString();
            return true;
        }

        // =====================================================================
        //  ⭐ WO-1800 — THE STOREWIDE SALE SIGNS, AND THE "FLASH".
        // ---------------------------------------------------------------------
        //  Owner, 2026-09-16: "put big sales signs with x% off!!!! you know some
        //  flash" + "also make the packs pulse when the store opens".
        //
        //  ⛔ THIS BLOCK IS DELIBERATELY NOT A SOURCE-TEXT SCAN. The rest of this
        //  file reads .cs bytes because layout budgets are constants; a sale badge
        //  is BEHAVIOUR over a server field, and a grep for `SaleBps` would pass
        //  on a file that read the field and drew nothing. So these cases call the
        //  LIVE types: the DTO is deserialized from real wire JSON, and the pulse
        //  curve and the contrast ratio are evaluated, not matched.
        //
        //  ⛔ THE FAIL-CLOSED CASES ARE THE POINT. "No sale ⇒ no badge" and
        //  "0 bps ⇒ no badge" are the two the owner would never see go wrong from
        //  the outside — a shelf reading "0% OFF" is a sale sign advertising
        //  nothing, on the one screen in the game that takes money.
        // =====================================================================

        /// <summary>The standard's text-contrast bar: WCAG AA, 4.5:1 (memory: mobile-ui-touch-contrast-standard).</summary>
        private const double MinBadgeContrastRatio = 4.5d;

        /// <summary>WO-1819: a badge PLATE must also separate from the surface it sits on. 3:1 is the
        /// standard's bar for a non-text graphical object, and it is the pair this suite used to omit.</summary>
        private const double MinPlateVsSurfaceRatio = 3.0d;

        private static void CheckStorewideSale(string store, string card, List<string> failures)
        {
            // ── 1. NO SALE ⇒ NO BADGE. The server's ordinary row, unchanged. ──
            var plain = Deserialize("{\"sku\":\"basket-small\",\"usdAnchor\":4.99,\"usdEffective\":4.99}", failures);
            if (plain != null)
            {
                if (plain.IsOnSale)
                    failures.Add("a quote with no saleBps reads as ON SALE.");
                if (!string.IsNullOrEmpty(plain.SaleBadgeText))
                    failures.Add($"a quote with no saleBps produced badge copy \"{plain.SaleBadgeText}\".");
                if (plain.HasStruckAnchor)
                    failures.Add("a quote with no saleBps wants its anchor struck through.");
            }

            // ── 2. A REAL SALE ⇒ "<pct>% OFF" + a struck anchor. ──────────────
            var sale = Deserialize("{\"sku\":\"basket-small\",\"usdAnchor\":4.99,\"usdEffective\":3.49," +
                                   "\"saleBps\":3000}", failures);
            if (sale != null)
            {
                if (!sale.IsOnSale) failures.Add("saleBps 3000 does not read as a sale.");
                if (sale.SaleBadgeText != "30% OFF")
                    failures.Add($"saleBps 3000 worded itself as \"{sale.SaleBadgeText}\", not \"30% OFF\".");
                if (!sale.HasStruckAnchor)
                    failures.Add("a sale with both an anchor and a lower effective price draws no strike.");
                if (sale.SaleAnchorLabel != "$4.99")
                    failures.Add($"struck anchor reads \"{sale.SaleAnchorLabel}\", not \"$4.99\".");
                if (sale.SaleEffectiveLabel != "$3.49")
                    failures.Add($"effective price reads \"{sale.SaleEffectiveLabel}\", not \"$3.49\".");
                string struck = DeNelle.Wallet.StorePackCard.Strike(sale.SaleAnchorLabel);
                if (struck != "<s>$4.99</s>")
                    failures.Add($"the strike markup is \"{struck}\" - TMP renders <s>...</s>.");
            }

            // ── 3. THE SERVER'S OWN COPY OUTRANKS THE bps CONVERSION. ─────────
            var authored = Deserialize("{\"sku\":\"x\",\"saleBps\":2500,\"saleLabel\":\"QUARTER OFF\"}", failures);
            if (authored != null && authored.SaleBadgeText != "QUARTER OFF")
                failures.Add($"an authored saleLabel was overridden by the bps conversion " +
                             $"(\"{authored.SaleBadgeText}\").");

            // ── 4. FAIL-CLOSED: 0 bps, and >= 10000 bps, draw NOTHING. ────────
            foreach (int bps in new[] { 0, -1, 10000, 12000 })
            {
                var bad = Deserialize("{\"sku\":\"x\",\"usdAnchor\":4.99,\"usdEffective\":1.00,\"saleBps\":" +
                                      bps.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", failures);
                if (bad == null) continue;
                if (bad.IsOnSale)
                    failures.Add($"saleBps {bps} reads as a usable sale - it must fail closed.");
                if (!string.IsNullOrEmpty(bad.SaleBadgeText))
                    failures.Add($"saleBps {bps} produced badge copy \"{bad.SaleBadgeText}\" " +
                                 "- there is no \"0% OFF\".");
            }

            // ── 5. A STRIKE NEEDS BOTH NUMBERS, never one. ─────────────────────
            var anchorOnly = Deserialize("{\"sku\":\"x\",\"usdAnchor\":4.99,\"saleBps\":3000}", failures);
            if (anchorOnly != null && anchorOnly.HasStruckAnchor)
                failures.Add("a sale with an anchor but NO effective price struck the only figure on the card.");

            // ── 6. THE COUNTDOWN — present, absent, and already expired. ───────
            var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
            var ends = Deserialize("{\"sku\":\"x\",\"saleBps\":3000,\"saleEndsAt\":\"2026-09-18T16:00:00Z\"}", failures);
            if (ends != null && ends.SaleCountdownLabel(now) != "ends in 2d 4h")
                failures.Add($"countdown reads \"{ends.SaleCountdownLabel(now)}\", not \"ends in 2d 4h\".");
            var noEnd = Deserialize("{\"sku\":\"x\",\"saleBps\":3000}", failures);
            if (noEnd != null && !string.IsNullOrEmpty(noEnd.SaleCountdownLabel(now)))
                failures.Add("a sale with no saleEndsAt invented a countdown.");
            var over = Deserialize("{\"sku\":\"x\",\"saleBps\":3000,\"saleEndsAt\":\"2026-09-15T00:00:00Z\"}", failures);
            if (over != null && !string.IsNullOrEmpty(over.SaleCountdownLabel(now)))
                failures.Add("an ENDED sale still prints a countdown.");

            // ── 7. GREYSCALE: the ribbon must read with every hue removed. ─────
            //  ⛔ WCAG RELATIVE LUMINANCE, NOT A RAW CHANNEL DELTA. The owner is
            //  red/green colourblind and reviews captures desaturated, so the
            //  ribbon's legibility is a LUMINANCE fact about two colours - and the
            //  values are READ OFF the live type, never re-typed here, so this
            //  case fails the day someone lightens the fill.
            double ratio = ContrastRatio(DeNelle.Wallet.StorePackCard.SaleRibbonFill,
                                         DeNelle.Wallet.StorePackCard.SaleRibbonInk);
            if (ratio < MinBadgeContrastRatio)
                failures.Add($"sale ribbon fill vs ink is {ratio:0.0}:1 - the standard's bar is " +
                             $"{MinBadgeContrastRatio:0.0}:1 (it must read in greyscale).");

            // ⭐ WO-1819 — THE SECOND PAIR, AND IT IS THE ONE THAT WAS MISSING. A badge has to clear its
            // own ink so the WORDS read, and clear the surface behind it so the BADGE reads. This case
            // measured only the first pair and passed at 17:1 while the plate scored a MEASURED 1.20:1
            // against the card on the shipped frame - legible text on an invisible sign, which is not
            // what "big sales signs ... you know some flash" asks for. Both pairs are pinned now.
            double vsCard = ContrastRatio(DeNelle.Wallet.StorePackCard.SaleRibbonFill,
                                          DeNelle.Wallet.NightMarketPalette.GroundRaised);
            if (vsCard < MinPlateVsSurfaceRatio)
                failures.Add($"sale ribbon fill vs the card surface is {vsCard:0.0}:1 - the bar is " +
                             $"{MinPlateVsSurfaceRatio:0.0}:1. A plate this close to its own background " +
                             "is not a sign, it is text with extra steps (WO-1819, measured 1.20:1).");

            // ⚠ THE POLARITY RULE THAT STOOD HERE IS RETIRED (WO-1819), not weakened by accident.
            // It required the ribbon to be the state pill's exact inverse - a DARK plate - so the two
            // could never read as one shape desaturated. But the two badges CANNOT SHARE A CARD: the
            // ranking at the pill site (state word > sale ribbon > merchandising badge) draws exactly
            // one. The rule was guarding a collision that cannot occur, and the cost was the invisible
            // plate above. The two ratio gates that replace it are measurements of what the owner
            // actually asked for, and the ribbon is still unmistakable from the pill by plate, by
            // ANGLE (only the ribbon is rotated - the case below) and by position.

            // ── 8. THE PULSE CURVE — seats at 1, peaks at the midpoint, and an
            //      undiscounted card's single beat NEVER repeats. ──────────────
            const float dur = 0.6f, peak = 1.05f;
            if (Mathf.Abs(DeNelle.Wallet.StorePackCard.EvaluatePulse(0f, dur, false, peak) - 1f) > 0.0001f)
                failures.Add("the open pulse does not start at the card's authored scale.");
            if (Mathf.Abs(DeNelle.Wallet.StorePackCard.EvaluatePulse(dur, dur, false, peak) - 1f) > 0.0001f)
                failures.Add("the open pulse does not END at the card's authored scale - a card can " +
                             "be left permanently enlarged.");
            if (Mathf.Abs(DeNelle.Wallet.StorePackCard.EvaluatePulse(dur * 0.5f, dur, false, peak) - peak) > 0.001f)
                failures.Add("the open pulse does not reach its peak at the midpoint.");
            // ⛔ THE "no loop on undiscounted cards" PIN. Well past the window, a non-looping pulse
            // is FLAT at 1 forever - not back at the peak on the next period.
            for (int k = 1; k <= 4; k++)
                if (Mathf.Abs(DeNelle.Wallet.StorePackCard.EvaluatePulse(dur * (k + 0.5f), dur, false, peak) - 1f) > 0.0001f)
                    failures.Add($"a NON-looping pulse moved again at t={dur * (k + 0.5f):0.00}s - an " +
                                 "undiscounted card must pulse once per open and then stop.");
            // And the sale beat DOES repeat: same phase, one period later, same scale.
            float a = DeNelle.Wallet.StorePackCard.EvaluatePulse(0.3f, 1.2f, true, 1.06f);
            float b = DeNelle.Wallet.StorePackCard.EvaluatePulse(1.5f, 1.2f, true, 1.06f);
            if (Mathf.Abs(a - b) > 0.0001f || Mathf.Abs(a - 1f) < 0.0001f)
                failures.Add("the sale badge's beat does not LOOP - a discounted card stops flashing.");
            if (Mathf.Abs(DeNelle.Wallet.StorePackCard.EvaluatePulse(0.5f, 0f, true, 1.06f) - 1f) > 0.0001f)
                failures.Add("a zero-duration pulse does not fail closed to the authored scale.");

            // ── 9. THE CARD MUST GROW FOR THE SALE LINE, not absorb it. ───────
            foreach (DeNelle.Wallet.StorePackCardVariant v in
                     Enum.GetValues(typeof(DeNelle.Wallet.StorePackCardVariant)))
            {
                float plainH = DeNelle.Wallet.StorePackCard.CardHeight(v, false, false);
                float saleH  = DeNelle.Wallet.StorePackCard.CardHeight(v, false, true);
                if (Mathf.Abs((saleH - plainH) - DeNelle.Wallet.StorePackCard.SaleExtraPx) > 0.01f)
                    failures.Add($"a {v} sale card grows by {saleH - plainH:0.#}px, not the " +
                                 $"{DeNelle.Wallet.StorePackCard.SaleExtraPx:0.#}px its own block costs - " +
                                 "the sale line is being squeezed into a lane that is already spent.");
                // The 2-arg overload must be byte-identical to "no sale", or every existing caller
                // silently changed answer.
                if (Mathf.Abs(DeNelle.Wallet.StorePackCard.CardHeight(v, false) - plainH) > 0.01f)
                    failures.Add($"CardHeight({v}, false) changed meaning - existing callers moved.");
            }

            // ── 10. The ribbon must NOT be gated on the VARIANT, because the pill
            //       is: BuildPill is skipped on LandscapeStandard, which is the
            //       variant PackStore.VariantFor(Basket) returns for the shipped
            //       landscape shelf. A ribbon gated the same way renders NOWHERE
            //       on the cards the owner actually looks at, with every gate green.
            //
            // ⛔ ASSERTED ON THE GUARD EXPRESSION, NEVER BY PROXIMITY, AND THE FIRST VERSION OF THIS
            // CASE GOT THAT WRONG AND FAILED THE WHOLE SUITE (2026-09-16, run 19:08). It matched
            // `variant != StorePackCardVariant.LandscapeStandard` within 200 characters of
            // `BuildSaleRibbon` over Code() - and Code() STRIPS COMMENTS, so the ~60 lines of block
            // comment that legitimately sit between the pill's guard and the ribbon's call collapsed
            // to nothing and the two landed ~80 characters apart. The ribbon was never gated; the
            // ORACLE was measuring text adjacency in a file whose comments had been deleted, which is
            // not a property of the code at all. Distance between two tokens is not a control-flow
            // fact. Read the guard instead - that IS the fact.
            string cardCode = Code(card);
            if (!card.Contains("BuildSaleRibbon"))
                failures.Add("no BuildSaleRibbon in StorePackCard - the sale sign has no builder.");
            var ribbonGuard = Regex.Match(cardCode, @"bool\s+drawRibbon\s*=\s*([^;]*);");
            if (!ribbonGuard.Success)
                failures.Add("StorePackCard has no `bool drawRibbon = ...` guard - the one expression " +
                             "that decides whether the sale sign draws cannot be read, so nothing can " +
                             "assert it is not variant-gated.");
            else if (ribbonGuard.Groups[1].Value.Contains("variant"))
                failures.Add("the sale ribbon's guard consults the card VARIANT (`" +
                             ribbonGuard.Groups[1].Value.Trim() + "`) - gated like the state pill, it " +
                             "would render NOWHERE on the landscape Basket cards the owner sees.");

            // ── 10b. ONE TOP BADGE. The ribbon's band (0.02..0.62) OVERLAPS the
            //         pill's (0.26..0.96), so if both could draw they would collide
            //         on the same rect - and desaturated, two loud plates in one
            //         place is worse than either alone.
            if (!cardCode.Contains("drawRibbon"))
                failures.Add("StorePackCard no longer ranks the sale ribbon against the state pill - " +
                             "their x bands overlap, so both drawing means both colliding.");
            if (!Regex.IsMatch(cardCode, @"drawRibbon\s*\?\s*string\.Empty\s*:\s*model\.Badge"))
                failures.Add("the merchandising badge is no longer suppressed while a sale ribbon " +
                             "draws - the two plates would overlap in the card's top band.");
            if (!cardCode.Contains("if (drawRibbon) BuildSaleRibbon"))
                failures.Add("the sale ribbon draws without consulting the badge ranking.");
            // ⛔ AND NOTHING ON THIS CARD IS ROTATED. A rotation about a top pivot lifts the plate's
            // outer corner clear of the card root (~23px on a 500px card against a 16px top offset),
            // and the capture harness's containment audit is right to report that.
            //
            // ⚠ ASSERTED OVER THE WHOLE TEMPLATE, NOT "near the ribbon". The same comment-stripping
            // trap as case 10: a proximity window is not a control-flow or ownership fact. The honest
            // invariant is simpler AND stronger anyway - this template authors every element in
            // axis-aligned reference px, so ANY rotation here is the defect, whoever added it.
            //
            // ⭐ AND THIS CASE HAS NOW CAUGHT THE SAME MISTAKE TWICE — WO-1800 authored it after removing
            // a -9 deg tilt, and it went RED again on 2026-09-16 when WO-1819 re-added one at -8 deg with
            // clearances derived from the angle. The coordinator ruled the SLANT dropped rather than this
            // oracle re-pointed, and that is the right way round: "containable at every card width by
            // construction" is a stronger property than a slant is a flourish. ⛔ A rule that has survived
            // two attempts is not one to re-litigate a third time - if a future ticket wants the tilt, it
            // needs the owner, not a cleverer derivation.
            if (cardCode.Contains("localRotation") || cardCode.Contains("localEulerAngles"))
                failures.Add("StorePackCard rotates an element - a rotated child's corner leaves the " +
                             "card rect at the measured card widths, which the capture's containment " +
                             "audit reports. The brief allows a bold rounded TAG instead of a slant.");

            // ── 11. The pulse must tick ABOVE PackStore.Update's early return. ─
            //  `Update` returns immediately unless a purchase is in flight, which is FALSE for the
            //  whole time the shelf is being browsed. A tick below that return compiles, gates green
            //  and never moves a card.
            string code = Code(store);
            int tick = code.IndexOf("TickCardPulses()", StringComparison.Ordinal);
            int guard = code.IndexOf("if (!_purchaseInFlight) return;", StringComparison.Ordinal);
            if (tick < 0)
                failures.Add("PackStore never calls TickCardPulses - nothing drives the pulse.");
            else if (guard >= 0 && tick > guard)
                failures.Add("TickCardPulses is called BELOW Update's !_purchaseInFlight early " +
                             "return, so it can never run while the player is browsing.");
            // The frame-path instrument must be the 4-arg accumulating overload (CLAUDE.md 12).
            // ⛔ THIS ONE ASSERTION RUNS ON THE RAW SOURCE, NOT ON Code(), AND IT HAS TO. Code()
            // DELETES string literals (see its body) - which is correct for counting call-site
            // authority, and fatal here, because the two literals "Perf" and "PackStore.SaleBadge"
            // ARE the thing being asserted. Against stripped source this pattern can never match, so
            // it would have failed on its very first run against working code - the "oracle reports
            // its own staleness in the defect's voice" failure this file's own header records.
            if (!Regex.IsMatch(store, @"FlowTrace\.Measure\(\s*""Perf""\s*,\s*""PackStore\.SaleBadge""\s*,\s*[\d.]+f\s*,\s*[\d.]+f\s*\)"))
                failures.Add("the pulse tick is not wrapped in the 4-arg FlowTrace.Measure(\"Perf\", " +
                             "\"PackStore.SaleBadge\", ...) frame-path scope.");
            // Unscaled time, or a world hold freezes a card mid-beat at the wrong size.
            if (code.Contains("TickCardPulses") && !code.Contains("Time.unscaledTime"))
                failures.Add("the pulse reads scaled time - a timeScale 0 hold would freeze a card " +
                             "enlarged with no way back.");
        }

        /// <summary>Deserializes one wire row, recording a failure rather than throwing.</summary>
        private static DeNelle.Wallet.PurchaseQuote Deserialize(string json, List<string> failures)
        {
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<DeNelle.Wallet.PurchaseQuote>(json);
            }
            catch (Exception e)
            {
                failures.Add($"a sale wire row failed to deserialize ({e.GetType().Name}): {json}");
                return null;
            }
        }

        /// <summary>WCAG 2.1 relative luminance of an sRGB colour.</summary>
        private static double Luminance(Color c)
        {
            return 0.2126d * Linearize(c.r) + 0.7152d * Linearize(c.g) + 0.0722d * Linearize(c.b);
        }

        private static double Linearize(double channel) =>
            channel <= 0.03928d ? channel / 12.92d : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

        /// <summary>WCAG contrast ratio between two colours, always >= 1.</summary>
        private static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05d) / (lo + 0.05d);
        }

        // =====================================================================
        //  §R2 — three bands vertically, three columns horizontally, in ref px.
        // =====================================================================
        private static void CheckLandscapeBudget(string store, float ctaHeight, List<string> failures)
        {
            var panelMin = Vector(store, "PanelMin", failures);
            var panelMax = Vector(store, "PanelMax", failures);
            if (panelMax.x - panelMin.x < MinPanelWidthShare)
                failures.Add($"Night Market panel uses only {(panelMax.x - panelMin.x):P0} of landscape width " +
                             $"(minimum {MinPanelWidthShare:P0}).");

            // WO-1162: CardsPerRow is owned by NightMarketComposition (the shelf minimum is stated
            // in terms of it), so it is read off the live type rather than parsed as a literal.
            float cardsPerRow = NightMarketComposition.CardsPerRow;
            if (Math.Abs(cardsPerRow - RequiredCardsPerRow) > 0.01f)
                failures.Add($"priced shelf is {cardsPerRow:0}-up; the device-verified readability " +
                             $"ruling is {RequiredCardsPerRow}-up.");

            float usableW = Scalar(store, "UsableWidthPx",   failures);
            float usableH = Scalar(store, "UsableHeightPx",  failures);
            float topBar  = Scalar(store, "TopBarPx",        failures);
            // ⚠ RE-POINTED 2026-08-23 (WO-1162). The three column widths are no longer literals in
            // PackStore: NightMarketComposition DERIVES each rail's minimum from the narrowest
            // content it must hold and RESOLVES a composition for the surface. So this suite asks
            // the live type for the plan it would resolve at the reference surface instead of
            // parsing two numbers that no longer exist — the acceptance bounds below are unchanged.
            float pad  = Scalar(store, "EdgePadPx", failures);
            float gap  = DeNelle.Wallet.NightMarketComposition.ColumnGapPx;

            var refPlan = DeNelle.Wallet.NightMarketComposition.Resolve(
                usableW - 2f * pad, usableH - topBar - ctaHeight);
            float spot     = refPlan.SpotlightWidthPx;
            float commerce = refPlan.CommerceWidthPx;

            if (refPlan.Mode == DeNelle.Wallet.NightMarketMode.StackedTwoColumn)
                failures.Add("the REFERENCE landscape surface (2120x978) no longer resolves to three " +
                             "columns - the derived minimums have grown past the surface the store " +
                             "ships on. Composition: " +
                             DeNelle.Wallet.NightMarketComposition.Describe(refPlan));
            if (refPlan.Deficit)
                failures.Add("the reference landscape surface resolves to a DEFICIT composition (" +
                             refPlan.DeficitPx.ToString("0") + "px short) - cards would overrun the shelf mask.");

            // The formula itself must stay a derivation, not drift back into a literal.
            Require(Read(Application.dataPath + CompositionRel, failures),
                "SpotlightMinPx + ShelfMinForTwoCardsPx + CommerceMinPx + 2f * ColumnGapPx",
                "the three-column breakpoint is no longer derived from the content minimums - a " +
                "hardcoded breakpoint cannot know what it is protecting.", failures);

            // The ONE bottom band IS the canon CTA height — that identity is what makes a second
            // band structurally impossible (§6 / P1-6). Assert the identity, do not restate 132.
            Require(store, "BottomBandPx = ElarionUiKit.CanonCtaHeight",
                "the single bottom band no longer IS the canon CTA height — a second band can " +
                "reappear underneath it (P1-6).", failures);
            Require(store, "BodyPx = UsableHeightPx - TopBarPx - BottomBandPx",
                "body height is no longer derived from the budget; a hardcoded body height can " +
                "silently over- or under-run the 978-unit landscape canvas.", failures);

            float body = usableH - topBar - ctaHeight;
            if (usableH > 0f && body / usableH < MinBodyHeightShare)
                failures.Add($"store body is only {(body / usableH):P0} of the landscape canvas " +
                             $"(minimum {MinBodyHeightShare:P0}) — too much vertical space is chrome.");

            // Three columns, side by side, inside the padded width. Overlap here is the P0-3 class.
            float rails = spot + commerce + (2f * gap) + (2f * pad);
            float shelf = usableW - rails;
            if (shelf <= 0f)
                failures.Add("spotlight + commerce rails consume the whole body width — the shelf " +
                             "has no room and the columns overlap.");
            else
            {
                if (usableW > 0f && shelf / usableW < MinShelfWidthShare)
                    failures.Add($"shelf owns only {(shelf / usableW):P0} of the body width " +
                                 $"(minimum {MinShelfWidthShare:P0}).");
                if (shelf < spot || shelf < commerce)
                    failures.Add("shelf is narrower than one of its side rails — the merchandise is " +
                                 "no longer the widest column.");
            }
        }

        // =====================================================================
        //  §R3 — the ONE card template.
        // =====================================================================
        private static void CheckCard(string card, float minTouch, float fontFloor, List<string> failures)
        {
            // ⚠ RE-POINTED 2026-08-23 (WO-1162 FIX 2). The card heights are DERIVED from the block
            // budget they must carry now, so they are properties, not parseable literals. Read them
            // off the live type; the acceptance bounds below are unchanged.
            float standard = StorePackCard.StandardHeightPx;
            float compact  = StorePackCard.CompactHeightPx;
            float minWidth = StorePackCard.MinCardWidthPx;

            // The budget must SUM, or the stack overruns the price lane again (the 268..330 overlap).
            float requiredStandard = StorePackCard.StandardArtPx
                                   + StorePackCard.NameBlockPx(StorePackCardVariant.Standard)
                                   + StorePackCard.ContentsBlockPx(StorePackCardVariant.Standard)
                                   + StorePackCard.PriceBlockPx(StorePackCardVariant.Standard);
            if (standard < requiredStandard)
                failures.Add($"standard card ({standard:0}px) is shorter than the blocks it must carry " +
                             $"({requiredStandard:0}px of art + name + contents + price, before any gap) - " +
                             "the text stack will reach into the bottom-pinned price lane.");

            if (standard < MinStandardCardPx)
                failures.Add($"standard card height {standard:0}px is below the {MinStandardCardPx:0}px " +
                             "readability floor.");
            // A card carries an art well AND a text stack; each must clear the touch/readability
            // floor on its own, so the shortest variant is bounded at two floors, not one.
            if (compact < minTouch * 2f)
                failures.Add($"compact card height {compact:0}px is under two touch floors " +
                             $"({minTouch * 2f:0}px) — art well and text stack cannot both clear it.");
            if (minWidth < minTouch * 2f)
                failures.Add($"minimum card width {minWidth:0}px is under two touch floors " +
                             $"({minTouch * 2f:0}px).");

            // Every authored type size on the card clears the project's own mobile floor. Generic on
            // purpose: adding a new Font* constant under the floor fails without editing this suite.
            foreach (Match m in Regex.Matches(card, @"private const int (Font\w+)\s*=\s*(\d+);"))
            {
                if (float.TryParse(m.Groups[2].Value, out float px) && px < fontFloor)
                    failures.Add($"card type size {m.Groups[1].Value}={px:0} is below the mobile " +
                                 $"readability floor ({fontFloor:0}).");
            }
            float fontName  = Scalar(card, "FontName",  failures);
            float fontPrice = Scalar(card, "FontPrice", failures);
            if (fontPrice < fontName)
                failures.Add("the price is set smaller than the pack name — price is the one string " +
                             "on this screen that must never be the hardest to read.");

            // Structure, so the name/state lanes CANNOT overlap: the pill lives in the art well and
            // the text stack starts BELOW it. (The old suite compared two anchored rects that the
            // rebuild retired; this asserts the property those rects were a proxy for.)
            Require(card, "float y = artH + TextGapPx",
                "the card's text stack no longer starts below the art well — name and the state/badge " +
                "pill can occupy the same lane.", failures);
            Require(card, "float priceLaneTop = cardH - (BottomPadPx + priceBlock)",
                "the card's text stack no longer reserves the bottom-pinned price lane before it " +
                "spends its budget - that is the 268..330 contents-over-price overlap (WO-1162 FIX 2).",
                failures);
            Require(card, "budget >= CaptionBlockPx",
                "the OPTIONAL value caption is drawn without checking the remaining budget - it was " +
                "landing 62px BELOW the card's own bottom edge.", failures);
            Require(card, "BuildPill(card, Ascii(pill), cardH, artH)",
                "the state/badge pill is no longer seated against the art well.", failures);
            Require(card, "BottomAnchoredText(card, model.PriceMajor",
                "the price is no longer bottom-pinned — a two-line name can push it out of the card " +
                "(P0-1/P0-2 showed '20 SKR' for a 120 SKR pack).", failures);
            Require(card, "FitSingleLine(handle.PriceLabel",
                "the price has no single-line fit guard and can clip its leading digit.", failures);
            Require(card, "StateWord",
                "the card no longer renders commerce state as a WORD — hue alone cannot carry state " +
                "(the owner is red/green colourblind).", failures);
        }

        // =====================================================================
        //  FREE TONIGHT — one tab rail, every tab over the touch floor.
        //  (Re-pointed 2026-08-23: the two two-up "free door" card rows were
        //  replaced by a single row of three utility TABS. The assertion is the
        //  same one it always was — nothing here may be authored under the floor,
        //  because a sub-floor control is GROWN over its neighbour by the clamp.)
        // =====================================================================
        private static void CheckFreeBand(string store, float minTouch, List<string> failures)
        {
            string freeBand = Slice(store, "private void BuildFreeBand", "private void BuildUtilityTab", failures);

            if (Regex.Matches(freeBand, @"BuildCardRow\(").Count != 1)
                failures.Add("FREE TONIGHT is not composed as ONE tab rail.");
            if (Regex.Matches(freeBand, @"BuildUtilityTab\(").Count != 2)
                failures.Add("FREE TONIGHT must carry exactly redeem + monthly ledger after Season retirement.");
            if (freeBand.Contains("PanelId.BattlePass"))
                failures.Add("retired Season Track returned to the public FREE band.");
            // Nothing is asked for before something is given: the redeem door is first on the rail.
            int redeemAt = freeBand.IndexOf("OpenRedeemPanel", StringComparison.Ordinal);
            int seasonAt = freeBand.IndexOf("PanelId.BattlePass", StringComparison.Ordinal);
            if (redeemAt < 0)
                failures.Add("the promo redeem door has no entry point in the FREE band.");
            else if (seasonAt >= 0 && redeemAt > seasonAt)
                failures.Add("the redeem door is not first on the FREE rail.");

            string tab = Slice(store, "private void BuildUtilityTab", "private void BuildFreeDoor", failures);
            float rowH  = Scalar(store, "FreeTabRowPx",      failures);
            float minW  = Scalar(store, "FreeTabMinWidthPx", failures);
            float padY  = RowVerticalPadding(store);
            float usable = rowH - padY;

            var face = AnchorsAfter(tab, "ObsidianButtonColor.Gray,", failures);
            float facePx = (face.max.y - face.min.y) * usable;
            if (facePx < minTouch)
                failures.Add($"FREE-band utility tab derives to {facePx:0}px tall, below the {minTouch:0}px " +
                             "touch floor — the clamp will grow it over the tab beside it.");
            float faceWidthPx = (face.max.x - face.min.x) * minW;
            if (faceWidthPx < minTouch)
                failures.Add($"FREE-band utility tab derives to {faceWidthPx:0}px wide, below the " +
                             $"{minTouch:0}px touch floor.");

            Require(tab, "FitSingleLine(text,",
                "the FREE-band tab label has no explicit readable single-line guard — 'Redeem a Code' " +
                "truncated to 'Redee...' on the owner's device (UI-001 defect #4).", failures);
        }

        // =====================================================================
        //  The ONE Buy control, in the commerce column.
        // =====================================================================
        private static void CheckCommerceCta(string store, float minTouch, float ctaWidth, List<string> failures)
        {
            // ⚠ RE-POINTED 2026-08-23 (WO-1162). The CTA is no longer a fraction pair typed into
            // BuildCommerce; it is authored in PIXELS against the resolved plan's CTA sub-host. So
            // the assertion is now the stronger one it was always a proxy for: the Buy control
            // clears the touch floor in EVERY composition, not just the one the literals were
            // measured in. The old fraction-of-a-fixed-440 form could not see the stacked case.
            Require(store, "NightMarketComposition.CtaBottomPadPx / ctaHostPx",
                "the Buy control's height is no longer authored in pixels against its real host - a " +
                "fraction of a host that shrinks lands the button under the touch floor, where the " +
                "clamp GROWS it over its neighbour.", failures);

            foreach (var probe in CompositionProbes())
            {
                var plan = DeNelle.Wallet.NightMarketComposition.Resolve(probe.w, probe.h);
                float ctaPx = DeNelle.Wallet.NightMarketComposition.CtaButtonPx;
                if (ctaPx < minTouch)
                    failures.Add($"[{probe.name}] commerce CTA is {ctaPx:0}px tall (floor {minTouch:0}px).");
                if (plan.CtaHostPx < DeNelle.Wallet.NightMarketComposition.CtaHostMinPx)
                    failures.Add($"[{probe.name}] CTA sub-host resolved to {plan.CtaHostPx:0}px, under the " +
                                 $"{DeNelle.Wallet.NightMarketComposition.CtaHostMinPx:0}px the button + " +
                                 "its padding needs - the button cannot be seated at canon size.");

                float ctaWidthPx = plan.CommerceWidthPx - 2f * DeNelle.Wallet.NightMarketComposition.CommerceGutterPx;
                if (ctaWidthPx < ctaWidth)
                    failures.Add($"[{probe.name}] commerce rail leaves {ctaWidthPx:0}px for a canon " +
                                 $"{ctaWidth:0}px-wide Buy control.");

                if (plan.CardWidthPx < StorePackCard.MinCardWidthPx)
                    failures.Add($"[{probe.name}] shelf card resolves to {plan.CardWidthPx:0}px, under the " +
                                 $"{StorePackCard.MinCardWidthPx:0}px readable minimum - the row will overrun " +
                                 "its mask and clip a price.");
            }
        }

        /// <summary>
        /// The body boxes this suite resolves a composition for. Landscape only — the store is a
        /// landscape screen — and deliberately including a 4:3 tablet, which is the aspect that
        /// actually crosses the two-column breakpoint.
        /// </summary>
        private static (string name, float w, float h)[] CompositionProbes()
        {
            // Reference box = sqrt(1080*1920/aspect) tall by aspect*that wide (CanvasScaler 1080x1920,
            // MatchWidthOrHeight 0.5). Body = that minus 2*EdgePad(18) and the 100/132 bands.
            (string name, float aspect)[] surfaces =
            {
                ("2340x1080 (phone)", 2340f / 1080f),
                ("2670x1200 (Seeker)", 2670f / 1200f),
                ("1920x1080",          1920f / 1080f),
                ("1600x1200 (4:3)",    4f / 3f),
            };
            var probes = new (string, float, float)[surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++)
            {
                float h = Mathf.Sqrt(1080f * 1920f / surfaces[i].aspect);
                float w = surfaces[i].aspect * h;
                probes[i] = (surfaces[i].name, w - 36f, h - 100f - 132f);
            }
            return probes;
        }

        // =====================================================================
        //  One visible title; one owner of the legal band.
        // =====================================================================
        private static void CheckOneTitleAndOneLegalOwner(string store, string footer, float ctaWidth,
                                                          List<string> failures)
        {
            // WO-1866: a second KeyWordmark reference was added deliberately -
            // DeNelle.Core.UI.LocalizedLabel.Attach(_modal.chrome.title, StoreStrings.KeyWordmark) -
            // to make the already-built title retext itself on a runtime locale switch. It attaches
            // a component to the SAME title object built by the first reference; it does not draw a
            // second visible label, so the "one visible title" invariant this check exists to guard
            // still holds. 2 source references, still exactly ONE rendered title.
            if (Regex.Matches(store, "KeyWordmark").Count != 2)
                failures.Add("wordmark occurrence count drifted; confirm the visible title is rendered exactly once.");

            Require(store, "StoreLegalFooter.Build(",
                "shared legal/footer component is absent — the store authors its claims inline again.", failures);
            Require(footer, "public static class StoreLegalFooter",
                "StoreLegalFooter is not a shared component.", failures);

            // ⛔ ONE OWNER. The keep-out the canon Close carves out of the band lives with the copy
            // it protects; a second copy in PackStore is how the band and the button came to hold
            // two versions of one measurement.
            if (Code(store).Contains("CloseKeepOutPx"))
                failures.Add("PackStore declares a second Close keep-out — StoreLegalFooter owns it.");
            Require(footer, "ElarionUiKit.CanonCtaWidth",
                "the legal band's Close keep-out is not derived from the canon button width.", failures);
            if (ctaWidth <= 0f)
                failures.Add("could not read CanonCtaWidth from the kit — the keep-out cannot be verified.");

            Require(footer, "FontLegalPx",
                "the legal band has no named type size; legal copy can drift under the readability floor.",
                failures);
            Require(footer, "ElarionUiKit.FitBlock(t,",
                "the legal band's copy has no fit guard — a claim long enough to wrap OVERFLOWS the " +
                "132-unit band upward, over the shelf card above it (seen in the 2026-08-23 capture).",
                failures);
        }

        // =====================================================================
        //  The persistent HUD door — exactly ONE routing authority.
        // =====================================================================
        private static void CheckHudDoor(string hud, string deck, List<string> failures)
        {
            // ⚠ THE LEGACY FACE, NOT THE NEW CARD. `RealmHudButton` was the retired two-control
            // island over the world that mislabeled its own route ("Realm" opening the Store).
            // WO-1335's permanent face is the NIGHT MARKET CARD (CheckHudStoreCard below) and is a
            // different widget with a different name, so this law is unchanged by that ruling - what
            // is retired is the mislabeled button, never the idea of a permanent door.
            if (hud.Contains("RealmHudButton"))
                failures.Add("the legacy mislabeled 'Realm' HUD face is back. The permanent store " +
                             "door is the Night Market card (WO-1335); this button named a route it " +
                             "did not open and is retired.");
            // WO-1398: the row is labelled for what it OPENS (the Realm deck) and its command is
            // named for it too. It used to read "Night Market" -> OpenRealmStore while opening
            // PanelId.RealmDeck: one name for two screens, a method name that lied about its target.
            // WO-1857: the label is now a LocalizedText resolve (hud.gearDock.realm), not a bare
            // "Realm" literal - the needle pins the resolved key plus the handler.
            Require(hud, "new LocalizedText(\"hud.gearDock.realm\").Resolve(), OpenRealmDeck)",
                "Realm drawer row is absent or not bound to OpenRealmDeck (WO-1398)", failures);
            Require(hud, "DockTabCount = 6",
                "drawer capacity was not expanded for the Night Market touch row", failures);
            Require(hud, "HudLayoutBands.DockEdgePx",
                "HUD menu handle is not seated from the shared dock safe-edge value", failures);
            Require(hud, "PanelRouter.Open(PanelId.RealmDeck)",
                "Night Market drawer command does not route to the Realm workspace", failures);
            Require(deck, "PanelId.RealmStore",
                "Realm workspace does not route its Store card to the existing store door", failures);

            // Count CALL SITES, not substrings — comments and log strings are not authorities.
            int authorities = Regex.Matches(Code(deck), @"PanelId\.RealmStore").Count;
            if (authorities != 1)
                failures.Add($"HUD declares {authorities} Realm Store routing authorities; there must be exactly one.");

            Require(hud, "Register(\"chatDock\"",
                "Night Market/Menu drawer is not posture-owned with the dock", failures);
        }

        // =====================================================================
        //  WO-1334 — THE HEADER WALLET CHIP, on the surface that takes real money.
        // ---------------------------------------------------------------------
        //  Owner ruling 2026-09-03, from a device capture of the Night Market:
        //    "the white text top right needs moved left and simplified connected
        //     they dont need address"  ... "or even better SKR: balance"
        //
        //  ⭐ WHY EACH BOUND BELOW IS A BOUND, so none of them is "helpfully" relaxed:
        //
        //  [one-line]  The defect was FIVE elements stacked into one rect. A "\n" in the
        //              connected branch is the whole clump growing back, so the newline is
        //              banned in the render method rather than the visual re-judged by eye.
        //  [no-address] "they dont need address" is a RULING, not a size complaint. The
        //              address must be ABSENT from the chip, so the shortener may not be
        //              reached from RenderBalanceLabel at all - shrinking it would satisfy
        //              a layout test and violate the instruction.
        //  [words]     ⛔ THE ONE THAT MATTERS MOST. The owner is RED/GREEN COLOURBLIND
        //              (CLAUDE.md §7): a greyed-out number, a dimmed chip or a coloured dot
        //              is NOT a message to her. Every DISCONNECTED state must therefore
        //              carry letters. This is checked against canon-strings.json, both
        //              shipped copies, because that is where the sentences live.
        //  [network]   Mainnet-vs-Testnet is a MONEY-SAFETY signal: on devnet the SKR is
        //              free and a purchase completes for nothing (the matched-pair invariant
        //              MonetizationActivationRegression pins). The old carrier was authored
        //              ART that baked the word "Mainnet" into a texture and so said "Mainnet"
        //              on devnet. The chip must read the LIVE network instead, and the baked
        //              plate must stay gone.
        //  [top-left]  ⛔ SUPERSEDES [left] (WO-1334b, same day). WO-1334 moved the chip from
        //              x 0.70-1.00 to 0.62-0.955 and pinned it there - still the right-hand side,
        //              and the pin REQUIRED it to stay there. The owner then ruled without
        //              ambiguity: "in the top left put their balance ... it shouldn't be on the
        //              top right hand side". The rect is now bounded on BOTH sides so neither
        //              drift is silent, and the vertical half ("top") is pinned by the label
        //              still being built in BuildHeader, which owns the top bar.
        //  [balance-word] The chip leads with the WORD "Balance", then the SKR total. This
        //              RETIRES the same day's "SKR: <balance>" - she reconsidered out loud and
        //              gave the reason: it is the storefront convention, and the word is what
        //              makes the digits legible as "what I can afford".
        //  [ground]    ⛔ A READABILITY DEFECT SHE STATED IN WORDS: "white where it's over top
        //              of everything else ... you can't read it". The carrier is a dark plate
        //              behind the label - a LUMINANCE contrast, the only kind that survives
        //              red/green colour blindness. Re-tinting the text is not a fix.
        // =====================================================================
        private static void CheckWalletChip(string store, List<string> failures)
        {
            string render = Slice(store, "private void RenderBalanceLabel", "//  Contents description", failures);
            if (string.IsNullOrEmpty(render)) return;
            string renderCode = Code(render);

            // [one-line] — the connected chip is ONE line.
            if (render.Contains("\\n"))
                failures.Add("[one-line] RenderBalanceLabel still composes a newline - the header wallet chip " +
                             "is stacking labels again. Owner ruling WO-1334: the connected chip is one line, " +
                             "'SKR: <balance>'.");

            // [no-address] — the base58 address is ABSENT, not shrunk.
            if (renderCode.Contains("Account.Address"))
                failures.Add("[no-address] RenderBalanceLabel reads the wallet address again. Owner ruling " +
                             "WO-1334: 'they dont need address' - it is REMOVED from the chip, not resized.");
            if (renderCode.Contains("Shorten("))
                failures.Add("[no-address] RenderBalanceLabel calls Shorten() - the only thing it shortened " +
                             "here was the address the owner removed.");

            // [network] — the baked plate stays gone and the live network is read.
            // ⚠ MATCHED ON THE RAW SOURCE AND ON THE CALL SHAPE, NOT ON THE BARE NAME. Code()
            // blanks string LITERALS, so it can never see an asset name; and the bare name appears
            // in the comment that explains why the plate was removed, which a substring test would
            // report as the defect it is documenting.
            if (Regex.IsMatch(store, @"AddArt\s*\(\s*host\s*,\s*""network-frame"""))
                failures.Add("[network] the network-frame plate is back in the header. It BAKES the word " +
                             "'Mainnet' plus a green dot into a texture, so it printed 'Mainnet' over a DEVNET " +
                             "session - a confident lie about whether real money is at stake, carried partly " +
                             "by a hue the owner cannot see.");
            if (!renderCode.Contains("WalletNetwork.Mainnet"))
                failures.Add("[network] RenderBalanceLabel no longer tests the LIVE network. Mainnet-vs-devnet " +
                             "is a money-safety signal: on devnet the tokens are free and a purchase completes " +
                             "for nothing. Removing it to reduce clutter is not a cleanup.");
            if (!renderCode.Contains("NetworkLabel"))
                failures.Add("[network] the chip does not render a network word at all - the safety signal has " +
                             "no carrier left on this screen.");

            // [top-left] — WO-1334b. TIGHTENED, NOT RELAXED, and the direction matters.
            //
            // ⛔ THE OLD BOUND HERE WAS THE DEFECT. It asserted `chip.max.x >= 0.75` - i.e. it
            // REQUIRED the chip to stay on the right-hand side, because WO-1334 read "needs moved
            // left" as a nudge. The owner then said it in words that admit no nudge: *"in the top
            // left put their balance ... it shouldn't be on the top right hand side"*. An oracle
            // that pins the rejected placement is worse than no oracle: it makes the correct fix
            // fail the suite. Both directions are now bounded so neither drift is silent.
            var chip = AnchorsAfter(store, "_balanceLabel = MakeText(", failures);
            if (chip.max.x > 0.5f)
                failures.Add($"[top-left] the wallet chip's right edge is at x={chip.max.x:0.###} - past the " +
                             "half-way line, so the chip is not in the top LEFT. Owner ruling 2026-09-03: " +
                             "\"in the top left put their balance of what they have just in SKR so they know " +
                             "what they can afford immediately\" and \"it shouldn't be on the top right hand " +
                             "side\". This is the bound that a re-drift to the right trips first.");
            if (chip.min.x > 0.10f)
                failures.Add($"[top-left] the wallet chip starts at x={chip.min.x:0.###} - it has floated off " +
                             "the panel's left margin. The balance is the first thing the eye lands on when " +
                             "the store opens; it belongs AT the corner, not near it.");
            // The chip must be in the TOP bar. BuildHeader IS the top bar (it is handed _topBar,
            // a region pinned to the panel's top edge), so the pin is that the label is still built
            // there rather than having been re-parented into the body while nobody was looking.
            string header = Slice(store, "private void BuildHeader", "private static void SeatWordmark", failures);
            if (!string.IsNullOrEmpty(header) && !header.Contains("_balanceLabel = MakeText("))
                failures.Add("[top-left] the wallet chip is no longer built in BuildHeader - it has left the " +
                             "top bar. 'Top left' is two constraints and this is the vertical one.");

            // [balance-word] — the word "Balance" LEADS the chip. Owner ruling 2026-09-03:
            // *"Maybe we could put the word balance and then put their SKR total ... I see every
            // other site in the world does it."* This RETIRES the same day's earlier `SKR: <balance>`
            // form, which is why the canon check below tests for the new lead rather than the old.
            // Pinned in canon-strings.json (both copies) by CheckDisconnectedWords.

            // [ground] — READABILITY IS A STATED DEFECT, NOT A NICETY.
            // *"white where it's over top of everything else, because that's just ugly, it doesn't
            // make sense and you can't read it."* The fix is a dark plate behind the label - a
            // LUMINANCE contrast, which is the only kind that survives the owner's red/green colour
            // blindness (CLAUDE.md §7). Re-tinting the text would satisfy nothing.
            if (!Regex.IsMatch(header ?? string.Empty, @"PlateBehind\s*\(\s*_balanceLabel"))
                failures.Add("[ground] the wallet chip has no plate behind it. Owner ruling 2026-09-03: " +
                             "white text over the panel art is unreadable (\"you can't read it\"). The ground " +
                             "is what makes it legible, and it is a brightness contrast rather than a hue - " +
                             "the owner is red/green colourblind, so a re-tint is not a fix.");

            // [words] — every DISCONNECTED state says its state in letters, in canon.
            CheckDisconnectedWords(failures);
        }

        /// <summary>
        /// The four non-connected wallet sentences, read from canon-strings.json itself.
        /// <para>⛔ BOTH SHIPPED COPIES ARE CHECKED. Resources/ and StreamingAssets/ each carry a
        /// canon-strings.json and they are meant to be identical; editing one is a real and repeated
        /// failure mode, and a chip that reads correctly in the editor and blankly on the device is
        /// exactly what a single-copy check would miss.</para>
        /// </summary>
        private static void CheckDisconnectedWords(List<string> failures)
        {
            string[] copies =
            {
                Application.dataPath + "/Resources/Data/Canonical/canon-strings.json",
                Application.dataPath + "/StreamingAssets/Data/Canonical/canon-strings.json",
            };
            string[] disconnected =
            {
                "storeBalanceNoWallet", "storeBalanceBoundAddress",
                "storeBalanceBoundIdentity", "storeBalanceChecking", "storeBalanceUnavailable",
            };

            foreach (string path in copies)
            {
                if (!File.Exists(path)) { failures.Add("[words] missing canon copy: " + path); continue; }
                string json = File.ReadAllText(path);

                foreach (string key in disconnected)
                {
                    string value = CanonValue(json, key);
                    if (value == null)
                    {
                        failures.Add($"[words] '{key}' is absent from {Path.GetFileName(path)} - a wallet " +
                                     "state with no sentence renders as an EMPTY chip, which is the " +
                                     "colour-only failure in its purest form.");
                        continue;
                    }
                    int letters = 0;
                    foreach (char c in value) if (char.IsLetter(c)) letters++;
                    if (letters < 8)
                        failures.Add($"[words] '{key}' = \"{value}\" carries {letters} letters. The owner is " +
                                     "red/green colourblind: a disconnected wallet must SAY it is disconnected. " +
                                     "A dimmed number, a dash or a dot is not a message.");
                    foreach (char c in value)
                        if (c > 127)
                        {
                            failures.Add($"[words] '{key}' contains a non-ASCII glyph - it renders as a tofu box " +
                                         "on the device font.");
                            break;
                        }
                }

                // The connected sentence is the owner's exact form. ⛔ RE-POINTED 2026-09-03
                // (WO-1334b), NOT WEAKENED: this used to require the sentence to START WITH "SKR",
                // pinning the form she retired hours later. Her re-ruling: *"Maybe we could put the
                // word balance and then put their SKR total ... I see every other site in the world
                // does it."* So the WORD leads and the token trails - `Balance: {0} SKR`. Both
                // halves are still bound, because dropping either loses something real: without
                // "Balance" the number has no job, and without "SKR" it has no unit.
                string connected = CanonValue(json, "storeBalanceValue");
                if (connected == null)
                    failures.Add("[one-line] 'storeBalanceValue' is absent - the connected chip has no sentence.");
                else
                {
                    if (!connected.StartsWith("Balance", StringComparison.Ordinal))
                        failures.Add($"[balance-word] 'storeBalanceValue' = \"{connected}\" - the owner ruled " +
                                     "the chip leads with the WORD 'Balance', then the SKR total. The earlier " +
                                     "'SKR: <balance>' form is RETIRED; a bare token name does not tell the " +
                                     "player what the number is for.");
                    if (!connected.Contains("SKR"))
                        failures.Add($"[balance-word] 'storeBalanceValue' = \"{connected}\" - the unit is gone. " +
                                     "The figure mirrors the player's OWN wallet in SKR and must say so; an " +
                                     "unlabelled number on a money screen reads as an in-game currency, which " +
                                     "is precisely the thing this game never holds.");
                    if (!connected.Contains("{0}"))
                        failures.Add("[one-line] 'storeBalanceValue' has no {0} - the chip would print a " +
                                     "label with no balance, and the balance IS the proof of connection.");
                }

                // ⛔ AND THE ADDRESS SENTENCE MUST NO LONGER TAKE ONE. A surviving {0} means a caller
                // can still pour an address back in without touching PackStore.
                string bound = CanonValue(json, "storeBalanceBoundAddress");
                if (bound != null && bound.Contains("{0}"))
                    failures.Add("[no-address] 'storeBalanceBoundAddress' still takes a {0} - the slot the " +
                                 "removed address used to fill. Owner ruling WO-1334: 'they dont need address'.");
            }
        }

        // =====================================================================
        //  WO-1335 RULING 1 — THE NIGHT MARKET CARD IS A PERMANENT FACE ON THE HUD.
        // ---------------------------------------------------------------------
        //  Owner ruling 2026-09-03:
        //    "the realm store is hidden away needs a permanent face on hud"
        //    "can you take the realm store card from settings > night market and anchor it
        //     smaller to left side on hud"
        //
        //  ⭐ WHY EACH BOUND IS A BOUND:
        //
        //  [stick]  ⛔ THE ONE THAT CANNOT BE NEGOTIATED. The virtual movement stick is the game's
        //           only locomotion control on a phone. A card drawn over it does not degrade the
        //           HUD, it removes the player's ability to move. This is asserted as GEOMETRY at
        //           the owner's real device size against HudAreasHost's own MoveCluster row - not
        //           by eye, and not by trusting a comment.
        //  [reuse]  She picked the card BY NAME ("the realm store card from settings > night
        //           market"). Authoring a second store-entry widget, or swapping the art for
        //           something new, answers a question she did not ask. The art key is pinned.
        //  [door]   One destination, two doorways (WO-1164): this card and the walk-up building
        //           open the SAME PanelId.RealmStore. A second store surface is the failure.
        //  [left]   "anchor it smaller to LEFT side" - so the band must actually be on the left.
        //  [touch]  Phone-first: >= MinTouchPx on BOTH axes, parsed from the kit, never retyped.
        //  [seat]   The seat is DERIVED from HudLayoutBands. A HUD element that hardcodes its own
        //           rect is how the left column came to hold seven elements positioned in four
        //           files with nobody owning the sum (that file's own header).
        //  [posture] It is a permanent TOWN face, so it must be in the calm(town) occupancy rows
        //           of BOTH shipped hud-areas.json copies - a widget registered but never listed
        //           is built, hidden, and invisible forever.
        // =====================================================================
        private static void CheckHudStoreCard(string hud, string areas, float minTouch, List<string> failures)
        {
            string hudCode = Code(hud);

            // [reuse] the EXISTING card, taken rather than reinterpreted.
            Require(hud, "BuildNightMarketCard(pool)",
                "[reuse] the permanent Night Market card is never built. Owner ruling WO-1335: the " +
                "store is the one verb in the game with no permanent door.", failures);
            Require(hud, "UI/ElarionMedieval/cards/realm-store",
                "[reuse] the HUD card no longer loads the authored `realm-store` card art. The owner " +
                "picked this card BY NAME from settings > night market; a different sprite is a " +
                "second widget wearing its label.", failures);

            // [door] one destination, two doorways.
            if (!hudCode.Contains("PanelRouter.Open(PanelId.RealmStore)"))
                failures.Add("[door] the HUD card does not open PanelId.RealmStore - the door the " +
                             "walk-up Realm Store building already opens. WO-1164 rules ONE store, " +
                             "two doorways; a second store surface is the defect.");

            // [seat] derived from the column's one authority, never hardcoded here or there.
            Require(hud, "HudLayoutBands.NightMarketCardWidthPx",
                "[seat] the HUD card sizes itself instead of reading HudLayoutBands. The left column " +
                "has ONE owner precisely because seven elements positioned in four files is how it " +
                "came to overlap without anyone being able to see it.", failures);

            // [touch] the authored card clears the kit floor on both axes.
            if (minTouch > 0f)
            {
                if (HudLayoutBands.NightMarketCardWidthPx < minTouch)
                    failures.Add($"[touch] the Night Market card is {HudLayoutBands.NightMarketCardWidthPx:0}px " +
                                 $"wide, under the {minTouch:0}px touch floor.");
                if (HudLayoutBands.NightMarketCardHeightPx < minTouch)
                    failures.Add($"[touch] the Night Market card is {HudLayoutBands.NightMarketCardHeightPx:0}px " +
                                 $"tall, under the {minTouch:0}px touch floor.");
            }

            // ── the GEOMETRY, resolved at real device sizes rather than asserted in prose ──
            var sizes = new[]
            {
                new Vector2(HudLayoutBands.DeviceWidth, HudLayoutBands.DeviceHeight),   // the owner's Seeker
                new Vector2(2400f, 1080f),
                new Vector2(1920f, 1080f),
            };
            // ⭐ WO-1464: THE STICK'S BAND IS NO LONGER PARSED OUT OF HudAreasHost SOURCE.
            // It moved into HudLayoutBands.MoveClusterMount (DeNelle.Core.UI) because the raid
            // deploy tray in DeNelle.Village has to start to the right of it and cannot reference
            // DeNelle.HUD. Reading the TYPED constant is strictly stronger than regexing the
            // literal it replaced: the parse could go quiet the moment the authored form changed
            // (it did), and a [stick] failure raised by a REFACTOR rather than by a real overlap
            // is the noise that gets an oracle ignored. HudAreasHost is still asserted to consume
            // the constant, below, so this can never drift from what the game mounts.
            Rect moveCluster = HudLayoutBands.MoveClusterMount;
            bool haveStick = moveCluster.width > 0f && moveCluster.height > 0f;
            if (!haveStick)
                failures.Add("[stick] HudLayoutBands.MoveClusterMount resolved to a ZERO band (" +
                             moveCluster.xMin.ToString("0.###") + ".." + moveCluster.xMax.ToString("0.###") +
                             " x, " + moveCluster.yMin.ToString("0.###") + ".." +
                             moveCluster.yMax.ToString("0.###") + " y), so the card cannot be PROVEN " +
                             "clear of the movement " +
                             "stick. An unverifiable seat over the only locomotion control is not an " +
                             "acceptable unknown.");
            if (areas != null &&
                areas.IndexOf("Add(HudArea.MoveCluster, HudLayoutBands.MoveClusterMount)",
                              StringComparison.Ordinal) < 0)
                failures.Add("[stick] HudAreasHost no longer mounts HudArea.MoveCluster from " +
                             "HudLayoutBands.MoveClusterMount, so the band asserted here is not the " +
                             "band the game builds. The stick's seat has exactly one author " +
                             "(CLAUDE.md sec.5 - DeNelle.Village cannot see DeNelle.HUD).");

            foreach (var size in sizes)
            {
                var card = HudLayoutBands.ResolveNightMarketCard(size.x, size.y);

                if (haveStick && HudLayoutBands.Intersects(card, moveCluster))
                    failures.Add($"[stick] at {size.x:0}x{size.y:0} the Night Market card " +
                                 $"({card.xMin:0.###}..{card.xMax:0.###} x, {card.yMin:0.###}..{card.yMax:0.###} y) " +
                                 $"overlaps the MoveCluster band " +
                                 $"({moveCluster.xMin:0.###}..{moveCluster.xMax:0.###} x, " +
                                 $"{moveCluster.yMin:0.###}..{moveCluster.yMax:0.###} y). That band holds the " +
                                 "virtual movement stick - covering it removes the player's ability to move.");

                // [left] it is a LEFT-side element, not a floater that drifted inboard.
                if (card.xMax > 0.30f)
                    failures.Add($"[left] at {size.x:0}x{size.y:0} the card's right edge is at " +
                                 $"x={card.xMax:0.###}. The owner asked for it anchored to the LEFT side.");
                if (card.xMin > 0.05f)
                    failures.Add($"[left] at {size.x:0}x{size.y:0} the card's left edge is at " +
                                 $"x={card.xMin:0.###} - it has drifted off the left margin.");

                // Clear of the other things that are actually DRAWN in this column.
                var bands = HudLayoutBands.ResolveLeftColumn(size.x, size.y);
                var names = HudLayoutBands.LeftColumnNames;
                for (int i = 0; i < bands.Length && i < names.Length; i++)
                {
                    if (string.Equals(names[i], "Night Market card", StringComparison.Ordinal)) continue;
                    // WO-1825: the two name-skips that used to sit here ("minimap plate" and
                    // "status line") are GONE, and so is the reason for them. They existed because
                    // the card took the minimap plate's seat while nothing constructed the plate,
                    // so asserting the overlap would have failed on a band nothing drew. The owner
                    // has now removed the minimap outright, ResolveLeftColumn no longer returns
                    // those bands, and every band this loop sees is one that really draws - which
                    // is what makes the assertion below meaningful rather than skipped.
                    if (HudLayoutBands.Intersects(card, bands[i]))
                        failures.Add($"[seat] at {size.x:0}x{size.y:0} the Night Market card overlaps the " +
                                     $"'{names[i]}' band. Each element in the left column gets its OWN band; " +
                                     "it does not get drawn across one that is already spoken for.");
                }
            }

            // [posture] listed in calm(town) in BOTH shipped occupancy copies.
            string[] areaCopies =
            {
                Application.dataPath + "/Resources/Data/Canonical/hud-areas.json",
                Application.dataPath + "/StreamingAssets/Data/Canonical/hud-areas.json",
            };
            foreach (string path in areaCopies)
            {
                if (!File.Exists(path)) { failures.Add("[posture] missing hud-areas copy: " + path); continue; }
                string json = File.ReadAllText(path);
                int town = json.IndexOf("\"calm(town)\"", StringComparison.Ordinal);
                int nextPosture = town >= 0 ? json.IndexOf("\"posture\"", town + 12, StringComparison.Ordinal) : -1;
                string townBlock = town < 0 ? null
                    : (nextPosture > town ? json.Substring(town, nextPosture - town) : json.Substring(town));
                if (townBlock == null)
                    failures.Add("[posture] no calm(town) posture in " + Path.GetFileName(path) + ".");
                else if (townBlock.IndexOf("\"nightMarketCard\"", StringComparison.Ordinal) < 0)
                    failures.Add("[posture] 'nightMarketCard' is not in the calm(town) occupancy rows of " +
                                 Path.GetFileName(path) + ". A widget that is registered but never listed " +
                                 "is built, switched off, and invisible forever - which is exactly what " +
                                 "'hidden away' meant in the owner's report.");
            }
        }

        /// <summary>Parse an <c>Add(HudArea.&lt;name&gt;, new Vector2(a,b), new Vector2(c,d));</c> row
        /// out of HudAreasHost source into a screen-fraction Rect.</summary>
        private static bool TryParseAreaRect(string src, string area, out Rect rect)
        {
            rect = default(Rect);
            if (string.IsNullOrEmpty(src)) return false;
            var m = Regex.Match(src, @"Add\s*\(\s*HudArea\." + Regex.Escape(area) +
                                     @"\s*,\s*new\s+Vector2\s*\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*\)\s*,\s*" +
                                     @"new\s+Vector2\s*\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*\)");
            if (!m.Success) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float a, b, c, d;
            if (!float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out a)) return false;
            if (!float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, ci, out b)) return false;
            if (!float.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, ci, out c)) return false;
            if (!float.TryParse(m.Groups[4].Value, System.Globalization.NumberStyles.Float, ci, out d)) return false;
            rect = Rect.MinMaxRect(a, b, c, d);
            return true;
        }

        // =====================================================================
        //  WO-1335 RULING 2 — THE "CLOSE THE GAP" PACKS ARE ALWAYS BUYABLE.
        // ---------------------------------------------------------------------
        //  Asked directly whether the WO-1037 shortfall gating should stand, the owner chose:
        //  "They should always be buyable."
        //
        //  ⛔ THIS OVERTURNS A PRIOR OWNER RULING, DELIBERATELY. It is recorded, not treated as a
        //  bug fix - and this oracle is what stops the retired rule creeping back in as a
        //  "restored guardrail".
        //
        //  ⭐ AND IT IS NOT A PRICING TICKET. Every one of the three is already in USD_ANCHORS in
        //  api/_lib/purchase-catalog.js and the server has always quoted it. No price, SKU,
        //  entitlement or grant is asserted here, on purpose: this file must never become a place
        //  where a money value can be changed to make a test pass.
        //
        //  [offerable] The strongest available bound, and it asks the RUNTIME helper rather than
        //              re-deriving shelf membership: PackCatalog.IsOnBrowsableShelf is the same
        //              predicate PackStore.PacksInBand asks, so a SKU cannot be buyable here and
        //              gated there.
        //  [one-key]   ⛔ NOT OVERTURNED. WO-947 s12c: each grants exactly ONE economy key and
        //              nothing else. The surfacing changed; the guardrail did not.
        //  [note]      A `theme` note that still asserts the retired gate is a live contradiction
        //              between the data and the shipped behaviour, which is how this repo's most
        //              expensive bugs start.
        //  [no-gate]   The catch-up rail may not re-acquire a shortfall precondition. "Always
        //              buyable" is a property of the RAIL, not just of the rows.
        //  [twins]     Resources/ wins at load, so a StreamingAssets-only edit ships a build whose
        //              editor and device disagree.
        // =====================================================================
        private static readonly string[] GapSkus =
            { "impulse-wood-medium", "impulse-iron-medium", "impulse-stone-medium" };

        private const string RetiredGateSentence = "Surfaced ONLY against a real shortfall";

        private static void CheckGapPacksAlwaysBuyable(string store, List<string> failures)
        {
            foreach (string sku in GapSkus)
            {
                PackDef pack = null;
                try { pack = PackCatalog.Find(sku); }
                catch (Exception ex)
                {
                    failures.Add($"[offerable] PackCatalog.Find('{sku}') threw {ex.GetType().Name} - " +
                                 "the catch-up shelf cannot be verified.");
                    continue;
                }
                if (pack == null)
                {
                    failures.Add($"[offerable] '{sku}' is gone from packs.json. The owner ruled these " +
                                 "three permanent storefront rows on 2026-09-03.");
                    continue;
                }

                // [offerable] — buyable with NO shortfall present. Nothing in this call knows about
                // a shortfall, which is the entire point: the row's visibility is unconditional.
                if (!PackCatalog.IsOnBrowsableShelf(pack))
                    failures.Add($"[offerable] '{sku}' is not on the browsable shelf, so it is reachable " +
                                 "only through a shortfall offer. Owner ruling WO-1335: 'They should " +
                                 "always be buyable.'");
                if (PackCatalog.BandOf(pack) != StoreBand.Gap)
                    failures.Add($"[offerable] '{sku}' left the Gap band, so it no longer appears under " +
                                 "CLOSE THE GAP at all.");

                // [one-key] — the WO-947 s12c guarantee, which this ruling did NOT overturn.
                int keys = 0;
                foreach (string key in PackCatalog.LedgerEconomyKeys)
                    if (pack.EconomyAmount(key) > 0) keys++;
                if (keys != 1)
                    failures.Add($"[one-key] '{sku}' now grants {keys} economy keys. WO-947 s12c: a " +
                                 "single-resource impulse pack grants exactly ONE, and making these rows " +
                                 "permanent did not relax that.");
                if (pack.Contents != null && pack.Contents.Cosmetics != null && pack.Contents.Cosmetics.Count > 0)
                    failures.Add($"[one-key] '{sku}' carries cosmetics. It grants one resource and nothing else.");
                if (pack.Contents != null && pack.Contents.Convenience != null && pack.Contents.Convenience.Count > 0)
                    failures.Add($"[one-key] '{sku}' carries convenience items. It grants one resource and " +
                                 "nothing else.");
            }

            // [note] + [twins] — read the shipped DATA, both copies.
            string[] copies =
            {
                Application.dataPath + "/Resources/Data/Canonical/packs.json",
                Application.dataPath + "/StreamingAssets/Data/Canonical/packs.json",
            };
            string first = null;
            foreach (string path in copies)
            {
                if (!File.Exists(path)) { failures.Add("[twins] missing packs copy: " + path); continue; }
                string json = File.ReadAllText(path);
                if (first == null) first = json;
                else if (!string.Equals(first, json, StringComparison.Ordinal))
                    failures.Add("[twins] the two canonical packs.json copies differ. Resources/ wins at " +
                                 "load, so a StreamingAssets-only edit ships a build whose editor and " +
                                 "device disagree about the shelf.");

                foreach (string sku in GapSkus)
                {
                    int at = json.IndexOf("\"" + sku + "\"", StringComparison.Ordinal);
                    if (at < 0) continue;   // absence is already reported by [offerable]
                    int next = json.IndexOf("\"sku\":", at + sku.Length + 2, StringComparison.Ordinal);
                    string block = next > at ? json.Substring(at, next - at) : json.Substring(at);
                    if (block.IndexOf(RetiredGateSentence, StringComparison.Ordinal) >= 0)
                        failures.Add($"[note] '{sku}' still authors \"{RetiredGateSentence}...\" in " +
                                     Path.GetFileName(path) + ". That gate was RETIRED by the owner on " +
                                     "2026-09-03 and the row is a permanent storefront row now. A note " +
                                     "that contradicts shipped behaviour is how this repo's most " +
                                     "expensive bugs start.");
                }
            }

            // [no-gate] — the rail itself asks for no shortfall before drawing the offers.
            string rail = Slice(store, "private void BuildLandscapeGapOffers", "private static void BuildUtilityHeading",
                                failures);
            if (!string.IsNullOrEmpty(rail) && Code(rail).IndexOf("Shortfall", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("[no-gate] BuildLandscapeGapOffers consults a shortfall again before building " +
                             "the CLOSE THE GAP rail. The owner ruled these offers unconditional; a rail " +
                             "that only appears against a real shortfall is the retired gate rebuilt one " +
                             "level up from the rows.");
        }

        /// <summary>One canon string by key, or null. Deliberately not a JSON parser: this file is a
        /// flat one-line-per-key object and a regex keeps the oracle independent of the loader it
        /// is testing.</summary>
        private static string CanonValue(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // =====================================================================
        //  Source helpers.
        // =====================================================================
        private static string Read(string path, List<string> failures)
        {
            if (File.Exists(path)) return File.ReadAllText(path);
            failures.Add("missing source: " + path);
            return string.Empty;
        }

        /// <summary>
        /// The file with `//` comments, `/* */` blocks and "string literals" blanked out, so a text
        /// scan measures CODE. Written character-wise rather than by regex because a regex that
        /// tries to do all three at once gets the nesting wrong on exactly the lines that matter.
        /// </summary>
        private static string Code(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char n = i + 1 < source.Length ? source[i + 1] : '\0';

                if (c == '/' && n == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i = Math.Min(source.Length, i + 2);
                    continue;
                }
                if (c == '"')
                {
                    // Verbatim strings (@"...") escape only by doubling the quote.
                    bool verbatim = i > 0 && source[i - 1] == '@';
                    i++;
                    while (i < source.Length)
                    {
                        if (!verbatim && source[i] == '\\') { i += 2; continue; }
                        if (source[i] == '"')
                        {
                            if (verbatim && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        i++;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < source.Length && source[i] != '\'')
                        i += source[i] == '\\' ? 2 : 1;
                    i++;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static Vector2 Vector(string source, string name, List<string> failures)
        {
            var m = Regex.Match(source, name + @"\s*=\s*new Vector2\(([-0-9.]+)f,\s*([-0-9.]+)f\)");
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) return new Vector2(x, y);
            failures.Add("could not parse independent layout value " + name);
            return Vector2.zero;
        }

        private static float Scalar(string source, string name, List<string> failures)
        {
            var m = Regex.Match(source, @"\b" + name + @"\s*=\s*([-0-9.]+)f?");
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value)) return value;
            failures.Add("could not parse independent layout scalar " + name);
            return 0f;
        }

        /// <summary>Top+bottom padding of a shelf row, parsed from BuildCardRow's RectOffset.</summary>
        private static float RowVerticalPadding(string store)
        {
            var m = Regex.Match(store, @"padding = new RectOffset\(\d+,\s*\d+,\s*(\d+),\s*(\d+)\)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int top) &&
                int.TryParse(m.Groups[2].Value, out int bottom)) return top + bottom;
            return 0f;   // absent padding cannot make the bound laxer than the raw card height
        }

        private static string Slice(string source, string start, string end, List<string> failures)
        {
            int a = source.IndexOf(start, StringComparison.Ordinal);
            int b = a >= 0 ? source.IndexOf(end, a + start.Length, StringComparison.Ordinal) : -1;
            if (a >= 0 && b > a) return source.Substring(a, b - a);
            failures.Add("could not isolate source block " + start);
            return string.Empty;
        }

        private static (Vector2 min, Vector2 max) AnchorsAfter(string source, string marker, List<string> failures)
        {
            int start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var matches = Regex.Matches(source.Substring(start, Math.Min(800, source.Length - start)),
                    @"new Vector2\(([-0-9.]+)f,\s*([-0-9.]+)f\)");
                if (matches.Count >= 2)
                {
                    float Parse(Group g) => float.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture);
                    return (new Vector2(Parse(matches[0].Groups[1]), Parse(matches[0].Groups[2])),
                            new Vector2(Parse(matches[1].Groups[1]), Parse(matches[1].Groups[2])));
                }
            }
            failures.Add("could not derive anchored rect after " + marker);
            return (Vector2.zero, Vector2.zero);
        }

        private static void Require(string source, string marker, string failure, List<string> failures)
        {
            if (source.IndexOf(marker, StringComparison.Ordinal) < 0) failures.Add(failure);
        }
    }
}

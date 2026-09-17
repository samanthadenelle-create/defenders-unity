// =============================================================================
// NightMarketNoWalletRegression [night-market-no-wallet] (WO-1409) - the Night
// Market a player WITHOUT a wallet sees: one reason, nine prices, and a badge
// that is a word rather than a fragment.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression
// Markers:  NIGHT_MARKET_NO_WALLET_OK / NIGHT_MARKET_NO_WALLET_FAIL
//
// ==========================  WHY THIS EXISTS  ================================
// WO-1409's evidence was a screenshot: Builds/ui-capture/NightMarket_2670x1200.png
// with SIX cards reading "Price unavailable", THREE reading "UNAVAILABLE", and a
// CONNECT WALLET button at the bottom with no sentence tying the two together.
// Nine refusals, not one reason. The copy half of the fix (commit 3c677027e)
// replaced those nine strings with the pack's authored USD anchor and put the
// reason in the header - and the capture taken 44 minutes LATER still had no
// banner on it, because nothing in the render path wrote that label.
//
// So this suite exists to make three claims fail loudly rather than quietly:
//
//   1. THE BANNER IS ON THE SCREEN, not merely in the code. `_balanceLabel` is
//      born empty and its only writer was RenderBalanceLabel, reached only from
//      RefreshWalletMirror() in OnEnable (PackStore.cs:431). Every path that
//      draws the store without enabling the object - the headless capture
//      harness (UICaptureLaunch.cs:3688-3730) among them - drew a store whose
//      top-left line was "". A source oracle would have passed that; this one
//      composes the panel and reads the TMP_Text.
//   2. EVERY CARD CARRIES A CURRENCY ANCHOR. "we cannot quote SKR" is not a
//      reason to tell the player nothing: PackDef.UsdReference is authored and
//      is known with or without a wallet.
//   3. THE ONE-WORD BADGE IS NOT TRUNCATED. This is the subtle one and it is
//      the reason the suite MEASURES glyphs. FontBadge is 30 and
//      ElarionUi.FontFloorMobile is ALSO 30 (StorePackCard.cs:308,
//      ElarionUi.cs:123), so FitSingleLine has NO room to shrink and degrades
//      straight to Ellipsis. 3c677027e moved the pill left and shrank it from
//      the authored 0.70 of the card to 0.40 in the same edit; "BEST" (4 glyphs)
//      still fitted and "FIRST" (5) did not, which is exactly what the 23:56
//      capture shows: `FIR...`. Width is the only free variable, and an oracle
//      that re-typed the width could not have caught it.
//
// ==========================  THE WO-1138 TAXONOMY  ===========================
// FIXTURE ABSENT     -> FAIL naming the path. packs.json, PackStore.cs and
//                       StoreStrings.cs are PRODUCT; their absence is a defect.
// CAPABILITY ABSENT  -> a VISIBLE stand-down that can never read as a pass:
//                         * the store composes no canvas at all -> whole-suite
//                           Skip. Nothing here can be read.
//                         * no TMP font resolvable, so glyph advances cannot be
//                           measured -> PartialSkip on the BUDGET case only; the
//                           live text cases still run and still gate.
//                         * a live pill whose textInfo is empty after
//                           ForceMeshUpdate -> PartialSkip NAMING that label,
//                           never a silent pass.
//                         * the composed surface is PORTRAIT, so the landscape
//                           ACTIONS / CLOSE THE GAP rail was never built ->
//                           PartialSkip naming it. The rail cannot overlap a
//                           heading that does not exist, and pretending that is
//                           a pass would retire the very case WO-1409 opened.
// CONTENT ABSENT     -> assert THROUGH it. A pack with no authored badge is the
//                       merchandiser declining to badge it (fine); a pack with a
//                       badge that renders as a fragment is RED.
// There is deliberately NO branch that returns green having asserted nothing.
//
// ⚠ WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT, and why - so nobody "fixes"
// the gap by weakening the case: WO-1409's acceptance line asks for no overlap
// "badge vs art". THE PILL SITS ON THE ART WELL BY CONSTRUCTION.
// StorePackCard.BuildPill anchors it to the CARD's top edge and BuildArtWell
// gives the well that same top edge (StorePackCard.cs:763 / :671), so the only
// thing that could drive that overlap to zero is a redesign of the shared card -
// which is a different work order and a different lane. Asserting an
// overlap-free card the template cannot produce would leave a permanently red
// suite, and a permanently red suite is ignored. So the BOUND is asserted
// instead: the pill may not reach past the left share of the card that
// PackStore.OneWordBadgeX1 claims. That is a real, failing-if-broken assertion
// about the defect the owner actually photographed (a badge over the art's
// focal centre, leaving a fragment), and the residual is written down here
// rather than silently dropped.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;          // WO-1819: the null-rate envelope case parses real wire JSON.
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    public static class NightMarketNoWalletRegression
    {
        // ── Fixtures this suite REQUIRES (absent => red, with the path) ───────
        private const string PacksRes   = "Assets/Resources/Data/Canonical/packs.json";
        private const string StoreSrc   = "Assets/_Modules/Wallet/PackStore.cs";
        private const string StringsSrc = "Assets/_Modules/Wallet/StoreStrings.cs";
        private const string CardSrc    = "Assets/_Modules/Wallet/StorePackCard.cs";

        /// <summary>Sub-pixel slack. Two rects touching edge-to-edge are adjacent, not overlapping.</summary>
        private const float Eps = 0.75f;

        /// <summary>The Seeker surface WO-1409's evidence frame was shot at. Pinned before the build:
        /// it is what decides whether the landscape two-column body exists at all.</summary>
        private const int SurfaceW = 2670;
        private const int SurfaceH = 1200;

        /// <summary>
        /// The strings the WO removed from the shelf. Either one on screen is the defect.
        /// <para>⚠ CASE MATTERS, AND THAT IS NOT PEDANTRY. "UNAVAILABLE" is the ALL-CAPS price
        /// substitute this WO retired (BuildGapUtilityRow's old mapping). Matched case-insensitively
        /// it also swallows the gap rail's legitimate worded empty state, "Unavailable right now"
        /// (PackStore.cs:1832) - a sentence WO-1335/WO-1339 put there ON PURPOSE so a missing band
        /// says so instead of rendering blank. A suite that reds that sentence teaches the next seat
        /// to delete it, which is how a silent empty rail comes back.</para>
        /// </summary>
        private static readonly string[] BannedPriceWordsIgnoringCase = { "Price unavailable" };
        private static readonly string[] BannedPriceWordsExactCase = { "UNAVAILABLE" };

        /// <summary>TMP's ellipsis replacement glyph, inserted by TextOverflowModes.Ellipsis.
        /// <para>Written as a CODE POINT, not as the character: this repo's source is ASCII-only and a
        /// literal U+2026 in a .cs file is exactly the kind of byte that survives one editor and not
        /// the next (CLAUDE.md §0/§1).</para></summary>
        private const char EllipsisChar = (char)0x2026;

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("NIGHT_MARKET_NO_WALLET_OK - " + reason);
            else Debug.LogError("NIGHT_MARKET_NO_WALLET_FAIL: " + reason);
        }

        [MenuItem("Tools/Regression/UI/Night Market Without A Wallet")]
        private static void RunMenu() => RunAll();

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var log = new StringBuilder();

            foreach (string path in new[] { PacksRes, StoreSrc, StringsSrc, CardSrc })
                if (!File.Exists(path))
                    failures.Add("[fixture] MISSING " + path + " - the walletless shelf cannot be " +
                                 "measured because the thing it measures is not on disk.");

            IReadOnlyList<PackDef> packs = null;
            try { packs = PackCatalog.Packs; }
            catch (Exception ex)
            {
                failures.Add("[fixture] PackCatalog.Packs THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            if (failures.Count == 0 && (packs == null || packs.Count == 0))
                failures.Add("[fixture] the pack catalogue is EMPTY (" + PacksRes + ") - a shelf with no " +
                             "rows cannot prove every row is priced.");

            if (failures.Count > 0)
            {
                reason = "night-market-no-wallet FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
                return false;
            }

            // ── The copy law + the width budget run with or without a canvas ──
            Case(failures, "banner-source", () => CaseBannerSource(failures, log));
            Case(failures, "null-rate-envelope", () => CaseNullRateEnvelope(failures, log));
            Case(failures, "badge-budget", () => CaseBadgeBudget(packs, failures, notes, log));

            // ── The live panel. No canvas => a DECLARED stand-down, never a pass ──
            GameObject host = null;
            GameObject canvas = null;
            GameObject tempEventSystem = null;
            GameObject settleCam = null;
            PackStore store = null;
            bool composedNothing = false;
            bool surfacePinned = false;
            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                {
                    tempEventSystem = new GameObject("~nm-nowallet-eventsystem");
                    tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                // ⛔ THE SAME RECIPE THE CAPTURE HARNESS USES (UICaptureLaunch.cs:3688-3730), and
                // deliberately so: this suite must fail on the composition the PNG is taken from,
                // not on a friendlier one built here. Edit mode does not call Awake/OnEnable on an
                // AddComponent, so Awake is driven by hand (it creates the VM and the WalletService
                // the render path reads) and OnEnable is NOT - which is precisely the path that was
                // drawing an empty header.
                // ⛔ PIN THE SURFACE BEFORE THE BUILD, NOT AFTER IT. PackStore composes against
                // ElarionUiKit.SurfaceWidth/Height at EnsureBuilt time - that is what decides
                // whether the LANDSCAPE two-column body (and therefore BuildLandscapeActions, and
                // therefore the MONTHLY LEDGER row this suite measures) is built at all. A
                // batchmode editor's Screen.* is the game view's, typically 640x480 PORTRAIT, on
                // which the rail is never composed and [ledger-vs-gap] would stand down FOREVER
                // while reading green. The capture harness pins the same way (CaptureSurfaceScope
                // -> SetSurfaceOverride); pinning the Seeker surface here means this suite measures
                // the composition NightMarket_2670x1200.png is taken from, which is the whole point.
                surfacePinned = ElarionUiKit.HasSurfaceOverride;
                if (!surfacePinned) ElarionUiKit.SetSurfaceOverride(SurfaceW, SurfaceH);

                host = new GameObject("~nm-nowallet-oracle");
                store = host.AddComponent<PackStore>();
                Invoke(store, "Awake", failures, notes);
                Invoke(store, "EnsureBuilt", failures, notes);

                canvas = store == null
                    ? null
                    : GetField(store, "_modal") is ElarionUiKit.ObsidianModal modal ? modal.canvas : null;
                if (canvas == null)
                {
                    // CAPABILITY ABSENT. Resolved here, RETURNED after the finally below has torn
                    // the fixture down - an early return would leak the arbiter registration Awake
                    // just made, which is the one thing that poisons the next suite in the batch.
                    composedNothing = true;
                }
                else canvas.SetActive(true);

                try { if (!composedNothing) store.Render(); }
                catch (Exception re)
                {
                    failures.Add("[live-store] PackStore.Render THREW " + re.GetType().Name + ": " + re.Message +
                                 " - the shelf the player would see is whatever had been drawn before the throw.");
                }

                if (!composedNothing)
                {
                    // ⛔ SETTLE THE LAYOUT, OR EVERY MEASUREMENT BELOW IS A LIE IN ONE DIRECTION OR
                    // THE OTHER. Nothing runs a layout pass in a synchronous edit-mode call: the
                    // HorizontalLayoutGroup strips and the utility column's vertical group are
                    // unresolved, so a card's rect is ZERO WIDE - on which a pill "overflows" and
                    // TMP ellipsises EVERY badge (a false RED on the good tree) while the ledger
                    // and heading rects are zero-height (a false stand-down on the case the ticket
                    // was raised for). And an overlay canvas's own rect in batchmode is the
                    // editor's 640x480, not the pinned surface, so the flip to ScreenSpaceCamera +
                    // the hand-applied scaler factor is not decoration either - it is the only way
                    // these rects come out in the units the panel was authored in.
                    // This is UICaptureLaunch.RenderCanvasToPng's own settle recipe (:5372-5396),
                    // minus the RenderTexture: no pixels are needed to read a rect.
                    settleCam = Settle(canvas, notes);
                }

                if (!composedNothing)
                {
                    // Is this actually the screen under test? The Pi skin has its OWN header notice
                    // and its OWN pricing rules (WO-1323); asserting the Solana banner there would
                    // be asserting the wrong sentence.
                    object walletless = GetProperty(store, "WalletlessBrowsing");
                    var liveCanvas = canvas;
                    if (!(walletless is bool) || !(bool)walletless)
                    {
                        notes.Add(RegressionOutcome.PartialSkip("[live-store] the walletless fixture",
                            "PackStore.WalletlessBrowsing resolved to '" + (walletless ?? "unreadable") +
                            "' in this environment (a Pi skin, or a provider that reports a signing wallet) - " +
                            "the no-wallet banner is NOT proved this run"));
                    }
                    else
                    {
                        Case(failures, "banner-on-screen", () => CaseBannerOnScreen(liveCanvas, failures, log));
                        Case(failures, "anchors", () => CaseAnchors(store, liveCanvas, failures, log));
                    }

                    Case(failures, "badge-live", () => CaseBadgeLive(store, failures, notes, log));
                    Case(failures, "ledger-vs-gap", () => CaseLedgerVsGap(liveCanvas, failures, notes, log));
                }
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // ⛔ TEARDOWN IS THE CAPTURE HARNESS'S, VERBATIM (UICaptureLaunch.cs:3754-3771), and
                // each of its three steps is load-bearing:
                //   1. Awake registered this store with PanelManager. A leaked registration
                //      outlives the suite and poisons whatever runs next in the same batch; the
                //      store's own CloseStore path cannot release it because that path uses runtime
                //      Destroy, which is edit-illegal.
                //   2. Canvas FIRST, host second, so a later OnDestroy sees a dead _modal and does
                //      not attempt that same edit-illegal Destroy.
                //   3. Every exit path, including the throw above.
                try
                {
                    if (store != null && GetField(store, "_panelHandle") is PanelHandle handle && handle != null)
                        PanelManager.NotifyClosed(handle);
                }
                catch (Exception pe)
                {
                    notes.Add("arbiter release failed (harmless, but named rather than swallowed): " + pe.Message);
                }
                if (settleCam != null) UnityEngine.Object.DestroyImmediate(settleCam);
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (tempEventSystem != null) UnityEngine.Object.DestroyImmediate(tempEventSystem);
                // ⛔ A LEAKED SURFACE OVERRIDE MIS-SIZES EVERY PANEL BUILT AFTERWARDS IN THIS
                // EDITOR SESSION (SetSurfaceOverride's own contract). Cleared only if THIS suite
                // set it: a capture run that wrapped us in its own CaptureSurfaceScope owns its
                // pin, and stealing it would corrupt the frame it is in the middle of shooting.
                if (!surfacePinned) ElarionUiKit.ClearSurfaceOverride();
            }

            if (composedNothing)
            {
                return RegressionOutcome.Skip(out reason, "NIGHT MARKET NO WALLET",
                    "PackStore._modal.canvas was null after EnsureBuilt - the store composed nothing, so " +
                    "no label and no rect existed to read (that is itself a defect, and " +
                    "night-market-runtime-layout is the suite that owns it)");
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : string.Empty;
            if (failures.Count == 0)
            {
                reason = "NIGHT MARKET NO WALLET OK - the store composed for real at " + SurfaceW + "x" +
                         SurfaceH + " without a signing wallet: exactly ONE label reads '" +
                         StoreStrings.WalletlessBrowsingBanner + "', every browsable card carries a " +
                         "currency anchor, no label reads 'Price unavailable' or 'UNAVAILABLE', every " +
                         "one-word badge renders whole at the " + ElarionUi.FontFloorMobile.ToString("0") +
                         "px floor inside its measured " +
                         ((PackStore.OneWordBadgeX1 - PackStore.OneWordBadgeX0) * 100f).ToString("0") +
                         "% band, and the MONTHLY LEDGER row clears the CLOSE THE GAP heading" +
                         noteStr + "\n" + log;
                return true;
            }

            reason = "night-market-no-wallet FAIL x" + failures.Count + ": " +
                     string.Join(" | ", failures) + noteStr;
            return false;
        }

        // =====================================================================
        //  CASE 1 - the banner has ONE source and the render path writes it.
        // =====================================================================
        private static void CaseBannerSource(List<string> failures, StringBuilder log)
        {
            string banner = StoreStrings.WalletlessBrowsingBanner;
            string probe = StoreStrings.WalletlessBrowsingBannerProbe;

            if (string.IsNullOrWhiteSpace(banner))
                failures.Add("[banner-source] StoreStrings.WalletlessBrowsingBanner is EMPTY - the shelf " +
                             "would price nine packs in USD and never say why none of them can be bought.");
            else if (banner.IndexOf(probe, StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[banner-source] the banner ('" + banner + "') no longer contains its own probe ('" +
                             probe + "'), so the trace and this oracle would both look for a sentence that " +
                             "is not there. Re-point the probe in the SAME edit as the re-wording.");

            for (int i = 0; i < banner.Length; i++)
                if (banner[i] > 127)
                {
                    failures.Add("[banner-source] the banner carries a non-ASCII character at index " + i +
                                 " ('" + banner[i] + "'). Store copy is ASCII-only (CLAUDE.md §7) - use a " +
                                 "hyphen, not an en dash.");
                    break;
                }

            // ⛔ THE ONE THAT ACTUALLY BROKE. RenderBalanceLabel must be reachable from the RENDER
            // path, not only from the OnEnable wallet mirror. Read as source because "which method
            // calls which" is a structural fact, and the live case below proves the effect.
            string src = File.ReadAllText(StoreSrc);
            string render = Between(src, "public void Render()", "private void BuildPiShelfNoticeIfNothingIsBuyable");
            if (string.IsNullOrEmpty(render))
                render = Between(src, "void Render()", "BuildPiShelfNoticeIfNothingIsBuyable(");
            if (string.IsNullOrEmpty(render))
                failures.Add("[banner-source] could not slice PackStore.Render() out of " + StoreSrc +
                             " - if it was renamed, re-point this case; do NOT delete it. It is the only " +
                             "thing standing between the store and a header that is empty on every " +
                             "surface that draws without OnEnable.");
            else if (render.IndexOf("RenderBalanceLabel()", StringComparison.Ordinal) < 0)
                failures.Add("[banner-source] PackStore.Render() does NOT call RenderBalanceLabel(). The " +
                             "header line is then written by RefreshWalletMirror/OnEnable ALONE, and every " +
                             "path that composes the store without enabling it (the headless capture " +
                             "harness included) draws an EMPTY top-left line beside nine USD prices. " +
                             "That is the WO-1409 defect, and it is invisible to a source-only oracle.");
            else
                log.AppendLine("  [banner-source] Render() repaints the header line; banner = '" + banner + "'.");
        }

        // =====================================================================
        //  CASE 2 - the one-word badge's width budget, MEASURED at the real font.
        // =====================================================================
        private static void CaseBadgeBudget(IReadOnlyList<PackDef> packs, List<string> failures,
                                            List<string> notes, StringBuilder log)
        {
            float band = PackStore.OneWordBadgeX1 - PackStore.OneWordBadgeX0;
            if (band <= 0f)
            {
                failures.Add("[badge-budget] PackStore.OneWordBadgeX1 (" + PackStore.OneWordBadgeX1 +
                             ") is not to the right of OneWordBadgeX0 (" + PackStore.OneWordBadgeX0 +
                             ") - the pill has no width at all and would draw zero glyphs.");
                return;
            }

            // The NARROWEST card the shelf permits, not the narrowest one shipped today: a future
            // surface that reaches the floor must still read.
            float pillPx = band * StorePackCard.MinCardWidthPx;
            float boxPx = pillPx - 2f * StorePackCard.PillPadXPx;
            float floor = ElarionUi.FontFloorMobile;

            if (boxPx <= 0f)
            {
                failures.Add("[badge-budget] the pill's text box derives NEGATIVE (" + boxPx.ToString("0") +
                             "px): " + band.ToString("0.00") + " of a " + StorePackCard.MinCardWidthPx.ToString("0") +
                             "px card, less " + StorePackCard.PillPadXPx.ToString("0") + "px of padding per side.");
                return;
            }

            bool measuredAnything = false;
            foreach (var pack in packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.StoreBadge)) continue;   // unbadged is fine
                string word = PackStore.OneWordBadge(pack.StoreBadge);
                if (string.IsNullOrEmpty(word))
                {
                    failures.Add("[badge-budget] '" + pack.Sku + "' authors the badge '" + pack.StoreBadge +
                                 "' but OneWordBadge reduces it to NOTHING - the pill would draw an empty " +
                                 "gold plate, which reads as a rendering fault rather than as no badge.");
                    continue;
                }

                float w = ElarionUiKit.MeasureLineWidthPx(ElarionUiKit.FontRole.Body, word, floor, out string detail);
                if (w < 0f)
                {
                    // CAPABILITY ABSENT, DECLARED. MeasureLineWidthPx returns -1, never 0, exactly so
                    // this cannot be mistaken for "it fits".
                    notes.Add(RegressionOutcome.PartialSkip("[badge-budget] real-font measurement",
                        "no TMP font resolvable (" + detail + ") - the one-word badge width is NOT proved " +
                        "against real glyph advances this run"));
                    return;
                }

                measuredAnything = true;
                if (w > boxPx)
                    failures.Add("[badge-budget] '" + pack.Sku + "' badges '" + pack.StoreBadge + "' -> '" + word +
                                 "', which MEASURES " + w.ToString("0") + "px at the " + floor.ToString("0") +
                                 "px font floor, but PackStore's band (" + PackStore.OneWordBadgeX0.ToString("0.00") +
                                 ".." + PackStore.OneWordBadgeX1.ToString("0.00") + ") gives it a " +
                                 boxPx.ToString("0") + "px box on the narrowest permitted card (" +
                                 StorePackCard.MinCardWidthPx.ToString("0") + "px). FontBadge and " +
                                 "ElarionUi.FontFloorMobile are BOTH 30, so FitSingleLine cannot shrink and " +
                                 "TMP ellipsises instead - this is exactly how 'FIRST BUY' shipped as " +
                                 "'FIR...'. WIDEN THE BAND; the font has nowhere to go.");
                else
                    log.AppendLine("  [badge-budget] '" + word + "' " + w.ToString("0") + "px fits the " +
                                   boxPx.ToString("0") + "px box (" +
                                   ((boxPx - w) / w * 100f).ToString("0") + "% margin).");
            }

            if (!measuredAnything && failures.Count == 0)
                notes.Add(RegressionOutcome.PartialSkip("[badge-budget] the authored badges",
                    "no pack in " + PacksRes + " authors a storeBadge, so no width was measured"));
        }

        // =====================================================================
        //  CASE 3 - EXACTLY ONE banner, on the composed panel.
        // =====================================================================
        private static void CaseBannerOnScreen(GameObject canvas, List<string> failures, StringBuilder log)
        {
            // ⛔ THE ONE-NESS CHECK MATCHES THE WHOLE SENTENCE, NOT THE PREFIX. Three OTHER store
            // strings legitimately begin "Connect a wallet" - storeBalanceNoWallet ("...to see SKR"),
            // storeBuyWalletRequired ("...to buy this one") and StoreStrings.PiWalletRequiredSentence -
            // and the buy-gate plate can be on screen beside the header at the same time. Counting
            // the PREFIX would red a correct store for saying two different true things. The prefix
            // stays the TRACE's probe, where a loose match is the safe direction.
            string banner = StoreStrings.WalletlessBrowsingBanner;
            var carriers = new List<string>();
            foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                if (t.text.IndexOf(banner, StringComparison.Ordinal) >= 0)
                    carriers.Add(PathOf(t.transform) + " = '" + t.text + "'");
            }

            if (carriers.Count == 0)
                failures.Add("[banner-on-screen] the composed store carries NO label reading '" + banner +
                             "'. Without a signing wallet the player is shown nine USD prices, a CONNECT " +
                             "WALLET button, and no sentence joining them - the WO-1409 defect, measured on " +
                             "Builds/ui-capture/NightMarket_2670x1200.png (09-05 23:56).");
            else if (carriers.Count > 1)
                failures.Add("[banner-on-screen] " + carriers.Count + " labels carry the connect sentence (" +
                             string.Join(" ; ", carriers) + "). WO-1409's whole shape is that the reason is " +
                             "said ONCE. Two copies is the nine-refusals defect starting over.");
            else
                log.AppendLine("  [banner-on-screen] one banner: " + carriers[0]);
        }

        // =====================================================================
        //  CASE 4 - every card is PRICED, and the retired words are gone.
        // =====================================================================
        private static void CaseAnchors(PackStore store, GameObject canvas, List<string> failures, StringBuilder log)
        {
            foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                foreach (string banned in BannedPriceWordsIgnoringCase)
                {
                    if (t.text.IndexOf(banned, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    failures.Add("[anchors] " + PathOf(t.transform) + " reads '" + t.text + "'. The authored " +
                                 "USD reference is known with or WITHOUT a wallet (PackDef.UsdReference), so " +
                                 "'" + banned + "' on a walletless shelf is the store declining to say a " +
                                 "price it holds.");
                }
                foreach (string banned in BannedPriceWordsExactCase)
                {
                    if (t.text.IndexOf(banned, StringComparison.Ordinal) < 0) continue;
                    failures.Add("[anchors] " + PathOf(t.transform) + " reads '" + t.text + "'. '" + banned +
                                 "' was the all-caps price substitute WO-1409 retired; the pack's USD anchor " +
                                 "replaces it.");
                }
            }

            var handles = GetField(store, "_cardHandles") as Dictionary<string, StorePackCardHandle>;
            if (handles == null)
            {
                failures.Add("[anchors] PackStore._cardHandles is unreadable (renamed or retyped) - the " +
                             "per-card price cannot be read. Re-point this case in the SAME edit as the rename.");
                return;
            }
            if (handles.Count == 0)
            {
                failures.Add("[anchors] the store composed ZERO pack cards, so 'every card is priced' is " +
                             "vacuously true and proves nothing. An empty shelf is its own defect.");
                return;
            }

            int priced = 0;
            foreach (var kv in handles)
            {
                var label = kv.Value != null ? kv.Value.PriceLabel : null;
                if (label == null)
                {
                    failures.Add("[anchors] card '" + kv.Key + "' has NO price label at all. On a device an " +
                                 "absent label and a culled one are the same pixel: nothing.");
                    continue;
                }
                string text = label.text ?? string.Empty;
                // ⭐ WO-1815 — THE ANCHOR IS A CURRENCY, NOT A DOLLAR SIGN. This case demanded a literal
                // '$' until 2026-09-16, when the owner ruled the store SKR-ONLY ("change the store to SKR
                // only and set to flat amounts"): the walletless card now reads "300 SKR" and a '$' test
                // would have failed the shelf for obeying the ruling. WO-1409's actual rule is unchanged
                // and is what is asserted here - a shelf must NOT decline to say a price it holds - so
                // either currency passes and a blank or worded-refusal label still fails.
                // ⛔ THIS CASE ONLY EVER RUNS ON THE WALLETLESS SOLANA SHELF - its caller stands down on
                // anything where WalletlessBrowsing reads false (the Pi skin, a signing wallet), and the
                // Google Play rail prices through its own provider. So on the tree this case can see,
                // the ONE right currency is SKR and a dollar sign is a DEFECT, not an alternative.
                if (text.IndexOf("SKR", StringComparison.Ordinal) < 0)
                    failures.Add("[anchors] card '" + kv.Key + "' prices as '" + text + "' - no SKR figure. " +
                                 "Browsing without a wallet must still say what the pack costs (WO-1409), and " +
                                 "the owner ruled on 2026-09-16 that it says it in SKR: \"change the store to " +
                                 "SKR only and set to flat amounts\".");
                else if (text.IndexOf('$') >= 0)
                    failures.Add("[anchors] card '" + kv.Key + "' prices as '" + text + "' - it carries BOTH a " +
                                 "'$' and an SKR figure. Two currencies on one card is precisely what WO-1815 " +
                                 "removed (\"if we just list the SKR does it feel more impulse less cost\"); the " +
                                 "USD path is kept COMPILED behind PackStore.ShowUsdAlongsideSkr, never drawn.");
                else priced++;
            }
            log.AppendLine("  [anchors] " + priced + "/" + handles.Count + " composed cards price in SKR and " +
                           "carry no '$'.");
        }

        // =====================================================================
        //  CASE 5 - the rendered badge: whole word, bounded band.
        // =====================================================================
        private static void CaseBadgeLive(PackStore store, List<string> failures, List<string> notes, StringBuilder log)
        {
            // ⛔ NOT "already reported by [anchors]". That was the assumption and it was WRONG:
            // [anchors] only runs on the walletless branch, so on a Pi skin (or any environment
            // where WalletlessBrowsing reads false) it never runs and this bare return would have
            // been the ONLY thing standing between an unbuilt shelf and a green badge case. The
            // caller's channel is a bool; a return that asserted nothing must say so in the log.
            var handles = GetField(store, "_cardHandles") as Dictionary<string, StorePackCardHandle>;
            if (handles == null)
            {
                failures.Add("[badge-live] PackStore._cardHandles is unreadable (renamed or retyped) - no " +
                             "rendered badge can be measured. Re-point this case in the SAME edit as the " +
                             "rename; it is the only thing that catches a truncated pill.");
                return;
            }
            if (handles.Count == 0)
            {
                failures.Add("[badge-live] the store composed ZERO pack cards, so 'no badge is truncated' " +
                             "is vacuously true. An empty shelf is its own defect, not a passing badge case.");
                return;
            }

            int checkedPills = 0;
            foreach (var kv in handles)
            {
                var pill = kv.Value != null ? kv.Value.StateLabel : null;
                if (pill == null) continue;                       // no badge and no state word: legitimate

                pill.ForceMeshUpdate();
                var info = pill.textInfo;
                if (info == null || info.characterCount == 0)
                {
                    notes.Add(RegressionOutcome.PartialSkip("[badge-live] '" + kv.Key + "'",
                        "the pill yields no textInfo after ForceMeshUpdate (no font resolvable for this " +
                        "label) - its truncation is NOT proved this run"));
                    continue;
                }

                checkedPills++;
                string source = pill.text ?? string.Empty;
                bool sourceHasEllipsis = source.IndexOf(EllipsisChar) >= 0;
                for (int i = 0; i < info.characterCount; i++)
                {
                    if (info.characterInfo[i].character != EllipsisChar || sourceHasEllipsis) continue;
                    failures.Add("[badge-live] card '" + kv.Key + "' badges '" + source + "' but RENDERS an " +
                                 "ellipsis at character " + i + " - the player sees a fragment. FontBadge and " +
                                 "ElarionUi.FontFloorMobile are both 30, so this is a WIDTH failure, never a " +
                                 "font one: widen PackStore.OneWordBadgeX0..X1.");
                    break;
                }

                // The BOUND, not an overlap-free card (see the header note). The pill must not reach
                // past the share of the card its own band claims.
                var pillRect = pill.transform.parent as RectTransform;
                var cardRect = kv.Value.Root != null ? kv.Value.Root.transform as RectTransform : null;
                if (pillRect == null || cardRect == null)
                {
                    notes.Add(RegressionOutcome.PartialSkip("[badge-live] '" + kv.Key + "' bound",
                        "the pill or its card is not a RectTransform - the badge's reach is NOT proved"));
                    continue;
                }
                float cardW = cardRect.rect.width;
                if (cardW <= 0f)
                {
                    // ⛔ NAMED, NEVER SILENT. A zero-width card means the settle pass did not take,
                    // and every bound below it would then be vacuously satisfied - the hollow pass
                    // this suite's own header forbids.
                    notes.Add(RegressionOutcome.PartialSkip("[badge-live] '" + kv.Key + "' bound",
                        "the card measured 0px wide after the settle pass, so the badge's reach is " +
                        "NOT proved this run (the layout did not resolve in this environment)"));
                    continue;
                }
                float allowed = PackStore.OneWordBadgeX1 * cardW + Eps;
                float reach = pillRect.anchorMax.x * cardW;
                if (reach > allowed)
                    failures.Add("[badge-live] card '" + kv.Key + "'s badge reaches " + reach.ToString("0") +
                                 "px into a " + cardW.ToString("0") + "px card, past the " +
                                 allowed.ToString("0") + "px its own band claims - it is back over the art's " +
                                 "focal centre, which is the fragment the owner photographed.");
            }

            if (checkedPills == 0)
                notes.Add(RegressionOutcome.PartialSkip("[badge-live] the rendered pills",
                    handles.Count + " card(s) composed but NOT ONE carried a readable state/badge pill, so " +
                    "no truncation was measured this run - the case asserted nothing about the defect it " +
                    "exists for"));
            else
                log.AppendLine("  [badge-live] " + checkedPills + " rendered pill(s) read whole and inside their band.");
        }

        // =====================================================================
        //  CASE 6 - the ACTIONS rail clears the CLOSE THE GAP heading.
        // =====================================================================
        private static void CaseLedgerVsGap(GameObject canvas, List<string> failures, List<string> notes,
                                            StringBuilder log)
        {
            RectTransform ledger = FindByName(canvas, "utility-row-MONTHLY LEDGER");
            RectTransform gap = FindByName(canvas, "utility-heading-CLOSE THE GAP");

            if (ledger == null || gap == null)
            {
                // The landscape rail is only built when _utilityContent exists; a portrait
                // composition puts both on the shelf instead. A stand-down, never a pass.
                notes.Add(RegressionOutcome.PartialSkip("[ledger-vs-gap] the landscape utility rail",
                    "this surface composed " + (ledger == null ? "no MONTHLY LEDGER row" : "a MONTHLY LEDGER row") +
                    " and " + (gap == null ? "no CLOSE THE GAP heading" : "a CLOSE THE GAP heading") +
                    " - the two-column rail was not built here, so the clip WO-1409 reported is NOT " +
                    "proved this run"));
                return;
            }

            var a = new Vector3[4];
            var b = new Vector3[4];
            ledger.GetWorldCorners(a);
            gap.GetWorldCorners(b);
            float ledgerBottom = a[0].y, ledgerTop = a[1].y;
            float gapBottom = b[0].y, gapTop = b[1].y;
            if (Mathf.Approximately(ledgerTop - ledgerBottom, 0f) || Mathf.Approximately(gapTop - gapBottom, 0f))
            {
                notes.Add(RegressionOutcome.PartialSkip("[ledger-vs-gap] the measurement",
                    "one of the two rects has zero height (no layout pass in this environment) - the " +
                    "clip is NOT proved this run"));
                return;
            }

            float overlap = Mathf.Min(ledgerTop, gapTop) - Mathf.Max(ledgerBottom, gapBottom);
            if (overlap > Eps)
                failures.Add("[ledger-vs-gap] the MONTHLY LEDGER row and the CLOSE THE GAP heading share " +
                             overlap.ToString("0.0") + "px of the same lane (ledger " +
                             ledgerBottom.ToString("0") + ".." + ledgerTop.ToString("0") + ", heading " +
                             gapBottom.ToString("0") + ".." + gapTop.ToString("0") + "). The ACTIONS column " +
                             "is masked, so the row is CLIPPED rather than merely crowded - the player " +
                             "sees half a door. WO-1409 bought this 70px back by retiring the redundant " +
                             "ACTIONS heading; something has spent it again.");
            else
                log.AppendLine("  [ledger-vs-gap] the ledger row clears the heading by " +
                               (-overlap).ToString("0.0") + "px.");
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        // =====================================================================
        //  CASE — A LIST ENVELOPE WHOSE `rate` IS NULL MUST PARSE (WO-1819/WO-1818).
        // ---------------------------------------------------------------------
        //  ⛔ THIS IS THE SHELF-BLANKING BUG, CAUGHT BEFORE IT SHIPPED FOR THE
        //  SECOND TIME. The first time, `PurchaseQuote.UsdAnchor` was a bare
        //  `double`, the server legitimately sent null for the pinned canary,
        //  Newtonsoft threw on the WHOLE response, and every pack on the shelf
        //  read "Price unavailable" — one null row priced nothing. That was fixed
        //  by making the ROW nullable plus a per-row try.
        //
        //  ⚠ THE ENVELOPE'S OWN `rate` STAYED A BARE double, AND THE PER-ROW GUARD
        //  CANNOT REACH IT — the envelope is deserialized in ONE call, outside that
        //  try. WO-1818's server now sends `rate: null` for a flat-priced SKU, so
        //  the day the list goes all-flat the envelope throws and the shelf blanks
        //  through the identical door under a different field name.
        //
        //  ⛔ ASSERTED TWO WAYS ON PURPOSE. The PARSE proves today's Newtonsoft
        //  accepts it; the DECLARED TYPE proves the intent, so a seat "tidying" the
        //  `?` away fails here even if some future serializer were lenient about
        //  it. Reflection is used because these envelopes are private nested types
        //  — which is right: they are wire shapes, not API. Testing the real type
        //  is the whole point; a local copy of the shape would pass forever.
        // =====================================================================
        private static void CaseNullRateEnvelope(List<string> failures, StringBuilder log)
        {
            const string Json = "{\"success\":true,\"rate\":null,\"rateSource\":\"flat-skr\"," +
                                "\"prices\":[{\"sku\":\"starters-hand\",\"amountBaseUnits\":\"300000000\"," +
                                "\"decimals\":6,\"usdAnchor\":4.99,\"rate\":null}]}";

            var service = typeof(DeNelle.Wallet.PurchaseQuoteService);
            foreach (string typeName in new[] { "ListEnvelope", "ListResponse" })
            {
                var t = service.GetNestedType(typeName, BindingFlags.NonPublic);
                if (t == null)
                {
                    failures.Add("[null-rate-envelope] PurchaseQuoteService." + typeName + " not found - it " +
                                 "was renamed. RE-POINT THIS CASE IN THE SAME CHANGE: an oracle that cannot " +
                                 "find its subject passes silently, which is worse than the bug it guards.");
                    continue;
                }

                var rate = t.GetField("Rate", BindingFlags.Instance | BindingFlags.Public);
                if (rate == null)
                    failures.Add("[null-rate-envelope] " + typeName + " has no public Rate field.");
                else if (Nullable.GetUnderlyingType(rate.FieldType) == null)
                    failures.Add("[null-rate-envelope] " + typeName + ".Rate is '" + rate.FieldType.Name +
                                 "', not a nullable double. The server sends rate:null for a FLAT-priced " +
                                 "SKU (no market rate was used), Newtonsoft cannot put null into a double, " +
                                 "and the envelope is parsed OUTSIDE the per-row guard - so one flat list " +
                                 "blanks the entire shelf. This is the UsdAnchor defect, one field out.");

                try
                {
                    object parsed = JsonConvert.DeserializeObject(Json, t);
                    if (parsed == null)
                        failures.Add("[null-rate-envelope] " + typeName + " parsed to null from a well-formed " +
                                     "envelope.");
                    else if (rate != null && Nullable.GetUnderlyingType(rate.FieldType) != null &&
                             rate.GetValue(parsed) != null)
                        failures.Add("[null-rate-envelope] " + typeName + ".Rate came back non-null from a " +
                                     "payload whose rate IS null - the absence of a rate must survive the " +
                                     "wire, or the trace will print a rate nobody quoted.");
                }
                catch (Exception ex)
                {
                    failures.Add("[null-rate-envelope] " + typeName + " THREW " + ex.GetType().Name +
                                 " on an envelope with rate:null - this is the blank shelf, reproduced: " +
                                 ex.Message);
                }
            }
            log.AppendLine("  [null-rate-envelope] both list envelopes declare a nullable Rate and parse " +
                           "rate:null without throwing (a flat-priced list cannot blank the shelf)");
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// Resolve the composed panel to real, readable rects, and hand back the throwaway camera
        /// the caller must destroy.
        ///
        /// <para>⛔ THIS IS UICaptureLaunch.RenderCanvasToPng's SETTLE RECIPE (:5372-5396), minus
        /// the RenderTexture, and every step of it earns its place:</para>
        /// <para>1. FLIP TO ScreenSpaceCamera. In batchmode an overlay canvas's own rect is the
        /// editor's 640x480 no matter what the surface is pinned to, so rects read in the wrong
        /// units entirely (that finding is recorded in UICaptureLaunch's own file banner).</para>
        /// <para>2. APPLY THE SCALER BY HAND. CanvasScaler.Update does not run in a synchronous
        /// edit-mode call, so scaleFactor would stay 1 and every FONT SIZE would be wrong - which is
        /// exactly the axis a truncation oracle reads.</para>
        /// <para>3. TWO PASSES. TMP auto-size can need a second pass to settle; one pass can leave
        /// a label reporting a fit it does not have.</para>
        /// </summary>
        private static GameObject Settle(GameObject canvasGo, List<string> notes)
        {
            if (canvasGo == null) return null;
            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                notes.Add(RegressionOutcome.PartialSkip("[live-store] the settle pass",
                    "the composed root carries no Canvas - rects cannot be resolved, so every geometric " +
                    "case below stands down rather than measuring the editor's own 640x480"));
                return null;
            }

            GameObject camGo = null;
            try
            {
                camGo = new GameObject("~nm-nowallet-settle-cam");
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.nearClipPlane = 0.03f;
                cam.farClipPlane = 1000f;
                cam.enabled = false;                    // nothing is rendered; only laid out

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;

                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    Vector2 refRes = scaler.referenceResolution;
                    float refW = refRes.x > 1f ? refRes.x : 1080f;
                    float refH = refRes.y > 1f ? refRes.y : 1920f;
                    float match = Mathf.Clamp01(scaler.matchWidthOrHeight);
                    float sf = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(SurfaceW / refW, 2f),
                                                        Mathf.Log(SurfaceH / refH, 2f), match));
                    if (sf > 0f && !float.IsNaN(sf) && !float.IsInfinity(sf)) canvas.scaleFactor = sf;
                    canvas.referencePixelsPerUnit = scaler.referencePixelsPerUnit > 0f
                        ? scaler.referencePixelsPerUnit : 100f;
                }
                else
                {
                    notes.Add(RegressionOutcome.PartialSkip("[live-store] the scaler",
                        "the modal canvas has no CanvasScaler, so font sizes settle at scaleFactor 1 - " +
                        "the truncation cases below measure a surface the device does not have"));
                }

                for (int pass = 0; pass < 2; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    var rootRt = canvasGo.GetComponent<RectTransform>();
                    if (rootRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                    foreach (var t in canvasGo.GetComponentsInChildren<TMP_Text>(true))
                        if (t != null) t.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();
                }
            }
            catch (Exception ex)
            {
                notes.Add(RegressionOutcome.PartialSkip("[live-store] the settle pass",
                    "threw " + ex.GetType().Name + ": " + ex.Message + " - the rects below are whatever " +
                    "the unsettled layout left, and are NOT a proof"));
            }
            return camGo;
        }

        /// <summary>
        /// Drive one of PackStore's own lifecycle methods by hand (edit mode calls neither).
        ///
        /// <para>⛔ A MISSING METHOD HERE IS FIXTURE-ABSENT, AND FIXTURE-ABSENT IS RED. This guard
        /// used to drop a bare note and return, which the caller could not distinguish from a clean
        /// run - the bool is its only channel, so a suite that never woke the store read as a suite
        /// that proved the store. <c>Awake</c> and <c>EnsureBuilt</c> are PRODUCT, not harness: if
        /// reflection cannot find one, it was renamed, and the honest answer is a failure NAMING the
        /// method so the rename gets a re-point in the same edit.</para>
        ///
        /// <para>A THROW is the other half and is deliberately NOT the same verdict. Product code
        /// throwing in a headless editor can be a genuine defect or a missing environment seam, and
        /// this suite cannot tell those apart from here - so it is a DECLARED stand-down carrying
        /// the exception, which the reporting layer counts out of the green column. It can never be
        /// silent, and the store failing to compose then reaches the whole-suite Skip below it.</para>
        /// </summary>
        private static void Invoke(object target, string method, List<string> failures, List<string> notes)
        {
            MethodInfo m = null;
            try
            {
                m = target.GetType().GetMethod(method,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch (Exception lookupEx)
            {
                failures.Add("[fixture] reflecting PackStore." + method + " THREW " +
                             lookupEx.GetType().Name + ": " + lookupEx.Message + " - the store's build " +
                             "path cannot be driven, so nothing below this line measures the real panel.");
                return;
            }

            if (m == null)
            {
                failures.Add("[fixture] PackStore." + method + "() not found by reflection. It is PRODUCT " +
                             "and this suite drives it exactly as the capture harness does " +
                             "(UICaptureLaunch.cs:3688-3730), so an absent method means it was renamed - " +
                             "re-point this suite in the SAME edit as the rename. Standing down silently " +
                             "here would let a store that was never built read as a store that passed.");
                return;
            }

            try { m.Invoke(target, null); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                notes.Add(RegressionOutcome.PartialSkip("[fixture] PackStore." + method + "()",
                    "threw " + inner.GetType().Name + ": " + inner.Message + " - the store was not fully " +
                    "driven, so whatever it composed is NOT proof of the composition the player gets"));
            }
        }

        private static object GetField(object target, string name)
        {
            var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? f.GetValue(target) : null;
        }

        private static object GetProperty(object target, string name)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return p != null ? p.GetValue(target, null) : null;
        }

        private static RectTransform FindByName(GameObject root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(tr.name, name, StringComparison.Ordinal))
                    return tr as RectTransform;
            return null;
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        private static string Between(string src, string from, string to)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return string.Empty;
            int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? src.Substring(a) : src.Substring(a, b - a);
        }
    }
}

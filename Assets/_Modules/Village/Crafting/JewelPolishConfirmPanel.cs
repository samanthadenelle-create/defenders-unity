// =============================================================================
// JewelPolishConfirmPanel — the REQUIRED disclosure + confirm step before a re-polish
// (WO-1042; owner ruling 2026-08-16: "throw a warning up there that, hey. You could get
// any of these understones? Are you sure you wanna do this?").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// ⛔ THIS SCREEN IS NOT OPTIONAL POLISH. It is the disclosure a random, purchasable-attempt
// outcome legally and ethically requires, and the owner asked for it by name. It shows:
//   • the stone the player currently holds, BESIDE the outcome table, so the DOWNSIDE is
//     concrete rather than abstract — the whole point of a re-roll that can trade down,
//   • every possible outcome with its REAL percentage,
//   • the SHATTER chance, stated explicitly,
//   • an explicit confirm, with cancel as the easy default.
//
// ⛔⛔ THE PERCENTAGES ARE DERIVED, NEVER AUTHORED HERE. ⛔⛔
// Every number on this screen comes from JewelPolishService.DescribeOdds, which computes
// from the SAME table JewelPolishService.RollOutcome and the polish effect actually use.
// There is no second copy anywhere in this file — do not add one, not even "just for the
// summary line". Two hand-maintained copies of the same odds is the defect class that has
// already produced several bugs in this project, and HERE a drift between displayed and
// actual would be MISREPRESENTATION of a random outcome, not merely a bug. Pinned by
// DungeonGemExclusivityRegression.
//
// LAYOUT: top-down PIXEL flow (StackDown), deliberately NOT per-element fractional bands.
// The odds list is variable-height (3 gems + a shatter line today, more if the table grows),
// which is exactly the WO-865 shape that broke DungeonTreasurePanel — a variable-height
// element among fixed fractional neighbours silently grows through them. Built with
// ElarionUiKit throughout: the UiObsidianConformance ratchet HARD-FAILS hand-rolled uGUI,
// and UXML does not render in player builds.
// ASCII-only copy (tofu on device otherwise); meaning never by colour alone (every line
// prints its percentage as TEXT, so it reads in greyscale and for a colourblind player).
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village.Items;

namespace DeNelle.Village.Crafting
{
    /// <summary>The "are you sure?" modal shown before a re-polish, with the real odds disclosed.</summary>
    public static class JewelPolishConfirmPanel
    {
        private const string Sys = "JewelPolish";
        private const string PanelName = "JewelPolishConfirm";

        // Fixed-pixel bands (a fractional band culls glyphs), flowed from the content's top edge.
        private const float LinePx = 46f;
        private const float HeadingPx = 52f;
        private const float StackTopPx = 10f;
        private const float StackGapPx = 6f;

        private static GameObject s_canvas;
        private static PanelHandle s_handle;
        private static Action s_onConfirm;

        /// <summary>True while the confirm modal is on screen.</summary>
        public static bool IsOpen => s_canvas != null;

        /// <summary>
        /// Ask the player to confirm re-polishing <paramref name="heldGemId"/>, disclosing the real
        /// odds first. <paramref name="onConfirm"/> runs ONLY on an explicit confirm — never on
        /// cancel, never on a forced close.
        /// <para>
        /// Returns FALSE when the modal could not open, in which case the caller must NOT proceed:
        /// an un-disclosed re-roll is the one outcome this screen exists to prevent, so failing
        /// closed is correct even though it costs the player a tap.
        /// </para>
        /// </summary>
        public static bool Show(string heldGemId, Action onConfirm)
        {
            if (s_canvas != null)
            {
                FlowTrace.Warn(Sys, "confirm panel already open - ignoring duplicate Show.");
                return false;
            }

            int score = JewelPolishCatalog.RePolishScore;
            var odds = JewelPolishService.DescribeOdds(score, isRePolish: true);
            if (odds == null || odds.Count == 0)
            {
                // FAIL CLOSED. Without odds there is nothing to disclose, and proceeding would be an
                // undisclosed random outcome - exactly what this screen exists to prevent.
                FlowTrace.Fail(Sys, "confirm REFUSED: no odds to disclose (jewel-polish.json missing " +
                                    "or empty). The re-polish is blocked rather than run undisclosed.");
                return false;
            }

            var modal = ElarionUiKit.BuildObsidianModal(
                PanelName, new LocalizedText("village.crafting.polish.confirm_title").Resolve(),
                new Vector2(0.18f, 0.10f), new Vector2(0.82f, 0.90f),
                onClose: CloseCancelled, sortingOrder: 31040,
                frameName: RpgUiCatalog.FrameCore);
            if (modal == null || modal.canvas == null || modal.chrome == null || modal.chrome.content == null)
            {
                FlowTrace.Fail(Sys, "BuildObsidianModal returned no usable chrome - confirm NOT shown.");
                if (modal != null && modal.canvas != null) UnityEngine.Object.Destroy(modal.canvas);
                return false;
            }
            s_canvas = modal.canvas;
            MedievalUiSkin.ApplyShell(modal.chrome, compact: true);
            if (modal.chrome.close != null) modal.chrome.close.gameObject.SetActive(false);
            var content = modal.chrome.layout != null && modal.chrome.layout.body != null
                ? modal.chrome.layout.body
                : modal.chrome.content.transform;
            var actions = modal.chrome.layout != null && modal.chrome.layout.footer != null
                ? modal.chrome.layout.footer
                : content;

            float cursor = StackTopPx;

            // The stone they are risking, named first and concretely - the downside must not be
            // abstract. This is the "shown BESIDE the outcome table" requirement: it reads
            // immediately above the odds, in the same visual block.
            string heldName = MaterialCatalog.DisplayName(heldGemId);
            var held = ElarionUiKit.Label(content, LocalText.Format("village.crafting.polish.risk_label", heldName), 0f, 0f,
                ElarionUi.Gilt, ElarionUi.FontBody, TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
            StackDown(held, HeadingPx, ref cursor);
            ElarionUiKit.FitSingleLine(held, 24f, 34f);

            var heading = ElarionUiKit.Label(content, new LocalizedText("village.crafting.polish.outcomes_heading").Resolve(), 0f, 0f,
                ElarionUi.ParchmentDim, ElarionUi.FontBody, TextAlignmentOptions.Center, 0.06f, 0.94f);
            StackDown(heading, HeadingPx, ref cursor);
            ElarionUiKit.FitSingleLine(heading, 22f, 30f);

            // The disclosed table. One TMP block so the whole list shares one flowed band, and every
            // number comes straight from DescribeOdds - see this file's header.
            var sb = new System.Text.StringBuilder();
            int rows = 0;
            foreach (var o in odds)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append(o.Label).Append("   ").Append(Mathf.RoundToInt(o.Chance * 100f)).Append('%');
                rows++;
            }
            var table = ElarionUiKit.Label(content, sb.ToString(), 0f, 0f,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.Center,
                0.08f, 0.92f, bold: true);
            StackDown(table, LinePx * Mathf.Max(1, rows), ref cursor);
            ElarionUiKit.FitBlock(table, 22f, 32f);

            // The shatter risk again, in plain words. It is already a row in the table above, but a
            // percentage in a list is easy to skim past and "the stone is destroyed" is the one
            // consequence the player cannot undo.
            int shatterPct = Mathf.RoundToInt(JewelPolishCatalog.RePolishShatterChance * 100f);
            if (shatterPct > 0)
            {
                var warn = ElarionUiKit.Label(content,
                    LocalText.Format("village.crafting.polish.shatter_warning", shatterPct),
                    0f, 0f, ElarionUi.Gilt, ElarionUi.FontBody, TextAlignmentOptions.Center, 0.05f, 0.95f);
                StackDown(warn, HeadingPx, ref cursor);
                ElarionUiKit.FitBlock(warn, 20f, 28f);
            }

            // The VISIBLE COUNTER the owner asked for. It reads "up to N more chances" because the
            // cap is a CEILING, not a promise - the stone can shatter on the very next roll, so
            // wording it as "N chances" would promise attempts the shatter can take away.
            var counter = ElarionUiKit.Label(content, DeNelle.Core.Catalog.DungeonRunPayout.RollsLeftLabel(),
                0f, 0f, ElarionUi.ParchmentDim, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            StackDown(counter, HeadingPx, ref cursor);
            ElarionUiKit.FitSingleLine(counter, 22f, 30f);

            // Cancel is the WIDER, left-hand default. On a screen whose whole purpose is to slow the
            // player down, the safe choice should be the easy one to hit.
            var keep = ElarionUiKit.ButtonPack(actions, new LocalizedText("village.crafting.polish.keep_button").Resolve(), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.04f, 0.08f), new Vector2(0.52f, 0.92f),
                CloseCancelled, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(keep, primary: false);
            var polish = ElarionUiKit.ButtonPack(actions, new LocalizedText("village.crafting.polish.polish_button").Resolve(), ElarionUiKit.ButtonKind.Danger,
                new Vector2(0.56f, 0.08f), new Vector2(0.96f, 0.92f),
                CloseConfirmed, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(polish, primary: false);

            s_onConfirm = null;      // armed only after the arbiter accepts (see DungeonTreasurePanel)
            if (s_handle == null) s_handle = PanelManager.Register(PanelName, CloseCancelled, () => IsOpen);
            if (!PanelManager.NotifyOpened(s_handle))
            {
                FlowTrace.Warn(Sys, "PanelManager rejected the confirm panel - re-polish NOT started.");
                Teardown();
                return false;
            }
            s_onConfirm = onConfirm;

            FlowTrace.Step(Sys, $"re-polish confirm shown for '{heldGemId}': {rows} disclosed outcome(s), " +
                                $"shatter {shatterPct}%, score row {score}.");
            return true;
        }

        /// <summary>The player confirmed: tear down, then run the action.</summary>
        private static void CloseConfirmed()
        {
            var pending = s_onConfirm;
            s_onConfirm = null;                 // consume FIRST - a re-entrant close cannot double-run
            Teardown();
            if (pending == null) return;
            Guard.Try(Sys, "run confirmed re-polish", () => pending());
        }

        /// <summary>
        /// The player cancelled, or the arbiter forced us closed. The action is DISCARDED — an
        /// un-confirmed re-roll must never run, so every non-confirm exit drops it.
        /// </summary>
        private static void CloseCancelled()
        {
            s_onConfirm = null;
            Teardown();
            FlowTrace.Step(Sys, "re-polish cancelled - the stone is untouched.");
        }

        private static void Teardown()
        {
            if (s_canvas != null) UnityEngine.Object.Destroy(s_canvas);
            s_canvas = null;
            if (s_handle != null) PanelManager.NotifyClosed(s_handle);
        }

        /// <summary>
        /// Place <paramref name="label"/> as the next element in a top-down PIXEL flow and advance
        /// <paramref name="cursor"/>. Growth PUSHES what follows instead of overlapping it — the
        /// WO-865 fix, applied here from the start rather than after the bug (see the header).
        /// </summary>
        private static void StackDown(TMP_Text label, float pixels, ref float cursor)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
            rt.offsetMax = new Vector2(0f, -cursor);
            rt.offsetMin = new Vector2(0f, -(cursor + pixels));
            cursor += pixels + StackGapPx;
        }
    }
}

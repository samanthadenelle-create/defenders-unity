using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DeNelle.Core.UI
{
    public readonly struct CostPart
    {
        public readonly string ConceptId;
        public readonly string Word;
        public readonly int Amount;
        public readonly string AmountText;

        public CostPart(string conceptId, string word, int amount)
        {
            ConceptId = (conceptId ?? string.Empty).Trim().ToLowerInvariant();
            Word = string.IsNullOrWhiteSpace(word) ? "Resource" : word.Trim();
            Amount = amount;
            AmountText = ElarionUi.CompactNumber(amount);
        }
    }

    public static class CostFormat
    {
        public static IReadOnlyList<CostPart> Parts(IEnumerable<(string conceptId, string word, int amount)> raw)
        {
            if (raw == null) return Array.Empty<CostPart>();
            var parts = new List<CostPart>();
            foreach (var item in raw)
                if (item.amount > 0)
                    parts.Add(new CostPart(item.conceptId, item.word, item.amount));
            return parts;
        }

        public static string Words(IReadOnlyList<CostPart> parts)
        {
            if (parts == null || parts.Count == 0) return string.Empty;
            var words = new string[parts.Count];
            for (int i = 0; i < parts.Count; i++)
                words[i] = parts[i].Word + " " + parts[i].AmountText;
            return string.Join("  ", words);
        }

        internal static Sprite IconOrWarn(CostPart part)
        {
            var icon = UiStyle.Icon(part.ConceptId);
            if (icon == null)
                FlowTrace.Once("CostFormat", "no-icon-" + part.ConceptId,
                    "no icon for concept=" + part.ConceptId + "; using full-word fallback");
            return icon;
        }
    }

    public static class CostRowElement
    {
        public static VisualElement Build(IReadOnlyList<CostPart> parts, string prefix = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (!string.IsNullOrEmpty(prefix)) AddText(row, prefix);
            if (parts == null) return row;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var icon = CostFormat.IconOrWarn(part);
                if (icon != null)
                {
                    var image = new UnityEngine.UIElements.Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
                    image.style.width = 22; image.style.height = 22; image.style.marginLeft = 4;
                    row.Add(image);
                    AddText(row, part.AmountText);
                }
                else AddText(row, part.Word + " " + part.AmountText);
            }
            return row;
        }

        private static void AddText(VisualElement row, string text)
        {
            var label = new Label(text);
            label.style.marginLeft = 4;
            label.style.color = ElarionUi.Parchment;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);
        }
    }

    public static partial class ElarionUiKit
    {
        public static RectTransform CostRow(Transform parent, IReadOnlyList<CostPart> parts,
            Vector2 anchorMin, Vector2 anchorMax, Color color, string prefix = null, float fontPx = 13f)
        {
            var root = new GameObject("CostRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            // WO-1060: childControlWidth MUST stay true. With it false the group lays children out at
            // their RAW sizeDelta (Image 100, TextMeshProUGUI 200) and IGNORES the LayoutElement
            // preferredWidth authored in AddCostText below -- a 3-part row then measures ~920 ref px
            // inside a 228.8 px band and spills onto the neighbouring build card (33 BUTTON OVER TEXT
            // findings, Builds/ship-ui-capture.log 2026-08-25). Pinned by CostRowFitRegression.
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.spacing = 4;
            // WO-1640 ITEM A: the PREFIX is a WORD and a word never breaks. The amounts are
            // untouched (see SealPrefixCell) -- this change is confined to the prefix cell.
            if (!string.IsNullOrEmpty(prefix))
                SealPrefixCell(AddCostText(root.transform, prefix, color, fontPx), prefix, fontPx);
            if (parts == null) return rt;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var icon = CostFormat.IconOrWarn(part);
                if (icon == null) AddCostText(root.transform, part.Word + " " + part.AmountText, color, fontPx);
                else
                {
                    var iconGo = new GameObject("Icon_" + part.ConceptId, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(LayoutElement));
                    iconGo.transform.SetParent(root.transform, false);
                    var image = iconGo.GetComponent<UnityEngine.UI.Image>();
                    image.sprite = icon; image.preserveAspect = true; image.raycastTarget = false;
                    var size = iconGo.GetComponent<LayoutElement>(); size.preferredWidth = 22; size.preferredHeight = 22;
                    AddCostText(root.transform, part.AmountText, color, fontPx);
                }
            }
            return rt;
        }

        /// <summary>Builds one cell of the row. Returns the label so the caller can seal the
        /// PREFIX cell (WO-1640) without a second construction path.</summary>
        private static TextMeshProUGUI AddCostText(Transform parent, string value, Color color, float fontPx)
        {
            var go = new GameObject("CostText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            // WO-1640: the kit's own font law (ElarionUiKit.Label, "assign a font BEFORE .text
            // so first generation can't NRE") was never applied on this construction path.
            // EnsureFont no-ops when TMP has already resolved one, and it authors no width --
            // this is the ONE line here that touches the amount cells too, and it changes no
            // authored geometry. It also makes the prefix's GetPreferredValues honest.
            EnsureFont(text);
            text.text = value; text.fontSize = fontPx; text.fontStyle = FontStyles.Bold;
            text.color = color; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            var layout = go.GetComponent<LayoutElement>();
            float metricScale = fontPx / 13f;
            layout.preferredWidth = Math.Max(28f, value.Length * 8f) * metricScale;
            layout.preferredHeight = Math.Max(24f, fontPx + 4f);
            return text;
        }

        /// <summary>Breathing room in reference px between the prefix's measured glyph run and
        /// its cell edge, so a sub-pixel metric difference can never re-open the wrap.</summary>
        private const float PrefixPadPx = 6f;

        // =====================================================================
        //  WO-1640 ITEM A -- THE COST-ROW PREFIX IS A WORD, AND A WORD NEVER BREAKS.
        // ---------------------------------------------------------------------
        //  Builds/device-frames/2026-09-10_0605_raid_staging.png (2670x1200, build
        //  363529, Seeker) -- the raid staging screen's spoils row read:
        //
        //        SPOIL
        //            S      1800   1100   2200
        //
        //  RaidDeployScreen.BuildSpoilsChips passes prefix "SPOILS" at fontPx 24. The
        //  8-px-per-character heuristic in AddCostText above is calibrated for the 13 px
        //  DEFAULT size, so it authored max(28, 6*8) * (24/13) = 88.6 ref px for six BOLD
        //  CAPS -- and AddCostText had never authored a wrapping mode, so TMP's default
        //  word-wrap was live and broke the single word onto two lines.
        //
        //  TWO THINGS, BOTH CONFINED TO THE PREFIX CELL:
        //   1. NoWrap. Exactly what the kit already authors for the wallet amount
        //      (ElarionUiKitObsidian.cs:973). NoWrap cannot break a word, full stop.
        //   2. The cell is widened to TMP's OWN measurement of this string at this size
        //      and weight, taken as a MAX against the heuristic -- so no existing caller's
        //      prefix can ever come back NARROWER than the width it has today. This can
        //      remove a wrap; it can never create one.
        //
        //  ⛔ NOT A FITTER, DELIBERATELY. FitSingleLine at fontPx 24 clamps min=max=24
        //  (24 is already below ElarionUiKitObsidian.FontFloor=30) and switches overflow to
        //  Ellipsis -- the row would read "SPOIL..." instead of "SPOIL / S", which is not
        //  an improvement. And WO-697's kit law (ElarionUiKitObsidian.cs:964-967) forbids
        //  ellipsis/auto-shrink on a currency VALUE; NoWrap does neither, so the law holds
        //  even though the prefix is not a number.
        //
        //  ⛔ NO minWidth. LayoutElement.minWidth here would let the row's minimum sum
        //  exceed the band it was given, and the HorizontalLayoutGroup would then push
        //  children OUTSIDE it -- the exact WO-1060 escape CostRowFitRegression reds for.
        //
        //  Pinned by CostRowFitRegression's SPOILS prefix case + its RED companion. The tag
        //  carries the SIZE the screen passes and is built from it, so it is [fit-SPOILS-30]
        //  since WO-1669 raised that row to ElarionUiKit.FontFloor (owner ruling 2026-09-10
        //  12:16). ⛔ Do not re-write a number into this line: the fixture reads
        //  RaidDeployScreen.SpoilsChipFontPx, and a tag typed here would go stale the next
        //  time it moves — which is exactly how the 24 above became history.
        // =====================================================================
        private static void SealPrefixCell(TextMeshProUGUI text, string value, float fontPx)
        {
            if (text == null) return;

            var layout = text.GetComponent<LayoutElement>();
            float heuristicPx = layout != null ? layout.preferredWidth : 0f;

            float measuredPx = 0f;
            try { measuredPx = text.GetPreferredValues(value).x; }
            catch { measuredPx = 0f; }   // no font resolved yet -> keep the heuristic

            float wantedPx = measuredPx > 0.5f ? measuredPx + PrefixPadPx : heuristicPx;
            bool widened = layout != null && wantedPx > heuristicPx;
            if (widened) layout.preferredWidth = wantedPx;

            text.textWrappingMode = TextWrappingModes.NoWrap;

            // §5.2 of the WO -- the authoring-time half of the fit numbers. The RENDER-time
            // half (resolved rect width, textInfo.lineCount) is read post-layout by
            // CostRowFitRegression, which is the only place a settled canvas exists.
            // Parts into locals first: the gate's brace scanner has no interpolated-string
            // model (CLAUDE.md §1).
            string heuristicTxt = heuristicPx.ToString("0.#");
            string measuredTxt = measuredPx.ToString("0.#");
            string wantedTxt = wantedPx.ToString("0.#");
            string fontTxt = fontPx.ToString("0.#");
            FlowTrace.Once("CostFormat", "prefix-fit-" + value,
                "CostRow prefix '" + value + "' len=" + value.Length + " fontPx=" + fontTxt +
                " heuristicPx=" + heuristicTxt + " measuredPx=" + measuredTxt +
                " authoredPx=" + wantedTxt + " widened=" + widened +
                " wrap=NoWrap (WO-1640: the prefix is a word, never a broken one)");
        }
    }
}

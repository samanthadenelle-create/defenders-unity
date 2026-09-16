// =============================================================================
// ElarionUiKit.DetailCard — the shared COMPACT PARCHMENT DETAIL CARD (WO-693).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI   (partial of ElarionUiKit)
//
// One builder used by every FrameCrafting master-detail panel (Jeweler, Alchemy;
// adopt-on-touch for the rest): a CONTENT-SIZED card seated at the TOP of the
// parchment detail zone — icon plate + name (+ rarity chip) + one flavor line
// -> divider -> BESTOWS rows -> divider -> REQUIRES rows -> divider -> COST
// chips -> the ONE action CTA. The zone's leftover space stays empty parchment
// (calm — the content defines the card, the card never stretches to the frame).
//
// Mobile readability (owner report 2026-07-12, BINDING):
//   * every string uses the ElarionUi reference-px ladder (FontBody/FontLabel/
//     FontMicro on the 1080x1920 canvas) and is Fit-protected with the
//     ElarionUi.FontFloorMobile floor — text ellipsizes/truncates, NEVER
//     shrinks below the floor. No per-screen font literals.
//   * met/unmet requirement state is carried by an ASCII GLYPH + have/need
//     COUNTS ("OK  Ruby  1 / 1" vs "X  Ruby  0 / 2"); color is reinforcement
//     only (the owner is red/green colorblind — colorblind law, BINDING).
//     Glyphs are plain ASCII ("OK"/"X"/"+"/"*") — the build TMP font tofu's
//     non-ASCII (star/diamond/check precedents: EndStateView, RaidDeployScreen,
//     Dungeons/CraftingPanelController TickChar = "OK").
//   * cost renders as the WO-675/676 currency chip grammar: the mirrored
//     Resources/RpgUi/currency/currency_* icon + amount (text fallback when
//     the art is absent — the card never blanks).
//
// ADDITIVE-ONLY kit change (WO-693 coordination rule): new partial file, new
// members only — no existing kit code touched.
// =============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    public static partial class ElarionUiKit
    {
        /// <summary>Opaque kit card face for controls whose full caption wraps across lines.</summary>
        public static void ApplyMultilineButtonPlate(UnityEngine.UI.Button button)
        {
            if (button == null) return;
            var border = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (border == null) return;
            if (border.gameObject.name == "MultilinePlateFill") return;
            // Ornate button textures paint only the middle of their rect. A wrapping
            // caption needs the same full-height card surface used by content panels.
            border.overrideSprite = null;
            border.sprite = null;
            ApplyRounded(border, 6f);
            border.fillCenter = true;
            border.color = ElarionUi.Gold;
            var fillGo = new GameObject("MultilinePlateFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(border.transform, false);
            fillGo.transform.SetAsFirstSibling();
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            var fill = fillGo.GetComponent<Image>();
            ApplyRounded(fill, 4f);
            fill.color = new Color(.078f, .073f, .066f, 1f);
            fill.raycastTarget = false;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = default;
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.25f, 1.15f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.7f, .7f, .7f, 1f);
            colors.disabledColor = new Color(.6f, .6f, .6f, 1f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }
        // ── Parchment ink palette (dark text ON the parchment well) ──────────
        // Mirrors the values the crafting-family panels carried privately; the
        // card owns them now so every detail surface agrees.

        /// <summary>Primary dark ink on parchment.</summary>
        public static readonly Color ParchmentInk     = new Color(0.16f, 0.12f, 0.08f, 1f);
        /// <summary>Secondary / flavour ink on parchment.</summary>
        public static readonly Color ParchmentInkDim  = new Color(0.34f, 0.28f, 0.20f, 1f);
        /// <summary>Met / positive-grant ink (reinforcement only — never the sole carrier).</summary>
        public static readonly Color ParchmentInkGood = new Color(0.10f, 0.42f, 0.16f, 1f);
        /// <summary>Unmet / blocked ink (reinforcement only — never the sole carrier).</summary>
        public static readonly Color ParchmentInkBad  = new Color(0.55f, 0.12f, 0.10f, 1f);

        /// <summary>Tone of one structured detail row (color = reinforcement; the row's
        /// glyph + counts carry the state for colorblind readers).</summary>
        public enum DetailRowTone { Neutral, Good, Bad, Dim }

        /// <summary>One structured card row: "[glyph] Name ...... value" (all ASCII).</summary>
        public readonly struct DetailCardRow
        {
            public readonly string Glyph;   // ASCII only: "OK", "X", "+", "*"
            public readonly string Name;
            public readonly string Value;   // "1 / 1", "+50", "" for none
            public readonly DetailRowTone Tone;

            public DetailCardRow(string glyph, string name, string value, DetailRowTone tone)
            {
                Glyph = glyph ?? "";
                Name = name ?? "";
                Value = value ?? "";
                Tone = tone;
            }
        }

        /// <summary>One cost chip: the mirrored currency icon + amount (WO-675/676 grammar).
        /// FallbackName renders as text when the currency art is absent.</summary>
        public readonly struct DetailCardChip
        {
            public readonly string ConceptId;
            public readonly string FallbackName;    // e.g. "Iron" — text cue when art absent
            public readonly int Amount;

            public DetailCardChip(string conceptId, string fallbackName, int amount)
            {
                ConceptId = conceptId ?? "";
                FallbackName = fallbackName ?? "";
                Amount = amount;
            }
        }

        /// <summary>Everything the compact detail card renders. Sections with no rows/chips
        /// are omitted entirely (no empty headers).</summary>
        public sealed class DetailCardSpec
        {
            public string IconPath;                  // Resources sprite path ("" = no plate)
            /// <summary>Player-facing display name. WO-714 P3/P10: NEVER a raw itemId — pass
            /// catalog displayName, or run the id through ElarionUiKit.SpacedDisplayName.</summary>
            public string Title = "";
            /// <summary>Owned/stack count ("x3") shown right of the header (WO-683 grammar);
            /// "" = none. WO-714 P3, additive.</summary>
            public string CountText = "";
            public string RarityText = "";           // "EPIC" etc — chip by the name; "" = none
            public string Flavor = "";               // Wrapping content-sized band in the detail scroll.
            public string BestowsHeader = "BESTOWS";
            public List<DetailCardRow> Bestows;
            public string RequiresHeader = "REQUIRES";
            public List<DetailCardRow> Requires;
            public List<DetailCardChip> CostChips;
            public string CtaLabel = "";
            public bool CtaEnabled;
            public Action OnCta;
        }

        /// <summary>Ink-legible rarity tint for parchment (reinforcement only — the rarity
        /// WORD is the carrier). Unknown rarities read as dim ink.</summary>
        public static Color ParchmentRarityColor(string rarity)
        {
            switch ((rarity ?? "").ToLowerInvariant())
            {
                case "uncommon":  return ParchmentInkGood;
                case "rare":      return new Color(0.12f, 0.24f, 0.52f, 1f); // deep blue ink
                case "epic":      return new Color(0.38f, 0.14f, 0.50f, 1f); // deep violet ink
                case "legendary": return new Color(0.55f, 0.30f, 0.05f, 1f); // burnt gold ink
                default:          return ParchmentInkDim;                    // common / unknown
            }
        }

        // Reference-px metrics (1080x1920 canvas, CanvasScaler match 0.5 — same space as
        // the ElarionUi ladder). Rows seat a FontLabel(40) line fitted down to the
        // FontFloorMobile(30) floor; the §1.14 fit-guard protects the rest.
        private const float CardSidePad   = 0.04f;  // fraction of the zone width each side
        private const float CardTopPad    = 16f;
        private const float CardHeaderH   = 112f;
        private const float CardFlavorH   = 44f;
        private const float CardDividerH  = 18f;
        private const float CardSectionH  = 40f;
        private const float CardRowH      = 52f;
        private const float CardChipRowH  = 64f;
        private const float CardCtaH      = 128f;
        private const float CardBottomPad = 12f;

        /// <summary>
        /// Build the compact content-sized detail card at the TOP of a parchment detail
        /// zone. Returns the card root (destroyed with the zone's children on repaint).
        /// </summary>
        public static GameObject BuildParchmentDetailCard(Transform detailZone, DetailCardSpec spec)
        {
            if (detailZone == null || spec == null) return null;

            var card = new GameObject("DetailCard", typeof(RectTransform));
            card.transform.SetParent(detailZone, false);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(CardSidePad, 1f);
            cardRt.anchorMax = new Vector2(1f - CardSidePad, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);
            cardRt.anchoredPosition = new Vector2(0f, -CardTopPad);

            float y = 0f;   // running offset from the card top (reference px)

            RectTransform Slot(string name, float h)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(card.transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0f, -(y + h));
                rt.offsetMax = new Vector2(0f, -y);
                y += h;
                return rt;
            }

            void Divider()
            {
                var slot = Slot("Divider", CardDividerH);
                var rule = new GameObject("Rule", typeof(Image));
                rule.transform.SetParent(slot, false);
                var rt = rule.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.offsetMin = new Vector2(4f, -1f);
                rt.offsetMax = new Vector2(-4f, 1f);
                var img = rule.GetComponent<Image>();
                img.color = new Color(ParchmentInk.r, ParchmentInk.g, ParchmentInk.b, 0.30f);
                img.raycastTarget = false;
            }

            Color ToneColor(DetailRowTone tone)
            {
                switch (tone)
                {
                    case DetailRowTone.Good: return ParchmentInkGood;
                    case DetailRowTone.Bad:  return ParchmentInkBad;
                    case DetailRowTone.Dim:  return ParchmentInkDim;
                    default:                 return ParchmentInk;
                }
            }

            void Section(string header, IReadOnlyList<DetailCardRow> rows)
            {
                if (rows == null || rows.Count == 0) return;   // no empty headers
                Divider();
                if (!string.IsNullOrEmpty(header))
                {
                    var hs = Slot("SectionHeader", CardSectionH);
                    var h = CardTmp(hs, header, ElarionUi.FontMicro, ParchmentInkDim,
                        FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 6f, -6f);
                    FitSingleLine(h, ElarionUi.FontFloorMobile);
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var rs = Slot("Row", CardRowH);
                    Color tone = ToneColor(row.Tone);

                    // Glyph column (fixed 64px) — the colorblind-safe state carrier.
                    if (!string.IsNullOrEmpty(row.Glyph))
                    {
                        var g = CardTmp(rs, row.Glyph, ElarionUi.FontLabel, tone,
                            FontStyles.Bold, TextAlignmentOptions.Center, 4f, 0f);
                        var grt = g.rectTransform;
                        grt.anchorMin = new Vector2(0f, 0f);
                        grt.anchorMax = new Vector2(0f, 1f);
                        grt.offsetMin = new Vector2(4f, 0f);
                        grt.offsetMax = new Vector2(68f, 0f);
                        FitSingleLine(g, ElarionUi.FontFloorMobile);
                    }

                    bool hasValue = !string.IsNullOrEmpty(row.Value);

                    var n = CardTmp(rs, row.Name, ElarionUi.FontLabel, tone,
                        FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
                        76f, hasValue ? -190f : -6f);
                    FitSingleLine(n, ElarionUi.FontFloorMobile);

                    if (hasValue)
                    {
                        var v = CardTmp(rs, row.Value, ElarionUi.FontLabel, tone,
                            FontStyles.Bold, TextAlignmentOptions.MidlineRight, 0f, -6f);
                        var vrt = v.rectTransform;
                        vrt.anchorMin = new Vector2(1f, 0f);
                        vrt.anchorMax = new Vector2(1f, 1f);
                        vrt.offsetMin = new Vector2(-186f, 0f);
                        vrt.offsetMax = new Vector2(-6f, 0f);
                        FitSingleLine(v, ElarionUi.FontFloorMobile);
                    }
                }
            }

            // ── Header: icon plate + name + rarity chip ──────────────────────
            {
                var hs = Slot("Header", CardHeaderH);
                float textX0 = 8f;

                Sprite icon = string.IsNullOrEmpty(spec.IconPath)
                    ? null : Resources.Load<Sprite>(spec.IconPath);
                if (icon != null)
                {
                    var plate = new GameObject("IconPlate", typeof(Image));
                    plate.transform.SetParent(hs, false);
                    var prt = plate.GetComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0f, 0f);
                    prt.anchorMax = new Vector2(0f, 1f);
                    prt.offsetMin = new Vector2(0f, 8f);
                    prt.offsetMax = new Vector2(96f, -8f);
                    var pimg = plate.GetComponent<Image>();
                    pimg.color = new Color(ParchmentInk.r, ParchmentInk.g, ParchmentInk.b, 0.10f);
                    pimg.raycastTarget = false;

                    var ic = new GameObject("Icon", typeof(Image));
                    ic.transform.SetParent(plate.transform, false);
                    var irt = ic.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.offsetMin = new Vector2(6f, 6f);
                    irt.offsetMax = new Vector2(-6f, -6f);
                    var iimg = ic.GetComponent<Image>();
                    iimg.sprite = icon;
                    iimg.preserveAspect = true;
                    iimg.raycastTarget = false;

                    textX0 = 112f;
                }

                bool hasRarity = !string.IsNullOrEmpty(spec.RarityText);
                bool hasCount  = !string.IsNullOrEmpty(spec.CountText);
                var name = CardTmp(hs, spec.Title, ElarionUi.FontBody, ParchmentInk,
                    FontStyles.Bold, TextAlignmentOptions.MidlineLeft, textX0, -6f);
                var nrt = name.rectTransform;
                nrt.anchorMin = new Vector2(0f, hasRarity ? 0.42f : 0f);
                nrt.anchorMax = new Vector2(1f, 1f);
                nrt.offsetMin = new Vector2(textX0, 0f);
                nrt.offsetMax = new Vector2(hasCount ? -150f : -6f, -4f);
                FitSingleLine(name, ElarionUi.FontFloorMobile);

                // WO-714 P3 (WO-683 grammar): owned/stack count chip, right-aligned in the
                // header band — text carries the count (never color-only, never a raw id).
                if (hasCount)
                {
                    var cnt = CardTmp(hs, spec.CountText, ElarionUi.FontLabel, ParchmentInkDim,
                        FontStyles.Bold, TextAlignmentOptions.MidlineRight, 0f, -6f);
                    var crt = cnt.rectTransform;
                    crt.anchorMin = new Vector2(1f, hasRarity ? 0.42f : 0f);
                    crt.anchorMax = new Vector2(1f, 1f);
                    crt.offsetMin = new Vector2(-144f, 0f);
                    crt.offsetMax = new Vector2(-6f, -4f);
                    FitSingleLine(cnt, ElarionUi.FontFloorMobile);
                }

                if (hasRarity)
                {
                    var rare = CardTmp(hs, spec.RarityText, ElarionUi.FontMicro,
                        ParchmentRarityColor(spec.RarityText), FontStyles.Bold,
                        TextAlignmentOptions.MidlineLeft, textX0, -6f);
                    var rrt = rare.rectTransform;
                    rrt.anchorMin = new Vector2(0f, 0f);
                    rrt.anchorMax = new Vector2(1f, 0.42f);
                    rrt.offsetMin = new Vector2(textX0, 4f);
                    rrt.offsetMax = new Vector2(-6f, 0f);
                    FitSingleLine(rare, ElarionUi.FontFloorMobile);
                }
            }

            // ── One flavor line (ellipsized, never shrunk below the floor) ───
            if (!string.IsNullOrEmpty(spec.Flavor))
            {
                var fs = Slot("Flavor", CardFlavorH);
                var f = CardTmp(fs, spec.Flavor, ElarionUi.FontMicro, ParchmentInkDim,
                    FontStyles.Italic, TextAlignmentOptions.MidlineLeft, 8f, -8f);
                FitBlock(f, ElarionUi.FontFloorMobile);
                Canvas.ForceUpdateCanvases();
                float flavorWidth = Mathf.Max(240f, cardRt.rect.width - 16f);
                float flavorHeight = Mathf.Max(CardFlavorH,
                    f.GetPreferredValues(spec.Flavor, flavorWidth, float.PositiveInfinity).y + 8f);
                fs.offsetMin -= new Vector2(0f, flavorHeight - CardFlavorH);
                y += flavorHeight - CardFlavorH;
            }

            // ── BESTOWS -> REQUIRES (each section brings its own divider) ────
            Section(spec.BestowsHeader, spec.Bestows);
            Section(spec.RequiresHeader, spec.Requires);

            // ── COST chips (WO-675/676 currency grammar) ─────────────────────
            if (spec.CostChips != null && spec.CostChips.Count > 0)
            {
                Divider();
                var cs = Slot("CostChips", CardChipRowH);
                float x = 8f;
                for (int i = 0; i < spec.CostChips.Count; i++)
                {
                    var chip = spec.CostChips[i];
                    var sprite = string.IsNullOrEmpty(chip.ConceptId)
                        ? null : UiStyle.Icon(chip.ConceptId);

                    if (sprite != null)
                    {
                        var ig = new GameObject("ChipIcon", typeof(Image));
                        ig.transform.SetParent(cs, false);
                        var irt = ig.GetComponent<RectTransform>();
                        irt.anchorMin = new Vector2(0f, 0f);
                        irt.anchorMax = new Vector2(0f, 1f);
                        irt.offsetMin = new Vector2(x, 8f);
                        irt.offsetMax = new Vector2(x + 48f, -8f);
                        var img = ig.GetComponent<Image>();
                        img.sprite = sprite;
                        img.preserveAspect = true;
                        img.raycastTarget = false;
                        x += 54f;
                    }

                    // Amount (text names the currency when the art is absent — never
                    // an icon-only OR blank cue). WO-697 kit law: the value renders via
                    // ElarionUi.CompactNumber (never per-surface formatting) and is NOT
                    // Fit/ellipsis-protected — a currency value never shrinks/truncates;
                    // the slot widens to the text instead (content-fit).
                    string amount = sprite != null
                        ? ElarionUi.CompactNumber(chip.Amount)
                        : chip.FallbackName + " " + ElarionUi.CompactNumber(chip.Amount);
                    var a = CardTmp(cs, amount, ElarionUi.FontLabel, ParchmentInk,
                        FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 0f, 0f);
                    float w = Mathf.Max(sprite != null ? 96f : 190f,
                        a.GetPreferredValues(amount).x + 12f);
                    var art = a.rectTransform;
                    art.anchorMin = new Vector2(0f, 0f);
                    art.anchorMax = new Vector2(0f, 1f);
                    art.offsetMin = new Vector2(x, 0f);
                    art.offsetMax = new Vector2(x + w, 0f);
                    x += w + 14f;
                }
            }

            // ── The ONE action CTA (disabled state carries the blocker text) ─
            if (!string.IsNullOrEmpty(spec.CtaLabel))
            {
                var slot = Slot("CtaSlot", CardCtaH);
                var onCta = spec.OnCta;
                var btn = BuildObsidianButton(slot, spec.CtaLabel,
                    ObsidianButtonStyle.Style1,
                    spec.CtaEnabled ? ObsidianButtonColor.Green : ObsidianButtonColor.Gray,
                    new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f),
                    () => { onCta?.Invoke(); });
                if (btn != null)
                {
                    btn.interactable = spec.CtaEnabled;
                    var caption = btn.GetComponentInChildren<TMP_Text>();
                    if (caption != null) FitBlock(caption, ElarionUi.FontFloorMobile);
                    ApplyMultilineButtonPlate(btn);
                }
            }

            y += CardBottomPad;
            cardRt.sizeDelta = new Vector2(0f, y);
            return card;
        }

        /// <summary>
        /// The shared detail-zone EMPTY STATE (nothing selected): prompt + one-line
        /// explanation, in parchment ink — the skill tree's empty-fold pattern.
        /// </summary>
        public static void BuildParchmentDetailEmpty(Transform detailZone, string title, string body)
        {
            if (detailZone == null) return;
            var t = Label(detailZone, title ?? "", 0.54f, 0.64f, ParchmentInkDim,
                ElarionUi.FontBody, TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
            t.raycastTarget = false;
            FitSingleLine(t, ElarionUi.FontFloorMobile);
            if (!string.IsNullOrEmpty(body))
            {
                var b = Label(detailZone, body, 0.40f, 0.53f, ParchmentInkDim,
                    ElarionUi.FontMicro, TextAlignmentOptions.Top, 0.08f, 0.92f);
                b.raycastTarget = false;
                b.fontStyle = FontStyles.Italic;
                FitBlock(b, ElarionUi.FontFloorMobile);
            }
        }

        /// <summary>
        /// Right-align a readable STATE on a master-list row button ("+ Ready" / "2 of 3"):
        /// re-seats the button's own label to the LEFT band and adds the state label on the
        /// right — glyph/count text carries the state, color is reinforcement only.
        /// </summary>
        public static void AddRowStateSuffix(Button row, string stateText, Color color)
        {
            if (row == null || string.IsNullOrEmpty(stateText)) return;

            var lbl = row.GetComponentInChildren<TMP_Text>();
            if (lbl != null)
            {
                var lrt = lbl.rectTransform;
                lrt.anchorMin = new Vector2(0.05f, 0f);
                lrt.anchorMax = new Vector2(0.60f, 1f);
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                FitSingleLine(lbl, ElarionUi.FontFloorMobile);
            }

            var go = new GameObject("RowState", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.60f, 0f);
            rt.anchorMax = new Vector2(0.95f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = stateText;
            t.fontSize = ElarionUi.FontLabel;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.MidlineRight;
            t.raycastTarget = false;
            EnsureFont(t);
            FitSingleLine(t, ElarionUi.FontFloorMobile);
        }

        // Pixel-slot TMP helper for the card (full-rect child with left/right px insets).
        private static TextMeshProUGUI CardTmp(RectTransform slot, string text, int size,
            Color color, FontStyles style, TextAlignmentOptions align, float leftPx, float rightPx)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(slot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(leftPx, 0f);
            rt.offsetMax = new Vector2(rightPx, 0f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text ?? "";
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            EnsureFont(t);
            return t;
        }
    }
}

// =============================================================================
// ShopTheme — shared Elarion shop visual identity (WO-175).
// -----------------------------------------------------------------------------
// One palette + a set of UI Toolkit styling helpers so every shop surface
// (the Cosmetic Shop in DeNelle.HUD and the PackStore in DeNelle.Wallet) reads
// as ONE authored merchant boutique in Elarion — wood-and-aether frame, warm
// gold accents, violet/aether highlights — instead of a generic dark dialog.
//
// Lives in DeNelle.Core (both DeNelle.HUD and DeNelle.Wallet already reference
// Core, neither references the other) so the identity is shared without a new
// cross-module dependency. Pure styling — no gameplay, no data, no UXML. All
// helpers apply INLINE styles so they survive the project's "UXML/USS does not
// render in player builds" trap (PIPELINE_STATE §8): a re-skin that works in a
// build, not just the editor.
//
// Mobile-first: tap targets are >= 40 px high, text stays legible at thumb size.
// =============================================================================

using UnityEngine;
using UnityEngine.UIElements;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Shared Elarion shop palette + UI Toolkit styling helpers. Static, stateless,
    /// all inline-style based. Used by CosmeticShopPanel and PackStore so the two
    /// shop surfaces share one merchant-boutique identity.
    /// </summary>
    public static class ShopTheme
    {
        // ── Palette ──────────────────────────────────────────────────────────
        // Warm-wood + violet/aether merchant boutique. Names describe role.

        // WOOD CANON (single-source unification): the merchant skin's hues now derive from the ONE
        // ElarionUi source (warm stone/wood + runic gold + parchment). The soft-violet "aether" FRAME/
        // PANEL tints are retired so shops read the same dark-wood + gold as the town HUD; the violet
        // is kept ONLY as the canon arcane ACCENT (ElarionUi.Aether) for selected tabs.
        /// <summary>Full-screen dim behind the shop window.</summary>
        public static readonly Color Scrim = new Color(ElarionUi.Scrim.r, ElarionUi.Scrim.g, ElarionUi.Scrim.b, 0.86f);

        /// <summary>Carved-wood frame outer border (warm-wood trim).</summary>
        public static readonly Color FrameWood = ElarionUi.StoneTrim;

        /// <summary>Inner gold rim that lines the frame (runic gold).</summary>
        public static readonly Color FrameRim = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.85f);

        /// <summary>Gilt edge highlight on the frame + title rule.</summary>
        public static readonly Color Gilt = ElarionUi.Gilt;

        /// <summary>Shop window background (dark stained wood).</summary>
        public static readonly Color PanelBg = new Color(ElarionUi.PanelStoneDark.r, ElarionUi.PanelStoneDark.g, ElarionUi.PanelStoneDark.b, 0.99f);

        /// <summary>Item / card slot background (a touch lighter than the panel).</summary>
        public static readonly Color SlotBg = new Color(ElarionUi.PanelStone.r, ElarionUi.PanelStone.g, ElarionUi.PanelStone.b, 1f);

        /// <summary>Recessed list well behind the scrolling cards.</summary>
        public static readonly Color WellBg = new Color(ElarionUi.PanelStoneDark.r, ElarionUi.PanelStoneDark.g, ElarionUi.PanelStoneDark.b, 0.92f);

        /// <summary>Primary display text (warm parchment cream).</summary>
        public static readonly Color Parchment = ElarionUi.Parchment;

        /// <summary>Secondary / flavour text (muted parchment).</summary>
        public static readonly Color ParchmentDim = ElarionUi.ParchmentDim;

        /// <summary>Aether-violet — selected tabs / arcane accent (canon magic accent).</summary>
        public static readonly Color Aether = ElarionUi.Aether;

        /// <summary>Aether-violet, dim — unselected tab / chip rest state.</summary>
        public static readonly Color AetherDim = ElarionUi.AetherDim;

        /// <summary>Selected tab / chip accent — canon runic gold (black+gold canon, owner 2026-06-28).</summary>
        public static readonly Color TabSelected = ElarionUi.Gold;

        /// <summary>Unselected tab / chip rest — muted obsidian that recedes into the black panel.</summary>
        public static readonly Color TabRest = new Color(0.06f, 0.06f, 0.07f, 1f);

        /// <summary>Buy / confirm green.</summary>
        public static readonly Color Confirm = ElarionUi.Affordable;

        /// <summary>Owned / equipped success green.</summary>
        public static readonly Color Owned = ElarionUi.Affordable;

        /// <summary>Disabled / locked stone grey.</summary>
        public static readonly Color Disabled = ElarionUi.Disabled;

        // Small decorative glyphs (no font dependency — plain unicode the default
        // UI font renders). A crest before the title, a coin before currency amounts.
        public const string CrestGlyph = "*";
        public const string CoinGlyph  = "*";

        // ── Frame / panel ────────────────────────────────────────────────────

        /// <summary>
        /// Turns a full-screen element into the dim scrim that sits behind a shop
        /// window and visibly covers the world beneath it.
        /// </summary>
        public static void StyleScrim(VisualElement scrim)
        {
            if (scrim == null) return;
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.right = 0;
            scrim.style.top = 0;  scrim.style.bottom = 0;
            scrim.style.backgroundColor = Scrim;
            scrim.style.alignItems = Align.Center;
            scrim.style.justifyContent = Justify.Center;
        }

        /// <summary>
        /// Applies the carved-wood-and-aether shop-window frame to a panel: stained
        /// wood fill, a gilt outer edge, an inner aether rim, generous rounding and
        /// padding. Reads as a merchant's stall window, not a dialog box.
        /// </summary>
        public static void StylePanelFrame(VisualElement panel)
        {
            if (panel == null) return;
            panel.style.backgroundColor = PanelBg;

            SetBorderRadius(panel, 18);
            SetBorderWidth(panel, 3);
            SetBorderColor(panel, FrameWood);

            panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.paddingLeft = 22; panel.style.paddingRight = 22;
        }

        /// <summary>
        /// A thin gilt accent line — used under a header to evoke an inlaid rule.
        /// </summary>
        public static VisualElement MakeRule()
        {
            var rule = new VisualElement();
            rule.style.height = 2;
            rule.style.marginTop = 6;
            rule.style.marginBottom = 10;
            rule.style.backgroundColor = new Color(Gilt.r, Gilt.g, Gilt.b, 0.55f);
            SetBorderRadius(rule, 1);
            return rule;
        }

        /// <summary>
        /// Builds the shop title with a small crest glyph in the game's display
        /// styling (bold, parchment, gilt crest). Returns the row so the caller
        /// can drop it straight into a header.
        /// </summary>
        public static VisualElement MakeTitle(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var crest = new Label(CrestGlyph);
            crest.style.fontSize = 22;
            crest.style.color = Gilt;
            crest.style.marginRight = 8;
            row.Add(crest);

            var title = new Label(text);
            title.style.fontSize = 24;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Parchment;
            title.style.letterSpacing = 1f;
            row.Add(title);

            return row;
        }

        // ── Currency display ─────────────────────────────────────────────────

        // ── Item icon slot ───────────────────────────────────────────────────

        /// <summary>
        /// A framed item-card icon slot. Prefers a real preview sprite/render; when
        /// none exists yet it falls back to a framed colour tile (still beats a bare
        /// swatch — it reads as an item portrait in a slot, not a debug colour box).
        /// </summary>
        public static VisualElement MakeIconSlot(Texture2D preview, Color tint, float size = 60f)
        {
            var slot = new VisualElement();
            slot.style.width = size; slot.style.height = size;
            slot.style.marginRight = 12;
            SetBorderRadius(slot, 10);
            SetBorderWidth(slot, 2);
            SetBorderColor(slot, FrameWood);
            slot.style.overflow = Overflow.Hidden;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;

            if (preview != null)
            {
                slot.style.backgroundImage = new StyleBackground(preview);
                slot.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
                slot.style.backgroundColor = SlotBg;
            }
            else
            {
                // Framed colour tile with an inner gem highlight so it reads as a
                // portrait slot, not a flat swatch.
                slot.style.backgroundColor = SlotBg;
                var gem = new VisualElement();
                gem.style.width = size * 0.5f; gem.style.height = size * 0.5f;
                gem.style.backgroundColor = tint;
                SetBorderRadius(gem, 7);
                SetBorderWidth(gem, 1);
                SetBorderColor(gem, new Color(1f, 1f, 1f, 0.30f));
                slot.Add(gem);
            }
            return slot;
        }

        // ── Buttons ──────────────────────────────────────────────────────────

        /// <summary>
        /// Styles a themed button with hover/press feedback. <paramref name="kind"/>
        /// picks the palette: Confirm (Buy/green), Aether (neutral/violet),
        /// Disabled (locked/grey). Rounds, pads, and bolds for thumb-size taps.
        /// </summary>
        public static void StyleButton(Button button, ButtonKind kind)
        {
            if (button == null) return;
            Color rest = ButtonRest(kind);
            Color text = kind == ButtonKind.Disabled ? ParchmentDim : Parchment;

            button.style.backgroundColor = rest;
            button.style.color = text;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 14;
            button.style.height = 42; // mobile tap target
            button.style.paddingLeft = 16; button.style.paddingRight = 16;
            button.style.marginTop = 2; button.style.marginBottom = 2;
            SetBorderRadius(button, 10);
            SetBorderWidth(button, 1);
            SetBorderColor(button, new Color(0f, 0f, 0f, 0.30f));

            // Hover / press feedback via inline restyle (USS :hover won't load in a
            // build, so we wire pointer callbacks instead).
            if (kind == ButtonKind.Disabled) return;
            Color hover = Lighten(rest, 0.10f);
            Color press = Darken(rest, 0.12f);
            button.RegisterCallback<PointerEnterEvent>(_ => { if (button.enabledSelf) button.style.backgroundColor = hover; });
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (button.enabledSelf) button.style.backgroundColor = rest; });
            button.RegisterCallback<PointerDownEvent>(_ => { if (button.enabledSelf) button.style.backgroundColor = press; });
            button.RegisterCallback<PointerUpEvent>(_ => { if (button.enabledSelf) button.style.backgroundColor = hover; });
        }

        /// <summary>
        /// The shared Close affordance (WO-562: black + gold canon, ONE consistent labelled "Close",
        /// never a per-panel violet "X"). A gold-trim chip with dark ink text, sized for a thumb tap.
        /// </summary>
        public static void StyleCloseButton(Button button)
        {
            if (button == null) return;
            button.text = new LocalizedText(CommonText.KeyClose).Resolve();
            // Raw device px (no PanelSettings ref scaler): 148 ≈ 60 dp on the Seeker
            // (VISUAL_TOUCH_CONTRAST_AUDIT 2026-07-14, P0 — was 34px ≈ 14 dp).
            button.style.minWidth = 148; button.style.height = 148;
            button.style.fontSize = 34;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.backgroundColor = ElarionUi.GoldButton;
            button.style.color = ElarionUi.Ink;
            button.style.paddingLeft = 14; button.style.paddingRight = 14;
            button.style.paddingTop = 0; button.style.paddingBottom = 0;
            SetBorderRadius(button, 9);
            SetBorderWidth(button, 1);
            SetBorderColor(button, new Color(Gilt.r, Gilt.g, Gilt.b, 0.9f));
            Color rest  = ElarionUi.GoldButton;
            Color hover = Lighten(rest, 0.10f);
            Color press = Darken(rest, 0.12f);
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = hover);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = rest);
            button.RegisterCallback<PointerDownEvent>(_ => button.style.backgroundColor = press);
            button.RegisterCallback<PointerUpEvent>(_ => button.style.backgroundColor = hover);
        }

        /// <summary>
        /// Styles a category tab with a clear selected / unselected state. Selected
        /// glows runic gold with a gilt left marker; unselected sits recessed in obsidian.
        /// </summary>
        public static void StyleTab(Button tab, bool selected)
        {
            if (tab == null) return;
            tab.style.height = 40;
            tab.style.marginBottom = 6;
            tab.style.fontSize = 14;
            tab.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            tab.style.color = selected ? ElarionUi.Ink : ParchmentDim;
            tab.style.backgroundColor = selected ? TabSelected : TabRest;
            SetBorderRadius(tab, 9);
            SetBorderWidth(tab, 1);
            // A gilt left edge marks the active tab.
            tab.style.borderLeftWidth = selected ? 4 : 1;
            tab.style.borderLeftColor = selected ? Gilt : new Color(0f, 0f, 0f, 0.25f);
            tab.style.borderTopColor = new Color(0f, 0f, 0f, 0.25f);
            tab.style.borderRightColor = new Color(0f, 0f, 0f, 0.25f);
            tab.style.borderBottomColor = new Color(0f, 0f, 0f, 0.25f);
        }

        /// <summary>
        /// Styles a small currency-rail / price chip (e.g. SOL / USDC / SKR) with a
        /// selected highlight.
        /// </summary>
        public static void StyleChip(Button chip, bool selected)
        {
            if (chip == null) return;
            chip.style.height = 32;
            chip.style.fontSize = 13;
            chip.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            chip.style.paddingLeft = 12; chip.style.paddingRight = 12;
            chip.style.marginRight = 6; chip.style.marginTop = 2; chip.style.marginBottom = 2;
            chip.style.color = selected ? ElarionUi.Ink : ParchmentDim;
            chip.style.backgroundColor = selected ? TabSelected : TabRest;
            SetBorderRadius(chip, 8);
            SetBorderWidth(chip, 1);
            SetBorderColor(chip, selected ? new Color(Gilt.r, Gilt.g, Gilt.b, 0.6f) : new Color(0f, 0f, 0f, 0.25f));
        }

        // ── Scroll well ──────────────────────────────────────────────────────

        /// <summary>
        /// Recesses a ScrollView into a themed list well and hides the default OS
        /// scrollbars (the greyest "unfinished" tell). Content still scrolls by
        /// drag / wheel; the native bars just don't show.
        /// </summary>
        public static void StyleScrollWell(ScrollView scroll)
        {
            if (scroll == null) return;
            scroll.style.backgroundColor = WellBg;
            SetBorderRadius(scroll, 12);
            scroll.style.paddingTop = 8; scroll.style.paddingBottom = 8;
            scroll.style.paddingLeft = 10; scroll.style.paddingRight = 10;

            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // Collapse the scroller gutters so hidden bars don't reserve space.
            if (scroll.verticalScroller != null) scroll.verticalScroller.style.width = 0;
            if (scroll.horizontalScroller != null) scroll.horizontalScroller.style.height = 0;
        }

        // ── Card slot ────────────────────────────────────────────────────────

        /// <summary>Applies the standard item / pack card-slot styling.</summary>
        public static void StyleCard(VisualElement card)
        {
            if (card == null) return;
            card.style.backgroundColor = SlotBg;
            card.style.marginBottom = 8;
            card.style.paddingTop = 10; card.style.paddingBottom = 10;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            SetBorderRadius(card, 12);
            SetBorderWidth(card, 1);
            SetBorderColor(card, new Color(Gilt.r, Gilt.g, Gilt.b, 0.22f));
        }

        // ── Button kind ──────────────────────────────────────────────────────

        public enum ButtonKind { Confirm, Aether, Disabled }

        private static Color ButtonRest(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Confirm:  return Confirm;
                case ButtonKind.Disabled: return Disabled;
                default:                  return Aether;
            }
        }

        // ── Colour helpers ───────────────────────────────────────────────────

        public static Color Lighten(Color c, float amount) =>
            new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);

        public static Color Darken(Color c, float amount) =>
            new Color(
                Mathf.Clamp01(c.r - amount),
                Mathf.Clamp01(c.g - amount),
                Mathf.Clamp01(c.b - amount),
                c.a);

        // ── Border shorthand (no shorthand props on IStyle) ──────────────────

        public static void SetBorderRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void SetBorderWidth(VisualElement e, float w)
        {
            e.style.borderTopWidth = w;
            e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w;
            e.style.borderRightWidth = w;
        }

        public static void SetBorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
            e.style.borderRightColor = c;
        }
    }
}

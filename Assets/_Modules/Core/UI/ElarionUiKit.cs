// =============================================================================
// ElarionUiKit — the SHARED code-built uGUI coherence kit for Echoes of Elarion.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// ONE visual language, built from reusable pieces. Every HUD / modal / screen in
// the game can be assembled by calling these static builders instead of each
// surface re-implementing its own AddImage/AddLabel/AddButton/RoundedSprite recipe
// (which is how ArenaPanel, HeroInventoryController, HeroEquipHud and the HUD each
// grew their own near-identical copy). This kit CONSOLIDATES the BEST of those —
// the rich depth/frame/niche + rarity-escalating frames from the inventory, the
// proven Canvas/Scaler/Raycaster modal boilerplate + sleek dark-glass panels +
// WebGL-safe rounded-sprite fallback from ArenaPanel / HudTheme — into one place.
//
// WHY HERE: DeNelle.Core.UI is the ONE assembly that DeNelle.HUD (uGUI) AND
// DeNelle.Village (panels) both reference, so a shared kit lives here WITHOUT a
// forbidden HUD<->Village edge (CLAUDE.md §5). Colours / fonts / radii all SOURCE
// from the canonical ElarionUi palette so every surface reads as ONE designed game.
//
// CODE-BUILT uGUI ONLY (Canvas/Image/Button/ScrollRect/TextMeshProUGUI) — the
// proven-reliable path; UXML/UI-Toolkit HUDs come up empty in player builds
// (PIPELINE_STATE §8). The procedural rounded sprite is built lazily once and is
// failure-safe: if the Texture2D build throws under WebGL it falls back to null and
// Images render as flat tinted quads — a surface can NEVER blank (WO-334 guard).
//
// ADDITIVE: this file only ADDS the kit. No existing UI is modified by it; the
// older surfaces keep their private helpers and compile unchanged. A later pilot
// converts the main HUD by calling these methods (`// TODO adopt kit`).
//
// ASCII-only structural strings; callers pass their own display text.
// =============================================================================

using System;
using System.Collections.Generic;   // WO-1060: the clamp-growth ring
using System.Text;                  // WO-1060: control-path formatting
using UnityEngine;
using UnityEngine.EventSystems;   // WO-899 analog stick: pointer down/drag/up on the move widget
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// Shared, static, ElarionUi-sourced uGUI builders so every surface is built
    /// from the same coherent pieces (modal canvas, scrim, depth panel, button,
    /// header, rarity slot/card, label). Each method returns the created object so
    /// callers can parent children / restyle / wire events. Stateless + WebGL-safe.
    /// </summary>
    public static partial class ElarionUiKit
    {
        // NOTE (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 §1): the Obsidian widget family
        // (BuildObsidianBar/Button/ActionSlot/CastBar/TargetFrame/Nameplate/...) lives in
        // the sibling partial file ElarionUiKitObsidian.cs (same single Kit-team owner).
        // ── Sleek surface tints (the dark-glass language, consolidated) ───────
        // These were duplicated verbatim across ArenaPanel / HeroInventory* / HUD.
        // Centralised here so the whole game's glass depth reads identically.
        // WOOD CANON (single-source unification): the kit's surfaces now derive their HUE from the
        // ONE ElarionUi source (warm stone/wood) at the kit's own translucency alphas — so the HUD
        // keeps its see-through depth but reads dark-WOOD, not cool-glass. Tune the wood in ONE
        // place (ElarionUi.PanelStone / PanelStoneDark) and it propagates through every kit screen.
        // PHASE (b) ROUTING: these tokens are no longer hardcoded here — they RESOLVE FROM UiStyle.Theme
        // (the single style authority). The values are identical (UiTheme seeds them with these exact
        // literals), so this is a pure internal rewire with ZERO visual change; the public read API
        // (`ElarionUiKit.Glass` etc.) is unchanged for every call site. Swap the active UiTheme and the
        // whole kit's surface language reskins at once.
        /// <summary>Primary panel fill — dark translucent WOOD (play area shows through).</summary>
        public static Color Glass      => UiStyle.Theme.Glass;
        /// <summary>Deeper wood for heavier panels / modal backboards.</summary>
        public static Color GlassDeep  => UiStyle.Theme.GlassDeep;
        /// <summary>Recessed near-black well / track behind a value or bar.</summary>
        public static Color Track      => UiStyle.Theme.Track;
        /// <summary>Cell rest fill — warm wood a touch lighter than the tray.</summary>
        public static Color Cell       => UiStyle.Theme.Cell;
        /// <summary>Selected cell fill — brighter warm wood than the rest cell.</summary>
        public static Color CellSelected => UiStyle.Theme.CellSelected;
        /// <summary>Warm stone backboard behind a hero / display niche.</summary>
        public static Color StoneNiche => UiStyle.Theme.StoneNiche;

        /// <summary>Thin gold accent line (a hint of runic gold, not a heavy frame).</summary>
        public static Color Accent     => UiStyle.Theme.AccentLine;
        /// <summary>Even fainter gold for inner rims / soft underlines.</summary>
        public static Color AccentSoft => UiStyle.Theme.AccentSoft;

        // ── Canonical button kinds (consolidates StyleButtonColors variants) ──
        /// <summary>Button intent. Gold = primary CTA (dark-ink text); Confirm = green;
        /// Danger = red; Quiet = neutral glass.</summary>
        public enum ButtonKind { Gold, Confirm, Danger, Quiet }

        // =====================================================================
        // MODAL CANVAS — the boilerplate every full-screen surface repeats.
        // =====================================================================

        /// <summary>
        /// Create a standalone ScreenSpaceOverlay Canvas (with CanvasScaler 1080x1920
        /// ScaleWithScreenSize, match 0.5) + GraphicRaycaster. Returns the root
        /// GameObject; parent your scrim / panel under it. Mobile-first reference res.
        /// </summary>
        public static GameObject BuildModalCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "ModalCanvas" : name);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        /// <summary>
        /// Full-screen dark backdrop behind a modal (alpha ~0.85), raycast-blocking so
        /// clicks don't fall through to the scene. If <paramref name="onTapClose"/> is
        /// supplied the scrim becomes a transition-less Button that fires it (tap-outside
        /// to dismiss). Returns the scrim GameObject.
        /// </summary>
        public static GameObject Scrim(Transform parent, Action onTapClose = null)
        {
            var go = AddImage(parent, "Scrim", Vector2.zero, Vector2.one,
                              new Color(0.02f, 0.015f, 0.04f, 0.85f), rounded: false);
            if (onTapClose != null)
            {
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => onTapClose());
            }
            return go;
        }

        // =====================================================================
        // PANEL — the framed depth panel (glass fill + soft gold rim).
        // =====================================================================

        /// <summary>
        /// The canonical framed panel: dark glass rounded rect with a soft gold
        /// underline rim and (optionally) an inner hairline rim for crisp depth.
        /// Anchored by fraction-of-parent (anchorMin/anchorMax) so it reflows.
        /// </summary>
        public static GameObject Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                       bool deep = false, bool innerRim = true)
        {
            var p = AddImage(parent, "Panel", anchorMin, anchorMax, deep ? GlassDeep : Glass);
            AddRimUnderline(p);
            if (innerRim) AddInnerRim(p, AccentSoft);
            return p;
        }

        /// <summary>A recessed near-black well (scroll tray / value plate), lightly framed.</summary>
        public static GameObject Well(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var w = AddImage(parent, "Well", anchorMin, anchorMax, Track);
            AddInnerRim(w, new Color(0f, 0f, 0f, 0.4f));
            return w;
        }

        /// <summary>A warm stone display niche (hero portrait / showcase alcove) with a gold inner rim.</summary>
        public static GameObject Niche(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            // BlinkChrome: keep the GameObject (callers parent content onto it) but make the stone
            // backing transparent + skip the rim, so the Blink panel shows through the alcove.
            // WO-714 P9: art-presence gated (BlinkChromeActive), never the raw flag — with the
            // flag ON but the art absent the procedural chrome must still render (sprite-first).
            bool chrome = !BlinkChromeActive;
            var n = AddImage(parent, "Niche", anchorMin, anchorMax, chrome ? StoneNiche : new Color(0f, 0f, 0f, 0f));
            if (chrome) AddInnerRim(n, AccentSoft);
            return n;
        }

        // =====================================================================
        // OBSIDIAN PANEL CHROME — THE one canonical panel chrome (WO-554).
        // ---------------------------------------------------------------------
        // A near-BLACK panel fill + a GOLD TRIM border + a gold header title + a
        // SINGLE standard Close button. Built ONCE here and reused by EVERY panel
        // so the chrome is never re-authored per surface (owner directive
        // 2026-06-28: "black panel + gold trim — kill the brown; NO per-panel 'X'
        // buttons, one consistent Close"). This SUPERSEDES the per-panel recipe of
        // backdrop + PanelFramed(brown wood sprite) + dark solidFill + Header + own
        // X/Close that every panel had copy-pasted. Sprite-free (pure tinted quads)
        // so it is identical on every target incl. WebGL and can never blank.
        // =====================================================================

        /// <summary>The canonical near-black panel fill (owner-specified value).</summary>
        public static readonly Color ObsidianFill = new Color(0.02f, 0.02f, 0.025f, 0.98f);
        /// <summary>The canonical gold trim border colour (runic gold, opaque).</summary>
        public static Color ObsidianTrim => new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 1f);
        /// <summary>Gold-trim border thickness, in reference px.</summary>
        public const float ObsidianTrimPx = 3f;

        /// <summary>
        /// The pieces of a built Obsidian panel chrome. Parent your UI under
        /// <see cref="content"/> — it spans the inner black area at 0..1 anchors, exactly like
        /// the old framed-panel transform, so existing fraction-anchored content drops in
        /// unchanged. <see cref="title"/> + <see cref="close"/> are pre-built and wired.
        /// </summary>
        public sealed class PanelChrome
        {
            /// <summary>Full-screen dark backdrop behind the panel (raycast-off); null when not requested.</summary>
            public GameObject backdrop;
            /// <summary>The gold-trim panel root (the border layer).</summary>
            public GameObject root;
            /// <summary>The near-black inner fill — PARENT YOUR UI HERE (0..1 anchors work as before).</summary>
            public GameObject content;
            /// <summary>The gold header title label (retext / recolour as needed).</summary>
            public TextMeshProUGUI title;
            /// <summary>The ONE standard Close button (top-right), wired to onClose.</summary>
            public Button close;
            /// <summary>The pre-styled drop-zones (medallion / body / footer). Frame path: measured
            /// from that frame's art. PROCEDURAL path (WO-714 P6): the DEFAULT zone set with the
            /// same close-band reservation, so content dropped into <c>layout.body/footer</c> ends
            /// above the shared Close even when the frame art is absent (sweep-9413 class killed at
            /// the factory). Never null after BuildObsidianPanel; medallion is null procedurally.</summary>
            public FrameLayout layout;
        }

        // =====================================================================
        // FRAME LAYOUT — a Blink frame's pre-styled content DROP-ZONES.
        // ---------------------------------------------------------------------
        // The owner's architecture: the Blink panel IS the style (chrome). Each frame
        // has fixed content regions carved into its art (a portrait medallion, a main
        // well, a footer strip). A screen drops its CHROME-LESS objects into these
        // zones — it never re-styles a card/well/footer itself. Define a frame's zones
        // ONCE here (measured from the art, in fractions so they survive any stretch);
        // every screen that uses that frame reuses them. DRY + consistent across all UI.
        // =====================================================================

        /// <summary>The drop-zones of a framed panel. Each is a transparent RectTransform parented
        /// under the panel content at the frame's measured region — parent your objects to these.
        /// medallion/footer may be null for frames without that region.</summary>
        public sealed class FrameLayout
        {
            /// <summary>Title band (top), already carrying the gold title label.</summary>
            public RectTransform header;
            /// <summary>WO-839: thin SUB-HEADER band under the title (meta rows: badge / stars /
            /// timers), or null for frames without one. Clears the medallion socket by design.</summary>
            public RectTransform subHeader;
            /// <summary>The main content well (grid / list / detail) — the big dark interior.</summary>
            public RectTransform body;
            /// <summary>Circular portrait socket (top-left medallion), or null for frames without one.</summary>
            public RectTransform medallion;
            /// <summary>Footer strip (wallet / actions) along the base, or null.</summary>
            public RectTransform footer;
            /// <summary>LEFT half of a PRE-SPLIT body well (dark obsidian — icon/card LISTS live here),
            /// or null for single-well frames. Owner ruling 2026-07-03 (FrameCrafting is the
            /// master-detail template): dark wells carry lists, parchment wells carry prose/detail.</summary>
            public RectTransform bodyLeft;
            /// <summary>RIGHT half of a PRE-SPLIT body well (parchment — DETAIL/prose lives here),
            /// or null for single-well frames.</summary>
            public RectTransform bodyRight;
            /// <summary>§1.14 fit-or-scroll handle installed on the BODY zone at the factory
            /// (owner flag_06 "scrollable area on all menus"). <see cref="body"/> already points at
            /// its scrolling content column; this exposes the ScrollRect/viewport/scrollbar for
            /// screens that need them. Null only when the zone pre-carried its own ScrollRect.</summary>
            public ScrollZoneHandle bodyScroll;
        }

        /// <summary>Per-frame zone rects (fractions: xMin,yMin,xMax,yMax of the panel).
        /// hasMedallion/hasFooter/hasSplitBody flag optional regions. MEASURED FROM THE REAL ART
        /// PIXELS (owner ruling 2026-07-03) — the committed Resources/RpgUi/frame PNGs were column/
        /// row-sampled (dark-well vs parchment vs border classification) to place every zone;
        /// see the P1 report for the sampling method. `close` is UNIFIED (owner ruling 2026-07-03):
        /// every frame's close resolves to the single bottom-center thumb-zone (DefaultCloseZone),
        /// NOT the measured top-right art notch — reach/continuity was chosen over the notches, so
        /// the gold corner notch simply goes unused (see the force at the end of ZonesFor).</summary>
        private struct FrameZones
        {
            public Vector4 header, body, medallion, footer, close;
            public Vector4 bodyLeft, bodyRight;
            public bool hasMedallion, hasFooter, hasSplitBody;
            /// <summary>WO-839: optional thin SUB-HEADER band under the title (right of the
            /// medallion socket) for meta rows (difficulty badge / stars / target time), so a
            /// screen never stacks a second header row into the body top. Off by default.</summary>
            public Vector4 subHeader;
            public bool hasSubHeader;
            /// <summary>TWO-TONE FRAME (FrameCrafting / FrameQuest): the frame art bakes a dark
            /// obsidian well on the LEFT and a tan parchment well on the RIGHT, split at an art seam.
            /// A screen that drops content into the full <see cref="body"/> straddles that seam, so
            /// half its content sits on black and half on tan ("reads half-drawn"). When true,
            /// BuildObsidianPanel paints UNIFORM backing plates over the content wells (dark under the
            /// full body + list side, parchment under the detail side), so content never depends on
            /// where the baked seam falls — killing the parchment bleed at the shared kit level.</summary>
            public bool twoToneBody;
        }

        /// <summary>The default Close anchor when a frame has no designed notch. MOBILE-FIRST (owner
        /// rule 2026-07-03): the close is a labeled sleek button, so the default is a COMPACT,
        /// horizontally-CENTERED thumb-zone button seated in the bottom close/footer band — not the
        /// legacy top-right corner sliver (that rect was sized for an X glyph, now retired). Frames
        /// that carve a designed close region still override this via <see cref="ZonesFor"/>.close.</summary>
        // NOTE: with the SeatSharedCloseInside bottom-pivot seat, `y` is the band's lower
        // edge and the fixed-size box grows UPWARD from it; y is nudged to 0.050 (from
        // 0.035) so the box seats a hair above the interior floor, clear of the frame's
        // ornate bottom border. x/z centre it; w is unused once the fixed size is stamped.
        private static readonly Vector4 DefaultCloseZone = new Vector4(0.360f, 0.050f, 0.640f, 0.125f);

        // =====================================================================
        // CANONICAL CTA SIZE (owner F8 x3, 2026-07-04): "Continue button should be
        // the same size in ALL screens" / "Continue bar or Close button same size
        // everywhere." Buttons are anchored by FRACTION-OF-PARENT, so their pixel
        // size drifted with each parent's rect (a Close on a small popup was tiny,
        // on a big modal it was huge). The primary Continue and the ONE shared Close
        // now pin to this fixed pixel size (at the 1080x1920 modal reference res via
        // PinCanonicalCtaSize), so they render identically on every screen.
        // =====================================================================
        /// <summary>Canonical Continue/Close CTA width in reference pixels (1080x1920 modal canvas).</summary>
        // Generous one-handed target. RESTORED 300 -> 360 on 2026-08-22, see the height note below.
        public const float CanonCtaWidth = 360f;
        /// <summary>Canonical Continue/Close CTA height in reference pixels (1080x1920 modal canvas).
        /// Raised 120 -> 132 (~60 dp one-handed thumb, VISUAL_TOUCH_CONTRAST_AUDIT 2026-07-14 P0).</summary>
        // =====================================================================
        // ⛔ DO NOT LOWER THIS. Restored 120 -> 132 on 2026-08-22 after a UI pass
        // shrank it (with CanonCtaWidth 360 -> 300) as an undeclared side effect of an
        // unrelated modal-geometry refactor. The doc line above was left in place while the
        // value moved, so the comment documented a raise that had been undone.
        //
        // SOURCE OF RECORD (owner, 2026-08-22): this value was derived from APPLE's published
        // touch-target guidance (2026), whose minimum is 44 x 44 pt - NOT from Android's 48 dp
        // Material figure, which is a different and slightly larger standard. Both are cleared
        // comfortably at 132; cite Apple when this number is questioned, because that is where
        // it actually came from.
        //
        // WHY 132 AND NOT 120 - the reasoning is about SMALL PHONES specifically:
        // these are REFERENCE px on a 1080x1920 ScaleWithScreenSize canvas
        // (MatchWidthOrHeight 0.5), so they track RESOLUTION, not PHYSICAL size. Two phones at
        // the same resolution but different physical sizes get the same pixel count - so the
        // button is physically SMALLER on the small phone, and that is the case this protects.
        //   112 (MinTouchPx) ~= 50 dp / ~44 pt - the hard floor, right AT Apple's minimum
        //   120              ~= 54 dp / ~47 pt - legal, but the one-handed margin is gone
        //   132              ~= 60 dp / ~53 pt - the audited one-handed thumb target  <-- THIS
        // 120 is not UNSAFE; it is merely legal. The 2026-07-14 P0 raised it for one-handed
        // reach, and one-handed reach on the smallest supported phone is exactly where those
        // 6 dp get spent. Roughly 25 files derive layout from these two constants, so moving
        // them silently resizes screens nobody looked at.
        // =====================================================================
        // Slightly above the audited touch floor so the framed-button label keeps its clear inset.
        public const float CanonCtaHeight = 132f;

        /// <summary>Kit touch floor: the SHORTEST resolved side of any kit-built button in
        /// reference px (~50 dp on the Seeker). The analogue of the FontFloor for buttons —
        /// buttons already larger are never shrunk (pure floor). Enforced post-layout by
        /// <see cref="ClampMinTouch"/> (VISUAL_TOUCH_CONTRAST_AUDIT 2026-07-14, P0).</summary>
        public const float MinTouchPx = 112f;

        /// <summary>The drop-zone rects for a named frame (defaults are a sane full-well layout).</summary>
        private static FrameZones ZonesFor(string frameName)
        {
            // Default: header band across the top, a large inset body well, a thin footer, no medallion.
            var z = new FrameZones
            {
                header   = new Vector4(0.10f, 0.905f, 0.82f, 0.975f),
                body     = new Vector4(0.06f, 0.10f, 0.94f, 0.875f),
                footer   = new Vector4(0.08f, 0.030f, 0.92f, 0.095f),
                close    = DefaultCloseZone,
                hasFooter = true,
                hasMedallion = false,
                hasSplitBody = false,
            };
            switch (frameName)
            {
                case RpgUiCatalog.FrameInventory:
                    // Tall frame: circular medallion top-left, header right of it, big well below.
                    z.medallion   = new Vector4(0.045f, 0.835f, 0.225f, 0.985f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.27f, 0.905f, 0.82f, 0.975f);
                    z.body        = new Vector4(0.055f, 0.105f, 0.945f, 0.80f);
                    z.footer      = new Vector4(0.07f, 0.030f, 0.93f, 0.095f);
                    break;
                case RpgUiCatalog.FrameCharacter:
                    // Stats_Panel (1232x1828, pixel-measured): top-left square medallion socket
                    // (left of the vertical divider carved into the top band), header band right of
                    // it, the designed CLOSE notch top-right, the stat-row well below the portrait
                    // arch, bottom filigree footer. Owner-assigned anatomy (2026-07-03 ruling).
                    z.medallion   = new Vector4(0.045f, 0.900f, 0.165f, 0.985f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.190f, 0.905f, 0.865f, 0.975f);
                    z.body        = new Vector4(0.060f, 0.110f, 0.940f, 0.605f); // below the portrait arch
                    z.footer      = new Vector4(0.090f, 0.050f, 0.910f, 0.105f);
                    break;
                case RpgUiCatalog.FrameCrafting:
                    // THE master-detail template (owner ruling 2026-07-03, "spot on, 100% perfect").
                    // 2182x1567, pixel-measured: the body is PRE-SPLIT — dark obsidian well left
                    // (scrollable LIST zone) / parchment well right (DETAIL zone); the dark/parchment
                    // seam sits at x=0.486 of the frame. Medallion = the top-left circle socket;
                    // footer = the action strip (Craft button) between the wells and the base border.
                    z.medallion   = new Vector4(0.014f, 0.885f, 0.097f, 0.990f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.115f, 0.900f, 0.860f, 0.975f);
                    // PARCHMENT-BLEED FIX: the full `body` is CONFINED to the dark-left region (== bodyLeft)
                    // so a single-well screen never straddles the baked dark/parchment seam — its content
                    // (over the dark backing plate) reads uniformly dark. Split screens use bodyLeft/bodyRight.
                    z.body        = new Vector4(0.030f, 0.150f, 0.482f, 0.875f);
                    z.bodyLeft    = new Vector4(0.030f, 0.150f, 0.482f, 0.875f); // dark well: lists
                    z.bodyRight   = new Vector4(0.490f, 0.150f, 0.955f, 0.875f); // parchment: detail
                    z.hasSplitBody = true;
                    z.twoToneBody  = true;   // dark/parchment baked seam — paint uniform well plates
                    z.footer      = new Vector4(0.060f, 0.085f, 0.940f, 0.145f); // action strip
                    break;
                case RpgUiCatalog.FrameMerchant:
                    // Landscape use of the portrait Merchant_Panel (1005x1507, pixel-measured
                    // 2026-07-03): big top-left circle socket, header band right of it, the
                    // designed close notch top-right. Fleet eyes-on pass caught the socket
                    // rendering EMPTY — this case previously declared no medallion at all.
                    z.medallion   = new Vector4(0.040f, 0.845f, 0.260f, 0.990f);
                    z.hasMedallion = true;
                    z.header = new Vector4(0.28f, 0.88f, 0.85f, 0.965f);
                    z.body   = new Vector4(0.05f, 0.115f, 0.95f, 0.845f);
                    break;
                case RpgUiCatalog.FrameTalent:
                    // Talent_Tree_Panel (2779x1843, pixel-measured 2026-07-03): landscape frame,
                    // circle socket top-left, deco header band, close notch top-right. Previously
                    // fell to the DEFAULT zones (no medallion) — the socket art rendered empty.
                    z.medallion   = new Vector4(0.014f, 0.870f, 0.100f, 0.992f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.12f, 0.900f, 0.86f, 0.975f);
                    z.body        = new Vector4(0.035f, 0.115f, 0.965f, 0.855f);
                    break;
                case RpgUiCatalog.FrameQuest:
                    // Quest_Log_Panel (2190x1570, pixel-measured 2026-07-03): split master-detail
                    // like FrameCrafting — dark list well LEFT, parchment detail RIGHT (parchment
                    // top sits lower, under its own deco band). Circle socket top-left. Previously
                    // DEFAULT zones (no medallion, no split).
                    z.medallion   = new Vector4(0.028f, 0.850f, 0.125f, 0.992f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.14f, 0.900f, 0.86f, 0.975f);
                    // PARCHMENT-BLEED FIX: full `body` confined to the dark-left region (see FrameCrafting).
                    z.body        = new Vector4(0.035f, 0.115f, 0.495f, 0.858f);
                    z.bodyLeft    = new Vector4(0.035f, 0.115f, 0.495f, 0.858f);
                    z.bodyRight   = new Vector4(0.505f, 0.115f, 0.966f, 0.760f);
                    z.hasSplitBody = true;
                    z.twoToneBody  = true;   // dark/parchment baked seam — paint uniform well plates
                    break;
                case RpgUiCatalog.FrameCore:
                    // Core_Panel (1210x1815, pixel-measured 2026-07-03): portrait frame, circle
                    // socket top-left, header band, close notch top-right. Previously DEFAULT
                    // zones (no medallion).
                    z.medallion   = new Vector4(0.037f, 0.868f, 0.220f, 0.988f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.24f, 0.900f, 0.88f, 0.972f);
                    // WO-839 ROOT CAUSE fix: FrameCore previously INHERITED the default thin
                    // footer (0.030-0.095). The sweep-9413 relocation re-seats that band above
                    // the Close box but keeps its designed ~0.065 height -- too thin for the
                    // MinTouchPx=112 button floor, so ClampMinTouch grew footer CTAs past the
                    // band into the shared Close underneath (the Raid Deploy bottom-row overlap).
                    // Explicit RAISED, button-height band instead: 0.13 panel height holds a
                    // MinTouch CTA on the post-scale landscape canvas; the relocation still
                    // fires as a safety net when the Close box tops out higher than 0.140.
                    z.footer      = new Vector4(0.055f, 0.155f, 0.945f, 0.285f);
                    // WO-839 #1: thin SUB-HEADER band under the title, RIGHT of the medallion
                    // socket (x >= 0.24 clears the socket's 0.220 edge) -- badge / stars /
                    // target-time meta rows seat here instead of stacking into the body top.
                    z.subHeader    = new Vector4(0.24f, 0.845f, 0.945f, 0.896f);
                    z.hasSubHeader = true;
                    z.body        = new Vector4(0.055f, 0.075f, 0.945f, 0.835f);
                    break;
                case RpgUiCatalog.FrameSettings:
                    // 1936x1461, pixel-measured: full-bleed dark slab with a top-centre tab (the
                    // header); no footer strip designed in. Close rides just inside the top-right.
                    z.header    = new Vector4(0.290f, 0.905f, 0.710f, 0.995f);
                    z.body      = new Vector4(0.060f, 0.120f, 0.940f, 0.865f);
                    z.hasFooter = false;
                    break;
                case RpgUiCatalog.FrameOptions:
                    // 824x1363, pixel-measured: narrow portrait frame, top tab header, bottom band.
                    z.header = new Vector4(0.230f, 0.900f, 0.770f, 0.975f);
                    z.body   = new Vector4(0.080f, 0.150f, 0.920f, 0.875f);
                    z.footer = new Vector4(0.240f, 0.065f, 0.760f, 0.125f);
                    break;
                case RpgUiCatalog.FrameLoot:
                    // 720x1138, pixel-measured: top-left title plate (used as the medallion socket),
                    // header band right of it, deep well, thin band above the bottom filigree.
                    z.medallion   = new Vector4(0.100f, 0.850f, 0.290f, 0.990f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.330f, 0.870f, 0.900f, 0.960f);
                    z.body        = new Vector4(0.075f, 0.145f, 0.925f, 0.800f);
                    z.footer      = new Vector4(0.100f, 0.070f, 0.900f, 0.125f);
                    break;
                case RpgUiCatalog.FramePet:
                    // 1230x1484, pixel-measured: same family as Stats_Panel — top-left medallion
                    // socket, header band, centred portrait arch, well below it, bottom filigree.
                    z.medallion   = new Vector4(0.045f, 0.895f, 0.175f, 0.985f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.200f, 0.900f, 0.860f, 0.975f);
                    z.body        = new Vector4(0.060f, 0.115f, 0.940f, 0.510f); // below the pet arch
                    z.footer      = new Vector4(0.090f, 0.045f, 0.910f, 0.100f);
                    break;
                case RpgUiCatalog.FrameDialogue:
                case RpgUiCatalog.FrameDialogue2:
                    // Landscape strip: portrait socket FAR-LEFT, speaker-name header + body text right.
                    // frame_dialogue_2 (NPC card variant) shares the measured Dialogue anatomy until
                    // its art lands (P0 gap-fill) and is re-sampled.
                    z.medallion   = new Vector4(0.012f, 0.16f, 0.178f, 0.88f);
                    z.hasMedallion = true;
                    z.header      = new Vector4(0.205f, 0.64f, 0.96f, 0.93f);
                    z.body        = new Vector4(0.205f, 0.12f, 0.97f, 0.62f);
                    z.hasFooter   = false;
                    break;
            }
            // OWNER RULING 2026-07-03 (unify closes): EVERY panel's close resolves to the ONE
            // bottom-center thumb-zone (DefaultCloseZone) — reach/continuity was chosen over the
            // measured top-right art notches. Forced AFTER the switch so no per-frame case can
            // point the close back to a corner; the gold top-right notch simply goes unused.
            z.close = DefaultCloseZone;
            return z;
        }

        private static RectTransform Zone(Transform parent, string name, Vector4 frac)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(frac.x, frac.y);
            rt.anchorMax = new Vector2(frac.z, frac.w);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Uniform dark obsidian well fill for a two-tone frame's content wells (parchment-bleed fix).</summary>
        private static readonly Color TwoToneWellFill = new Color(0.055f, 0.050f, 0.060f, 0.98f);
        /// <summary>Uniform warm parchment fill for a two-tone frame's DETAIL well (parchment-bleed fix).</summary>
        private static readonly Color TwoToneParchmentFill = new Color(0.827f, 0.760f, 0.576f, 1f);

        /// <summary>Paint a UNIFORM backing plate as the FIRST child of a drop-zone so any content
        /// dropped into it reads on one flat tone — used by the two-tone (FrameCrafting / FrameQuest)
        /// parchment-bleed fix. Content added to the zone AFTER this call renders on top (SetAsFirstSibling).
        /// Raycast-off so it never eats taps. No-op on a null zone.</summary>
        private static void ZoneBacking(RectTransform zone, Color fill)
        {
            if (zone == null) return;
            var go = new GameObject("ZoneBacking", typeof(Image));
            go.transform.SetParent(zone, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = fill;
            ApplyRounded(img);
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// SHARED store/shop modal rect — EVERY purchase panel (Realm Store, Vendor Wares,
        /// Party Shop/Forge, Cosmetic Shop) opens at THIS one size so they all match (owner
        /// felt-test 2026-07-15: "all stores the same size, scaled to matching Y height").
        /// Portrait ~0.35w x 0.93h (aspect ~0.667) to match the shared FrameMerchant art:
        /// BuildObsidianPanel draws the frame Image.Type.Simple (stretched to the rect), so a
        /// wide rect stretches the portrait art into a landscape slab. Reference these two
        /// constants instead of per-panel literals so the store sizes can never drift apart.
        /// </summary>
        public static readonly Vector2 StorePanelAnchorMin = new Vector2(0.325f, 0.035f);
        public static readonly Vector2 StorePanelAnchorMax = new Vector2(0.675f, 0.965f);

        /// <summary>
        /// THE canonical panel chrome: a near-black panel with a GOLD TRIM border, a gold header
        /// title, and ONE standard Close button — created here ONCE and reused by every panel (DRY).
        /// Parent it under your modal canvas (build that with <see cref="BuildModalCanvas"/> +
        /// <see cref="Scrim"/>, or use <see cref="BuildObsidianModal"/> for the whole thing).
        /// Anchor by fraction-of-parent. Returns a <see cref="PanelChrome"/> whose <c>content</c> is
        /// the inner fill to populate. No per-panel X buttons — the Close is consistent game-wide.
        /// </summary>
        /// <param name="withClose">
        /// ⭐ WO-1491 - THE PER-PANEL CLOSE OPTION. Default TRUE, so every one of the ~19 existing
        /// callers is byte-for-byte unchanged.
        /// <para>The mockup sheet (docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png) draws CLOSE on
        /// panel 1 ONLY; panels 2 and 4-8 carry the back arrow instead, and the owner's device
        /// walk found CLOSE on five panels that should not have it. A screen that offers TWO ways
        /// out teaches neither.</para>
        /// <para>⛔ THIS IS NOT THE SAME LEVER AS A NULL <paramref name="onClose"/>. A null handler
        /// means "this surface has no exit at all" (a forced choice); FALSE here means "the exit
        /// exists and this panel draws it somewhere else". Collapsing the two would make every
        /// close-less panel un-closable, which is a softlock, not a copy fix.</para>
        /// <para>⚠ A panel that switches BETWEEN screens on one chrome (Manage) must still toggle
        /// <c>chrome.close</c> at runtime - this flag is a BUILD-time choice and cannot follow a
        /// screen change. ManageScreenPanel.ApplyScreenVisibility is that toggle.</para>
        /// </param>
        public static PanelChrome BuildObsidianPanel(Transform parent, string title,
            Vector2 anchorMin, Vector2 anchorMax, Action onClose,
            float headerX0 = 0.06f, float headerX1 = 0.94f, bool withBackdrop = true,
            string frameName = null, string medallionIcon = null, bool withClose = true)
        {
            var chrome = new PanelChrome();

            if (withBackdrop)
            {
                chrome.backdrop = AddImage(parent, "Backdrop", Vector2.zero, Vector2.one,
                    new Color(0.02f, 0.015f, 0.012f, 0.94f), rounded: false);
                var bdImg = chrome.backdrop.GetComponent<Image>();
                if (bdImg != null) bdImg.raycastTarget = false;
            }

            // SPRITE-FIRST: when the caller names a Blink Obsidian frame AND the mirrored art is
            // present (Resources/RpgUi/frame), render the REAL ornate panel background instead of
            // the procedural black-fill + gold-border. The frame art carries its own border/header
            // filigree/corners, so we skip the procedural trim. Content is a transparent full-rect
            // overlay — screens lay out by the same fractions, now over the real frame. Falls back
            // to the procedural panel (the C# "make our own" path) when the art is absent.
            Sprite frameSprite = string.IsNullOrEmpty(frameName)
                ? null : RpgUiCatalog.Get(RpgUiCatalog.RoleFrame, frameName);
            if (frameSprite != null)
            {
                var frameGo = new GameObject("ObsidianPanel", typeof(Image));
                frameGo.transform.SetParent(parent, false);
                var frt = frameGo.GetComponent<RectTransform>();
                frt.anchorMin = anchorMin; frt.anchorMax = anchorMax;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                var fimg = frameGo.GetComponent<Image>();
                fimg.sprite = frameSprite;
                fimg.type = Image.Type.Simple;   // full ornate background, drawn to the rect
                fimg.color = ChromeTint;         // A3 global chrome-tint hook (white until the palette ruling)
                fimg.raycastTarget = true;       // eat taps so they can't fall through
                chrome.root = frameGo;

                // Transparent content layer at 0..1 so existing fraction-anchored layouts drop in.
                chrome.content = AddImage(frameGo.transform, "PanelContent", Vector2.zero, Vector2.one,
                    new Color(0f, 0f, 0f, 0f), rounded: false);
                var cimg = chrome.content.GetComponent<Image>();
                if (cimg != null) cimg.raycastTarget = false;

                // Build the frame's pre-styled DROP-ZONES (the templated areas the owner described):
                // screens parent chrome-less content into chrome.layout.{header,body,medallion,footer}.
                var z = ZonesFor(frameName);
                // ── CLOSE-BAND RESERVATION (eyes-sweep 2026-07-06) ─────────────────────
                // The shared Close is seated in the DEFAULT bottom-center band (ZonesFor
                // forces z.close = DefaultCloseZone) as a FIXED 360x120px box growing UP
                // from the band's lower edge y=0.050 (SeatSharedCloseInside). The zone
                // fractions never accounted for that pixel box, so body content freely
                // extended under it and z-order painted the Close OVER the content
                // (GameGuide / Crafting / HeroLoadout / RealmStore / Leaderboard / ...).
                // Reserve the band AT THE FACTORY: raise the body zones' bottom edge to
                //   reservedYMin = z.close.y (0.050)
                //                + CanonCtaHeight / (panelHeightFrac * 1920 ref px)
                //                + 0.020 gap
                // so content geometrically ENDS ABOVE the Close on every framed panel.
                // Applies only when the Close sits in the default band (a future frame-
                // measured close notch keeps its own geometry). Geometry only — no
                // scroll-wrapping, no restyle; the sanity clamp keeps a very short panel
                // from losing more than 45% of its height to the band.
                float bodyYBefore   = z.body.y;
                float footerYBefore = z.footer.y;
                bool closeIsDefault = z.close == DefaultCloseZone;
                bool reservationFired = false;
                float reservedYMinDbg = 0f;
                float canvasHDbg = 0f, closeBandTopDbg = 0f;
                if (closeIsDefault)
                {
                    float panelFracH   = Mathf.Max(0.05f, anchorMax.y - anchorMin.y);
                    // Top of the fixed 360x120px Close box (SeatSharedCloseInside grows it UP
                    // from z.close.y) expressed as a fraction of THIS panel's height.
                    // LANDSCAPE FIX (overnight sweep 2026-07-07, proven by the panel-batch RCA):
                    // dividing by the PORTRAIT reference height (1920) under-reserves ~78% on a
                    // landscape canvas — the Close painted over Send report / DEPLOY / dialogue
                    // Close across panels. F8-5 FOLLOW-UP (DlgLayout capture 2026-07-07): reading
                    // the live canvas rect is ALSO wrong on the canvas's creation frame — the
                    // CanvasScaler hasn't applied yet, so rect.height returns RAW SCREEN PIXELS
                    // (captured 1351) instead of the post-scale local height (1047 @ scale 1.291);
                    // dividing the fixed 120-local-unit Close by the too-tall raw height
                    // under-reserved (0.211 vs the real 0.273) and the footer/Continue overlapped
                    // the Close band 0.276-0.323. PostScaleCanvasHeight replicates the scaler's
                    // own math from its settings — same value on the creation frame and after —
                    // so the reserved band always matches where the 120-unit box REALLY tops out.
                    float canvasH = PostScaleCanvasHeight(frameGo.transform);
                    float closeBandTop = z.close.y + CanonCtaHeight / (panelFracH * canvasH);
                    canvasHDbg = canvasH; closeBandTopDbg = closeBandTop;
                    // ── FOOTER RELOCATION (sweep 9413) ──────────────────────────────────
                    // The footer zone's designed bands (default 0.030–0.095; FrameCrafting
                    // action strip 0.085–0.145; etc.) sit INSIDE the Close band — the Close
                    // painted over every layout.footer hint/caption/action strip (RealmStore
                    // disclaimer, Crafting larder, ConsumableCrafting caption, BuildingUpgrade
                    // wallet...). Keep each frame's designed footer HEIGHT but re-seat the band
                    // to start just ABOVE the Close box; the body then stacks above the footer.
                    if (z.hasFooter && z.footer.y < closeBandTop + 0.015f)
                    {
                        float footerH = Mathf.Max(0.02f, z.footer.w - z.footer.y);
                        z.footer.y = Mathf.Min(closeBandTop + 0.015f, 0.40f);
                        z.footer.w = z.footer.y + footerH;
                        reservationFired = true;
                    }
                    // Body ends above the footer (when present) or above the Close band + gap.
                    float bodyFloor = z.hasFooter ? z.footer.w + 0.015f : closeBandTop + 0.020f;
                    float reservedYMin = Mathf.Min(bodyFloor, 0.45f);
                    reservedYMinDbg = reservedYMin;
                    if (z.body.y < reservedYMin) { z.body.y = reservedYMin; reservationFired = true; }
                    if (z.hasSplitBody)
                    {
                        if (z.bodyLeft.y  < reservedYMin) { z.bodyLeft.y  = reservedYMin; reservationFired = true; }
                        if (z.bodyRight.y < reservedYMin) { z.bodyRight.y = reservedYMin; reservationFired = true; }
                    }
                }
                // §12 instrumentation (sweep 9413: Close still over content on 9+ panels despite
                // the body reservation). One line per FRAME-path panel build: did the branch fire,
                // close values vs the default band, body/footer yMin before/after. Read it so:
                //  - closeIsDefault=False        -> the zone equality is the bug.
                //  - fired=True but still collides in capture -> that panel's content is NOT in
                //    layout.body/footer — it lays custom fractions on chrome.content (the
                //    unprotected class; known members: GameGuidePanel, PetSkillTreePanel).
                //  - this line ABSENT for a colliding panel -> it built on the PROCEDURAL path
                //    (frame sprite missing; see the PROCEDURAL Step below) — no zones exist.
                FlowTrace.Step("UI", string.Format(
                    "BuildObsidianPanel '{0}' frame={1} closeIsDefault={2} fired={3} " +
                    "close=({4:F3},{5:F3},{6:F3},{7:F3}) default=({8:F3},{9:F3},{10:F3},{11:F3}) " +
                    "bodyYMin {12:F3}->{13:F3} footerY {14:F3}->{15:F3} reservedYMin={16:F3} " +
                    "panelAnchors=({17:F2},{18:F2})-({19:F2},{20:F2}) " +
                    "canvasH={21:F0} closeBandTop={22:F3}",
                    title, frameName, closeIsDefault, reservationFired,
                    z.close.x, z.close.y, z.close.z, z.close.w,
                    DefaultCloseZone.x, DefaultCloseZone.y, DefaultCloseZone.z, DefaultCloseZone.w,
                    bodyYBefore, z.body.y, footerYBefore, z.footer.y, reservedYMinDbg,
                    anchorMin.x, anchorMin.y, anchorMax.x, anchorMax.y,
                    canvasHDbg, closeBandTopDbg));
                var layout = new FrameLayout
                {
                    header = Zone(chrome.content.transform, "Zone_Header", z.header),
                    body   = Zone(chrome.content.transform, "Zone_Body",   z.body),
                };
                // WO-839: optional thin SUB-HEADER band (badge / stars / meta rows) under the
                // title. Null for frames without one; screens fall back to their body top.
                if (z.hasSubHeader)
                    layout.subHeader = Zone(chrome.content.transform, "Zone_SubHeader", z.subHeader);
                // PARCHMENT-BLEED FIX (shared): a two-tone frame (FrameCrafting / FrameQuest) bakes a
                // dark-left / parchment-right seam into its art. A single-well screen that fills the
                // full body straddles that seam (half black, half tan). Paint a UNIFORM dark obsidian
                // well plate behind the whole body so ANY content reads on one tone; the parchment is
                // re-established only under the detail (bodyRight) zone below. Art-seam-independent.
                if (z.twoToneBody) ZoneBacking(layout.body, TwoToneWellFill);
                // #21 (Pause) fix — border-only / hollow frames (FrameOptions etc.) bake NO dark
                // body slab, so a screen dropped into the body zone showed the live scene through
                // it. Paint a UNIFORM near-black obsidian plate behind the body for every NON-two-tone
                // frame (two-tone keeps its dark/parchment plates above). Harmless on frames whose art
                // is already a dark well; guarantees the scene never bleeds through any body.
                else ZoneBacking(layout.body, ObsidianFill);
                if (z.hasMedallion)
                {
                    layout.medallion = Zone(chrome.content.transform, "Zone_Medallion", z.medallion);
                    // SEAT AN EMBLEM (eyes-on pass 2026-07-03: every panel rendered the Blink
                    // template's socket EMPTY — a black oval). The socket is a drop-zone; the
                    // panel names its concept, the resolver falls back to a generic crest so no
                    // socket ever ships blank. Inset so the circular rim stays visible.
                    Sprite emblem = UiStyle.Icon(
                        string.IsNullOrEmpty(medallionIcon) ? "crest" : medallionIcon,
                        "crest", "emblem", "shield", "settings");
                    if (emblem != null)
                    {
                        var em = AddImage(layout.medallion, "MedallionEmblem",
                            new Vector2(0.16f, 0.16f), new Vector2(0.84f, 0.84f),
                            Color.white, rounded: false);
                        var emImg = em.GetComponent<Image>();
                        emImg.sprite = emblem;
                        emImg.type = Image.Type.Simple;
                        emImg.preserveAspect = true;
                        emImg.raycastTarget = false;
                    }
                }
                if (z.hasFooter)    layout.footer    = Zone(chrome.content.transform, "Zone_Footer",    z.footer);
                if (z.hasSplitBody)
                {
                    // Pre-split master-detail well (owner ruling 2026-07-03): dark well = lists,
                    // parchment well = detail. Both are ALSO covered by the full `body` zone for
                    // screens that want one well; split-aware screens use these instead.
                    layout.bodyLeft  = Zone(chrome.content.transform, "Zone_BodyLeft",  z.bodyLeft);
                    layout.bodyRight = Zone(chrome.content.transform, "Zone_BodyRight", z.bodyRight);
                    // PARCHMENT-BLEED FIX (cont.): re-establish a UNIFORM parchment plate under the
                    // DETAIL side only. bodyRight is a LATER sibling than the full-body dark plate, so
                    // its tan plate renders on top — dark list left, clean parchment detail right, both
                    // aligned to OUR zones (never the baked art seam). Detail prose reads dark-on-tan.
                    if (z.twoToneBody) ZoneBacking(layout.bodyRight, TwoToneParchmentFill);
                }

                // §1.14 FIT-OR-SCROLL is OPT-IN per screen (MakeScrollZone on a list container),
                // NOT a factory auto-wrap. The auto-wrap shipped here briefly and was REVERTED on
                // captured proof (windowed runs 9400/9401, panel_PartyShop.png): the wrap re-points
                // layout.body at a VerticalLayoutGroup content column, but panel sections are
                // ANCHOR-STRETCHED RectTransforms (sizeDelta.y = 0) — under a layout column they
                // report height 0 no matter the childControl flags, and the whole body collapsed
                // ([Flow:Vendor] resolved 39 items, ZERO rendered — built-but-invisible, twice).
                // Screens with row-lists opt in (PartyShop RebuildList already does); anchor-layout
                // panels keep their own geometry.
                chrome.layout = layout;

                // Gold title sits in the header zone (no procedural shadow/rule — the frame has
                // the band). NO crest glyph on the frame path (eyes-on 2026-07-03: the glyph is
                // missing from the TMP font and rendered as an asterisk-box on every framed
                // panel; the ornate frame IS the ornament).
                chrome.title = Label(layout.header, title ?? "",
                    0f, 1f, ElarionUi.Gilt, ElarionUi.FontTitle,
                    TextAlignmentOptions.Center, 0f, 1f, spacing: 4f, bold: true);
                chrome.title.raycastTarget = false;
                // §1.14 (owner F8 flag_06: "The Forge" title clipped mid-glyph): bounded auto-size
                // + ellipsis — a panel title can never clip again.
                FitSingleLine(chrome.title);

                // A null handler means this is a forced-choice/progression surface: do not draw
                // a dead Close control over its actions. Non-null handlers use the frame's
                // measured close zone (Stats_Panel's top-right notch etc.).
                // WO-1491: `withClose` is the panel's own drawing choice; a null handler is the
                // absence of an exit. See the parameter doc for why they are two levers.
                if (onClose != null && withClose)
                    chrome.close = ObsidianCloseButton(chrome.content.transform, onClose, z.close);
                MedievalUiSkin.ApplyShell(chrome);
                return chrome;
            }

            // §12 instrumentation (sweep 9413): a panel that logs THIS line built on the
            // PROCEDURAL path — the frame sprite did not resolve (frameName null OR the
            // Resources/RpgUi art absent in this build). WO-714 P6: this path now ALSO builds
            // the DEFAULT drop-zones with the close-band reservation (below), so zone-consuming
            // screens end above the Close on the art-absent path too. Screens laying custom
            // fractions directly on chrome.content remain the unprotected legacy class.
            FlowTrace.Step("UI", string.Format(
                "BuildObsidianPanel '{0}' frame={1} PROCEDURAL path (frame sprite missing) — " +
                "default zones + close-band reservation (WO-714 P6); Close seats at default band ({2:F3},{3:F3},{4:F3},{5:F3})",
                title, string.IsNullOrEmpty(frameName) ? "<none>" : frameName,
                DefaultCloseZone.x, DefaultCloseZone.y, DefaultCloseZone.z, DefaultCloseZone.w));

            // Gold trim border layer (spans the whole rect; the black fill insets over it).
            // Its Image keeps raycastTarget = true so taps can't fall through the panel.
            chrome.root = AddImage(parent, "ObsidianPanel", anchorMin, anchorMax, ObsidianTrim, rounded: true);

            // Near-black fill, inset by the trim thickness so the gold reads as a clean border.
            chrome.content = AddImage(chrome.root.transform, "PanelFill", Vector2.zero, Vector2.one, ObsidianFill, rounded: true);
            var fillRt = chrome.content.GetComponent<RectTransform>();
            fillRt.offsetMin = new Vector2(ObsidianTrimPx, ObsidianTrimPx);
            fillRt.offsetMax = new Vector2(-ObsidianTrimPx, -ObsidianTrimPx);
            var fillImg = chrome.content.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            // Gold header title across the top.
            chrome.title = Header(chrome.content.transform, title ?? "", x0: headerX0, x1: headerX1, y0: 0.92f, y1: 0.98f);
            // §1.14 (owner F8 flag_06): bounded auto-size + ellipsis — the title can never clip.
            // Header fits BOTH the title and its drop-shadow copy, so no re-fit is done here:
            // a second FitSingleLine would re-read the title's fontSize as its new maxSize and
            // could ratchet the title below the shadow it is supposed to sit on top of.

            // A null handler means this is a forced-choice/progression surface: do not draw
            // a dead Close control over its actions.
            if (onClose != null && withClose)
                chrome.close = ObsidianCloseButton(chrome.content.transform, onClose);

            // ── WO-714 P6: DEFAULT DROP-ZONES + CLOSE-BAND RESERVATION on the PROCEDURAL
            // path too. Same math as the frame path above (PostScaleCanvasHeight so the
            // reserved band matches where SeatSharedCloseInside's fixed 120-unit box really
            // tops out). Zone-consuming screens (layout.body/footer) geometrically end above
            // the shared Close whether or not the frame art resolved; chrome.content stays
            // the full-rect legacy surface, byte-identical for screens that ignore layout.
            {
                var pz = ZonesFor(null);   // the default zone set (close already forced to the band)
                float pPanelFracH = Mathf.Max(0.05f, anchorMax.y - anchorMin.y);
                float pCanvasH    = PostScaleCanvasHeight(chrome.root.transform);
                float pCloseTop   = pz.close.y + CanonCtaHeight / (pPanelFracH * pCanvasH);
                if (pz.hasFooter && pz.footer.y < pCloseTop + 0.015f)
                {
                    float fh = Mathf.Max(0.02f, pz.footer.w - pz.footer.y);
                    pz.footer.y = Mathf.Min(pCloseTop + 0.015f, 0.40f);
                    pz.footer.w = pz.footer.y + fh;
                }
                float pBodyFloor = pz.hasFooter ? pz.footer.w + 0.015f : pCloseTop + 0.020f;
                float pReserved  = Mathf.Min(pBodyFloor, 0.45f);
                if (pz.body.y < pReserved) pz.body.y = pReserved;

                chrome.layout = new FrameLayout
                {
                    header = Zone(chrome.content.transform, "Zone_Header", pz.header),
                    body   = Zone(chrome.content.transform, "Zone_Body",   pz.body),
                    footer = pz.hasFooter ? Zone(chrome.content.transform, "Zone_Footer", pz.footer) : null,
                };
                // Zones must never occlude the Close: keep them behind the earlier-built
                // title/Close siblings (transparent RectTransforms; render order only).
                chrome.layout.header.SetAsFirstSibling();
                chrome.layout.body.SetSiblingIndex(1);
                if (chrome.layout.footer != null) chrome.layout.footer.SetSiblingIndex(2);
            }

            MedievalUiSkin.ApplyShell(chrome);
            return chrome;
        }

        /// <summary>The whole modal in one call: <see cref="BuildModalCanvas"/> (overrideSorting) +
        /// <see cref="Scrim"/> (tap-outside closes) + <see cref="BuildObsidianPanel"/>. Returns the
        /// canvas GameObject and the chrome. Use for new panels; existing panels keep their canvas
        /// and call <see cref="BuildObsidianPanel"/> directly.</summary>
        public sealed class ObsidianModal
        {
            /// <summary>The ScreenSpaceOverlay modal canvas root.</summary>
            public GameObject canvas;
            /// <summary>The built panel chrome (content / title / close).</summary>
            public PanelChrome chrome;
        }

        /// <summary>
        /// Reusable modal footprints. Content-heavy screens should choose by interaction pattern,
        /// not invent per-screen anchors; this keeps title scale, breathing room, and thumb-zone
        /// placement consistent across aspect ratios.
        /// </summary>
        public enum ModalArchetype
        {
            Compact,       // pause, confirmations, short decisions
            Standard,      // settings, tower/build management
            Browse,        // master-detail lists, maps, shops
            Workspace      // dense editors and battle preparation
        }

        /// <summary>Resolve the canonical safe-area footprint for a modal archetype.</summary>
        public static void ModalAnchors(ModalArchetype archetype,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            bool portrait = SurfaceHeight > SurfaceWidth;
            if (portrait)
            {
                switch (archetype)
                {
                    case ModalArchetype.Compact:
                        anchorMin = new Vector2(0.08f, 0.18f);
                        anchorMax = new Vector2(0.92f, 0.82f);
                        return;
                    case ModalArchetype.Standard:
                        anchorMin = new Vector2(0.05f, 0.10f);
                        anchorMax = new Vector2(0.95f, 0.90f);
                        return;
                    default:
                        anchorMin = new Vector2(0.025f, 0.04f);
                        anchorMax = new Vector2(0.975f, 0.96f);
                        return;
                }
            }

            switch (archetype)
            {
                case ModalArchetype.Compact:
                    anchorMin = new Vector2(0.35f, 0.10f);
                    anchorMax = new Vector2(0.65f, 0.90f);
                    break;
                case ModalArchetype.Standard:
                    anchorMin = new Vector2(0.20f, 0.10f);
                    anchorMax = new Vector2(0.80f, 0.90f);
                    break;
                case ModalArchetype.Browse:
                    anchorMin = new Vector2(0.06f, 0.06f);
                    anchorMax = new Vector2(0.94f, 0.94f);
                    break;
                default:
                    anchorMin = new Vector2(0.035f, 0.04f);
                    anchorMax = new Vector2(0.965f, 0.96f);
                    break;
            }
        }

        /// <summary>Build a complete modal using a shared responsive footprint.</summary>
        public static ObsidianModal BuildObsidianModal(string name, string title,
            ModalArchetype archetype, Action onClose, int sortingOrder = 31000,
            string frameName = null, string medallionIcon = null)
        {
            ModalAnchors(archetype, out var anchorMin, out var anchorMax);
            return BuildObsidianModal(name, title, anchorMin, anchorMax, onClose,
                sortingOrder, frameName, medallionIcon);
        }

        /// <summary>Build a complete Obsidian modal (canvas + scrim + chrome) in one call.</summary>
        public static ObsidianModal BuildObsidianModal(string name, string title,
            Vector2 anchorMin, Vector2 anchorMax, Action onClose, int sortingOrder = 31000,
            string frameName = null, string medallionIcon = null)
        {
            var canvas = BuildModalCanvas(name, sortingOrder);
            var c = canvas.GetComponent<Canvas>();
            if (c != null) c.overrideSorting = true;
            Scrim(canvas.transform, onClose);
            var chrome = BuildObsidianPanel(canvas.transform, title, anchorMin, anchorMax, onClose,
                frameName: frameName, medallionIcon: medallionIcon);
            return new ObsidianModal { canvas = canvas, chrome = chrome };
        }

        /// <summary>
        /// The ONE standard Close used by every panel (built by <see cref="BuildObsidianPanel"/>;
        /// exposed for surfaces that build their own chrome).
        ///
        /// OWNER CANON (2026-07-03 — SUPERSEDES the earlier "the gold X is Blink's native close =
        /// conformant" ruling): NO panel, popup, or modal anywhere may use an X for close. The close
        /// is a SLEEK OBSIDIAN BUTTON — a labeled <see cref="BuildObsidianButton"/> box ("Close"),
        /// Obsidian-chrome styled (Style1 / Gray to sit quiet against the black+gold frame), seated
        /// in the frame's MEASURED close zone (<see cref="ZonesFor"/>) or the legacy top-right anchor
        /// when no <paramref name="zone"/> is given. This routes the pack's dual-state art AND the
        /// null-art fallback through the button family — never the round <c>Close_Button</c> X notch,
        /// never a text-X chip. Same public contract (returns the <see cref="Button"/>, wires
        /// <paramref name="onClose"/>) so all ~19 panel Closes are unaffected.
        /// </summary>
        public static Button ObsidianCloseButton(Transform parent, Action onClose, Vector4? zone = null)
        {
            Vector4 zn = zone ?? DefaultCloseZone;
            // Sleek obsidian button box (Style1/Gray) — the kit's own labeled button, NOT an X glyph.
            // The click routes through the ONE shared close entry (Common.Close) so the close
            // BEHAVIOR is defined in exactly one place; the panel's own teardown is passed as the hook.
            var btn = BuildObsidianButton(parent, CommonText.Close.Resolve(),
                ObsidianButtonStyle.Style1, ObsidianButtonColor.Gray,
                new Vector2(zn.x, zn.y), new Vector2(zn.z, zn.w),
                () => Common.Close(onClose));
            // Keep the diagnostics/lookup name every prior caller expected.
            if (btn != null) btn.gameObject.name = "CloseButton";
            // OWNER F8 x3: every Close is the SAME pixel size on every screen. OWNER F8
            // 2026-07-04: it must also sit INSIDE the panel, never on top of the frame.
            // PinCanonicalCtaSize centred the fixed 360x120 box on the thin bottom close
            // band, so its lower half sank BELOW the band — over/through the ornate bottom
            // border ("close is on top of the panel"). SeatSharedCloseInside keeps the same
            // fixed size + bottom-centre placement but seats the box's BOTTOM at the band
            // and grows it UPWARD into the interior, so it can never dip into the border.
            SeatSharedCloseInside(btn, zn);
            return btn;
        }

        /// <summary>
        /// Pin a just-built primary Continue / shared Close button to the ONE canonical
        /// pixel size (owner F8 x3, 2026-07-04). Callers still pass fraction-of-parent
        /// anchors to POSITION the button; this collapses those stretch anchors to the
        /// CENTRE of that anchor rect and stamps <see cref="CanonCtaWidth"/> x
        /// <see cref="CanonCtaHeight"/> — so the button renders identically regardless of
        /// how big its parent (panel / footer / popup) is. Presentation-only, size-only:
        /// it does not restyle, re-colour, or re-wire the button.
        /// </summary>
        public static void PinCanonicalCtaSize(Button button)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            Vector2 centre = (rt.anchorMin + rt.anchorMax) * 0.5f;
            rt.anchorMin = centre;
            rt.anchorMax = centre;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(CanonCtaWidth, CanonCtaHeight);
        }

        /// <summary>
        /// Seat the ONE shared Close at the canonical size (<see cref="CanonCtaWidth"/> x
        /// <see cref="CanonCtaHeight"/>) INSIDE the panel interior (owner F8 2026-07-04:
        /// "everything must be inside the panel — the close was on top of the panel").
        /// Same fixed pixel size + bottom-centre placement as the F8-x3 canonical CTA, but
        /// where <see cref="PinCanonicalCtaSize"/> centres the box on the thin close band —
        /// sinking its lower half BELOW the band, over the ornate bottom border — this
        /// anchors the box's BOTTOM at the band's lower edge (pivot y=0) and grows it
        /// UPWARD into the interior. The fixed-size box therefore stays clear of the frame
        /// border on panels of any size (the overflow the centre-pin could not prevent).
        /// Position-only; does not restyle/re-wire the button.
        /// </summary>
        public static void SeatSharedCloseInside(Button button, Vector4 closeZone)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            float centreX = (closeZone.x + closeZone.z) * 0.5f;
            rt.anchorMin = new Vector2(centreX, closeZone.y);
            rt.anchorMax = new Vector2(centreX, closeZone.y);
            rt.pivot = new Vector2(0.5f, 0f);   // seat by the button's BOTTOM edge → grows up
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(CanonCtaWidth, CanonCtaHeight);
        }

        /// <summary>
        /// KIT TOUCH FLOOR (VISUAL_TOUCH_CONTRAST_AUDIT 2026-07-14, P0): grow a kit button so its
        /// SHORTEST resolved side is at least <see cref="MinTouchPx"/> reference px. Buttons already
        /// larger are untouched (pure floor, never shrinks). Kit buttons anchor by fraction-of-parent
        /// with zero offsets, so the physical size is unknown until the first layout pass — a one-shot
        /// guard measures the resolved rect and, if a side is sub-floor, grows the offsets symmetrically
        /// about the centre so the button stays put. When the rect is layout-group-driven the offset
        /// write is overridden (harmless no-op) — the same safety contract as the text-fit guard.
        /// Idempotent: re-uses an existing guard on the same button.
        /// </summary>
        public static void ClampMinTouch(Button button)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            var guard = rt.GetComponent<UiKitMinTouchGuard>();
            if (guard == null) guard = rt.gameObject.AddComponent<UiKitMinTouchGuard>();
            guard.Arm();
        }

        // =====================================================================
        //  WO-1060 ASSERT A — MAKE THE CLAMP'S FIRING VISIBLE (record-only).
        // ---------------------------------------------------------------------
        //  ⛔ ClampMinTouch's BEHAVIOUR IS DELIBERATELY UNCHANGED. It is the correct
        //  runtime rescue for a build that shipped wrong, and weakening it would turn
        //  a visible defect into an untappable one. What was missing is that it fired
        //  in TOTAL SILENCE: by the time the clamp grows a control, the layout it was
        //  meant to protect is already spilled into its neighbours, and nothing
        //  anywhere says so. Four panels shipped that way in three days.
        //
        //  So every growth is APPENDED to a ring here and to the log. A gate reads
        //  the ring; a device log reads the line. Neither changes a pixel.
        //
        //  ⚠ THIS IS NOT THE WHOLE ORACLE, and must not be mistaken for it. LateUpdate
        //  never runs in an edit-mode batchmode capture, so a headless run records
        //  ZERO growths on a genuinely broken panel. The gate-time assert is the
        //  AUTHORED-BAND measurement in UICaptureLaunch.AuditGeometry (rule 4), which
        //  measures the settled rect against MinTouchPx and therefore catches the same
        //  defect BEFORE the clamp would have rescued it. This ring is the runtime
        //  half: it is what turns an owner device session into evidence.
        // =====================================================================

        /// <summary>One recorded <see cref="ClampMinTouch"/> rescue. Presentation telemetry only.</summary>
        public readonly struct ClampGrowth
        {
            public readonly string ControlPath;
            public readonly float AuthoredW, AuthoredH, GrownW, GrownH;

            public ClampGrowth(string controlPath, float aw, float ah, float gw, float gh)
            {
                ControlPath = controlPath; AuthoredW = aw; AuthoredH = ah; GrownW = gw; GrownH = gh;
            }

            public override string ToString() =>
                ControlPath + ": authored " + AuthoredW.ToString("0.#") + "x" + AuthoredH.ToString("0.#") +
                " -> grown " + GrownW.ToString("0.#") + "x" + GrownH.ToString("0.#") +
                " (" + (AuthoredW > 0.5f ? (GrownW / AuthoredW) : 1f).ToString("0.0#") + "x on W, " +
                (AuthoredH > 0.5f ? (GrownH / AuthoredH) : 1f).ToString("0.0#") + "x on H). " +
                "Author the band above MinTouchPx(" + MinTouchPx.ToString("0.#") + "); do not rely on the clamp.";
        }

        private const int ClampGrowthRingMax = 128;   // bounded: a hot loop can never grow this unboundedly
        private static readonly List<ClampGrowth> _clampGrowths = new List<ClampGrowth>();

        /// <summary>Every clamp rescue recorded since the last <see cref="ClearClampGrowths"/>.
        /// A non-empty list on a panel under test is a WO-1060 Assert A FAILURE.</summary>
        public static IReadOnlyList<ClampGrowth> ClampGrowths => _clampGrowths;

        /// <summary>Reset the ring — call before building a panel you are about to assert on.</summary>
        public static void ClearClampGrowths() { _clampGrowths.Clear(); }

        private static void RecordClampGrowth(RectTransform rt, float aw, float ah, float gw, float gh)
        {
            string path;
            try
            {
                var sb = new StringBuilder(rt.name);
                var t = rt.parent;
                for (int i = 0; i < 4 && t != null; i++, t = t.parent) sb.Insert(0, t.name + "/");
                path = sb.ToString();
            }
            catch { path = rt != null ? rt.name : "<null>"; }

            var growth = new ClampGrowth(path, aw, ah, gw, gh);
            if (_clampGrowths.Count < ClampGrowthRingMax) _clampGrowths.Add(growth);
            Debug.LogWarning("[touch-oracle] CLAMP FIRED " + growth);
        }

        /// <summary>One-shot post-layout enforcer for <see cref="ClampMinTouch"/>.</summary>
        private sealed class UiKitMinTouchGuard : MonoBehaviour
        {
            private RectTransform _rt;
            private int _frames;

            private void Awake() { _rt = transform as RectTransform; }

            /// <summary>(Re)start the post-layout floor check.</summary>
            public void Arm() { _frames = 0; enabled = true; }

            private void LateUpdate()
            {
                if (_rt == null) { enabled = false; return; }
                if (_frames++ < 1) return;                 // let the first layout pass size the rect

                float w = _rt.rect.width;
                float h = _rt.rect.height;
                if (w <= 0f && h <= 0f)
                {
                    if (_frames > 600) enabled = false;    // never sized — stand down (not a size bug)
                    return;
                }

                bool grew = false;
                if (w > 0f && w < MinTouchPx)
                {
                    float half = (MinTouchPx - w) * 0.5f;
                    _rt.offsetMin = new Vector2(_rt.offsetMin.x - half, _rt.offsetMin.y);
                    _rt.offsetMax = new Vector2(_rt.offsetMax.x + half, _rt.offsetMax.y);
                    grew = true;
                }
                if (h > 0f && h < MinTouchPx)
                {
                    float half = (MinTouchPx - h) * 0.5f;
                    _rt.offsetMin = new Vector2(_rt.offsetMin.x, _rt.offsetMin.y - half);
                    _rt.offsetMax = new Vector2(_rt.offsetMax.x, _rt.offsetMax.y + half);
                    grew = true;
                }

                // WO-1060 Assert A: the rescue is recorded, never suppressed. Behaviour above is
                // byte-for-byte what it was; this line is the only thing that changed.
                if (grew) RecordClampGrowth(_rt, w, h, Mathf.Max(w, MinTouchPx), Mathf.Max(h, MinTouchPx));

                enabled = false;                           // one-shot floor; re-armed by ClampMinTouch if reused
            }
        }

        // =====================================================================
        //  SURFACE SIZE — the ONE place the kit asks "how big is the screen?"
        // ---------------------------------------------------------------------
        //  WHY THIS EXISTS (2026-08-05). Zone geometry is resolved AT BUILD TIME by
        //  PostScaleCanvasHeight below, which read Screen.width/height directly. In
        //  `-batchmode` there is NO game view for Screen.* to mirror: it reports the
        //  640x480 offscreen default and nothing in the editor moves it. That is
        //  measured, not theorised — Builds/ui-capture-rail.log:475 records
        //  "graphicsDevice=Direct3D11 ... screen=640x480" (a real GPU, so it is not
        //  the -nographics case) and :489 records the GameViewSizes /
        //  SizeSelectionCallback reflection SUCCEEDING while Screen stayed 640x480.
        //  Consequence: every UI capture was BUILT at one geometry and written under
        //  three different filenames, so a caption that rendered off its black plate
        //  on the device was structurally invisible to the harness.
        //
        //  The surface is therefore INJECTABLE. With no override the properties return
        //  Screen.* — byte-identical to the behaviour before this change, on device and
        //  in play mode. A capture may override it so a panel is BUILT against the
        //  resolution it is about to be SHOT at.
        //
        //  DELIBERATELY EDITOR-ONLY: the setter no-ops outside the editor, so no shipped
        //  build can run on an overridden surface however a future caller misuses it.
        //  Presentation-layer only — it reports a size, it never touches game state.
        // =====================================================================
        private static int _surfaceOverrideW;   // 0 == no override (the shipped state)
        private static int _surfaceOverrideH;

        /// <summary>Screen width the kit resolves geometry against (Screen.width unless a
        /// capture has overridden it via <see cref="SetSurfaceOverride"/>).</summary>
        public static int SurfaceWidth => _surfaceOverrideW > 0 ? _surfaceOverrideW : Screen.width;

        /// <summary>Screen height the kit resolves geometry against (Screen.height unless a
        /// capture has overridden it via <see cref="SetSurfaceOverride"/>).</summary>
        public static int SurfaceHeight => _surfaceOverrideH > 0 ? _surfaceOverrideH : Screen.height;

        /// <summary>True while a capture is driving the surface (never true in a player).</summary>
        public static bool HasSurfaceOverride => _surfaceOverrideW > 0 && _surfaceOverrideH > 0;

        /// <summary>EDITOR-ONLY. Make the kit resolve geometry as if the screen were
        /// <paramref name="width"/> x <paramref name="height"/>. Always pair with
        /// <see cref="ClearSurfaceOverride"/> in a finally/Dispose — a leaked override would
        /// mis-size every panel built afterwards in that editor session.</summary>
        public static void SetSurfaceOverride(int width, int height)
        {
            if (!Application.isEditor) return;   // structurally unreachable in a shipped build
            if (width <= 0 || height <= 0) { ClearSurfaceOverride(); return; }
            _surfaceOverrideW = width;
            _surfaceOverrideH = height;
        }

        /// <summary>Back to Screen.* — the default, shipped behaviour.</summary>
        public static void ClearSurfaceOverride()
        {
            _surfaceOverrideW = 0;
            _surfaceOverrideH = 0;
        }

        /// <summary>
        /// Height of the canvas above <paramref name="under"/> in CANVAS-LOCAL units, as it
        /// will be AFTER the CanvasScaler has applied — NOT the live rect. F8-5 root cause
        /// (DlgLayout capture 2026-07-07): a ScreenSpaceOverlay canvas created the SAME
        /// frame still has scaleFactor 1, so its rect.height reads RAW SCREEN PIXELS
        /// (captured 1351) while the ScaleWithScreenSize scaler later shrinks the local
        /// rect (÷1.291 → 1047). The close-band reservation divided the fixed 120-unit
        /// Close box by the too-tall raw height and UNDER-reserved (0.211 vs the real
        /// 0.273 panel fraction) — the footer/Continue overlapped the Close band
        /// 0.276–0.323 and the last-sibling Close won the raycasts. Replicating the
        /// scaler's own math from its settings yields the SAME value on the creation
        /// frame and every frame after, so the reserved band always matches where
        /// <see cref="SeatSharedCloseInside"/>'s fixed 120-local-unit box really tops out.
        /// Order of trust: overlay-canvas scaler math (deterministic) → laid-out live
        /// rect (non-overlay / physical-size modes) → the kit's 1920 portrait reference
        /// (no canvas / headless with no valid Screen).
        /// </summary>
        /// <remarks>PUBLIC so a screen can size its own bands in REFERENCE PIXELS on the frame
        /// it builds them (CANON_GROUND_TRUTH 2026-08-02 §4: a text band must be fixed px >= the
        /// font's line box, never a fraction of a parent). Reading the parent's rect on the
        /// creation frame returns RAW SCREEN PIXELS; this returns the post-scale height that the
        /// fraction anchors will actually resolve against, on that frame and every frame after.</remarks>
        public static float PostScaleCanvasHeight(Transform under)
        {
            const float fallbackH = 1920f;   // kit portrait reference height
            var canvas = under != null ? under.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return fallbackH;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            var rootRt = root.transform as RectTransform;
            float liveH = rootRt != null ? rootRt.rect.height : 0f;

            // SurfaceHeight/Width, not Screen.* directly: identical on device and in play mode
            // (no override), but a capture can drive them so this build-time read resolves the
            // TARGET resolution's geometry instead of the editor's 640x480 batchmode default.
            float screenH = SurfaceHeight;
            var scaler = root.GetComponent<CanvasScaler>();
            if (root.renderMode == RenderMode.ScreenSpaceOverlay && scaler != null && screenH > 1f)
            {
                float scale = 0f;
                switch (scaler.uiScaleMode)
                {
                    case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                        float refW = Mathf.Max(1f, scaler.referenceResolution.x);
                        float refH = Mathf.Max(1f, scaler.referenceResolution.y);
                        float screenW = Mathf.Max(1f, (float)SurfaceWidth);
                        switch (scaler.screenMatchMode)
                        {
                            case CanvasScaler.ScreenMatchMode.Expand:
                                scale = Mathf.Min(screenW / refW, screenH / refH);
                                break;
                            case CanvasScaler.ScreenMatchMode.Shrink:
                                scale = Mathf.Max(screenW / refW, screenH / refH);
                                break;
                            default:   // MatchWidthOrHeight — Unity's own log-space lerp
                                scale = Mathf.Pow(2f, Mathf.Lerp(
                                    Mathf.Log(screenW / refW, 2f),
                                    Mathf.Log(screenH / refH, 2f),
                                    scaler.matchWidthOrHeight));
                                break;
                        }
                        break;
                    case CanvasScaler.ScaleMode.ConstantPixelSize:
                        scale = scaler.scaleFactor;
                        break;
                    // ConstantPhysicalSize: DPI-dependent — fall through to the live rect.
                }
                if (scale > 0.01f) return screenH / scale;
            }
            // Non-overlay / no scaler / physical-size / headless: the laid-out rect, else the reference.
            return liveH > 100f ? liveH : fallbackH;
        }

        // =====================================================================
        // COMMON — the ONE shared close ENTRY (owner rule 2026-07-03).
        // ---------------------------------------------------------------------
        // "Almost every panel needs close, so close must be defined in exactly ONE
        // place." The shared sleek CloseButton (ObsidianCloseButton) invokes
        // Common.Close — NEVER bespoke per-panel close logic. A panel supplies its
        // teardown (hide/destroy its OWN canvas + VM unbind — the lifecycle it
        // legitimately owns, registered with PanelManager) as the onClose hook; this
        // funnel just RUNS it, guarded so one throwing teardown can't wedge the modal
        // system. PRESENTATION-ONLY: it shows the close + runs the caller's lifecycle
        // hook; it never owns or defines panel state / data.
        // =====================================================================

        /// <summary>The ONE shared close behavior. Every shared CloseButton wires here; every panel
        /// that closes routes through this single entry (no panel re-implements close). Guarded so a
        /// throwing teardown logs (§12 no-silent-failure) instead of wedging the UI.</summary>
        public static class Common
        {
            /// <summary>Run the panel-supplied close teardown through the single shared entry.
            /// Null-safe; a throwing hook is logged, never swallowed silently.</summary>
            public static void Close(Action onClose)
            {
                if (onClose == null) return;
                try { onClose(); }
                catch (Exception e)
                {
                    FlowTrace.Fail("UI", "Common.Close: panel teardown threw: " + e.Message);
                }
            }
        }

        // =====================================================================
        // TOAST — the ONE shared non-blocking notification card (WO-562).
        // ---------------------------------------------------------------------
        // Every hand-rolled toast (GearGrantToast blue-grey, BuildFeedbackToast
        // brown) built its own bg + accent + label colours. This is the single
        // obsidian toast visual: a near-BLACK rounded card + a tone accent bar
        // (left edge or top edge) + a legacy-uGUI Text label (NO TMP dependency
        // so it is WebGL-safe, matching the toasts' proven path). Callers keep
        // their own Canvas + fade/lifetime; they just stop hand-rolling the look.
        // =====================================================================

        /// <summary>Toast intent — drives the accent bar colour (Gold = grant/info-positive,
        /// Confirm = success, Danger = denied/error, Info = neutral gold-soft).</summary>
        public enum ToastTone { Gold, Confirm, Danger, Info }

        /// <summary>The accent-bar colour for a toast tone (sourced from the canon palette).</summary>
        public static Color ToastAccent(ToastTone tone)
        {
            switch (tone)
            {
                case ToastTone.Confirm: return new Color(ElarionUi.Affordable.r, ElarionUi.Affordable.g, ElarionUi.Affordable.b, 1f);
                case ToastTone.Danger:  return new Color(ElarionUi.Danger.r, ElarionUi.Danger.g, ElarionUi.Danger.b, 1f);
                case ToastTone.Info:    return AccentSoft;
                default:                return ObsidianTrim; // Gold
            }
        }

        /// <summary>The pieces of a built toast card: the card GameObject (caller sets its
        /// RectTransform anchors/size + parents it under its own Canvas), the legacy Text
        /// label (set <c>.text</c>), and the accent Image (retint if desired).</summary>
        public sealed class ToastParts
        {
            /// <summary>The obsidian card root — POSITION + SIZE this RectTransform.</summary>
            public GameObject card;
            /// <summary>The legacy uGUI Text label (WebGL-safe) — set its text.</summary>
            public Text label;
            /// <summary>The tone accent bar Image.</summary>
            public Image accent;
        }

        /// <summary>
        /// Build the ONE shared obsidian toast card under <paramref name="parent"/>: a near-black
        /// rounded fill + a soft gold inner rim + a tone accent bar (left edge when
        /// <paramref name="accentLeft"/>, else a top bar) + a WebGL-safe legacy Text label. Never
        /// raycast-blocks. The caller positions/sizes the returned <see cref="ToastParts.card"/> and
        /// sets <see cref="ToastParts.label"/>.text. Centralises the look so no toast hand-rolls chrome.
        /// </summary>
        public static ToastParts ToastCard(Transform parent, ToastTone tone,
                                           bool accentLeft = true, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
            cardGo.transform.SetParent(parent, false);
            var bg = cardGo.GetComponent<Image>();
            bg.raycastTarget = false;
            // SPRITE-FIRST (§1.5): the real Blink Notification plate (tone → variant), 9-sliced with
            // Fill Center, tinted only by the A3 chrome hook. Procedural obsidian card = null-art fallback.
            var notif = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, ToastPlateName(tone));
            if (notif == null) notif = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, RpgUiCatalog.ElementNotif1);
            if (notif != null)
            {
                bg.sprite = notif;
                bg.type = Image.Type.Sliced;
                bg.fillCenter = true;
                bg.color = ChromeTint;
            }
            else
            {
                bg.color = ObsidianFill;
                ApplyRounded(bg);
                AddInnerRim(cardGo, ObsidianTrim);   // soft gold rim (gold-trim canon)
            }

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(cardGo.transform, false);
            var art = (RectTransform)accentGo.transform;
            if (accentLeft)
            {
                art.anchorMin = new Vector2(0f, 0f); art.anchorMax = new Vector2(0f, 1f);
                art.pivot = new Vector2(0f, 0.5f); art.sizeDelta = new Vector2(7f, 0f);
            }
            else
            {
                art.anchorMin = new Vector2(0f, 1f); art.anchorMax = new Vector2(1f, 1f);
                art.pivot = new Vector2(0.5f, 1f); art.sizeDelta = new Vector2(0f, 6f);
            }
            art.anchoredPosition = Vector2.zero;
            var ai = accentGo.GetComponent<Image>();
            ai.color = ToastAccent(tone);
            ai.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(cardGo.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(accentLeft ? 22f : 18f, 10f);
            lrt.offsetMax = new Vector2(-16f, -12f);
            var text = labelGo.GetComponent<Text>();
            text.color = ElarionUi.Parchment;
            text.fontSize = 24;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) text.font = font;

            return new ToastParts { card = cardGo, label = text, accent = ai };
        }

        // =====================================================================
        // CONFIRM MODAL — the ONE shared yes/no (or OK) popup (WO-562).
        // ---------------------------------------------------------------------
        // A complete obsidian confirm/popup: BuildObsidianModal (canvas+scrim+
        // black panel+gold trim+shared Close) + a centred message + one or two
        // kit Buttons. So no surface hand-rolls a bespoke confirm dialog.
        // =====================================================================

        /// <summary>The pieces of a built confirm modal.</summary>
        public sealed class ConfirmModal
        {
            /// <summary>The modal canvas root (Destroy to dismiss).</summary>
            public GameObject canvas;
            /// <summary>The obsidian panel chrome (content / title / shared Close).</summary>
            public PanelChrome chrome;
            /// <summary>The confirm (primary) button.</summary>
            public Button confirm;
            /// <summary>The cancel button (null when no cancel label was supplied).</summary>
            public Button cancel;
            /// <summary>The centred message label.</summary>
            public TextMeshProUGUI message;
        }

        /// <summary>
        /// Build a complete shared confirm/popup modal: obsidian panel (black + gold trim + the
        /// shared Close) with a centred <paramref name="message"/> and a primary Confirm button
        /// (plus a Cancel when <paramref name="cancelLabel"/> is non-empty). The Close + Cancel
        /// both fire <paramref name="onCancel"/>; Confirm fires <paramref name="onConfirm"/>.
        /// Callers Destroy <see cref="ConfirmModal.canvas"/> to dismiss. No bespoke popup chrome.
        /// </summary>
        public static ConfirmModal BuildConfirmModal(string name, string title, string message,
            string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel,
            ButtonKind confirmKind = ButtonKind.Confirm, int sortingOrder = 32000)
        {
            // WO-1690c — SIZED TO ITS CONTENT, measured off
            // Builds/ui-capture/StartNewConfirm_2670x1200.png (opened at source 2026-09-10):
            //   header inset 6 + header band 37.7 + gap 8      =  51.7
            //   body, 4 wrapped lines at FontFloor(30) x 1.15  = 138.0
            //   gap 8 + face band 112 + face inset 22          = 142.0
            //                                             TOTAL 331.7 ref px
            // PanelFill measured 302.9 ref px at 0.34..0.66, so the modal was ~29 px SHORT of the
            // content it is asked to hold and the body had nowhere to go. 0.32 * (331.7/302.9) =
            // 0.350 -> 0.325..0.675. ⚠ The height alone is not the fix: the body is now bounded
            // (SeatConfirmBodyBetweenBands) so a longer copy truncates VISIBLY instead of painting
            // over the title. Both halves, or this returns the first time the copy grows.
            var modal = BuildObsidianModal(name, title,
                new Vector2(0.28f, 0.325f), new Vector2(0.72f, 0.675f),
                onCancel ?? onConfirm, sortingOrder);

            // WO-1690: this modal is only 0.32 of the canvas, so BuildObsidianPanel's 6%-of-panel
            // header band resolved to 26-28 px and the §1.14 guard shrank the title to 23 px —
            // 7 px under the owner's floor, on the FIRST modal a new player sees, and measured on
            // APK 2026.09.10.363866. Re-seat the band in reference PIXELS (see
            // ElarionUiKit.SeatHeaderBandInPixels for why a fraction cannot be right for both a
            // full-screen panel and this one). Must run BEFORE any other content is added below.
            float headerBandPx = SeatHeaderBandInPixels(modal.chrome);

            var content = modal.chrome.content.transform;

            // A confirm popup's Cancel button IS the close (Close + Cancel share onCancel), and the shared
            // bottom-band Close renders UNDER the Confirm/Cancel buttons (same y 0.10-0.26 band) so only its
            // middle "Clo..se" pokes out behind them — owner F8 2026-07-10 "remove the background close
            // button". Drop the redundant shared Close on every confirm modal.
            if (modal.chrome.close != null) modal.chrome.close.gameObject.SetActive(false);

            var msg = Label(content, message ?? "", 0.40f, 0.82f, ElarionUi.Parchment,
                            ElarionUi.FontBody, TextAlignmentOptions.Center, 0.08f, 0.92f);

            bool hasCancel = !string.IsNullOrEmpty(cancelLabel);
            Button cancel = null;
            if (hasCancel)
                cancel = Button(content, cancelLabel, ButtonKind.Quiet,
                                new Vector2(0.10f, 0.10f), new Vector2(0.48f, 0.26f),
                                () => { if (onCancel != null) onCancel(); });

            var confirm = Button(content, confirmLabel ?? "OK", confirmKind,
                                 hasCancel ? new Vector2(0.52f, 0.10f) : new Vector2(0.32f, 0.10f),
                                 hasCancel ? new Vector2(0.90f, 0.26f) : new Vector2(0.68f, 0.26f),
                                 () => { if (onConfirm != null) onConfirm(); });

            // WO-1690b: the faces were authored as 16% of the panel and resolved 356.9x48.5 ref px
            // on the WO-1688 START NEW confirm -- 63.5 px UNDER MinTouchPx(112), which the touch
            // oracle flagged with "author the band AT the floor" rather than leaving ClampMinTouch
            // to grow them into each other at runtime. Seat them in px.
            SeatConfirmFacesAtTouchFloor(content, cancel, confirm);

            // WO-1690c: LAST, because it reserves against both bands above. The message was built
            // by a bare Label() and had never been fit-protected, so it laid four lines out centred
            // on a band too short for them and spilled over the title AND behind the faces
            // (Builds/ui-capture/StartNewConfirm_2670x1200.png). Seat it strictly between the two
            // bands and bound it.
            SeatConfirmBodyBetweenBands(msg, headerBandPx);

            return new ConfirmModal { canvas = modal.canvas, chrome = modal.chrome, confirm = confirm, cancel = cancel, message = msg };
        }

        // =====================================================================
        // HEADER — section header (crest glyph + gilt underline rule).
        // =====================================================================

        /// <summary>
        /// A section header band: gilt crest glyph + spaced title at FontTitle, with a
        /// soft drop-shadow for depth and a thin gilt rule underneath. Spans the given
        /// x-band [x0,x1] across the top of <paramref name="parent"/>. Returns the
        /// header's title label so callers can retext / recolour it.
        /// </summary>
        public static TextMeshProUGUI Header(Transform parent, string text,
                                             float x0 = 0.06f, float x1 = 0.94f,
                                             float y0 = 0.92f, float y1 = 0.98f)
        {
            // BlinkChrome: skip the drop-shadow + the gilt underline rule (chrome); keep the TITLE (content).
            // WO-714 P9: gate on BlinkChromeActive (flag AND art present), never the raw flag.
            bool chrome = !BlinkChromeActive;
            TextMeshProUGUI shadow = null;
            if (chrome)
            {
                // Soft shadow under the title for legibility on busy scenes.
                shadow = Label(parent, ElarionUi.CrestGlyph + "  " + text, y0, y1,
                               new Color(0f, 0f, 0f, 0.55f), ElarionUi.FontTitle,
                               TextAlignmentOptions.Center, x0, x1, spacing: 6f, bold: true);
                shadow.GetComponent<RectTransform>().anchoredPosition += new Vector2(1.5f, -1.5f);
                // WO-1690: the twin was ALSO named "Label", so the fit guard's PathOf key was
                // identical for both and one modal logged two relaxations under one relaxKey —
                // unattributable. A distinct name makes the pair separable in every trace.
                // ⚠ It also disproves the claim two comments below: MedievalUiSkin.ApplyShell
                // uppercases chrome.title ONLY (:40), AFTER this method runs, so the pair is no
                // longer text-identical and resolved TWO different fonts on device (line factors
                // 1.15 vs 1.26, rects 826x26 vs 826x28). "Identical inputs" has not held since
                // ApplyShell was introduced.
                shadow.gameObject.name = "LabelShadow";
            }

            var title = Label(parent, ElarionUi.CrestGlyph + "  " + text, y0, y1,
                              ElarionUi.Gilt, ElarionUi.FontTitle,
                              TextAlignmentOptions.Center, x0, x1, spacing: 6f, bold: true);

            // DOUBLE-DRAWN TITLE FIX (capture Builds/ui-capture/LoreReadingModal_2340x1080.png):
            // the shadow is a pixel copy of the title drawn 1.5px behind it, but ONLY the title
            // was ever fit-protected (BuildObsidianPanel called FitSingleLine on the returned
            // label). The header band is 6% of the panel (~44 ref px) while FontTitle(88) wraps
            // at a ~110px line box, so the title auto-shrank to ~34px on one line and the
            // unfitted shadow stayed at 88, WRAPPED to two lines, and painted a giant dark ghost
            // out of the band in both directions. Fit BOTH labels here, from identical rect /
            // text / font / spacing inputs, so the pair can never resolve to different sizes
            // again -- and so no caller has to remember to fit the copy it cannot reach.
            if (shadow != null) FitSingleLine(shadow);
            FitSingleLine(title);

            // Gilt rule hugging the header's bottom edge.
            if (chrome) Rule(parent, y0 - 0.008f, x0, x1);
            return title;
        }

        /// <summary>A thin gilt hairline rule at fractional height <paramref name="y"/> across [x0,x1].</summary>
        public static GameObject Rule(Transform parent, float y, float x0, float x1)
        {
            var go = new GameObject("Rule", typeof(Image));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(x0, y); r.anchorMax = new Vector2(x1, y);
            r.offsetMin = new Vector2(0f, -1f); r.offsetMax = new Vector2(0f, 1f);
            var img = go.GetComponent<Image>();
            img.color = BlinkChromeActive ? new Color(0f, 0f, 0f, 0f) : Accent;   // chrome: invisible (WO-714 P9: art-presence gated)
            img.raycastTarget = false;
            return go;
        }

        // =====================================================================
        // BUTTON — canonical button with consistent state feedback.
        // =====================================================================

        /// <summary>
        /// The canonical button: a rounded-glass rect with a centred bold label, the
        /// kind-appropriate fill (Gold CTA / green Confirm / red Danger / neutral
        /// Quiet) and the shared brightness press/hover/disabled feedback. Anchored by
        /// fraction-of-parent. Returns the Button so callers can wire interactable /
        /// extra listeners. Gold uses dark-ink text; the rest use cream parchment.
        /// </summary>
        public static Button Button(Transform parent, string label, ButtonKind kind,
                                    Vector2 anchorMin, Vector2 anchorMax, Action onClick = null)
        {
            // BACK-COMPAT SHIM (§1.2): ButtonKind routes SPRITE-FIRST into the Obsidian 5x4 button
            // family — Gold→(Style1,Yellow), Confirm→(Style2,Green), Danger→(Style1,Red),
            // Quiet→(Style1,Gray) — so EVERY existing panel upgrades in this one place. When the
            // mirrored art is absent the procedural button below renders unchanged (the
            // ff.blinkchrome-OFF / fresh-clone state — dual-state contract, BLINK_OBSIDIAN doc §3.6).
            MapButtonKind(kind, out var obStyle, out var obColor);
            if (RpgUiCatalog.Get(RpgUiCatalog.RoleButton, ObsidianButtonSpriteName(obStyle, obColor)) != null)
                return BuildObsidianButton(parent, label, obStyle, obColor, anchorMin, anchorMax, onClick);

            var go = new GameObject("Btn_" + label, typeof(Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = FillFor(kind);
            ApplyRounded(img);

            var btn = go.GetComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            StyleButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            Color textColor = kind == ButtonKind.Gold ? ElarionUi.Ink : ElarionUi.Parchment;
            var tt = Label(go.transform, label, 0f, 1f, textColor, ElarionUi.FontBody,
                           TextAlignmentOptions.Center, 0f, 1f, spacing: 1f, bold: true);
            tt.raycastTarget = false;
            ClampMinTouch(btn);   // P0 kit touch floor
            return btn;
        }

        /// <summary>Rest fill colour for a button kind (sourced from ElarionUi state colours).</summary>
        public static Color FillFor(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Gold:    return ElarionUi.GoldButton;
                case ButtonKind.Confirm: return ElarionUi.ConfirmFace;   // deep green face — parchment ~5.4:1 (P1)
                case ButtonKind.Danger:  return ElarionUi.DangerFace;    // deep red face — parchment ~6.3:1 (P1)
                default:                 return Glass;   // Quiet
            }
        }

        /// <summary>Shared subtle brightness feedback (no colour shift) for any uGUI Button.</summary>
        public static void StyleButtonColors(Button button)
        {
            if (button == null) return;
            button.transition = Selectable.Transition.ColorTint;
            var cb = button.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            cb.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.selectedColor    = cb.highlightedColor;
            cb.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.12f;   // owner smoothness directive 2026-07-02: state changes ease, never snap
            button.colors = cb;
        }

        // =====================================================================
        // TECH HUD ELEMENTS — direct use of the "Tech hud elements" sprite pack
        // for ornate frames, play-button CTAs, loading bars (cooldowns/stats),
        // profile tabs (sockets), RPG icons, etc. Used for the full combat/vendor/
        // inventory restyle (WO-437/415/405).
        // Sprites are 9-slice friendly in many cases; we treat as sliced + tint.
        // =====================================================================

        /// <summary>
        /// Big thumb-friendly CTA button skinned from Tech pack Play Buttons / Menu Bars.
        /// Primary = gilt ornate frame (Equip, Buy, etc.). Falls back to kit button.
        /// </summary>
        public static Button TechPrimaryButton(Transform parent, string label,
                                               Vector2 anchorMin, Vector2 anchorMax,
                                               Action onClick = null)
        {
            // IMPORTANT: the pack's "Play buttons" sprite has the word PLAY baked INTO the art,
            // so a button using it reads "PLAY" no matter what label is passed (BUY/SELL/EQUIP all
            // showed "PLAY"). It is a literal Play/Start button, not a generic CTA frame. Use the
            // clean procedural gold button so the real label renders. (Swap in a confirmed
            // text-FREE ornate frame later if one exists in the pack.)
            Sprite frame = null;

            var go = new GameObject("TechBtn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            if (frame != null)
            {
                img.sprite = frame;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            else
            {
                img.color = ElarionUi.GoldButton;
                ApplyRounded(img);
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            // Guarantee large mobile target.
            if (rt.rect.height < 56f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, 56f);

            Color ink = ElarionUi.Ink;
            var lbl = Label(go.transform, label, 0.08f, 0.92f, ink, ElarionUi.FontBody,
                            TextAlignmentOptions.Center, 0.05f, 0.95f, bold: true);
            lbl.raycastTarget = false;

            return btn;
        }

        /// <summary>
        /// Ornate gear/weapon/armor socket frame from Tech hud elements pack.
        /// Uses Profile tabs (clean modern frames) and Healing Tabs (more ornate RPG style) for variety.
        /// Weapons get a slightly more "active" frame (e.g. Healing Tabs H1-H3 or Profiletab 2), Armor gets solid profile tabs.
        /// Tints to rarity. Large touch target. Returns the host so caller can parent an icon/child on top.
        /// Falls back to procedural rim if pack sprites not present (fresh clone note: pack may need re-import).
        /// </summary>
        public static GameObject TechGearSocket(Transform parent, string name,
                                                Vector2 anchorMin, Vector2 anchorMax,
                                                Color tint, bool isWeapon = false)
        {
            Sprite frame = null;
            try
            {
                if (isWeapon)
                {
                    // Heavy Tech pack: Healing Tabs (H1-H15.png) for dynamic weapon frames (ornate RPG tabs).
                    // Fallback to Play buttons (button N.png) for action-oriented weapon sockets.
                    frame = Resources.Load<Sprite>("Tech hud elements/Sprites/Healing Tabs/H1");
                    if (frame == null) frame = Resources.Load<Sprite>("Tech hud elements/Sprites/Play buttons/button 3");
                    if (frame == null) frame = Resources.Load<Sprite>("Tech hud elements/Sprites/GreenUielements/Buttons/Button 1");
                }
                else
                {
                    // Heavy Tech pack for armor: Profile tabs P1-P6 (use fill.png or bg.png as sliced frame for solid protective sockets).
                    // P1/fill.png etc provide layered bg/fill for depth; pick fill as main frame.
                    frame = Resources.Load<Sprite>("Tech hud elements/Sprites/Profile tabs/P1/fill.png");
                    if (frame == null) frame = Resources.Load<Sprite>("Tech hud elements/Sprites/Profile tabs/P1/bg.png");
                    if (frame == null) frame = Resources.Load<Sprite>("Tech hud elements/Sprites/Profile tabs/P3/fill.png");
                    if (frame == null) frame = Resources.Load<Sprite>("Tech hud elements/Sprites/GreenUielements/Shield/Shield 1");
                }
            }
            catch (Exception e) { FlowTrace.Warn("ElarionUiKit", $"TechGearSocket frame Resources.Load threw (pack may be partial on fresh clone — degrading to committed frame): {e.GetType().Name}: {e.Message}"); }

            // Clean-build fallback: the "Tech hud elements" pack is gitignored — only the committed
            // RpgUi slice ships. Degrade to the committed grid-plate frame so the socket keeps a
            // themed frame (instead of dropping to the bare procedural rim) on a fresh checkout.
            if (frame == null) frame = RpgUiCatalog.Get(RpgUiCatalog.RolePanel, RpgUiCatalog.PanelGrid);

            var host = AddImage(parent, name, anchorMin, anchorMax, new Color(tint.r, tint.g, tint.b, 0.18f));
            var img = host.GetComponent<Image>();
            if (frame != null)
            {
                img.sprite = frame;
                img.type = Image.Type.Sliced;
                img.color = new Color(tint.r, tint.g, tint.b, 0.95f);
            }
            else
            {
                AddInnerRim(host, new Color(tint.r, tint.g, tint.b, 0.6f));
            }
            return host;
        }

        // =====================================================================
        // SLOT — universal rarity-framed item / gear slot.
        // =====================================================================

        /// <summary>
        /// A universal rarity-framed slot: the frame strength ESCALATES by rarity
        /// (common = quiet hairline ... legendary = strong gilt halo), a recessed cell
        /// sits inset inside it, with a rarity gem pip top-left and toggleable
        /// equipped-check + lock overlay children (start hidden; flip via
        /// <see cref="SetSlotEquipped"/> / <see cref="SetSlotLocked"/>). Returns the
        /// slot's CELL GameObject so callers add their own icon / count / button.
        /// </summary>
        public static GameObject Slot(Transform parent, int rarityIndex,
                                      Vector2 anchorMin, Vector2 anchorMax, bool dim = false)
        {
            Color rc = RarityColor(rarityIndex);
            float strength = RarityFrameStrength(rarityIndex);

            var frame = AddImage(parent, "SlotFrame", anchorMin, anchorMax,
                                 new Color(rc.r, rc.g, rc.b, dim ? strength * 0.4f : strength));
            frame.GetComponent<Image>().raycastTarget = false;

            var cell = AddImage(frame.transform, "Cell", new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f),
                                dim ? new Color(Cell.r, Cell.g, Cell.b, 0.55f) : Cell);

            // Rarity gem pip — small tinted square top-left, reads the tier at a glance.
            var gem = AddImage(cell.transform, "Gem", new Vector2(0.06f, 0.78f), new Vector2(0.22f, 0.94f),
                               new Color(rc.r, rc.g, rc.b, 0.95f));
            gem.GetComponent<Image>().raycastTarget = false;

            // Equipped check chip (hidden by default).
            var check = AddImage(cell.transform, "EquippedCheck", new Vector2(0.62f, 0.78f), new Vector2(0.94f, 0.94f),
                                 new Color(ElarionUi.Affordable.r, ElarionUi.Affordable.g, ElarionUi.Affordable.b, 0.95f));
            check.GetComponent<Image>().raycastTarget = false;
            var checkLbl = Label(check.transform, "v", 0f, 1f, ElarionUi.Ink, ElarionUi.FontLabel,
                                 TextAlignmentOptions.Center, 0f, 1f, bold: true);
            checkLbl.raycastTarget = false;
            check.SetActive(false);

            // Lock overlay (veil + lock chip), hidden by default.
            var lockGo = AddImage(cell.transform, "LockOverlay", Vector2.zero, Vector2.one,
                                  new Color(0f, 0f, 0f, 0.55f), rounded: false);
            lockGo.GetComponent<Image>().raycastTarget = false;
            var lockChip = Label(lockGo.transform, "\U0001F512", 0.30f, 0.70f, ElarionUi.Gilt, ElarionUi.FontHead,
                                 TextAlignmentOptions.Center, 0f, 1f, bold: true);
            lockChip.raycastTarget = false;
            lockGo.SetActive(false);

            return cell;
        }

        /// <summary>Toggle a Slot's equipped-check overlay (built by <see cref="Slot"/>).</summary>
        public static void SetSlotEquipped(GameObject slotCell, bool equipped)
        {
            if (slotCell == null) return;
            var t = slotCell.transform.Find("EquippedCheck");
            if (t != null) t.gameObject.SetActive(equipped);
        }

        /// <summary>Toggle a Slot's lock overlay (built by <see cref="Slot"/>).</summary>
        public static void SetSlotLocked(GameObject slotCell, bool locked)
        {
            if (slotCell == null) return;
            var t = slotCell.transform.Find("LockOverlay");
            if (t != null) t.gameObject.SetActive(locked);
        }

        // =====================================================================
        // CARD — polished item card (icon well + rarity frame + name).
        // =====================================================================

        /// <summary>
        /// A polished item card: a rarity-framed glass tile with a recessed round icon
        /// well (the glyph/icon sits in it), a rarity gem pip, and a rarity-coloured
        /// name along the bottom band. Returns the card's CELL GameObject (which has a
        /// Button) so callers wire the tap + drop in the icon glyph. The icon well is
        /// found at child "IconWell" for callers that want to swap a sprite in.
        /// </summary>
        public static GameObject Card(Transform parent, int rarityIndex, string name, string icon,
                                      Vector2 anchorMin, Vector2 anchorMax, Action onTap = null)
        {
            Color rc = RarityColor(rarityIndex);
            float strength = RarityFrameStrength(rarityIndex);

            var frame = AddImage(parent, "CardFrame", anchorMin, anchorMax,
                                 new Color(rc.r, rc.g, rc.b, strength));
            frame.GetComponent<Image>().raycastTarget = false;

            var cell = new GameObject("Card", typeof(Image), typeof(Button));
            cell.transform.SetParent(frame.transform, false);
            var crt = cell.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.04f, 0.04f); crt.anchorMax = new Vector2(0.96f, 0.96f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var img = cell.GetComponent<Image>();
            img.color = Cell;
            ApplyRounded(img);
            var btn = cell.GetComponent<Button>();
            btn.targetGraphic = img;
            StyleButtonColors(btn);
            if (onTap != null) btn.onClick.AddListener(() => onTap());

            // Recessed icon well (decorative — non-raycast so the whole card is one tap target).
            var well = AddImage(cell.transform, "IconWell", new Vector2(0.26f, 0.40f), new Vector2(0.74f, 0.92f),
                                new Color(0f, 0f, 0f, 0.30f));
            well.GetComponent<Image>().raycastTarget = false;
            var ic = Label(well.transform, string.IsNullOrEmpty(icon) ? "?" : icon, 0f, 1f,
                           ElarionUi.Parchment, ElarionUi.FontTitle + 2, TextAlignmentOptions.Center, 0.05f, 0.95f);
            ic.raycastTarget = false;

            // Rarity gem pip.
            var gem = AddImage(cell.transform, "Gem", new Vector2(0.06f, 0.80f), new Vector2(0.20f, 0.94f),
                               new Color(rc.r, rc.g, rc.b, 0.95f));
            gem.GetComponent<Image>().raycastTarget = false;

            // Name in the rarity colour along the bottom.
            var nm = Label(cell.transform, name ?? "", 0.06f, 0.36f, rc, ElarionUi.FontMicro,
                           TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            nm.raycastTarget = false;
            return cell;
        }

        // =====================================================================
        // LABEL — shared text builder (fraction-anchored).
        // =====================================================================

        /// <summary>
        /// A TextMeshProUGUI label anchored by fraction-of-parent (x0,y0)-(x1,y1).
        /// The shared text primitive every surface used a private copy of. Returns the
        /// label so callers can mutate .text later. Raycast-off by default (decorative).
        /// </summary>
        public static TextMeshProUGUI Label(Transform parent, string text, float y0, float y1,
            Color color, int size, TextAlignmentOptions align,
            float x0 = 0.03f, float x1 = 0.97f, float spacing = 0f, bool bold = false)
        {
            var go = new GameObject("Label", typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var t = go.GetComponent<TextMeshProUGUI>();
            EnsureFont(t);          // assign a font BEFORE .text so first generation can't NRE
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = spacing;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }

        // Font-safe construction. A code-built TextMeshProUGUI whose font/material is still
        // unresolved at its first GenerateTextMesh() throws an NRE deep inside TMP — the
        // 2026-06-19 chaos-fleet caught exactly this (TextMeshProUGUI.GenerateTextMesh, 5/8
        // runs) on force-built panels (Canvas.ForceUpdateCanvases makes TMP generate the same
        // frame the row is created, before TMP_Settings.defaultFontAsset has been leaned on).
        // We never assigned a font here, so EVERY label was one timing edge from this NRE.
        // Assign the tracked locale font explicitly before any .text set; retain the package
        // default and legacy LiberationSans only as compatibility fallbacks.
        private static TMPro.TMP_FontAsset _fontCache;
        /// <summary>Assign a resolved TMP font to <paramref name="t"/> if it has none, so its first
        /// GenerateTextMesh() cannot NRE. Call this BEFORE setting .text on any code-built TMP that
        /// is constructed outside <see cref="Label"/> (e.g. direct <c>new GameObject(typeof(TextMeshProUGUI))</c>).</summary>
        public static void EnsureFont(TextMeshProUGUI t)
        {
            if (t == null || t.font != null) return;
            var f = ResolveDefaultFont();
            if (f != null) t.font = f;
            else DeNelle.Core.Diagnostics.FlowTrace.Warn("UI",
                "ElarionUiKit.Label: tracked locale font and compatibility TMP defaults are absent — " +
                "assigning none to avoid an NRE; text may not render until a font ships.");
        }

        /// <summary>THE default-chain font. The committed static locale atlas is authoritative;
        /// package-local TMP defaults remain compatibility fallbacks only. Resolved once and cached. Exposed because the
        /// numeral-legibility gate (ElarionUiKitObsidian) must be able to judge the font a rejected
        /// ROLE will actually fall back to — one resolution path, so the gate can never report on a
        /// font different from the one that draws. Null only when neither is present.</summary>
        public static TMPro.TMP_FontAsset ResolveDefaultFont()
        {
            if (_fontCache == null)
            {
                _fontCache = UnityEngine.Resources.Load<TMPro.TMP_FontAsset>(
                                 "Localization/Fonts/ElarionLocaleFallback")
                          ?? TMPro.TMP_Settings.defaultFontAsset
                          ?? UnityEngine.Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            }
            return _fontCache;
        }

        // =====================================================================
        // RARITY — ONE canonical map for the whole game.
        // =====================================================================
        // Index ladder: 0 common, 1 uncommon, 2 rare, 3 epic, 4 legendary. The
        // string overloads accept the catalog's rarity words. Centralises what the
        // inventory / HUD each hardcoded so every surface tiers items identically.

        /// <summary>Named rarity tiers (index == the int the kit's overloads accept).</summary>
        public enum Rarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }

        /// <summary>The canonical rarity colour for an index (0..4, clamped).</summary>
        public static Color RarityColor(int rarityIndex)
        {
            switch (Mathf.Clamp(rarityIndex, 0, 4))
            {
                case 1:  return new Color(0.46f, 0.74f, 0.42f, 1f);   // uncommon green
                case 2:  return new Color(0.32f, 0.58f, 0.92f, 1f);   // rare blue
                case 3:  return new Color(0.66f, 0.42f, 0.86f, 1f);   // epic purple
                case 4:  return new Color(0.92f, 0.62f, 0.24f, 1f);   // legendary orange
                default: return new Color(0.80f, 0.80f, 0.78f, 1f);   // common grey
            }
        }

        /// <summary>The canonical rarity colour for a catalog rarity word (case-insensitive).</summary>
        public static Color RarityColor(string rarity) => RarityColor(RarityIndex(rarity));

        /// <summary>A small font-safe ASCII glyph per rarity tier (. - = + *).</summary>
        public static string RarityGlyph(int rarityIndex)
        {
            switch (Mathf.Clamp(rarityIndex, 0, 4))
            {
                case 4:  return "*";   // legendary
                case 3:  return "+";   // epic
                case 2:  return "=";   // rare
                case 1:  return "-";   // uncommon
                default: return ".";   // common
            }
        }

        /// <summary>Glyph overload for a catalog rarity word.</summary>
        public static string RarityGlyph(string rarity) => RarityGlyph(RarityIndex(rarity));

        /// <summary>
        /// How loud the rarity frame glows (0..1): common a quiet hairline, legendary a
        /// strong gilt halo — so tiers feel visibly escalating. Used by Slot / Card.
        /// </summary>
        public static float RarityFrameStrength(int rarityIndex)
        {
            switch (Mathf.Clamp(rarityIndex, 0, 4))
            {
                case 4:  return 0.95f;
                case 3:  return 0.85f;
                case 2:  return 0.72f;
                case 1:  return 0.58f;
                default: return 0.40f;
            }
        }

        /// <summary>Frame-strength overload for a catalog rarity word.</summary>
        public static float RarityFrameStrength(string rarity) => RarityFrameStrength(RarityIndex(rarity));

        /// <summary>Map a catalog rarity word to the canonical index (0..4); unknown = common.</summary>
        public static int RarityIndex(string rarity)
        {
            switch ((rarity ?? "common").ToLowerInvariant())
            {
                case "legendary": return 4;
                case "epic":      return 3;
                case "rare":      return 2;
                case "uncommon":  return 1;
                default:          return 0;
            }
        }

        // =====================================================================
        // BAR — the town-HUD vitals/progress bar (Well track + filled rounded
        // fill + optional inline value), ported from VillageHudController so the
        // combat HUD / store / inventory render the IDENTICAL ornate bar.
        // =====================================================================

        /// <summary>Which vital/progress a bar shows — drives the fill colour + pack frame/fill ids.</summary>
        public enum BarKind { Hp, Mp, Xp, Atb, Castle }

        /// <summary>Handle returned by <see cref="Bar"/> / <see cref="BuildObsidianBar"/>: the track
        /// rect, the fillAmount-driven fill Image, the optional inline value label, and the ornate
        /// frame overlay. Drive it via <see cref="SetValue"/> — THE fill-binding contract (§1.1):
        /// the fill sprite is ALWAYS non-null, type Filled/Horizontal/Left, the ONLY width mutation
        /// is <c>fillAmount = cur/max</c> (never sizeDelta, never anchors), and bar + value label are
        /// written ATOMICALLY by the same call so they can never disagree.</summary>
        public sealed class BarHandle
        {
            /// <summary>The recessed Well track the fill rides in.</summary>
            public RectTransform track;
            /// <summary>The Image.Type.Filled fill — drive via <see cref="SetValue"/> (or fillAmount directly).</summary>
            public Image fill;
            /// <summary>Optional inline value label centred over the fill (null if not requested).</summary>
            public TMP_Text valueLabel;
            /// <summary>The ornate frame overlay Image (non-raycast, drawn above the fill); may be null.</summary>
            public Image frame;

            // Last shown ints — the label string is only rebuilt when a visible digit changes
            // (mobile lens: no per-frame alloc when swept by a tween/producer).
            private int _shownCur = int.MinValue, _shownMax = int.MinValue;

            /// <summary>Write bar + label atomically, easing the fill to cur/max (0.12s, owner
            /// smoothness directive). THE one sanctioned update path (§1.1).</summary>
            public void SetValue(float cur, float max) { Apply(cur, max, animate: true); }

            /// <summary>Write bar + label atomically with NO easing (per-frame sweeps / first paint).</summary>
            public void SetImmediate(float cur, float max) { Apply(cur, max, animate: false); }

            /// <summary>Blank the value label + drop the digit cache (a TOTAL Clear() shows an EMPTY
            /// value, not "0/1" — §1.10; the next SetValue rewrites it whatever the numbers).</summary>
            public void ResetLabel()
            {
                _shownCur = int.MinValue; _shownMax = int.MinValue;
                if (valueLabel != null) valueLabel.text = "";
            }

            private void Apply(float cur, float max, bool animate)
            {
                if (fill == null) return;
                max = Mathf.Max(1f, max);                       // §1.1: cur / Mathf.Max(1, max)
                cur = Mathf.Clamp(cur, 0f, max);
                float target = cur / max;
                if (animate && Application.isPlaying) UiKitTween.FillTo(fill, target, 0.12f);
                else { UiKitTween.CancelFill(fill); fill.fillAmount = target; }

                if (valueLabel != null)
                {
                    int ci = Mathf.CeilToInt(cur), mi = Mathf.CeilToInt(max);
                    if (ci != _shownCur || mi != _shownMax)
                    {
                        _shownCur = ci; _shownMax = mi;
                        valueLabel.text = ci + "/" + mi;        // same call as the fill — atomic
                    }
                }
            }
        }

        /// <summary>
        /// A complete vitals/progress bar: a recessed <see cref="Well"/> track with a
        /// rounded Image.Type.Filled fill (horizontal, origin-left, fillAmount=1) in the
        /// kind's colour, dressed sprite-FIRST with the RPG pack's ornate frame + colored
        /// fill (procedural rounded fallback when the pack is absent), and — when
        /// <paramref name="withValue"/> — a centred cream value label with a dark halo.
        /// Anchored by fraction-of-parent. Returns a <see cref="BarHandle"/> so callers
        /// drive fill.fillAmount + valueLabel.text without re-implementing the recipe.
        /// </summary>
        public static BarHandle Bar(Transform parent, BarKind kind,
                                    Vector2 anchorMin, Vector2 anchorMax, bool withValue = false)
        {
            var trackGo = Well(parent, anchorMin, anchorMax);
            var track = trackGo.GetComponent<RectTransform>();

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(trackGo.transform, false);
            var fr = fillGo.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(1.5f, 1.5f); fr.offsetMax = new Vector2(-1.5f, -1.5f);
            var fill = fillGo.GetComponent<Image>();
            fill.color = BarFillColor(kind);
            ApplyRounded(fill);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;

            DressBar(track, fill, kind);

            TMP_Text valueLabel = null;
            if (withValue)
            {
                valueLabel = Label(trackGo.transform, "", 0f, 1f, ElarionUi.Parchment,
                                   ElarionUi.FontLabel, TextAlignmentOptions.Center, 0f, 1f, bold: true);
                valueLabel.outlineColor = new Color32(10, 10, 14, 200);
                valueLabel.outlineWidth = 0.14f;
                valueLabel.raycastTarget = false;
            }

            return new BarHandle { track = track, fill = fill, valueLabel = valueLabel };
        }

        /// <summary>The canonical fill colour for a bar kind (sourced from ElarionUi).</summary>
        public static Color BarFillColor(BarKind kind)
        {
            switch (kind)
            {
                case BarKind.Hp:     return ElarionUi.HpRed;
                case BarKind.Mp:     return ElarionUi.ManaBlue;
                case BarKind.Xp:     return new Color(1f, 0.85f, 0.15f, 1f); // yellow XP strip
                case BarKind.Atb:    return ElarionUi.Aether;
                case BarKind.Castle: return ElarionUi.Gold;
                default:             return ElarionUi.Gold;
            }
        }

        /// <summary>
        /// Dress an existing bar (track + its Image.Type.Filled fill) with the RPG pack's
        /// ornate frame + colored fill, sprite-FIRST — the port of
        /// VillageHudController.TryDressBar. The fill keeps its fillAmount binding; we
        /// swap its sprite to the pack's colored fill and drop the gilded frame over the
        /// track as a NON-RAYCAST overlay rendered last (the frame art is hollow so the
        /// fill shows through). No-op (procedural look preserved) when the pack is absent.
        /// Returns true when the pack art dressed the bar.
        /// </summary>
        public static bool DressBar(RectTransform track, Image fill, BarKind kind)
        {
            if (track == null) return false;

            string frameName, fillName;
            bool tintFill;
            Color fillTint = BarFillColor(kind);
            BarPackIds(kind, out frameName, out fillName, out tintFill);

            var frameSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleBars, frameName);
            var fillSprite  = string.IsNullOrEmpty(fillName)
                ? null : RpgUiCatalog.Get(RpgUiCatalog.RoleBars, fillName);
            if (frameSprite == null && fillSprite == null) return false;

            // Swap the fill sprite (keeps Image.Type.Filled + fillAmount binding).
            if (fill != null && fillSprite != null)
            {
                fill.sprite = fillSprite;
                fill.color = tintFill ? fillTint : Color.white; // pack art's own colours unless tinted
            }

            // Drop the gilded frame over the track as a decorative, non-raycast overlay
            // rendered LAST so it sits above the fill; it stretches to the bar rect.
            if (frameSprite != null)
            {
                var fr = AddImage(track, "PackFrame", Vector2.zero, Vector2.one, Color.white, rounded: false)
                            .GetComponent<RectTransform>();
                fr.offsetMin = new Vector2(-10f, -8f);
                fr.offsetMax = new Vector2(10f, 8f); // ornate ends slightly overhang the track
                var fimg = fr.GetComponent<Image>();
                fimg.sprite = frameSprite;
                fimg.type = Image.Type.Simple;
                fimg.color = Color.white;
                fimg.raycastTarget = false;
                fr.SetAsLastSibling();
            }
            return true;
        }

        /// <summary>Map a bar kind to its RpgUiCatalog "bars" frame/fill ids + whether to tint the fill.</summary>
        private static void BarPackIds(BarKind kind, out string frameName, out string fillName, out bool tintFill)
        {
            switch (kind)
            {
                case BarKind.Hp:
                    frameName = RpgUiCatalog.BarFrameRed; fillName = RpgUiCatalog.BarFillRed; tintFill = false; break;
                case BarKind.Mp:
                    // Pack MP fill is green-glow; tint it blue (mana has no dynamic colour change).
                    frameName = RpgUiCatalog.BarFrameBlue; fillName = RpgUiCatalog.BarFillBlue; tintFill = true; break;
                case BarKind.Atb:
                    frameName = RpgUiCatalog.BarFrameBlue; fillName = RpgUiCatalog.BarFillBlue; tintFill = true; break;
                case BarKind.Xp:
                case BarKind.Castle:
                default:
                    // Generic gold/green socket frame; tint the fill to the kind colour.
                    frameName = RpgUiCatalog.BarFrameGreen; fillName = RpgUiCatalog.BarFillGreen; tintFill = true; break;
            }
        }

        // =====================================================================
        // PORTRAIT — circular class disc + gilt ring (ported from BattleHudUgui
        // CircleSprite/RingSprite) so combat + town frame portraits IDENTICALLY.
        // =====================================================================

        /// <summary>Handle returned by <see cref="Portrait"/>: the disc Image + its ring frame.</summary>
        public sealed class PortraitHandle
        {
            /// <summary>The circular portrait disc (assign .sprite to show class art).</summary>
            public Image image;
            /// <summary>The hollow ring frame around the disc (gilt when active, gold when not).</summary>
            public Image ring;
        }

        /// <summary>
        /// A circular portrait: a disc Image (shows <paramref name="sprite"/> when present,
        /// else a warm tan placeholder disc) inside a hollow gilt ring frame. The ring is
        /// brighter gilt when <paramref name="active"/>, soft gold otherwise. Fills its
        /// parent rect (size the parent). Returns a <see cref="PortraitHandle"/>.
        /// </summary>
        public static PortraitHandle Portrait(Transform parent, Sprite sprite, bool active = false)
        {
            var discGo = new GameObject("Portrait", typeof(Image));
            discGo.transform.SetParent(parent, false);
            var dr = discGo.GetComponent<RectTransform>();
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
            dr.offsetMin = Vector2.zero; dr.offsetMax = Vector2.zero;
            var disc = discGo.GetComponent<Image>();
            Image maskedArt = null;
            if (sprite != null)
            {
                // F8-32 hardening: non-square art (e.g. the 2:3 hero-select card) must never
                // letterbox as a raw rectangle inside the painted oval. When the aspect is off
                // by >10%, render it aspect-FILL under a circular Mask (CircleSprite as the
                // mask graphic) so any card/tall art reads as a round crop. Square sprites
                // keep the original direct path.
                float w = sprite.rect.width, hgt = sprite.rect.height;
                float aspect = hgt > 0f ? w / hgt : 1f;
                if (Mathf.Abs(aspect - 1f) > 0.10f)
                {
                    disc.sprite = CircleSprite;          // the round crop shape
                    disc.color = Color.white;
                    var mask = discGo.AddComponent<Mask>();
                    mask.showMaskGraphic = false;

                    var artGo = new GameObject("PortraitArt", typeof(Image));
                    artGo.transform.SetParent(discGo.transform, false);
                    var ar = artGo.GetComponent<RectTransform>();
                    ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one;
                    ar.offsetMin = Vector2.zero; ar.offsetMax = Vector2.zero;
                    maskedArt = artGo.GetComponent<Image>();
                    maskedArt.sprite = sprite;
                    maskedArt.color = Color.white;
                    maskedArt.raycastTarget = false;
                    var fit = artGo.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; // aspect-FILL the circle
                    fit.aspectRatio = aspect;
                    FlowTrace.Once("UiKit", "portrait-nonsquare/" + sprite.name,
                        "portrait " + sprite.name + " non-square (" + w + "x" + hgt + ") — circle-masked");
                }
                else { disc.sprite = sprite; disc.color = Color.white; disc.preserveAspect = true; }
            }
            else { disc.sprite = CircleSprite; disc.color = PortraitPlaceholder; }
            disc.raycastTarget = false;

            var ringGo = new GameObject("Ring", typeof(Image));
            ringGo.transform.SetParent(parent, false);
            var rr = ringGo.GetComponent<RectTransform>();
            rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one;
            rr.offsetMin = Vector2.zero; rr.offsetMax = Vector2.zero;
            var ring = ringGo.GetComponent<Image>();
            ring.sprite = CanonicalMedallionFrameSprite != null
                ? CanonicalMedallionFrameSprite : RingSprite;
            ring.type = Image.Type.Simple;
            ring.preserveAspect = true;
            // Preserve the authored black-iron/gold pixels. Active state is expressed by the
            // surrounding control, never by recolouring this shared identity frame.
            ring.color = Color.white;
            ring.raycastTarget = false;
            ringGo.transform.SetAsLastSibling();

            // Circle-masked path: the handle's image = the art Image (callers assign .sprite
            // there); the disc stays the mask shape.
            return new PortraitHandle { image = maskedArt != null ? maskedArt : disc, ring = ring };
        }

        /// <summary>Warm tan placeholder fill for a portrait disc with no class art yet (resolves from UiStyle.Theme).</summary>
        public static Color PortraitPlaceholder => UiStyle.Theme.PortraitPlaceholder;

        /// <summary>
        /// ONE shared class-portrait resolver (consolidates BattleHudUgui.PortraitFor +
        /// VillageHudController.PortraitNameForClass). Loads
        /// Resources/HudIcons/&lt;Class&gt;/&lt;class&gt;; FIXES the Wizard "wiard" typo by
        /// trying "wizard" then "wiard"; and degrades gracefully when the Healer art is
        /// absent (falls back to the RPG-pack heart icon, then null). Accepts the canonical
        /// class words: Knight / Ranger / Wizard|Mage / Healer|Cleric (case-insensitive).
        /// Returns null when nothing resolves (callers keep their placeholder disc).
        /// </summary>
        public static Sprite PortraitForClass(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return null;
            switch (cls.Trim().ToLowerInvariant())
            {
                case "knight": return Resources.Load<Sprite>("HudIcons/Knight/knight");
                case "ranger": return Resources.Load<Sprite>("HudIcons/Ranger/ranger");
                case "mage":
                case "wizard":
                {
                    var sp = Resources.Load<Sprite>("HudIcons/Wizard/wizard");
                    if (sp == null) sp = Resources.Load<Sprite>("HudIcons/Wizard/wiard"); // staged-art typo
                    return sp;
                }
                case "cleric":
                case "healer":
                {
                    var sp = Resources.Load<Sprite>("HudIcons/Healer/healer");
                    if (sp == null) sp = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconHeart); // graceful fallback
                    return sp; // may be null — caller keeps its placeholder disc
                }
                default: return null;
            }
        }

        // =====================================================================
        // PARTY FRAME ROW — the town-HUD party-row recipe (portrait + name banner
        // + HP/MP bars on player_frame_bg / player_hp_fill), extracted so combat
        // and town render the IDENTICAL frame (ported from BuildPartyFrames).
        // =====================================================================

        /// <summary>Handle returned by <see cref="PartyFrameRow"/>: the row's live pieces.</summary>
        public sealed class PartyRowHandle
        {
            /// <summary>The row's root rect (parent it / position it).</summary>
            public RectTransform root;
            /// <summary>The class portrait disc Image (assign .sprite via <see cref="PortraitForClass"/>).</summary>
            public Image portrait;
            /// <summary>The name banner label (gold ink).</summary>
            public TMP_Text nameLabel;
            /// <summary>The HP fill (Image.Type.Filled) — drive fillAmount.</summary>
            public Image hpFill;
            /// <summary>The MP fill (Image.Type.Filled) — drive fillAmount.</summary>
            public Image mpFill;
            /// <summary>Optional HP value label centred on the HP bar.</summary>
            public TMP_Text hpText;
        }

        /// <summary>
        /// The town-HUD party-row: a dark-stone frame (player_frame_bg art, sprite-FIRST
        /// with a dark-panel fallback) carrying a circular class portrait, a gold name
        /// banner, and stacked HP (red) + MP (blue) bars dressed with the player_hp_fill /
        /// player_mp_fill art. Fills its parent rect (size + position the parent). Returns
        /// a <see cref="PartyRowHandle"/> so callers drive the portrait/name/fills without
        /// re-implementing the frame. Identical recipe for combat + town.
        /// </summary>
        public static PartyRowHandle PartyFrameRow(Transform parent, string initialName = "Hero")
        {
            var frameSprite = Resources.Load<Sprite>("HudIcons/player_frame_bg");
            var hpSprite    = Resources.Load<Sprite>("HudIcons/player_hp_fill");
            var mpSprite    = Resources.Load<Sprite>("HudIcons/player_mp_fill");

            var rootGo = new GameObject("PartyRow", typeof(Image));
            rootGo.transform.SetParent(parent, false);
            var root = rootGo.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
            var frImg = rootGo.GetComponent<Image>();
            if (frameSprite != null) { frImg.sprite = frameSprite; frImg.color = Color.white; }
            else { frImg.color = new Color(0.10f, 0.09f, 0.11f, 0.96f); ApplyRounded(frImg); }
            frImg.raycastTarget = false;

            // Portrait (class disc) in the circle on the left.
            var portWrap = AddImage(rootGo.transform, "PortraitWrap",
                                    new Vector2(0.035f, 0.12f), new Vector2(0.26f, 0.94f),
                                    new Color(0f, 0f, 0f, 0f), rounded: false);
            portWrap.GetComponent<Image>().raycastTarget = false;
            var portrait = Portrait(portWrap.transform, null, active: true);

            // Name banner (upper-right) — gold ink, auto-sizing.
            var nameLabel = Label(rootGo.transform, initialName, 0.52f, 0.95f,
                                  new Color(0.95f, 0.88f, 0.62f), ElarionUi.FontHead,
                                  TextAlignmentOptions.Left, 0.31f, 0.97f, bold: true);
            nameLabel.enableAutoSizing = true; nameLabel.fontSizeMin = 30f; nameLabel.fontSizeMax = 64f;  // mobile floor (was 9–15, sub-legible)

            // HP bar (red, mid-right).
            var hp = Bar(rootGo.transform, BarKind.Hp,
                         new Vector2(0.31f, 0.30f), new Vector2(0.985f, 0.50f), withValue: true);
            if (hpSprite != null) { hp.fill.sprite = hpSprite; hp.fill.color = Color.white; }

            // MP bar (blue, lower-right).
            var mp = Bar(rootGo.transform, BarKind.Mp,
                         new Vector2(0.31f, 0.07f), new Vector2(0.985f, 0.27f), withValue: false);
            if (mpSprite != null) { mp.fill.sprite = mpSprite; mp.fill.color = Color.white; }

            return new PartyRowHandle
            {
                root = root,
                portrait = portrait.image,
                nameLabel = nameLabel,
                hpFill = hp.fill,
                mpFill = mp.fill,
                hpText = hp.valueLabel
            };
        }

        // =====================================================================
        // PANEL / BUTTON — pack-frame-over-glass variants (RpgUiCatalog
        // RolePanel / RoleButton), sprite-FIRST with the procedural fallback.
        // =====================================================================

        /// <summary>
        /// A <see cref="Panel"/> dressed sprite-FIRST with the RPG pack's ornate panel
        /// frame (RolePanel) over the dark glass: when the pack art is present the panel's
        /// Image shows the framed plate; when absent it is the procedural glass+rim panel.
        /// Anchored by fraction-of-parent. Returns the panel GameObject.
        /// </summary>
        public static GameObject PanelFramed(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                             bool deep = false, string packSpriteName = null)
        {
            var p = Panel(parent, anchorMin, anchorMax, deep: deep, innerRim: true);
            // Only dress with a pack panel sprite when the caller EXPLICITLY names one (WO-438 maps a
            // specific frame per screen). A null/empty name must stay "plain dark-glass" — some screens
            // (e.g. the store) deliberately keep dark glass so their rows read clearly. Never default via
            // RpgUiCatalog.Get(role, null): that returns the FIRST sprite in the role (a gold grid), which
            // is what made the store read as an empty gold grid. Sprite-first with the procedural fallback.
            if (!string.IsNullOrEmpty(packSpriteName))
            {
                var packSprite = RpgUiCatalog.Get(RpgUiCatalog.RolePanel, packSpriteName);
                if (packSprite != null && p.GetComponent<Image>() is Image img)
                {
                    img.sprite = packSprite;
                    img.type = Image.Type.Sliced;
                    img.color = Color.white;
                }
            }
            return p;
        }

        /// <summary>
        /// A <see cref="Button"/> dressed sprite-FIRST with the RPG pack's button frame
        /// (RoleButton) over the kind fill: pack art when present, procedural rounded glass
        /// otherwise. Same state feedback + label rules as <see cref="Button"/>. Returns
        /// the Button so callers wire interactable / extra listeners.
        /// </summary>
        public static Button ButtonPack(Transform parent, string label, ButtonKind kind,
                                        Vector2 anchorMin, Vector2 anchorMax, Action onClick = null,
                                        string packSpriteName = null)
        {
            // The procedural gold button already carries the label clearly.
            var btn = Button(parent, label, kind, anchorMin, anchorMax, onClick);
            // IMPORTANT: the default RoleButton sprite (ButtonGold = "Play buttons/button 3") and the
            // rest of the pack's button art live in the Play-buttons folder with the word PLAY baked
            // INTO the graphic — overlaying it on a LABELED button makes BUY/SELL/EQUIP all read
            // "PLAY". So we only overlay a pack sprite when the caller EXPLICITLY supplies one (and is
            // responsible for it being text-free); otherwise the clean procedural gold button + label
            // is used. This is what fixes "PLAY everywhere" on the store tabs / inventory / equip.
            if (!string.IsNullOrEmpty(packSpriteName) && btn != null)
            {
                var packSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleButton, packSpriteName);
                if (packSprite != null && btn.targetGraphic is Image img)
                {
                    img.sprite = packSprite;
                    img.type = Image.Type.Sliced;
                    img.color = Color.white;
                }
            }
            return btn;
        }

        // =====================================================================
        // PRIMITIVES — shared image / rim builders + the rounded sprite.
        // =====================================================================

        /// <summary>
        /// A fraction-anchored Image. rounded=true applies the 9-sliced rounded sprite
        /// (flat tinted quad if the sprite build failed under WebGL). The base building
        /// block for panels, wells, chips, frames.
        /// </summary>
        public static GameObject AddImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Color color, bool rounded = true)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            if (rounded) ApplyRounded(img);
            return go;
        }

        /// <summary>
        /// A fraction-anchored <see cref="RawImage"/> over a RAW TEXTURE — the sibling of
        /// <see cref="AddImage"/> for the one surface a sprite cannot serve: one whose UV RECT is
        /// SCROLLED or TILED at runtime.
        /// <para>WHY THIS EXISTS AS A KIT PRIMITIVE (and is not a caller's business): <c>uvRect</c>
        /// is declared on RawImage and on nothing else. An <see cref="Image"/> can only scroll by
        /// swapping sprites or by driving a per-frame material/Color, which is precisely the
        /// "4 ms a frame on a Seeker" shape decorative motion must avoid. So a drifting gradient
        /// ground has no expression through AddImage — but it is still PRESENTATION, and the law
        /// (ARCHITECTURE_PRINCIPLES §2; UiObsidianConformanceRegression) is that presentation is
        /// constructed HERE, in the one file sanctioned to touch raw uGUI, never hand-rolled in a
        /// feature module. The first caller was The Night Market's aurora (WO-1050 Lane G).</para>
        /// <para>Returns the RawImage itself rather than the GameObject that AddImage returns: the
        /// entire reason to reach for this primitive is to animate <c>uvRect</c>, so handing back
        /// the typed component removes a GetComponent every caller would otherwise have to make
        /// (and could get wrong).</para>
        /// <para><paramref name="raycastTarget"/> defaults to FALSE. A textured surface built by
        /// this primitive is decoration sitting over content; swallowing the tap on the card
        /// underneath is the default bug, so the safe value is the default one.</para>
        /// </summary>
        public static RawImage AddRawImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Texture texture, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var img = go.AddComponent<RawImage>();
            img.texture = texture;
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        /// <summary>Apply the shared rounded 9-slice sprite to an Image (no-op if build failed).</summary>
        public static void ApplyRounded(Image img)
        {
            if (img == null) return;
            var sprite = RoundedSprite;
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
        }

        /// <summary>
        /// UI-001 §R3: the shared rounded 9-slice at an AUTHORED corner radius in reference px.
        /// <para>⛔ THIS IS NOT A SECOND SPRITE. The asset bill for the Night Market card is exactly
        /// two white sprites and this is the FIRST of them — the same <see cref="RoundedSprite"/>
        /// every rounded corner in the game already uses. Only the 9-slice border SCALE moves:
        /// the sprite's baked border is 6 px, so <c>pixelsPerUnitMultiplier = 6 / radius</c> makes
        /// that border resolve to <paramref name="cornerRadiusPx"/> reference px. Authoring a new
        /// texture per radius is how a parallel chrome family starts (§2).</para>
        /// </summary>
        public static void ApplyRounded(Image img, float cornerRadiusPx)
        {
            if (img == null) return;
            ApplyRounded(img);
            if (img.sprite != null && cornerRadiusPx > 0.5f)
                img.pixelsPerUnitMultiplier = 6f / cornerRadiusPx;   // sprite border = 6 px
        }

        // ── Procedural RADIAL GLOW sprite (lazily built once; WebGL failure-safe) ──
        //
        //  UI-001 §R3 asset bill, sprite TWO of exactly two: a white radial falloff, tinted at
        //  the call site through the band light. It exists because the card's bloom must be a
        //  STATIC TINTED IMAGE and never a particle — the owner's ruling 4 is explicit that the
        //  glow costs ZERO VFX loop slots. CircleSprite cannot stand in: it is a HARD-EDGED disc
        //  (BuildCircleSprite antialiases one pixel and stops), so scaling it up reads as a pale
        //  coin, not a bloom.
        //
        //  The falloff is a smoothstep on radius rather than a linear ramp: a linear alpha ramp
        //  leaves a visible ring where it meets the ground because the human eye finds the
        //  first-derivative discontinuity. Squared smoothstep has zero slope at both ends.
        private static Sprite _radialGlow;
        private static bool _radialGlowTried;

        /// <summary>A white radial bloom (alpha 1 at centre -> 0 at the rim). Tint at the call
        /// site. Null only if the texture build threw; callers must null-check and skip the
        /// glow rather than draw a white quad.</summary>
        public static Sprite RadialGlowSprite
        {
            get
            {
                if (!_radialGlowTried)
                {
                    _radialGlowTried = true;
                    try { _radialGlow = BuildRadialGlowSprite(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ElarionUiKit] radial glow sprite build failed (no bloom): " + e.Message);
                        _radialGlow = null;
                    }
                }
                return _radialGlow;
            }
        }

        private static Sprite BuildRadialGlowSprite()
        {
            const int size = 64;
            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float r = size * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                    float t = Mathf.Clamp01(1f - d);
                    float a = t * t * (3f - 2f * t);        // smoothstep: no visible ring at the rim
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp((int)(a * 255f), 0, 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A single faint gold rule hugging a panel's bottom edge (sleek accent).</summary>
        public static void AddRimUnderline(GameObject panel)
        {
            if (panel == null || BlinkChromeActive) return;   // chrome: skip the bottom gold rule (WO-714 P9: art-presence gated)
            var go = new GameObject("Accent", typeof(Image));
            go.transform.SetParent(panel.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.06f, 0f);
            rt.anchorMax = new Vector2(0.94f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 1.5f);
            rt.anchoredPosition = new Vector2(0f, 1.5f);
            var img = go.GetComponent<Image>();
            img.color = AccentSoft;
            img.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        /// <summary>A 1px inner rim hugging an element's edges — crisp framed depth.
        /// <para>⚠ IT IS NOT A RIM AND IT IS NOT BEHIND THE HOST. This is a FULL-RECT filled
        /// rounded quad at a 1px inset, and a parent's own Graphic draws BEFORE all of its
        /// children — so SetAsFirstSibling orders it first among SIBLINGS while still drawing
        /// ON TOP of the host's face. It only reads as a rim because the fill is half-alpha, so
        /// on an ornate plate it VEILS the art rather than framing it. Two things keep that off
        /// screen today: it is skipped entirely while <c>BlinkChromeActive</c> (flag ON and the
        /// Blink art present, i.e. the shipping build), and BuildActionBarHousing already says
        /// so in its own header. Raise the alpha, or ship with the art absent, and every host
        /// that calls this gets a translucent slab over its face. A real rim is thin INSET edge
        /// bars in an image-less container — see HeroSkillTreePanelMvvm.BuildSpendPopup.</para></summary>
        public static void AddInnerRim(GameObject host, Color color)
        {
            if (host == null || BlinkChromeActive) return;   // chrome: skip the gilt inner rim (WO-714 P9: art-presence gated)
            var go = new GameObject("Rim", typeof(Image));
            go.transform.SetParent(host.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(1f, 1f); rt.offsetMax = new Vector2(-1f, -1f);
            var img = go.GetComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
            ApplyRounded(img);
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();   // behind content added after it
        }

        // ── Procedural rounded sprite (lazily built once; WebGL failure-safe) ──
        // Mirrors HudTheme.RoundedFrame so the whole game's corners match exactly.
        private static Sprite _rounded;
        private static bool _roundedTried;
        private static Sprite RoundedSprite
        {
            get
            {
                if (!_roundedTried)
                {
                    _roundedTried = true;
                    try { _rounded = BuildRoundedSprite(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ElarionUiKit] rounded sprite build failed (flat quad): " + e.Message);
                        _rounded = null;
                    }
                }
                return _rounded;
            }
        }

        private static Sprite BuildRoundedSprite()
        {
            const int size = 32;
            const int radius = 6;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x, y, size, size, radius);
                    byte a = (byte)Mathf.Clamp((int)((1f - d) * 255f), 0, 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                                 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static float RoundedRectDistance(int x, int y, int w, int h, int radius)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float dx = Mathf.Max(Mathf.Max(radius - fx, fx - (w - radius)), 0f);
            float dy = Mathf.Max(Mathf.Max(radius - fy, fy - (h - radius)), 0f);
            float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
            return Mathf.Clamp01(dist + 0.5f);
        }

        // ── Circle + ring sprites (ported from BattleHudUgui; lazily built once) ──
        // The portrait disc + gilt ring frame, shared so combat/town portraits match.
        private static Sprite _circle;
        private static bool _circleTried;
        private static Sprite _ring;
        private static bool _ringTried;
        private static Sprite _canonicalMedallionFrame;
        private static bool _canonicalMedallionFrameTried;

        /// <summary>The approved four-point black-iron/gold frame for generic round content.</summary>
        public static Sprite CanonicalMedallionFrameSprite
        {
            get
            {
                if (!_canonicalMedallionFrameTried)
                {
                    _canonicalMedallionFrameTried = true;
                    _canonicalMedallionFrame = Resources.Load<Sprite>(
                        "UI/ElarionMedieval/frames/circular-bezel-four-point");
                }
                return _canonicalMedallionFrame;
            }
        }

        /// <summary>Solid white AA disc for a portrait fill (null if the build failed under WebGL).</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (!_circleTried)
                {
                    _circleTried = true;
                    try { _circle = BuildCircleSprite(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ElarionUiKit] circle sprite build failed: " + e.Message);
                        _circle = null;
                    }
                }
                return _circle;
            }
        }

        /// <summary>Hollow white AA ring for a portrait frame (null if the build failed under WebGL).</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (!_ringTried)
                {
                    _ringTried = true;
                    try { _ring = BuildRingSprite(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ElarionUiKit] ring sprite build failed: " + e.Message);
                        _ring = null;
                    }
                }
                return _ring;
            }
        }

        private static Sprite BuildCircleSprite()
        {
            const int size = 64;
            float r = size * 0.5f - 1f;
            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite BuildRingSprite()
        {
            const int size = 64;
            float ro = size * 0.5f - 1f;
            float ri = ro - 5f; // ring thickness
            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float aOut = Mathf.Clamp01(ro - d + 0.5f);
                    float aIn = Mathf.Clamp01(d - ri + 0.5f);
                    float a = Mathf.Min(aOut, aIn);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // =====================================================================
        // SLIDE TAB / DOCK — an edge-pinned tab that toggles a slide-out panel (WO-439).
        // ---------------------------------------------------------------------
        // A reusable collapsible dock: a compact GEAR tab pinned to a screen edge (owner:
        // "a gear makes more sense than the down arrow") + an obsidian panel that shows on
        // tap and hides on a second tap. Collapsed by default. Parent your content (a tab
        // strip, list, etc.) into <see cref="SlideDockHandle.panel"/>. Sprite-first icon via
        // the concept resolver with a procedural gold-glyph fallback (never blanks).
        // =====================================================================

        /// <summary>Which screen edge a slide dock pins to.</summary>
        public enum SlideEdge { Left, Right }

        /// <summary>The live pieces of a <see cref="BuildSlideTab"/> dock.</summary>
        public sealed class SlideDockHandle
        {
            /// <summary>Full-area container (stretch) holding the tab + panel.</summary>
            public GameObject root;
            /// <summary>The obsidian slide-out panel — PARENT YOUR CONTENT HERE.</summary>
            public RectTransform panel;
            /// <summary>The edge tab button (toggles the panel).</summary>
            public Button tab;
            /// <summary>The tab's icon Image (retint / re-sprite if desired).</summary>
            public Image tabIcon;
            /// <summary>Optional caller hook fired on every toggle.</summary>
            public Action<bool> onToggle;
            /// <summary>Whether the panel is currently expanded.</summary>
            public bool Expanded { get; private set; }
            /// <summary>Show/hide the slide-out panel (collapsed by default).</summary>
            public void SetExpanded(bool v)
            {
                Expanded = v;
                if (panel != null) panel.gameObject.SetActive(v);
                if (onToggle != null) onToggle(v);
            }
        }

        /// <summary>
        /// Build an edge-pinned slide dock: a compact icon tab at <paramref name="tabYCenter"/> on the
        /// given <paramref name="edge"/> that toggles an obsidian slide-out panel (collapsed by default).
        /// Parent your content into the returned <see cref="SlideDockHandle.panel"/>. The tab icon
        /// resolves from the concept resolver (<paramref name="tabIconConcept"/>, default "settings"/gear)
        /// with a gold-glyph procedural fallback. Anchors are fractions of <paramref name="parent"/>.
        /// </summary>
        public static SlideDockHandle BuildSlideTab(Transform parent, SlideEdge edge,
            float tabYCenter = 0.5f, float panelWidthFrac = 0.24f, float panelHeightFrac = 0.52f,
            string tabIconConcept = "settings", Action<bool> onToggle = null)
        {
            var h = new SlideDockHandle { onToggle = onToggle };
            bool left = edge == SlideEdge.Left;

            var root = new GameObject("SlideDock", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            h.root = root;

            float y0 = Mathf.Clamp01(tabYCenter - panelHeightFrac * 0.5f);
            float y1 = Mathf.Clamp01(tabYCenter + panelHeightFrac * 0.5f);

            // Slide-out panel — obsidian (black + gold trim), pinned to the edge, hidden by default.
            Vector2 pMin = left ? new Vector2(0f, y0) : new Vector2(1f - panelWidthFrac, y0);
            Vector2 pMax = left ? new Vector2(panelWidthFrac, y1) : new Vector2(1f, y1);
            var panel = AddImage(root.transform, "SlidePanel", pMin, pMax, ObsidianFill, rounded: true);
            AddInnerRim(panel, ObsidianTrim);
            var pImg = panel.GetComponent<Image>();
            if (pImg != null) pImg.raycastTarget = true;   // eat taps over the open panel
            h.panel = (RectTransform)panel.transform;

            // Edge tab — a compact square icon button that toggles the panel.
            const float tabHalf = 0.055f, tabW = 0.05f;
            float ty0 = Mathf.Clamp01(tabYCenter - tabHalf), ty1 = Mathf.Clamp01(tabYCenter + tabHalf);
            Vector2 tMin = left ? new Vector2(0f, ty0) : new Vector2(1f - tabW, ty0);
            Vector2 tMax = left ? new Vector2(tabW, ty1) : new Vector2(1f, ty1);
            var tgo = new GameObject("SlideTab", typeof(Image), typeof(Button));
            tgo.transform.SetParent(root.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = tMin; trt.anchorMax = tMax;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tImg = tgo.GetComponent<Image>();
            tImg.color = ObsidianFill;
            ApplyRounded(tImg);
            AddInnerRim(tgo, ObsidianTrim);

            // GEAR icon (owner: replaces the down "v/>" trigger). Sprite-first; gold-glyph fallback.
            var iconGo = new GameObject("TabIcon", typeof(Image));
            iconGo.transform.SetParent(tgo.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0.15f, 0.15f); irt.anchorMax = new Vector2(0.85f, 0.85f);
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            h.tabIcon = iconGo.GetComponent<Image>();
            h.tabIcon.raycastTarget = false; h.tabIcon.preserveAspect = true;
            var iconSprite = UiStyle.Icon(string.IsNullOrEmpty(tabIconConcept) ? "settings" : tabIconConcept,
                                          "gear", "settings", "menu");
            if (iconSprite != null) { h.tabIcon.sprite = iconSprite; h.tabIcon.color = Color.white; }
            else
            {
                iconGo.SetActive(false);   // no icon art — draw an ASCII menu handle (build-font-safe; no gear-glyph tofu)
                var glyph = Label(tgo.transform, "=", 0f, 1f, ElarionUi.Gilt, ElarionUi.FontTitle,
                                  TextAlignmentOptions.Center, 0f, 1f, bold: true);
                glyph.raycastTarget = false;
            }

            h.tab = tgo.GetComponent<Button>();
            h.tab.targetGraphic = tImg;
            StyleButtonColors(h.tab);
            h.tab.onClick.AddListener(() => h.SetExpanded(!h.Expanded));

            h.SetExpanded(false);   // collapsed by default
            return h;
        }

        // =====================================================================
        // COMPASS — a compact Blink-Obsidian octagon compass (WO-438).
        // ---------------------------------------------------------------------
        // A dark octagon frame + gold rim, a centred cardinal label, and a rotating gold
        // needle. Reusable kit widget; the caller drives it via the handle each frame
        // (SetCardinal + SetNeedleAngle). Octagon is a lazily-built procedural sprite
        // (WebGL failure-safe — falls back to the rounded plate), so it never blanks.
        // =====================================================================

        /// <summary>The live pieces of a <see cref="BuildCompass"/> octagon.</summary>
        public sealed class CompassHandle
        {
            /// <summary>The octagon root GameObject (parent / reposition via this).</summary>
            public GameObject root;
            /// <summary>The centred cardinal heading label (N / NE / ...).</summary>
            public TextMeshProUGUI cardinal;
            /// <summary>The rotating gold needle (pivoted at the octagon centre, tip up at 0deg).</summary>
            public RectTransform needle;
            /// <summary>Set the cardinal heading text.</summary>
            public void SetCardinal(string s) { if (cardinal != null) cardinal.text = s ?? ""; }
            /// <summary>Rotate the needle to a bearing in degrees (0 = up/heading, + = clockwise/right).</summary>
            public void SetNeedleAngle(float deg) { if (needle != null) needle.localRotation = Quaternion.Euler(0f, 0f, -deg); }
            /// <summary>Show/hide the needle (e.g. hide when there is no objective bearing).</summary>
            public void SetNeedleVisible(bool v) { if (needle != null && needle.gameObject.activeSelf != v) needle.gameObject.SetActive(v); }
        }

        /// <summary>Build a compact octagon compass (dark octagon + gold rim + cardinal label + gold
        /// needle) under <paramref name="parent"/>, anchored by fraction. Drive it via the handle.</summary>
        public static CompassHandle BuildCompass(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var h = new CompassHandle();

            var root = new GameObject("CompassOctagon", typeof(Image));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            h.root = root;

            // Gold rim octagon (slightly larger, behind) + dark obsidian octagon face.
            var rimImg = root.GetComponent<Image>();
            var oct = OctagonSprite;
            if (oct != null) { rimImg.sprite = oct; rimImg.type = Image.Type.Simple; }
            rimImg.color = ObsidianTrim;
            rimImg.raycastTarget = false;

            var face = new GameObject("Face", typeof(Image));
            face.transform.SetParent(root.transform, false);
            var frt = (RectTransform)face.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);
            var faceImg = face.GetComponent<Image>();
            if (oct != null) { faceImg.sprite = oct; faceImg.type = Image.Type.Simple; }
            faceImg.color = new Color(0.04f, 0.04f, 0.05f, 0.96f);
            faceImg.raycastTarget = false;

            // Rotating gold needle: a thin blade pivoted at the octagon centre, tip pointing up.
            var needleGo = new GameObject("Needle", typeof(RectTransform));
            needleGo.transform.SetParent(root.transform, false);
            h.needle = (RectTransform)needleGo.transform;
            h.needle.anchorMin = new Vector2(0.5f, 0.5f);
            h.needle.anchorMax = new Vector2(0.5f, 0.5f);
            h.needle.pivot = new Vector2(0.5f, 0.5f);
            h.needle.sizeDelta = new Vector2(10f, 40f);
            h.needle.anchoredPosition = Vector2.zero;
            var blade = new GameObject("Blade", typeof(Image));
            blade.transform.SetParent(needleGo.transform, false);
            var brt = (RectTransform)blade.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(-2.5f, 0f); brt.offsetMax = new Vector2(2.5f, 0f);
            var bladeImg = blade.GetComponent<Image>();
            bladeImg.color = ElarionUi.Gilt;
            ApplyRounded(bladeImg);
            bladeImg.raycastTarget = false;

            // Centred cardinal heading label (drawn over the needle hub).
            h.cardinal = Label(root.transform, "N", 0f, 1f, ElarionUi.Parchment, ElarionUi.FontLabel,
                               TextAlignmentOptions.Center, 0f, 1f, spacing: 2f, bold: true);
            h.cardinal.raycastTarget = false;

            return h;
        }

        // ── Procedural octagon sprite (lazily built once; WebGL failure-safe) ──
        private static Sprite _octagon;
        private static bool _octagonTried;
        /// <summary>White AA regular-octagon fill for the compass frame (null if the build failed under WebGL).</summary>
        public static Sprite OctagonSprite
        {
            get
            {
                if (!_octagonTried)
                {
                    _octagonTried = true;
                    try { _octagon = BuildOctagonSprite(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[ElarionUiKit] octagon sprite build failed: " + e.Message);
                        _octagon = null;
                    }
                }
                return _octagon;
            }
        }

        private static Sprite BuildOctagonSprite()
        {
            const int size = 64;
            float half = (size - 1) * 0.5f;
            // Regular octagon: |u|<=1, |v|<=1, |u|+|v| <= 1+tan(22.5deg) ~= 1.4142 (corners cut).
            const float diag = 1.41421356f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - half) / half;
                    float v = (y - half) / half;
                    float au = Mathf.Abs(u), av = Mathf.Abs(v);
                    // Signed distance-ish to the octagon edge, scaled to pixels for a ~1px AA band.
                    float dEdge = Mathf.Max(Mathf.Max(au - 1f, av - 1f), au + av - diag);
                    float a = Mathf.Clamp01(-dEdge * half + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // =====================================================================
        // WO-611 COMBAT HUD — owner-designed combat widgets.
        // ---------------------------------------------------------------------
        // ADDITIVE builders used ONLY when FeatureFlags.CombatHud611 is ON (the
        // HudKit branches at each build site). The shipping HUD path (cluster /
        // flat rows / hard-sweep cooldown) is byte-identical when the flag is OFF.
        // Sprite-first with a procedural fallback — null art never blanks a widget.
        // =====================================================================

        /// <summary>WO-611 moveCluster: a VIRTUAL D-PAD (cross/plus) — a steel cross BODY, a dark
        /// centre HUB with a gold rim, and GOLD directional chevron press-zones on each arm. Same input
        /// contract as <see cref="BuildControllerCluster"/> (onMove receives the held direction vector on
        /// every press-state change, zero on release) so it drops straight into HudMoveInput.Set. Revives
        /// the VirtualDPadLean seam as ONE cross image rather than four discrete round buttons.</summary>
        public static ControllerHandle BuildVirtualDPad(Transform parent, Vector2 anchor, Action<Vector2> onMove)
        {
            const float Arm = 82f;      // half-length of one arm (reference px)
            const float Th  = 78f;      // arm thickness / hub band
            float span = Arm * 2f + Th;

            var go = new GameObject("VirtualDPad", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(span, span);
            rt.anchoredPosition = Vector2.zero;

            var h = new ControllerHandle { root = go };

            // STEEL CROSS BODY — a vertical + a horizontal bar forming the plus (raycast-off; the
            // chevron zones own the input).
            Color steel = new Color(0.28f, 0.30f, 0.35f, 0.96f);
            Color steelEdge = new Color(0.16f, 0.17f, 0.21f, 1f);
            void Bar(string name, Vector2 size)
            {
                var b = new GameObject(name, typeof(Image));
                b.transform.SetParent(go.transform, false);
                var brt = (RectTransform)b.transform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = size; brt.anchoredPosition = Vector2.zero;
                var img = b.GetComponent<Image>();
                img.color = steel; ApplyRounded(img); img.raycastTarget = false;
                AddInnerRim(b, steelEdge);
            }
            Bar("BarV", new Vector2(Th, span));
            Bar("BarH", new Vector2(span, Th));

            // CENTRE HUB — a dark disc with a gold rim.
            var hub = new GameObject("Hub", typeof(Image));
            hub.transform.SetParent(go.transform, false);
            var hrt = (RectTransform)hub.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(Th * 0.72f, Th * 0.72f);
            hrt.anchoredPosition = Vector2.zero;
            var hubImg = hub.GetComponent<Image>();
            hubImg.sprite = CircleSprite; hubImg.type = Image.Type.Simple;
            hubImg.color = new Color(0.10f, 0.10f, 0.12f, 1f); hubImg.raycastTarget = false;
            var hubRim = new GameObject("HubRim", typeof(Image));
            hubRim.transform.SetParent(hub.transform, false);
            var hubRimRt = (RectTransform)hubRim.transform;
            hubRimRt.anchorMin = Vector2.zero; hubRimRt.anchorMax = Vector2.one;
            hubRimRt.offsetMin = new Vector2(-3f, -3f); hubRimRt.offsetMax = new Vector2(3f, 3f);
            var hubRimImg = hubRim.GetComponent<Image>();
            hubRimImg.sprite = CircleSprite; hubRimImg.type = Image.Type.Simple;
            hubRimImg.color = ObsidianTrim; hubRimImg.raycastTarget = false;
            hubRim.transform.SetAsFirstSibling();
            // ⚠ THE LINE ABOVE DOES NOT PUT THE RIM BEHIND THE HUB FACE (audited 2026-09-03,
            // the Journey RAIDS sweep). hubRim is a CHILD of hub, and a parent's own Graphic
            // draws BEFORE all of its children - so this OPAQUE CircleSprite disc, grown 3px
            // past the hub on every edge, paints over hubImg's dark face and the hub reads as a
            // solid trim-coloured dot. Deliberately NOT changed on ship eve: it is a ~20px
            // compass detail with no capture behind it and no owner report. The fix when it is
            // wanted is a HOLLOW ring sprite here, or building the rim as a SIBLING of hub -
            // never a re-parent of hub itself (FTUE highlights resolve rects BY NAME).

            void Chevron(string name, Vector2 pos, Vector2 dir)
            {
                bool horizontal = Mathf.Abs(dir.x) > 0.5f;
                var zone = new GameObject(name, typeof(Image), typeof(Button), typeof(UiKitHoldButton));
                zone.transform.SetParent(go.transform, false);
                var zrt = (RectTransform)zone.transform;
                zrt.anchorMin = zrt.anchorMax = new Vector2(0.5f, 0.5f);
                zrt.sizeDelta = horizontal ? new Vector2(Arm, Th) : new Vector2(Th, Arm);
                zrt.anchoredPosition = pos;
                var zimg = zone.GetComponent<Image>();
                zimg.color = new Color(0f, 0f, 0f, 0f);   // transparent hit area
                var btn = zone.GetComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = zimg;
                var chev = Label(zone.transform, ChevronGlyph(dir), 0f, 1f, ObsidianTrim,
                                 ElarionUi.FontHead, TextAlignmentOptions.Center, 0f, 1f, bold: true);
                chev.raycastTarget = false;
                var hold = zone.GetComponent<UiKitHoldButton>();
                hold.onDown = () => { h.Current = dir; onMove?.Invoke(dir); };
                hold.onUp   = () => { if (h.Current == dir) { h.Current = Vector2.zero; onMove?.Invoke(Vector2.zero); } };
            }
            float off = (Arm + Th) * 0.5f;
            Chevron("Up",    new Vector2(0f,  off), Vector2.up);
            Chevron("Down",  new Vector2(0f, -off), Vector2.down);
            Chevron("Left",  new Vector2(-off, 0f), Vector2.left);
            Chevron("Right", new Vector2( off, 0f), Vector2.right);
            return h;
        }

        private static string ChevronGlyph(Vector2 dir)
        {
            // ASCII ONLY — the TMP build fonts lack the ▲▼◄► triangle glyphs (WO-611 landmine:
            // non-ASCII in TMP renders tofu boxes in the player).
            if (dir == Vector2.up)   return "^";
            if (dir == Vector2.down) return "v";
            if (dir == Vector2.left) return "<";
            return ">";
        }

        // =====================================================================
        // WO-899 §1 — the ANALOG STICK that replaces the digital D-pad.
        // ---------------------------------------------------------------------
        // Owner felt-test 2026-08-07: "replace the boxy joypad with a cleaner analog
        // joystick." BuildVirtualDPad above is a 4-zone DIGITAL pad — each chevron
        // emits a DISCRETE unit vector, so the hero only ever runs at full speed in
        // one of four directions. This builder emits a CONTINUOUS deflection instead.
        //
        // THE CONTRACT IS UNCHANGED (this is the whole point): onMove receives a
        // Vector2 in -1..1 whose MAGNITUDE is deflection/radius, and Vector2.zero on
        // release. That is exactly what HudMoveInput.Set takes, and HeroLocomotion
        // already clamps + eases it — so nothing in locomotion changes. BuildVirtualDPad
        // is KEPT (LeanTouchBuildDriver still uses it, and HudKitController falls back
        // to it if this build ever throws).
        //
        // FIXED BASE, not a floating origin: the moveCluster root is positioned by the
        // hud-areas.json occupancy rows, so a runtime re-centre would fight the layout
        // owner. The ring stays where the layout put it; the knob tracks the thumb and
        // clamps to the ring radius. Diameter is 2 x Radius = 236 reference px, well
        // over the MinTouchPx(112) floor.
        // =====================================================================

        /// <summary>WO-899 moveCluster: a clean floating-look ANALOG STICK — a semi-transparent
        /// dark base ring with a gold-dim rim and a filled knob that drags toward the thumb,
        /// clamped to the ring radius. <paramref name="onMove"/> receives a CONTINUOUS -1..1
        /// vector (magnitude = deflection/radius) while held and <see cref="Vector2.zero"/> on
        /// release — the same contract <see cref="BuildVirtualDPad"/> and
        /// <see cref="BuildControllerCluster"/> feed, so it drops straight into HudMoveInput.Set.
        /// Returns the same <see cref="ControllerHandle"/>; the four Button fields stay null
        /// (an analog stick has no discrete buttons) and <c>Current</c> carries the live vector.</summary>
        public static ControllerHandle BuildAnalogStick(Transform parent, Vector2 anchor, Action<Vector2> onMove)
        {
            using var _flow = FlowTrace.Enter("HudKit", "BuildAnalogStick");

            const float Radius  = 118f;   // ring radius in reference px (236 diameter >> MinTouchPx 112)
            const float KnobDia = 96f;    // knob diameter in reference px

            var h = new ControllerHandle();

            // Guarded construction (§12): a procedural-sprite failure must degrade the stick,
            // never abort the HUD build. A null handle root tells the caller to fall back.
            bool ok = Guard.Try("HudKit", "analog stick construction", () =>
            {
                // ROOT = the hit area. Transparent Image (raycastTarget ON) so the whole
                // ring circle claims the press; the driver owns down/drag/up.
                var go = new GameObject("AnalogStick", typeof(Image), typeof(UiKitAnalogStick));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = anchor; rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(Radius * 2f, Radius * 2f);
                rt.anchoredPosition = Vector2.zero;
                h.root = go;

                var hit = go.GetComponent<Image>();
                var disc = CircleSprite;
                if (disc != null) { hit.sprite = disc; hit.type = Image.Type.Simple; }
                hit.color = new Color(0f, 0f, 0f, 0.004f);   // effectively invisible, still raycastable
                hit.raycastTarget = true;

                // BASE RIM (behind) — a gold-dim ring: a slightly larger disc peeking out
                // from under the darker fill disc. Same trick the D-pad hub uses.
                var rim = new GameObject("BaseRim", typeof(Image));
                rim.transform.SetParent(go.transform, false);
                var rimRt = (RectTransform)rim.transform;
                rimRt.anchorMin = rimRt.anchorMax = new Vector2(0.5f, 0.5f);
                rimRt.sizeDelta = new Vector2(Radius * 2f, Radius * 2f);
                rimRt.anchoredPosition = Vector2.zero;
                var rimImg = rim.GetComponent<Image>();
                var medievalBezel = Resources.Load<Sprite>("UI/ElarionMedieval/frames/circular-bezel-four-point");
                if (medievalBezel != null)
                {
                    rimImg.sprite = medievalBezel;
                    rimImg.type = Image.Type.Simple;
                    rimImg.preserveAspect = true;
                    rimImg.color = new Color(1f, 1f, 1f, 0.78f);
                }
                else if (disc != null) { rimImg.sprite = disc; rimImg.type = Image.Type.Simple; }
                else ApplyRounded(rimImg);
                if (medievalBezel == null)
                    rimImg.color = new Color(C611GoldDim.r, C611GoldDim.g, C611GoldDim.b, 0.45f);
                rimImg.raycastTarget = false;

                // BASE FILL — the dark translucent well the knob rides in.
                var baseGo = new GameObject("BaseRing", typeof(Image));
                baseGo.transform.SetParent(go.transform, false);
                var baseRt = (RectTransform)baseGo.transform;
                baseRt.anchorMin = baseRt.anchorMax = new Vector2(0.5f, 0.5f);
                baseRt.sizeDelta = new Vector2(Radius * 2f - 8f, Radius * 2f - 8f);
                baseRt.anchoredPosition = Vector2.zero;
                var baseImg = baseGo.GetComponent<Image>();
                if (disc != null) { baseImg.sprite = disc; baseImg.type = Image.Type.Simple; }
                else ApplyRounded(baseImg);
                baseImg.color = new Color(C611Obsidian.r, C611Obsidian.g, C611Obsidian.b, 0.35f);
                baseImg.raycastTarget = false;

                // KNOB RIM + KNOB FACE — parented to the base so the driver can move the knob
                // in the base's own local units (which ARE reference px on this canvas).
                var knobGo = new GameObject("Knob", typeof(Image));
                knobGo.transform.SetParent(baseGo.transform, false);
                var knobRt = (RectTransform)knobGo.transform;
                knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f);
                knobRt.pivot = new Vector2(0.5f, 0.5f);
                knobRt.sizeDelta = new Vector2(KnobDia, KnobDia);
                knobRt.anchoredPosition = Vector2.zero;
                var knobImg = knobGo.GetComponent<Image>();
                if (disc != null) { knobImg.sprite = disc; knobImg.type = Image.Type.Simple; }
                else ApplyRounded(knobImg);
                knobImg.color = new Color(C611Gold.r, C611Gold.g, C611Gold.b, 0.50f);
                knobImg.raycastTarget = false;

                var knobCore = new GameObject("KnobCore", typeof(Image));
                knobCore.transform.SetParent(knobGo.transform, false);
                var coreRt = (RectTransform)knobCore.transform;
                coreRt.anchorMin = Vector2.zero; coreRt.anchorMax = Vector2.one;
                coreRt.offsetMin = new Vector2(5f, 5f); coreRt.offsetMax = new Vector2(-5f, -5f);
                var coreImg = knobCore.GetComponent<Image>();
                if (disc != null) { coreImg.sprite = disc; coreImg.type = Image.Type.Simple; }
                else ApplyRounded(coreImg);
                coreImg.color = new Color(0.20f, 0.22f, 0.26f, 0.92f);   // forged steel thumb pad
                coreImg.raycastTarget = false;

                var drv = go.GetComponent<UiKitAnalogStick>();
                drv.baseRect = baseRt;
                drv.knob = knobRt;
                drv.fallbackRadiusPx = Radius;
                drv.onMove = v => { h.Current = v; onMove?.Invoke(v); };

                FlowTrace.Step("HudKit",
                    $"analog stick built: radius={Radius:0}px, knob={KnobDia:0}px, contract=HudMoveInput.Set(-1..1 magnitude).");
            });

            if (!ok || h.root == null)
            {
                // A half-built stick must not be left parented in the widget pool: the caller is
                // about to build the D-pad fallback into the SAME moveCluster mount.
                if (h.root != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(h.root);
                    else UnityEngine.Object.DestroyImmediate(h.root);
                }
                FlowTrace.Warn("HudKit", "analog stick construction FAILED — caller should fall back to BuildVirtualDPad.");
                return null;
            }
            return h;
        }

        /// <summary>WO-899: the analog stick's input driver. Converts a pointer press/drag inside
        /// the base ring into a CONTINUOUS -1..1 deflection (magnitude = distance/radius) and
        /// pushes it through <see cref="onMove"/>; pushes <see cref="Vector2.zero"/> on release,
        /// on cancel AND on disable — a posture swap that hides the HUD mid-hold must never leave
        /// the hero running forever. Deliberately does NOT implement IPointerExitHandler: on a
        /// thumbstick the finger routinely leaves the ring while still steering, and the
        /// EventSystem keeps routing drag to the pressed object.</summary>
        private sealed class UiKitAnalogStick : MonoBehaviour,
            IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public RectTransform baseRect;
            public RectTransform knob;
            public Action<Vector2> onMove;
            public float fallbackRadiusPx = 100f;

            private bool _held;
            private Vector2 _value;

            /// <summary>The live deflection (-1..1 per axis, magnitude &lt;= 1).</summary>
            public Vector2 Value => _value;

            public void OnPointerDown(PointerEventData e) { _held = true; Track(e); }
            public void OnDrag(PointerEventData e) { if (_held) Track(e); }
            public void OnPointerUp(PointerEventData e) { Release(); }
            private void OnDisable() { Release(); }

            private void Track(PointerEventData e)
            {
                if (baseRect == null) { Release(); return; }
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        baseRect, e.position, e.pressEventCamera, out var local))
                    return;

                // Radius read LIVE off the base rect so the stick can never drift from its art
                // (the const is only the pre-layout fallback, when rect.width is still 0).
                float r = baseRect.rect.width * 0.5f;
                if (r < 1f) r = fallbackRadiusPx;
                if (r < 1f) return;

                Vector2 clamped = Vector2.ClampMagnitude(local, r);
                if (knob != null) knob.anchoredPosition = clamped;
                Push(clamped / r);
            }

            private void Release()
            {
                _held = false;
                if (knob != null) knob.anchoredPosition = Vector2.zero;
                if (_value != Vector2.zero) Push(Vector2.zero);
            }

            private void Push(Vector2 v)
            {
                _value = v;
                onMove?.Invoke(v);
            }
        }

        // ── WO-611 ratified palette (frozen mockup v8-spec-freeze) ──────────
        private static readonly Color C611Edge     = new Color(0.239f, 0.271f, 0.322f, 1f); // #3d4552
        private static readonly Color C611Gold     = new Color(0.831f, 0.686f, 0.353f, 1f); // #d4af5a
        private static readonly Color C611GoldDim  = new Color(0.604f, 0.498f, 0.243f, 1f); // #9a7f3e
        private static readonly Color C611Obsidian = new Color(0.055f, 0.063f, 0.075f, 1f); // #0e1013
        private static readonly Color C611Gray     = new Color(0.545f, 0.573f, 0.608f, 1f); // #8b929b
        private static readonly Color C611Amber    = new Color(0.878f, 0.725f, 0.361f, 1f); // #e0b95c

        // ── WO-611 procedural sprites (lazily built once; WebGL failure-safe) ──

        private static Sprite _c611Pill; private static bool _c611PillTried;
        /// <summary>The attack-pill face: a stadium (radius = half height) baked so the pill can
        /// never fall back to a white quad.
        ///
        /// WO-899 §3 (owner: the attack button "looks amateur / doesn't blend"): the fill was a
        /// bespoke dark-TEAL radial, which made the attack pill the ONE teal object in an
        /// otherwise obsidian+gold bottom bar — every ability medallion
        /// (<see cref="CombatMedallionSprite"/>) and the bar housing
        /// (<see cref="CombatHousingSprite"/>) are steel/obsidian with a gold-dim rim. The teal is
        /// retired: the pill now bakes the SAME vertical obsidian gradient as the housing
        /// (#20252d -> #13161c), the same 2px gold-dim border and inner gold inset ring, and a
        /// warm AMBER outer glow instead of a teal one — the one "this is the hit button" accent,
        /// carried by GLOW and SIZE (shape), never by a colour the player must decode.</summary>
        public static Sprite CombatPillSprite
        {
            get
            {
                if (!_c611PillTried)
                {
                    _c611PillTried = true;
                    try { _c611Pill = BuildCombatPillSprite(); }
                    catch (Exception e) { Debug.LogWarning("[ElarionUiKit] WO-611 pill sprite build failed: " + e.Message); _c611Pill = null; }
                }
                return _c611Pill;
            }
        }

        private static Sprite BuildCombatPillSprite()
        {
            const int w = 232, h = 104, glowPx = 8;
            // WO-899 §3: the SAME obsidian gradient the action-bar housing bakes, so the pill and
            // the bar it sits beside read as one material. Top-lit vertical ramp (not a radial) —
            // that is what the housing does, and a radial was part of what made the pill read as
            // a foreign object dropped onto the bar.
            var obsTop = new Color(0.125f, 0.145f, 0.176f, 1f);   // #20252d
            var obsBot = new Color(0.075f, 0.086f, 0.110f, 1f);   // #13161c
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            float halfH = (h - 2f * glowPx) * 0.5f;              // stadium radius (= half height)
            float halfW = (w - 2f * glowPx) * 0.5f;
            float r = halfH;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float ddx = Mathf.Max(Mathf.Abs(x - cx) - (halfW - r), 0f);
                    float ddy = y - cy;
                    float sd = Mathf.Sqrt(ddx * ddx + ddy * ddy) - r;   // signed dist: +outside
                    Color c;
                    if (sd <= 0f)
                    {
                        // Vertical obsidian ramp across the stadium body (bottom -> top).
                        float t = Mathf.Clamp01((y - (cy - halfH)) / Mathf.Max(1f, halfH * 2f));
                        c = Color.Lerp(obsBot, obsTop, t);
                        // A soft top-inner sheen so the plate has depth instead of reading flat.
                        if (sd < -8f && sd > -18f) c = Color.Lerp(c, obsTop, 0.35f * Mathf.Clamp01((y - cy) / halfH));
                        if (sd > -2.5f) c = C611GoldDim;                                   // 2px gold-dim border
                        else if (sd > -6.5f && sd <= -5.5f) c = Color.Lerp(c, C611Gold, 0.6f); // inner 1px gold ring
                        c.a = Mathf.Clamp01(-sd + 0.5f);                                   // AA edge
                    }
                    else
                    {
                        // Warm amber halo (was teal) — the combat accent, in the bar's gold family.
                        c = C611Amber;
                        c.a = 0.26f * Mathf.Clamp01(1f - sd / glowPx);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite _c611Medallion; private static bool _c611MedallionTried;
        /// <summary>The Q/W/E/R medallion face: circle with the mockup's radial steel fill
        /// (#2b323d centre -> #12161c edge) + a baked 2px gold-dim border.</summary>
        public static Sprite CombatMedallionSprite
        {
            get
            {
                if (!_c611MedallionTried)
                {
                    _c611MedallionTried = true;
                    try { _c611Medallion = BuildCombatMedallionSprite(); }
                    catch (Exception e) { Debug.LogWarning("[ElarionUiKit] WO-611 medallion sprite build failed: " + e.Message); _c611Medallion = null; }
                }
                return _c611Medallion;
            }
        }

        private static Sprite BuildCombatMedallionSprite()
        {
            const int size = 96;
            var inC  = new Color(0.169f, 0.196f, 0.239f, 1f);   // #2b323d
            var outC = new Color(0.071f, 0.086f, 0.110f, 1f);   // #12161c
            float r = size * 0.5f - 1f;
            float cx = (size - 1) * 0.5f, cy = cx;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float sd = d - r;
                    Color c;
                    if (sd <= 0f)
                    {
                        c = Color.Lerp(inC, outC, Mathf.Clamp01(d / r));
                        if (sd > -2.5f) c = C611GoldDim;                 // 2px gold-dim rim
                        c.a = Mathf.Clamp01(-sd + 0.5f);
                    }
                    else c = new Color(0f, 0f, 0f, 0f);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite _c611Housing; private static bool _c611HousingTried;
        /// <summary>The action-bar housing face: rounded (~11px radius) 9-sliced VERTICAL gradient
        /// #20252d (top) -> #13161c (bottom) — slicing keeps the gradient smooth at any size.</summary>
        public static Sprite CombatHousingSprite
        {
            get
            {
                if (!_c611HousingTried)
                {
                    _c611HousingTried = true;
                    try { _c611Housing = BuildCombatHousingSprite(); }
                    catch (Exception e) { Debug.LogWarning("[ElarionUiKit] WO-611 housing sprite build failed: " + e.Message); _c611Housing = null; }
                }
                return _c611Housing;
            }
        }

        private static Sprite BuildCombatHousingSprite()
        {
            const int size = 48, radius = 11;
            var top = new Color(0.125f, 0.145f, 0.176f, 1f);    // #20252d
            var bot = new Color(0.075f, 0.086f, 0.110f, 1f);    // #13161c
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var c = Color.Lerp(bot, top, y / (float)(size - 1));
                    c.a = 1f - RoundedRectDistance(x, y, size, size, radius);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                                 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>WO-611: a HOLLOW rounded-rect ring (border only) — the kit's 9-sliced rounded
        /// sprite with fillCenter OFF; pixelsPerUnitMultiplier scales the 6px sprite border down to
        /// <paramref name="thicknessPx"/>. Unlike AddInnerRim (BlinkChrome-gated translucent FILL)
        /// this is a true ring and never tints the interior. Disabled (never a white quad) if the
        /// rounded sprite failed to build.</summary>
        private static Image AddRoundedRing(Transform parent, string name, float inset, Color color, float thicknessPx)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            var img = go.GetComponent<Image>();
            var rs = RoundedSprite;
            if (rs != null)
            {
                img.sprite = rs;
                img.type = Image.Type.Sliced;
                img.fillCenter = false;
                img.pixelsPerUnitMultiplier = thicknessPx > 0f ? 6f / thicknessPx : 1f;   // sprite border = 6px
                img.color = color;
            }
            else img.enabled = false;   // no sprite -> no ring (cosmetic), never a filled white quad
            img.raycastTarget = false;
            return img;
        }

        /// <summary>WO-611 attack: the oblong stadium ATTACK PILL. Built on the proven
        /// <see cref="BuildActionSlot"/> (keeps the tap + cooldown contract), then the frame is REPLACED
        /// by the baked stadium face — never ApplyRounded on the prefab frame (that produced the 07-05
        /// white-quad capture: a white-tinted prefab root Image handed the plain white rounded sprite).
        /// Same <see cref="ActionSlotHandle"/> contract.
        ///
        /// WO-899 §3 — "blend it into the bar". Two things made it read as a pasted amateur button:
        ///   1. the PLATE was the only teal object on an obsidian+gold bar. <see cref="CombatPillSprite"/>
        ///      is now the housing's own obsidian gradient with the same gold-dim rim (fixed there).
        ///   2. the ICON sat flat ON the plate with nothing under it, so any icon art carrying its own
        ///      background square/disc looked stuck on. An INSET ICON WELL (a soft dark shadow disc +
        ///      a darker socket disc, both BEHIND the icon in sibling order) is added here so the glyph
        ///      sits IN the plate. The well is preserveAspect on a circle sprite, so it stays concentric
        ///      with the equally aspect-preserved icon at any pill size.
        /// Cooldown ring / seconds / count and the 112px touch floor are untouched.</summary>
        public static ActionSlotHandle BuildAttackPill(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Action onTap)
        {
            using var _flow = FlowTrace.Enter("HudKit", "BuildAttackPill");

            var h = BuildActionSlot(parent, anchorMin, anchorMax, onTap);
            if (h == null || h.root == null) { FlowTrace.Fail("HudKit", "attack pill: BuildActionSlot returned no root."); return h; }
            if (h.frame != null)
            {
                var pill = CombatPillSprite;
                if (pill != null)
                {
                    h.frame.sprite = pill;
                    h.frame.type = Image.Type.Simple;
                    h.frame.preserveAspect = false;   // the caller's rect IS the stadium proportion
                    h.frame.color = Color.white;      // colours are baked in the sprite
                    FlowTrace.Step("HudKit", "attack pill: baked obsidian+gold stadium face applied (WO-899 harmonised with the bar).");
                }
                else
                {
                    // Procedural fallback: flat obsidian rounded quad + gold-dim ring (never white/blank).
                    // WO-899: obsidian, NOT the old teal — the fallback must blend for the same reason
                    // the baked face does, or a WebGL sprite failure re-creates the exact defect.
                    h.frame.sprite = null;
                    h.frame.color = new Color(0.075f, 0.086f, 0.110f, 0.96f);   // #13161c
                    ApplyRounded(h.frame);
                    AddRoundedRing(h.root.transform, "GoldTrim", 1f, C611GoldDim, 2f);
                    FlowTrace.Warn("HudKit", "attack pill: baked stadium sprite unavailable — using the flat obsidian fallback face.");
                }
            }
            if (h.icon != null)
            {
                // The INSET WELL. Parented to the icon's own parent and inserted at the icon's sibling
                // index, so it is guaranteed to draw UNDER the icon in both BuildActionSlot modes
                // (prefab-bound icon can be nested deeper than the constructed one).
                Guard.Try("HudKit", "attack pill icon well", () =>
                {
                    var iconParent = h.icon.transform.parent;
                    int at = h.icon.transform.GetSiblingIndex();
                    var disc = CircleSprite;

                    Image Well(string name, float y0, float y1, Color color)
                    {
                        var go = new GameObject(name, typeof(Image));
                        go.transform.SetParent(iconParent, false);
                        var rt = (RectTransform)go.transform;
                        rt.anchorMin = new Vector2(0.02f, y0); rt.anchorMax = new Vector2(0.98f, y1);
                        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                        var img = go.GetComponent<Image>();
                        if (disc != null) { img.sprite = disc; img.type = Image.Type.Simple; }
                        else ApplyRounded(img);
                        img.preserveAspect = true;      // concentric with the aspect-preserved icon
                        img.color = color;
                        img.raycastTarget = false;
                        go.transform.SetSiblingIndex(at);
                        return img;
                    }

                    // ORDER MATTERS: each Well() inserts itself AT the icon's original index, so the
                    // LAST one created ends up deepest. Socket first, shadow second => final draw
                    // order is shadow -> socket -> icon.
                    Well("IconWell",   0.10f, 0.90f, new Color(0.031f, 0.039f, 0.055f, 0.92f)); // the socket
                    Well("IconShadow", 0.05f, 0.95f, new Color(0f, 0f, 0f, 0.30f));             // rim shadow
                    FlowTrace.Step("HudKit", "attack pill: inset icon well seated behind the glyph.");
                });

                // Owner 2026-07-06 ("fit to frame"): seat the glyph FLAT and aspect-true — the old
                // -16 deg mockup tilt was for the square icon_sword fallback and overflowed the pill.
                // WO-899: 0.86 -> 0.92 so the glyph fills its socket instead of floating in it.
                h.icon.transform.localRotation = Quaternion.identity;
                h.icon.transform.localScale = Vector3.one * 0.92f;
                h.icon.preserveAspect = true;
                h.icon.color = Color.white;
            }
            return h;
        }

        /// <summary>WO-611 ability arc: restyle an action slot as a ROUND GOLD MEDALLION — the baked
        /// radial-steel circle face (2px gold-dim rim) + an optional small KEY BADGE chip top-left
        /// ("Q"/"W"/"E"/"R", ASCII). Presentation-only; the slot keeps its tap + icon + count contract.</summary>
        public static void StyleAsRoundMedallion(ActionSlotHandle slot, string keyBadge = null)
        {
            if (slot == null || slot.root == null) return;

            // The full slot is a wide touch/caption cell. All circular artwork shares this one
            // square container, making the outer bezel the physical bound and guaranteeing that
            // the face, mask, and icon remain concentric at every phone/tablet aspect ratio.
            RectTransform medallionBounds;
            var existingBounds = slot.root.transform.Find("MedallionBounds") as RectTransform;
            if (existingBounds != null) medallionBounds = existingBounds;
            else
            {
                var boundsGo = new GameObject("MedallionBounds", typeof(RectTransform), typeof(AspectRatioFitter));
                boundsGo.transform.SetParent(slot.root.transform, false);
                boundsGo.transform.SetAsFirstSibling();
                medallionBounds = (RectTransform)boundsGo.transform;
                medallionBounds.anchorMin = new Vector2(0f, 0.20f);
                medallionBounds.anchorMax = Vector2.one;
                medallionBounds.offsetMin = Vector2.zero;
                medallionBounds.offsetMax = Vector2.zero;
                var boundsAspect = boundsGo.GetComponent<AspectRatioFitter>();
                boundsAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                boundsAspect.aspectRatio = 1f;
            }
            if (slot.frame != null)
            {
                var med = CombatMedallionSprite;
                if (med != null)
                {
                    slot.frame.sprite = med;
                    slot.frame.type = Image.Type.Simple;
                    slot.frame.preserveAspect = true;
                    slot.frame.color = Color.white;   // colours baked in the sprite
                }
                else
                {
                    // Fallback: plain kit disc tinted steel + gold-dim ring child (never blank).
                    slot.frame.sprite = CircleSprite;
                    slot.frame.type = Image.Type.Simple;
                    slot.frame.color = new Color(0.169f, 0.196f, 0.239f, 0.96f);   // #2b323d
                    if (slot.frame.sprite == null) ApplyRounded(slot.frame);
                    var rim = new GameObject("GoldRim", typeof(Image));
                    rim.transform.SetParent(medallionBounds, false);
                    var rimRt = (RectTransform)rim.transform;
                    rimRt.anchorMin = Vector2.zero; rimRt.anchorMax = Vector2.one;
                    rimRt.offsetMin = Vector2.zero; rimRt.offsetMax = Vector2.zero;
                    var rimImg = rim.GetComponent<Image>();
                    var fallbackFrame = CanonicalMedallionFrameSprite != null
                        ? CanonicalMedallionFrameSprite : RingSprite;
                    if (fallbackFrame != null)
                    {
                        rimImg.sprite = fallbackFrame;
                        rimImg.type = Image.Type.Simple;
                        rimImg.preserveAspect = true;
                        rimImg.color = CanonicalMedallionFrameSprite != null ? Color.white : C611GoldDim;
                    }
                    else rimImg.enabled = false;
                    rimImg.raycastTarget = false;
                }

                // The slot root is the full touch/caption cell, not the round artwork. Keeping
                // the medallion on that complete rect made the lower verb cross the bezel. Move
                // the visual face into a dedicated upper square-safe band and leave the bottom
                // fifth exclusively for the caption. The root Image stays transparent as the
                // Button targetGraphic, so interaction geometry does not change.
                var faceTr = medallionBounds.Find("MedallionFace");
                var faceGo = faceTr != null ? faceTr.gameObject : new GameObject("MedallionFace", typeof(Image));
                if (faceTr == null) faceGo.transform.SetParent(medallionBounds, false);
                faceGo.transform.SetAsFirstSibling();
                var faceRt = (RectTransform)faceGo.transform;
                faceRt.anchorMin = Vector2.zero;
                faceRt.anchorMax = Vector2.one;
                faceRt.offsetMin = Vector2.zero;
                faceRt.offsetMax = Vector2.zero;
                var face = faceGo.GetComponent<Image>();
                face.sprite = slot.frame.sprite;
                face.type = Image.Type.Simple;
                face.preserveAspect = true;
                face.color = slot.frame.color;
                face.raycastTarget = false;
                var transparent = slot.frame.color;
                transparent.a = 0f;
                slot.frame.color = transparent;
            }

            // A round FRAME is not enough: several authoritative navigation icons are authored
            // as square/tall card art.  preserveAspect merely letterboxes those rectangles inside
            // the medallion (the BUILD/HERO defect captured on Seeker).  Put the live icon Image
            // under a circular stencil so every caller may keep using SetIcon while the artwork is
            // aspect-filled and physically incapable of painting outside the round socket.
            if (slot.icon != null && slot.icon.transform.parent != null &&
                slot.icon.transform.parent.name != "RoundIconMask")
            {
                var icon = slot.icon;
                var maskGo = new GameObject("RoundIconMask", typeof(RectTransform), typeof(Image), typeof(Mask));
                maskGo.transform.SetParent(medallionBounds, false);
                var mrt = (RectTransform)maskGo.transform;
                var irt = icon.rectTransform;
                const float iconInset = 0.14f;
                mrt.anchorMin = new Vector2(iconInset, iconInset);
                mrt.anchorMax = new Vector2(1f - iconInset, 1f - iconInset);
                mrt.offsetMin = Vector2.zero;
                mrt.offsetMax = Vector2.zero;
                var maskImage = maskGo.GetComponent<Image>();
                maskImage.sprite = CircleSprite != null ? CircleSprite : SolidSprite;
                maskImage.type = Image.Type.Simple;
                maskImage.color = Color.white;
                maskImage.raycastTarget = false;
                maskGo.GetComponent<Mask>().showMaskGraphic = false;

                icon.transform.SetParent(maskGo.transform, false);
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                icon.preserveAspect = false; // square kit art fills the stencil; the stencil owns shape
                icon.raycastTarget = false;
            }
            // The radial combat face supplies the dark socket, but it is not the approved public
            // chrome: at HUD scale it reads as a plain brown disc beside the black-iron/gold UI.
            // Overlay the canonical transparent four-point bezel on every round action so HUD,
            // Skills and item medallions share one visual grammar without flattening live icons.
            if (medallionBounds.Find("CanonicalBezel") == null &&
                CanonicalMedallionFrameSprite != null)
            {
                var bezelGo = new GameObject("CanonicalBezel", typeof(Image));
                bezelGo.transform.SetParent(medallionBounds, false);
                var bezelRt = (RectTransform)bezelGo.transform;
                bezelRt.anchorMin = Vector2.zero;
                bezelRt.anchorMax = Vector2.one;
                bezelRt.offsetMin = Vector2.zero;
                bezelRt.offsetMax = Vector2.zero;
                var bezel = bezelGo.GetComponent<Image>();
                bezel.sprite = CanonicalMedallionFrameSprite;
                bezel.type = Image.Type.Simple;
                bezel.preserveAspect = true;
                bezel.color = Color.white;
                bezel.raycastTarget = false;
            }
            if (!string.IsNullOrEmpty(keyBadge))
            {
                // Small key badge chip, top-left of the medallion (mockup).
                var badge = new GameObject("KeyBadge", typeof(Image));
                badge.transform.SetParent(slot.root.transform, false);
                var brt = (RectTransform)badge.transform;
                brt.anchorMin = new Vector2(-0.06f, 0.68f); brt.anchorMax = new Vector2(0.34f, 1.08f);
                brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
                var bimg = badge.GetComponent<Image>();
                bimg.sprite = CircleSprite; bimg.type = Image.Type.Simple;
                bimg.color = new Color(C611Obsidian.r, C611Obsidian.g, C611Obsidian.b, 0.95f);
                bimg.raycastTarget = false;
                if (bimg.sprite == null) ApplyRounded(bimg);   // rounded fallback, never a bare quad
                var bring = new GameObject("Ring", typeof(Image));
                bring.transform.SetParent(badge.transform, false);
                var bringRt = (RectTransform)bring.transform;
                bringRt.anchorMin = Vector2.zero; bringRt.anchorMax = Vector2.one;
                bringRt.offsetMin = Vector2.zero; bringRt.offsetMax = Vector2.zero;
                var bringImg = bring.GetComponent<Image>();
                if (RingSprite != null) { bringImg.sprite = RingSprite; bringImg.type = Image.Type.Simple; bringImg.color = C611GoldDim; }
                else bringImg.enabled = false;
                bringImg.raycastTarget = false;
                var keyLabel = Label(badge.transform, keyBadge, 0f, 1f, C611Gold,
                                     ElarionUi.FontMicro, TextAlignmentOptions.Center, 0f, 1f, bold: true);
                keyLabel.raycastTarget = false;
            }

            // WO-867: the arc lays these out at ~93.7 ref px — 18.3 under the 112 touch floor.
            // See EnsureTouchFloorArea for the measurement and why the rect itself is not grown.
            EnsureTouchFloorArea(slot);
        }

        /// <summary>
        /// WO-1359 - present an OWNER-AUTHORED EMBLEM on a round action slot.
        /// <para>
        /// <see cref="StyleAsRoundMedallion"/> builds the medallion out of three kit layers: a dark
        /// socket face, a round STENCIL that aspect-fills whatever icon it is handed, and the gold
        /// four-point bezel on top. That is exactly right for a bare glyph. It is exactly WRONG for
        /// art that already IS a medallion: the owner's emblems ship with their own socket, their
        /// own gold ring and four diamond points that stand proud of the circle, so the kit would
        /// draw a second ring around hers, clip her points off at the stencil, and stretch a 386x411
        /// emblem into a square. Three ways of damaging finished art in one call.
        /// </para>
        /// <para>
        /// So when authored art resolves, the kit steps back: its own face and bezel stop drawing,
        /// the stencil stops clipping and opens to the full medallion bounds, and the icon keeps its
        /// aspect. Nothing is renamed, re-parented or destroyed - only enabled/disabled and
        /// re-anchored - so the FTUE's by-name lookups and the slot's tap contract are untouched,
        /// and a slot with no authored art never reaches here and looks exactly as it did.
        /// </para>
        /// </summary>
        public static void PresentAuthoredEmblem(ActionSlotHandle slot)
        {
            if (slot == null || slot.root == null || slot.icon == null) return;

            var bounds = slot.root.transform.Find("MedallionBounds");
            if (bounds != null)
            {
                // The kit's own socket + ring: hidden, not removed. Her emblem carries both.
                HideChildGraphic(bounds, "MedallionFace");
                HideChildGraphic(bounds, "CanonicalBezel");
                HideChildGraphic(bounds, "GoldRim");
            }
            // ⛔ slot.frame is NOT hidden here, and must not be: BuildActionSlot makes it the
            // Button's targetGraphic, so disabling the Image disables the raycast and the face
            // stops taking taps. StyleAsRoundMedallion already took it to alpha 0 while leaving it
            // enabled, which is exactly the invisible-but-tappable state we want. The bar is the
            // most-tapped surface in the game; nothing here may cost it a touch target.

            var maskTr = slot.icon.rectTransform.parent as RectTransform;
            if (maskTr != null && maskTr.name == "RoundIconMask")
            {
                var mask = maskTr.GetComponent<Mask>();
                if (mask != null) mask.enabled = false;      // stop clipping the four diamond points
                var maskImage = maskTr.GetComponent<Image>();
                if (maskImage != null) maskImage.enabled = false;
                maskTr.anchorMin = Vector2.zero;             // the 14% inset was room for the bezel
                maskTr.anchorMax = Vector2.one;
                maskTr.offsetMin = Vector2.zero;
                maskTr.offsetMax = Vector2.zero;
            }

            slot.icon.preserveAspect = true;   // never distort supplied art
            slot.icon.color = Color.white;     // never tint it either
            slot.icon.raycastTarget = false;
        }

        private static void HideChildGraphic(Transform parent, string childName)
        {
            var tr = parent.Find(childName);
            if (tr == null) return;
            var img = tr.GetComponent<Image>();
            if (img != null) img.enabled = false;
        }

        // ═══ WO-1671 — THE CAPTION BAND GETS AN OBSIDIAN BACKING PLATE ════════════
        //
        // MEASURED EVIDENCE (owner device frames, 2670x1200, read with PIL 12.3.0 on
        // 2026-09-10, not inferred):
        //   Builds/device-frames/2026-09-10_0814_363660_town_dock.png  and  ..._1134_363866_town.png
        //   The five calm-dock captions render in the band y 1138..1162 px, which falls BELOW the
        //   authored housing art, straight onto the town terrain. WCAG contrast of the parchment
        //   glyphs against the pixels actually behind them:
        //       BUILD 2.12:1   TALK 2.73:1   HERO 2.97:1   JOURNEY 1.87:1   MANAGE 2.16:1
        //   (second frame: 2.17 / 2.57 / 2.94 / 1.90 / 2.16). Every face is UNDER the 3:1 floor
        //   WCAG allows even for large text, and JOURNEY - the longest word, over the brightest
        //   grass - is the worst. This is not a taste call: the word is the only thing that makes
        //   the face readable without colour (the owner is red/green colourblind), so a caption
        //   the terrain can swallow is a functional defect.
        //
        // WHY A KIT PLATE AND NOT A TINT. ⛔ Never hand-tint a caption to "lift" it: a hue picked
        // against grass loses again over stone, water or a night sky, and it moves meaning onto
        // colour. The kit already owns the answer - the obsidian idiom (near-black fill + gold
        // trim, WO-562) that every other surface in this game sits on. The caption gets the SAME
        // fill every panel body gets, by REFERENCE (ObsidianFill), so a future reskin carries it.
        //
        // PREDICTED after-ratio against ObsidianFill: ~16.7:1 for all five. That is arithmetic
        // from the token, NOT a measurement - it must be re-measured off a device frame once this
        // ships (§11B).
        //
        // ⛔ SHAPE CONSTRAINTS THAT ARE NOT NEGOTIABLE, each one a way this could have gone wrong:
        //  * The plate is a SIBLING of the caption inside the slot root, never a wrapper.
        //    HudActionBarRegression.CheckMeasuredPeacefulDock finds a face's caption by walking the
        //    slot root's DIRECT children for a non-empty TMP_Text; reparenting the caption under a
        //    plate would red the oracle with "carries NO caption" - a true-looking failure about
        //    entirely the wrong thing.
        //  * It is inserted AT the caption's sibling index, so the caption slides one later and
        //    draws ON TOP. (SetCaption itself re-seats the caption under cdText, so a plate built
        //    BEFORE the caption would end up over the word.)
        //  * It is BIGGER than the caption rect on both axes and the caption rect is NOT touched.
        //    The oracle's CaptionInset(0.88) encodes SetCaption's authored x 0.06..0.94; shrinking
        //    the word to fit a padded plate would silently invalidate its label-fit case.
        //  * raycastTarget = false - the plate must never eat a tap meant for the medallion.
        // Idempotent: a second call finds the existing plate and returns it.
        /// <summary>
        /// WO-1671 — seat a dark obsidian backing band under a slot's caption strip so the word
        /// reads against ANY world behind the dock. Returns the plate (or null if the slot has no
        /// caption yet - build the caption first). Safe to call twice.
        /// </summary>
        public static GameObject AddCaptionPlate(ActionSlotHandle slot)
        {
            if (slot == null || slot.root == null || slot.caption == null) return null;
            var existing = slot.root.transform.Find(CaptionPlateObjectName);
            if (existing != null) return existing.gameObject;

            var capRt = (RectTransform)slot.caption.transform;
            // Pad OUTWARD from the authored caption rect on both axes. The pad is a fraction of the
            // slot, because the slot's pixel width is solved per surface by HudDockLayout - a fixed
            // px inset would be right at exactly one aspect (the WO-1468 lesson, one seam over).
            var min = new Vector2(Mathf.Max(0f, capRt.anchorMin.x - CaptionPlatePadX),
                                  Mathf.Max(0f, capRt.anchorMin.y - CaptionPlatePadY));
            var max = new Vector2(Mathf.Min(1f, capRt.anchorMax.x + CaptionPlatePadX),
                                  Mathf.Min(1f, capRt.anchorMax.y + CaptionPlatePadY));

            var go = AddImage(slot.root.transform, CaptionPlateObjectName, min, max,
                              ObsidianFill, rounded: true);
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            go.transform.SetSiblingIndex(slot.caption.transform.GetSiblingIndex());
            FlowTrace.Once("HudKit", "caption-plate:" + slot.root.name,
                "WO-1671: obsidian caption plate seated under '" + slot.caption.text +
                "' (anchors " + min + ".." + max + ", caption " + capRt.anchorMin + ".." +
                capRt.anchorMax + ") - the word no longer reads against the terrain");
            return go;
        }

        /// <summary>WO-1671 — the caption plate's object name. Named ONCE here; the dock builder
        /// and the oracle both read this const rather than re-typing the literal (the
        /// StackBadgeObjectName pattern).</summary>
        public const string CaptionPlateObjectName = "CaptionPlate";
        /// <summary>How far the plate extends past the caption rect, as a fraction of the SLOT.
        /// x: 0.06 -> 0.04 outward on each side (SetCaption authors the word at 0.06..0.94);
        /// y: 0.02 -> the band reaches the slot's bottom edge and a little above the word.</summary>
        public const float CaptionPlatePadX = 0.04f;
        /// <summary>See <see cref="CaptionPlatePadX"/>.</summary>
        public const float CaptionPlatePadY = 0.04f;

        /// <summary>
        /// WO-867 TOUCH FLOOR for a slot whose VISUAL size is owned by an external layout pass.
        /// MEASURED shortfall (2340x1080 landscape, CanvasScaler 1080x1920 match 0.5 => 2119.6 x
        /// 978.3 reference units): the ActionRail area is 0.780..0.995 x / 0.040..0.420 y => 455.7
        /// x 371.8 ref px; CombatArcLayout611 sizes each ability medallion at
        /// MedallionPerPillH(0.9) * (Pill611Y1 - Pill611Y0)(0.28) * 371.8 = <b>93.7 ref px</b> —
        /// 18.3 px UNDER <see cref="MinTouchPx"/> (112).
        /// That arc geometry lives in HudKitController.cs (another lane's file, not editable from
        /// this work order), and it rewrites sizeDelta on every re-layout, so growing the rect here
        /// would just be overwritten. Instead the slot gets a TRANSPARENT, raycast-only HIT AREA
        /// child at the touch floor: uGUI bubbles the pointer event up to the slot's Button, so the
        /// tap target meets 112 px while the drawn medallion keeps the arc's size. Idempotent.
        /// </summary>
        public static void EnsureTouchFloorArea(ActionSlotHandle slot)
        {
            if (slot == null || slot.root == null) return;
            if (slot.root.transform.Find("TouchFloor") != null) return;   // idempotent

            var go = new GameObject("TouchFloor", typeof(Image));
            go.transform.SetParent(slot.root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MinTouchPx, MinTouchPx);   // FIXED reference px, not a fraction
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // invisible
            img.sprite = SolidSprite;                // non-null so uGUI raycasts the full rect
            img.raycastTarget = true;
            go.transform.SetAsFirstSibling();        // never draws over the icon/badge
        }

        /// <summary>WO-611 SOFT under-glow cooldown driver (owner pick over a hard clock sweep). Set by
        /// the HUD from the ability's cooldown state: a soft GOLD radial glow that is brightest when the
        /// cooldown starts and DEPLETES to nothing as it completes.</summary>
        public sealed class SoftGlowCooldown
        {
            internal Image glow;
            /// <summary>Drive the glow from cooldown state (remaining/total): alpha fades 0.55 -> 0.</summary>
            public void Set(float remaining, float total)
            {
                if (glow == null) return;
                float frac = (remaining > 0f && total > 0f) ? Mathf.Clamp01(remaining / total) : 0f;
                var c = glow.color; c.a = 0.55f * frac; glow.color = c;
                glow.enabled = frac > 0.001f;
            }
        }

        /// <summary>WO-611: add a soft depleting gold under-glow to an ability medallion and SUPPRESS the
        /// slot's hard radial clock-sweep. Returns the driver the HUD updates each refresh.</summary>
        public static SoftGlowCooldown AddSoftCooldownGlow(ActionSlotHandle slot)
        {
            var g = new SoftGlowCooldown();
            if (slot == null || slot.root == null) return g;
            if (slot.cdRing != null) { var cc = slot.cdRing.color; cc.a = 0f; slot.cdRing.color = cc; }   // hide hard sweep
            var go = new GameObject("SoftCdGlow", typeof(Image));
            go.transform.SetParent(slot.root.transform, false);
            var rt = (RectTransform)go.transform;
            // UNDER-glow (mockup): the gold radial glow rises FROM THE BOTTOM of the medallion,
            // so the rect is bottom-biased rather than a concentric halo.
            rt.anchorMin = new Vector2(-0.12f, -0.30f); rt.anchorMax = new Vector2(1.12f, 0.62f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = CircleSprite; img.type = Image.Type.Simple;
            img.color = new Color(C611Gold.r, C611Gold.g, C611Gold.b, 0f);
            img.raycastTarget = false; img.enabled = false;
            go.transform.SetAsFirstSibling();   // glow behind the medallion
            g.glow = img;
            return g;
        }

        /// <summary>WO-611 actionBar housing (mockup spec): a rounded (~11px) obsidian panel — vertical
        /// gradient #20252d -> #13161c — with a 1px #3d4552 EDGE border and an INNER GOLD RING
        /// (rgba(212,175,90,.28), ~3px inside). Built EXPLICITLY: AddInnerRim is a BlinkChrome-gated
        /// translucent FILL (not a ring) and ObsidianFill alone read tan/washed against the Blink slot
        /// art in the 07-05 capture. Parented as the first child so the slots draw on top.</summary>
        public static GameObject BuildActionBarHousing(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("ActionBarHousing", typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            // Public docks use the authored medieval surface. The generated blue-grey rounded
            // tray was structurally sound but visibly belonged to the retired debug HUD family.
            var medievalPanel = Resources.Load<Sprite>("UI/ElarionMedieval/buttons/button-normal-empty");
            var grad = CombatHousingSprite;
            if (medievalPanel != null)
            {
                img.sprite = medievalPanel;
                // Its 112px importer borders exceed this compact HUD band and collapse a
                // nine-slice. Scale the complete authored plate so the housing stays visible.
                img.type = Image.Type.Simple;
                img.fillCenter = true;
                img.color = Color.white;
            }
            else if (grad != null)
            {
                img.sprite = grad;
                img.type = Image.Type.Sliced;
                img.color = Color.white;   // gradient baked in the sprite
            }
            else
            {
                img.color = new Color(0.100f, 0.115f, 0.143f, 0.98f);   // flat gradient midpoint fallback
                ApplyRounded(img);
            }
            img.raycastTarget = false;
            // Authored art owns its borders. The procedural fallback keeps the old rings so a
            // missing resource can never leave an unframed action surface.
            if (medievalPanel == null)
            {
                AddRoundedRing(go.transform, "Edge", 0f, C611Edge, 1f);
                AddRoundedRing(go.transform, "GoldRing", 3f,
                    new Color(C611Gold.r, C611Gold.g, C611Gold.b, 0.28f), 3f);
            }
            go.transform.SetAsFirstSibling();
            return go;
        }

        /// <summary>WO-611: restyle a HOUSED action slot (hot-swap bar) as an obsidian steel cell —
        /// replaces the tan/khaki Blink Action_Bar_Slot art that dominated the 07-05 capture with the
        /// mockup's dark steel cell (#1a1f27) + 1px #3d4552 edge. Presentation-only.</summary>
        public static void StyleAsObsidianCell(ActionSlotHandle slot)
        {
            if (slot == null || slot.root == null || slot.frame == null) return;
            slot.frame.sprite = null;
            slot.frame.color = new Color(0.102f, 0.122f, 0.153f, 0.97f);   // #1a1f27 steel cell
            ApplyRounded(slot.frame);            // rounded when available; tinted quad otherwise (never blank)
            slot.frame.type = slot.frame.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            slot.frame.fillCenter = true;
            AddRoundedRing(slot.root.transform, "Edge", 0f, C611Edge, 1f);
        }

        /// <summary>WO-611 target lock badge — a bordered CHIP (mockup spec): dark obsidian fill, 1px
        /// state-coloured border, the crosshair art (hud/crosshair_1|2|3, imported this branch) on the
        /// left and the uppercase state WORD on the right. States: 0 unlocked (gray #8b929b), 1 locking
        /// (amber #e0b95c, PULSE), 2 locked (gold #d4af5a). Driven by <see cref="SetState"/> bound to
        /// TargetModel.HasTarget/Locked. ASCII-only text (TMP glyph landmine).</summary>
        public sealed class LockCrosshairHandle : MonoBehaviour
        {
            internal Image icon;
            internal Image border;
            internal TMP_Text label;
            private Sprite _s1, _s2, _s3;
            private int _state = -1;
            private bool _pulse;

            internal void Init(Sprite s1, Sprite s2, Sprite s3) { _s1 = s1; _s2 = s2; _s3 = s3; }

            /// <summary>0 = unlocked (gray), 1 = acquiring/locking (amber pulse), 2 = locked (gold).</summary>
            public void SetState(int state)
            {
                if (state == _state) return;
                _state = state;
                _pulse = state == 1;
                Color c = state >= 2 ? C611Gold : state == 1 ? C611Amber : C611Gray;
                Sprite s = state >= 2 ? _s3 : state == 1 ? _s2 : _s1;
                if (icon != null)
                {
                    if (s != null) { icon.sprite = s; icon.enabled = true; }
                    icon.color = state == 0 ? new Color(c.r, c.g, c.b, 0.75f) : c;
                }
                if (label != null)
                {
                    label.text = state >= 2 ? "LOCKED" : state == 1 ? "LOCKING" : "UNLOCKED";
                    label.color = c;
                    // Capture 9406: "LOCKING" wrapped to two lines and spilled out of the chip —
                    // the badge label must fit-or-ellipsize, never wrap (kit fit, floor 12 for
                    // this deliberately-small chip word; state word is redundant with the color+
                    // crosshair shape, so a squeezed word stays readable enough).
                    FitSingleLine(label, 12f);
                }
                if (border != null)
                    border.color = state >= 2 ? c : new Color(c.r, c.g, c.b, 0.8f);
            }

            private void Update()
            {
                if (!_pulse) return;
                float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
                if (icon != null)   { var c = icon.color;   c.a = a; icon.color = c; }
                if (label != null)  { var c = label.color;  c.a = a; label.color = c; }
                if (border != null) { var c = border.color; c.a = a; border.color = c; }
            }
        }

        /// <summary>WO-611: build the 3-state lock badge CHIP. Resolves hud/crosshair_1|2|3 (imported by
        /// BlinkUiImporter this branch); falls back to the kit ring/circle vector when the frames are
        /// absent so the badge never blanks. Returns the handle whose
        /// <see cref="LockCrosshairHandle.SetState"/> the HUD drives.</summary>
        public static LockCrosshairHandle BuildLockCrosshairBadge(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("LockBadge", typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // Chip fill — dark obsidian, rounded.
            var fill = go.GetComponent<Image>();
            fill.color = new Color(C611Obsidian.r, C611Obsidian.g, C611Obsidian.b, 0.92f);
            ApplyRounded(fill);
            fill.raycastTarget = false;

            // 1px state-coloured border ring.
            var border = AddRoundedRing(go.transform, "Border", 0f, C611Gray, 1f);

            // Crosshair icon (left) — sprite-first, vector ring fallback (never blank).
            var s1 = RpgUiCatalog.Get(RpgUiCatalog.RoleHud, "crosshair_1");
            var s2 = RpgUiCatalog.Get(RpgUiCatalog.RoleHud, "crosshair_2");
            var s3 = RpgUiCatalog.Get(RpgUiCatalog.RoleHud, "crosshair_3");
            var vector = RingSprite != null ? RingSprite : CircleSprite;
            var iconGo = new GameObject("Crosshair", typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0.03f, 0.14f); irt.anchorMax = new Vector2(0.28f, 0.86f);
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true; iconImg.raycastTarget = false;
            if (s1 == null && vector != null) { iconImg.sprite = vector; iconImg.type = Image.Type.Simple; }
            else if (s1 == null && vector == null) iconImg.enabled = false;

            // Uppercase state word (right) — ASCII only.
            var word = Label(go.transform, "UNLOCKED", 0f, 1f, C611Gray,
                             ElarionUi.FontMicro, TextAlignmentOptions.MidlineLeft, 0.32f, 0.97f, bold: true);
            word.raycastTarget = false;

            var h = go.AddComponent<LockCrosshairHandle>();
            h.icon = iconImg;
            h.border = border;
            h.label = word;
            h.Init(s1 != null ? s1 : vector, s2 != null ? s2 : vector, s3 != null ? s3 : vector);
            h.SetState(0);
            return h;
        }
    }
}

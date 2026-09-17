// =============================================================================
// BuildStructureInfoPanel — the Structure Info Preview panel (WO-352).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A code-built UI-Toolkit panel that appears when the player TAPS a palette card
// in Build Mode — BEFORE the structure is armed. It shows the structure's name,
// tier badge, a short description, the multi-resource build cost, footprint, key
// stats (DPS / Range / Fire Rate), and a next-tier upgrade-cost preview. A
// "Place Structure" button arms the entry (deferring the old immediate-arm tap);
// "Cancel" (or a tap on the dimmed scrim) dismisses without arming.
//
// WHY: tapping a card used to arm + drop a ghost immediately, so players placed
// the wrong structure or didn't understand the cost. This preview prevents the
// regretted placement and clarifies the upgrade path (WO-352).
//
// CODE-BUILT, NO UXML (repo rule: .uxml UIDocuments render empty in player builds
// — see BuildPaletteUI / BuildMenu). Owns its own UIDocument + PanelSettings,
// adopting a sibling's PanelSettings (same pattern as BuildPaletteUI) so it
// renders. All styling routes through ElarionUi (the ONE in-game theme — warm
// parchment + stone + runic gold). WebGL-safe: no Resources.Load, no scene-mesh
// refs, no reflection; data is pulled straight from the CatalogEntry + repo.
//
// Data source: DeNelle.Core.Catalog.CatalogEntry (name/type) + entry.repo
// (RepoProps — range / damage / fireRate / maxLevel / cost / buildCost). Cost +
// upgrade-cost resolution MIRRORS BuildModeController (multi-cost wins, else a
// crystals-only buildCost fallback) so the preview never disagrees with what the
// place / upgrade path actually charges.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core.Catalog;
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Build Mode structure preview. Call <see cref="Show"/> with the tapped
    /// CatalogEntry; raises <see cref="OnPlaceRequested"/> when the player commits
    /// to placing it and <see cref="OnCancelRequested"/> when they dismiss. Built
    /// in code so it renders in player builds.
    /// </summary>
    public sealed class BuildStructureInfoPanel : MonoBehaviour
    {
        /// <summary>Raised when "Place Structure" is tapped — arg is the previewed entry.</summary>
        public event Action<CatalogEntry> OnPlaceRequested;

        /// <summary>Raised when "Cancel" / scrim is tapped (dismiss, no arm).</summary>
        public event Action OnCancelRequested;

        private UIDocument _document;
        private VisualElement _root;     // full-screen click-catcher (scrim)
        private VisualElement _panel;    // the parchment card itself
        private CatalogEntry _current;

        // Cached content rows (reused across Show calls — no per-preview reallocation).
        private Label _nameLabel;
        private Label _tierBadge;
        private Label _descLabel;
        private Label _targetingLabel;   // "Land only" / "Land + Air" / "Air only" (towers)
        private VisualElement _costLabel;
        private Label _footprintLabel;
        private Button _firstBuyButton;   // WO-1801 — the first-buy door row (hidden unless offered)
        private FirstBuyDoor _firstBuyDoor;
        private VisualElement _statsBox;
        private VisualElement _nextTierBox;
        private Label _nextTierTitle;
        private Label _nextTierStats;
        private VisualElement _nextTierCost;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            AdoptPanelSettings();
        }

        /// <summary>
        /// Adopt a sibling UIDocument's PanelSettings so the panel renders (a freshly
        /// created doc has none → invisible). Sorts just above the palette so the preview
        /// overlays the card strip. MVVM Silo C: the sibling scan (a presentation concern,
        /// not game state) lives in <see cref="SiblingPanelSettings"/> so this View names
        /// no Find*Type.
        /// </summary>
        private void AdoptPanelSettings()
        {
            SiblingPanelSettings.AdoptInto(_document, 8);
        }

        // ── Show / Hide ────────────────────────────────────────────────────────

        /// <summary>Render + show the preview for <paramref name="entry"/>.</summary>
        public void Show(CatalogEntry entry)
        {
            if (entry == null) return;
            _current = entry;
            EnsureBuilt();
            if (_root == null) return;
            Populate(entry);
            _root.style.display = DisplayStyle.Flex;
            _root.pickingMode = PickingMode.Position;
        }

        /// <summary>Hide the preview (no arm). Idempotent.</summary>
        public void Hide()
        {
            _current = null;
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
                _root.pickingMode = PickingMode.Ignore;
            }
        }

        // ── Build (once) ─────────────────────────────────────────────────────────

        private void EnsureBuilt()
        {
            if (_root != null) return;
            var docRoot = _document != null ? _document.rootVisualElement : null;
            if (docRoot == null) return;

            // Full-screen scrim — a tap on it (outside the panel) dismisses.
            _root = new VisualElement { name = "structure-info-scrim" };
            ElarionUi.StyleScrim(_root);
            // Left-anchored on landscape rather than centred, per the spec; the scrim
            // still fills the screen so click-outside dismiss works.
            _root.style.justifyContent = Justify.Center;
            _root.style.alignItems = Align.FlexStart;
            _root.style.display = DisplayStyle.None;
            // Closed scrim must not intercept pointer input. Show()/Hide() flip this.
            _root.pickingMode = PickingMode.Ignore;
            _root.RegisterCallback<PointerDownEvent>(OnScrimPointerDown);
            docRoot.Add(_root);

            // The parchment card.
            _panel = new VisualElement { name = "structure-info-panel" };
            ElarionUi.StylePanel(_panel);
            _panel.style.width = 300;
            _panel.style.maxHeight = Length.Percent(86f);
            _panel.style.marginLeft = 18;
            _panel.style.paddingTop = ElarionUi.PadPanel;
            _panel.style.paddingBottom = ElarionUi.PadPanel;
            _panel.style.paddingLeft = ElarionUi.PadPanel;
            _panel.style.paddingRight = ElarionUi.PadPanel;
            _panel.style.flexDirection = FlexDirection.Column;
            // Swallow taps so a click INSIDE the panel never bubbles to the scrim-dismiss.
            _panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            _root.Add(_panel);

            // Header: name + tier badge.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            _nameLabel = new Label("Structure");
            _nameLabel.style.color = ElarionUi.Gilt;
            _nameLabel.style.fontSize = ElarionUi.FontHead;
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.whiteSpace = WhiteSpace.Normal;
            _nameLabel.style.flexShrink = 1;
            header.Add(_nameLabel);

            _tierBadge = new Label("Lv 1");
            _tierBadge.style.color = ElarionUi.Aether;
            _tierBadge.style.fontSize = ElarionUi.FontLabel;
            _tierBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tierBadge.style.marginLeft = 8;
            _tierBadge.style.flexShrink = 0;
            header.Add(_tierBadge);
            _panel.Add(header);

            _panel.Add(ElarionUi.MakeRule());

            // Short description.
            _descLabel = new Label();
            _descLabel.style.color = ElarionUi.ParchmentDim;
            _descLabel.style.fontSize = ElarionUi.FontLabel;
            _descLabel.style.whiteSpace = WhiteSpace.Normal;
            _descLabel.style.marginBottom = 8;
            _panel.Add(_descLabel);

            // Targeting capability (towers only): "Land only" / "Land + Air" / "Air only".
            // Colorblind-safe: the meaning is carried by the TEXT label + a distinct leading
            // shape glyph per capability (never color alone). Gilt so it reads as a key stat.
            _targetingLabel = new Label();
            _targetingLabel.style.color = ElarionUi.Gilt;
            _targetingLabel.style.fontSize = ElarionUi.FontLabel;
            _targetingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _targetingLabel.style.whiteSpace = WhiteSpace.Normal;
            _targetingLabel.style.marginBottom = 8;
            _panel.Add(_targetingLabel);

            // Cost.
            _costLabel = new VisualElement();
            _panel.Add(_costLabel);

            // WO-1801 — THE FIRST-BUY DOOR, directly under the price it is a remedy for.
            // ONE row, built hidden, shown only when FirstBuyDoorModel says so. It sits here
            // because this is the panel the player is looking at when they decide they want a
            // structure and find they cannot pay for it — the highest-intent moment the economy
            // produces, and until this ticket a dead end (the store had no door here at all).
            // The View decides NOTHING: every rule, and the sentence itself, is the model's.
            _firstBuyButton = new Button(OnFirstBuyTapped) { text = string.Empty };
            ElarionUi.StyleButton(_firstBuyButton, ElarionUi.ButtonKind.Gold);
            _firstBuyButton.style.marginTop = 4;
            _firstBuyButton.style.whiteSpace = WhiteSpace.Normal;
            _firstBuyButton.style.display = DisplayStyle.None;
            _panel.Add(_firstBuyButton);

            // Footprint.
            _footprintLabel = MakeKeyValue("Footprint", "1x1");
            _panel.Add(_footprintLabel);

            // Stats box (DPS / Range / Fire Rate — rows added per-entry).
            _statsBox = new VisualElement();
            _statsBox.style.marginTop = 8;
            _statsBox.style.flexDirection = FlexDirection.Column;
            _panel.Add(_statsBox);

            // Next-tier preview box (blue/aether info panel).
            _nextTierBox = new VisualElement();
            _nextTierBox.style.marginTop = 10;
            _nextTierBox.style.paddingTop = 8; _nextTierBox.style.paddingBottom = 8;
            _nextTierBox.style.paddingLeft = 10; _nextTierBox.style.paddingRight = 10;
            _nextTierBox.style.backgroundColor = ElarionUi.AetherDim;
            ElarionUi.SetRadius(_nextTierBox, ElarionUi.RadiusMd);
            ElarionUi.SetBorderWidth(_nextTierBox, 1);
            ElarionUi.SetBorderColor(_nextTierBox, new Color(ElarionUi.Aether.r, ElarionUi.Aether.g, ElarionUi.Aether.b, 0.6f));

            _nextTierTitle = new Label("Upgrade to Lv 2");
            _nextTierTitle.style.color = ElarionUi.Parchment;
            _nextTierTitle.style.fontSize = ElarionUi.FontLabel;
            _nextTierTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nextTierBox.Add(_nextTierTitle);

            _nextTierStats = new Label();
            _nextTierStats.style.color = ElarionUi.ParchmentDim;
            _nextTierStats.style.fontSize = ElarionUi.FontLabel;
            _nextTierStats.style.whiteSpace = WhiteSpace.Normal;
            _nextTierBox.Add(_nextTierStats);

            _nextTierCost = new VisualElement();
            _nextTierCost.style.marginTop = 2;
            _nextTierBox.Add(_nextTierCost);
            _panel.Add(_nextTierBox);

            // Action buttons.
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.SpaceBetween;
            actions.style.marginTop = 12;

            var placeBtn = new Button(() =>
            {
                var entry = _current;
                Hide();
                OnPlaceRequested?.Invoke(entry);
            })
            { text = "Place Structure" };
            ElarionUi.StyleButton(placeBtn, ElarionUi.ButtonKind.Gold);
            placeBtn.style.flexGrow = 1;
            placeBtn.style.marginRight = 8;
            actions.Add(placeBtn);

            var cancelBtn = new Button(() =>
            {
                Hide();
                OnCancelRequested?.Invoke();
            })
            { text = "Cancel" };
            ElarionUi.StyleButton(cancelBtn, ElarionUi.ButtonKind.Neutral);
            cancelBtn.style.minWidth = 88;
            actions.Add(cancelBtn);
            _panel.Add(actions);
        }

        /// <summary>A "click outside the panel" on the scrim dismisses the preview.</summary>
        private void OnScrimPointerDown(PointerDownEvent evt)
        {
            // Only the scrim itself (not a child bubbling up — the panel stops props).
            if (evt.target == _root)
            {
                Hide();
                OnCancelRequested?.Invoke();
            }
        }

        // ── Populate (per Show) ──────────────────────────────────────────────────

        private void Populate(CatalogEntry e)
        {
            // MVVM Silo C: ALL the cost / DPS / tier / footprint math is the VM's
            // (StructureCardVM) — this View is a dumb skin that paints the projection.
            var card = StructureCardVM.CreateForEntry(e);

            _nameLabel.text = card.DisplayName;
            _tierBadge.text = card.TierBadge;
            _descLabel.text = card.Description;

            // Targeting line — towers only; hidden (no reserved gap) for non-combat structures.
            string targeting = card.TargetingLine;
            if (string.IsNullOrEmpty(targeting))
            {
                _targetingLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _targetingLabel.style.display = DisplayStyle.Flex;
                _targetingLabel.text = targeting;
            }

            // First-build freebie (owner 2026-07-13): the info panel agrees with the
            // palette/validator/commit — a live freebie reads "FREE" (the WORD, never
            // color-alone; ASCII); after consumption it reverts to the normal cost.
            _costLabel.Clear();
            var currentCostParts = CostParts(card.EffectiveCost);
            _costLabel.Add(card.Freebie ? new Label("Cost: FREE")
                : currentCostParts.Count == 0 ? new Label("Cost: Free")
                : CostRowElement.Build(currentCostParts, "Cost:"));
            SetKeyValue(_footprintLabel, "Footprint", card.FootprintLabel);
            RenderFirstBuyDoor(e, card);

            RenderCurrentStats(card);
            RenderNextTierPreview(card);
        }

        /// <summary>
        /// WO-1801 — paints (or hides) the ONE first-buy line. Every decision, gate and word comes
        /// from <see cref="FirstBuyDoorModel"/>; this method sets .text and a display flag, and tells
        /// the model it was actually painted so the impression is counted once and the no-nag latch
        /// closes. It reads no game state of its own — the same rule that failed BuildPreviewModal
        /// at the UI-MVVM oracle.
        /// </summary>
        private void RenderFirstBuyDoor(CatalogEntry e, StructureCardVM card)
        {
            if (_firstBuyButton == null) return;

            // An affordable build never sees an offer — that is the line between a remedy and a
            // storefront (the WO-1037 guardrail, honoured here too). The model re-checks it.
            _firstBuyDoor = card.Affordable
                ? FirstBuyDoor.None("affordable", e != null ? e.id : null)
                : FirstBuyDoorModel.Resolve(e, card.EffectiveCost);

            if (!_firstBuyDoor.Show)
            {
                _firstBuyButton.style.display = DisplayStyle.None;
                _firstBuyButton.text = string.Empty;
                return;
            }

            _firstBuyButton.text = _firstBuyDoor.Caption;
            _firstBuyButton.style.display = DisplayStyle.Flex;
            FirstBuyDoorModel.NotifyShown(_firstBuyDoor);
        }

        /// <summary>The tap: hand it straight back to the model, which routes to the store.</summary>
        private void OnFirstBuyTapped()
        {
            var door = _firstBuyDoor;
            if (!door.Show) return;
            Hide();
            FirstBuyDoorModel.Tap(door);
        }

        /// <summary>Current-tier key stats — DPS, Range, Fire Rate (or a "Type" row), from the VM.</summary>
        private void RenderCurrentStats(StructureCardVM card)
        {
            _statsBox.Clear();
            foreach (var row in card.CurrentStats)
                _statsBox.Add(MakeKeyValue(row.Key, row.Value));
        }

        /// <summary>Next-tier upgrade preview (L1→L2 stat deltas + cost), from the VM. Hidden for
        /// single-tier entries.</summary>
        private void RenderNextTierPreview(StructureCardVM card)
        {
            if (!card.HasNextTier)
            {
                _nextTierBox.style.display = DisplayStyle.None;
                return;
            }
            _nextTierBox.style.display = DisplayStyle.Flex;
            _nextTierTitle.text = card.NextTierTitle;
            _nextTierStats.text = card.NextTierStats;
            var nextCostParts = CostParts(card.NextTierCost);
            _nextTierCost.Clear();
            _nextTierCost.Add(nextCostParts.Count == 0 ? new Label("Cost: Free") : CostRowElement.Build(nextCostParts, "Cost:"));
        }

        // ── Cost string formatting (pure presentation; the math lives in the VM) ──────

        /// <summary>Compact multi-resource cost string (skips zero slots). The info-panel format
        /// (crystals as "*N"); mirrors the pre-MVVM view verbatim.</summary>
        private static IReadOnlyList<CostPart> CostParts(DeNelle.Core.Catalog.ResourceCost c)
        {
            return CostFormat.Parts(new[]
            {
                ("wood", "Wood", c.wood), ("stone", "Stone", c.stone),
                ("iron", "Iron", c.iron), ("crystal", "Crystals", c.crystals)
            });
        }

        // ── Small key/value row helper ───────────────────────────────────────────

        private static Label MakeKeyValue(string key, string value)
        {
            var row = new Label();
            row.style.color = ElarionUi.Parchment;
            row.style.fontSize = ElarionUi.FontLabel;
            row.style.marginTop = 2;
            SetKeyValue(row, key, value);
            return row;
        }

        private static void SetKeyValue(Label row, string key, string value)
        {
            if (row != null) row.text = key + ":  " + value;
        }
    }
}

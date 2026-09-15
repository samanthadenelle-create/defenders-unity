// =============================================================================
// TowerPlacementRotateMenu — WO-334 (Preview & Rotate tower-placement panel).
// -----------------------------------------------------------------------------
// A modal UIElements panel opened when the player places a tower. It shows a
// live 3D RenderTexture preview (driven by TowerPreviewCamera) and three axis
// sliders (X/Pitch, Y/Yaw, Z/Roll) to dial in the placement rotation before
// committing.
//
// PROCEDURAL UIElements ONLY — NO UXML (CLAUDE.md §8). Every VisualElement is
// built in code; all styles are inline. Renders in player builds by adopting a
// sibling/scene UIDocument's PanelSettings (the documented "code-built UIDocument
// with no PanelSettings renders nothing" trap — see MEMORY).
//
// API:
//   Open(TowerData, double costSkr, Quaternion initialRotation,
//        Action<Quaternion> onConfirm, Action onCancel)
//   Close()
//
// TowerData = DeNelle.Core.Data.TowerData (the real tower ScriptableObject).
// It has NO direct prefab field — the Level-1 visual is upgrades[0].visualPrefab
// and there is no PreviewSprite, so the thumbnail uses a coloured-square fallback.
//
// BUILD-RENDER RISK (flagged in handback): UIToolkit panels rendering in player
// BUILDS is a known project landmine. This is code-built (better than UXML) and
// adopts a live PanelSettings, but it still needs a felt-test in an actual build,
// not just the editor.
// =============================================================================

using System;
using System.IO;
using System.Globalization;
using DeNelle.Core.Data;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DeNelle.Village
{
    /// <summary>
    /// Modal "Preview &amp; Rotate" panel for tower placement. Driven by the build
    /// system: call <see cref="Open"/> with the chosen tower, then it invokes the
    /// onConfirm callback with the final <see cref="Quaternion"/> or onCancel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TowerPlacementRotateMenu : MonoBehaviour
    {
        // ── Palette — re-based on the canonical town-HUD dark-glass theme
        //     (ElarionUiKit Glass / GlassDeep / Track / Accent + ElarionUi palette) so
        //     the orient editor reads as ONE designed UI with the HUD + build panels +
        //     shop. Axis tints + rune decoration stay local (below). No hand-rolled hex. ─
        private static readonly Color PanelBg     = ElarionUiKit.Glass;     // dark-glass panel fill
        private static readonly Color RuneBorder  = ElarionUiKit.Accent;    // thin runic-gold rim
        private static readonly Color TitleGold   = ElarionUi.Gilt;
        private static readonly Color ViewportBg  = ElarionUiKit.Track;     // recessed near-black well
        private static readonly Color ReadoutBg   = ElarionUiKit.Track;
        private static readonly Color ReadoutBdr  = ElarionUiKit.AccentSoft; // faint gold inner rim
        private static readonly Color ConfirmBg   = ElarionUi.GoldButton;   // gold CTA fill
        private static readonly Color ConfirmBdr  = ElarionUi.Gilt;         // bright gilt rim on CTA
        private static readonly Color ConfirmTxt  = ElarionUi.Ink;          // dark ink on gold CTA
        private static readonly Color CancelBg    = ElarionUiKit.GlassDeep; // deep-glass secondary
        private static readonly Color CancelTxt   = ElarionUi.ParchmentDim;
        private static readonly Color ResetBg     = ElarionUiKit.GlassDeep; // deep-glass tertiary
        private static readonly Color ResetTxt    = ElarionUi.ParchmentDim;
        private static readonly Color RuneStripTxt = ElarionUiKit.AccentSoft; // faint gold rune strip

        private static readonly Color AxisX = Hex(0xd0, 0x40, 0x40); // Pitch
        private static readonly Color AxisY = Hex(0x38, 0xb8, 0x38); // Yaw
        private static readonly Color AxisZ = Hex(0x38, 0x78, 0xc0); // Roll

        private const string RuneGlyphs = "+ x + o + = + . ";
        private const string FontPath   = "Assets/_Modules/Village/Fonts/Cinzel-Regular.ttf";

        // ── State ───────────────────────────────────────────────────────────────
        /// <summary>True while the panel is shown — lets callers (BuildModeController) freeze
        /// the placement loop behind the modal.</summary>
        public bool IsOpen { get; private set; }

        private UIDocument _document;
        private VisualElement _root;

        private TowerData          _towerData;
        private GameObject         _previewPrefab;   // resolved preview model (TowerData→upgrades[0] OR caller-supplied)
        private string             _displayName;     // caller-supplied name (prefab overload); null → derive from _towerData
        private double             _costSkr;
        private Quaternion         _initialRotation;
        private Vector3            _initialEuler;
        private Action<Quaternion> _onConfirm;
        private Action             _onCancel;

        // Dev "orient & report" mode (AdminOverlay): when set, Confirm LOGS an
        // [OrientRecipe] line + appends a JSON recipe instead of placing a tower.
        private string             _devOrientId;

        private int   _snapDegrees = 45;          // 0=off, 15, 45, 90
        private float _xDeg, _yDeg, _zDeg;        // current (snapped) euler values
        // Per-axis (non-uniform) scale — stretch X or Z, keep Y (height). These are the
        // EFFECTIVE multipliers (uniform legacy `scale` is folded into all three on seed).
        private float _sxScale = 1f, _syScale = 1f, _szScale = 1f;

        // Per-axis scale slider range (modular pieces — wide enough to double a wall, thin
        // it to a slab, without runaway values).
        private const float ScaleMin = 0.1f;
        private const float ScaleMax = 5f;

        private Slider _xSlider, _ySlider, _zSlider;
        private Label  _xReadout, _yReadout, _zReadout;
        // Numeric entry fields alongside the euler sliders.
        private TextField _xInput, _yInput, _zInput;
        // Per-axis SCALE rows (slider + numeric field), mirroring the euler rows.
        private Slider    _sxSlider, _sySlider, _szSlider;
        private TextField _sxInput, _syInput, _szInput;
        // Dev-orient: a runtime dropdown of catalog ids (standalone path — pick instead of type).
        private DropdownField _catalogIdDrop;
        private VisualElement _viewport;

        private TowerPreviewCamera _preview;
        private Font _cinzel;

        // F8-30 — registration with the PanelManager modal arbiter, so ESC (PauseGate →
        // CloseOpen), the tutorial-skip / hostile-posture CloseAll sweeps, and the softlock
        // watchdog can all SEE and CLOSE this modal. Without it the full-screen scrim had no
        // external release path — the owner's tutorial click-lock. Close routes through
        // OnCancelClicked so the caller's onCancel fires (build mode un-freezes cleanly).
        private PanelHandle _panelHandle;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            AdoptPanelSettings();
            TryLoadCinzel();
        }

        private void OnDisable()
        {
            // F8-30 — a disable/destroy while open must be a FULL release, not just a
            // preview teardown: clear IsOpen (BuildModeController's freeze gate reads it),
            // hide the scrim, and release the PanelManager registration. Previously this
            // left IsOpen=true + the arbiter slot held — a leaked click-lock.
            if (IsOpen)
                FlowTrace.Step("Orient", "OnDisable while OPEN — forcing full release (preview + PanelManager + scrim).");
            IsOpen = false;
            DisposePreview();
            if (_root != null) _root.style.display = DisplayStyle.None;
            if (_document != null) _document.enabled = false;
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        private void Update()
        {
            // CRITICAL: the preview camera is manually driven — re-render every
            // frame so the RenderTexture stays live (URP won't auto-render it).
            if (_preview != null && _preview.IsValid)
                _preview.SetRotation(Quaternion.Euler(_xDeg, _yDeg, _zDeg));
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Open the panel for <paramref name="towerData"/> with the given starting rotation.</summary>
        public void Open(
            TowerData towerData,
            double costSkr,
            Quaternion initialRotation,
            Action<Quaternion> onConfirm,
            Action onCancel)
        {
            _towerData = towerData;
            _devOrientId = null;
            // The TowerData Level-1 visual is upgrades[0].visualPrefab — resolve it once
            // here so the shared core path is prefab-driven for BOTH overloads.
            GameObject prefab = towerData != null
                                && towerData.upgrades != null
                                && towerData.upgrades.Length > 0
                ? towerData.upgrades[0]?.visualPrefab
                : null;
            string name = towerData != null ? towerData.towerName : "Tower";

            OpenCore(prefab, name, costSkr, initialRotation, onConfirm, onCancel);
        }

        /// <summary>
        /// Prefab overload — drive the same Preview &amp; Rotate UI directly off a
        /// loaded visual prefab + display name (used by Build Mode, which arms a
        /// CatalogEntry with a visualPrefab PATH, not a TowerData SO). Shares the
        /// same UI/preview core as the TowerData overload.
        /// </summary>
        public void Open(
            GameObject previewPrefab,
            string displayName,
            double costSkr,
            Quaternion initialRotation,
            Action<Quaternion> onConfirm,
            Action onCancel)
        {
            _towerData = null;   // prefab path — no SO; tier label falls back to "TIER I"
            _devOrientId = null;
            OpenCore(previewPrefab, displayName, costSkr, initialRotation, onConfirm, onCancel);
        }

        /// <summary>
        /// DEV ONLY (AdminOverlay → "Orient Asset"): open the 3-axis panel on a
        /// catalog item's visual prefab to dial in an orientation offset. On
        /// Confirm it does NOT place anything — it LOGS a copy-pasteable
        /// <c>[OrientRecipe]</c> line and appends a JSON recipe line to
        /// Assets/Resources/Data/orientation-recipes.json, so the offset can be
        /// baked into the catalog. Reflection-friendly signature (string + GameObject)
        /// so the HUD asmdef can invoke it without referencing DeNelle.Village.
        /// </summary>
        public void OpenDevOrient(string catalogId, GameObject previewPrefab, string displayName)
        {
            _towerData   = null;
            _devOrientId = string.IsNullOrEmpty(catalogId) ? "unknown" : catalogId;

            // Seed from the entry's EXISTING orientation (if any) so re-opening shows the
            // current correction rather than identity, and the scale field starts there.
            var entry = CatalogRegistry.Get(_devOrientId);
            Quaternion initial = Quaternion.identity;
            _sxScale = _syScale = _szScale = 1f;
            if (entry != null && entry.orientation != null && entry.orientation.manual)
            {
                initial = Quaternion.Euler(entry.orientation.Euler);
                // Seed from the EFFECTIVE per-axis scale: a legacy uniform `scale` shows as
                // X=Y=Z=scale; an already-stretched entry shows its per-axis values.
                Vector3 es = entry.orientation.EffectiveScale;
                _sxScale = es.x; _syScale = es.y; _szScale = es.z;
            }

            OpenCore(
                previewPrefab,
                string.IsNullOrEmpty(displayName) ? _devOrientId : displayName,
                0d,
                initial,
                null,   // confirm handled by the dev-orient branch in OnConfirmClicked
                null);
        }

        /// <summary>Shared open path for both overloads — builds the panel + preview off a prefab.</summary>
        private void OpenCore(
            GameObject previewPrefab,
            string displayName,
            double costSkr,
            Quaternion initialRotation,
            Action<Quaternion> onConfirm,
            Action onCancel)
        {
            _previewPrefab   = previewPrefab;
            _displayName     = displayName;
            _costSkr         = costSkr;
            _initialRotation = initialRotation;
            _initialEuler    = NormalizeEuler(initialRotation.eulerAngles);
            _onConfirm       = onConfirm;
            _onCancel        = onCancel;

            _xDeg = _initialEuler.x;
            _yDeg = _initialEuler.y;
            _zDeg = _initialEuler.z;

            Debug.Log($"[Orient] OpenCore: {displayName} prefab={(previewPrefab != null ? previewPrefab.name : "<none>")}");
            BuildPanel();
            BeginPreview();
            Debug.Log($"[Orient] panel built; preview valid={(_preview != null && _preview.IsValid)}");
            ShowPanel();
        }

        /// <summary>Close the panel and tear down the preview rig. Fires NO callback.
        /// F8-30 — EVERY exit path funnels here (Confirm, Cancel, ESC via PanelManager,
        /// arbiter swap, disable/destroy) and this releases everything the modal holds:
        /// the preview camera rig, the full-screen scrim, and the PanelManager slot.</summary>
        public void Close()
        {
            IsOpen = false;
            DisposePreview();
            if (_root != null) _root.style.display = DisplayStyle.None;
            if (_document != null) _document.enabled = false;
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            FlowTrace.Step("Orient", "panel CLOSED — released: preview rig disposed, scrim hidden, PanelManager slot freed.");
        }

        // ── Panel construction ──────────────────────────────────────────────────

        private void BuildPanel()
        {
            if (_document == null) return;
            _document.enabled = true;            // enable BEFORE reading root (Awake/Close disable it → rootVisualElement is null)
            AdoptPanelSettings();                 // retry adopting a live PanelSettings (a sibling may exist now in Play)
            var rootVE = _document.rootVisualElement;
            if (rootVE == null)
            {
                Debug.LogWarning("[Orient] rootVisualElement null (no PanelSettings adopted) — aborting BuildPanel.");
                return;
            }
            rootVE.Clear();

            // Full-screen dimmer + centred card.
            _root = new VisualElement();
            ElarionUi.StyleScrim(_root);
            // Optional full-screen menu backdrop (Resources/UI/menu_bg) over the scrim.
            ElarionUi.TryApplyMenuBackground(_root);

            var card = new VisualElement();
            // Swappable Resources/UI/panel_bg when present, else solid stone fill.
            if (!ElarionUi.TryApplyPanelBackground(card))
                card.style.backgroundColor = PanelBg;
            SetBorder(card, RuneBorder, 2);
            SetRadius(card, 10);
            card.style.minWidth   = 440;
            card.style.maxWidth   = 520;
            card.style.paddingTop = 6; card.style.paddingBottom = 6;
            card.style.paddingLeft = 6; card.style.paddingRight = 6;

            card.Add(BuildRuneStrip(false));      // top strip

            var body = new VisualElement();
            body.style.paddingTop = 6; body.style.paddingBottom = 6;
            body.style.paddingLeft = 14; body.style.paddingRight = 14;

            body.Add(BuildHeader());
            // DEV (standalone orient): a dropdown of catalog ids so the owner can pick the
            // asset to orient instead of typing it (the build-mode path arms the entry, so the
            // dropdown is informational there). Built from CatalogRegistry at runtime.
            if (!string.IsNullOrEmpty(_devOrientId))
                body.Add(BuildCatalogIdRow());
            body.Add(BuildViewport());
            body.Add(BuildAxisRow("X Axis (Pitch)", AxisX, _xDeg, out _xSlider, out _xReadout, out _xInput, OnXChanged, OnXInput, () => ResetAxis(0)));
            body.Add(BuildAxisRow("Y Axis (Yaw)",   AxisY, _yDeg, out _ySlider, out _yReadout, out _yInput, OnYChanged, OnYInput, () => ResetAxis(1)));
            body.Add(BuildAxisRow("Z Axis (Roll)",  AxisZ, _zDeg, out _zSlider, out _zReadout, out _zInput, OnZChanged, OnZInput, () => ResetAxis(2)));
            // Per-axis scale rows (stretch X / Z, keep Y height) — mirror the euler rows.
            body.Add(BuildScaleSectionLabel());
            body.Add(BuildScaleAxisRow("Scale X (width)",  AxisX, _sxScale, out _sxSlider, out _sxInput, OnSxChanged, OnSxInput, () => ResetScaleAxis(0)));
            body.Add(BuildScaleAxisRow("Scale Y (height)", AxisY, _syScale, out _sySlider, out _syInput, OnSyChanged, OnSyInput, () => ResetScaleAxis(1)));
            body.Add(BuildScaleAxisRow("Scale Z (depth)",  AxisZ, _szScale, out _szSlider, out _szInput, OnSzChanged, OnSzInput, () => ResetScaleAxis(2)));
            body.Add(BuildInfoBar());
            body.Add(BuildControlsRow());

            card.Add(body);
            card.Add(BuildRuneStrip(false));      // bottom strip

            // Side rune strips overlaid (rotated) — decorative.
            var framed = new VisualElement();
            framed.style.flexDirection = FlexDirection.Row;
            framed.Add(BuildRuneStrip(true));
            framed.Add(card);
            framed.Add(BuildRuneStrip(true));

            _root.Add(framed);
            rootVE.Add(_root);
        }

        private VisualElement BuildHeader()
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems     = Align.Center;
            row.style.marginBottom   = 8;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            var badge = new Label("Preview & Rotate");
            badge.style.fontSize = 11;
            badge.style.color    = ConfirmTxt;
            badge.style.backgroundColor = ConfirmBg;
            badge.style.paddingTop = 2; badge.style.paddingBottom = 2;
            badge.style.paddingLeft = 8; badge.style.paddingRight = 8;
            SetRadius(badge, 4);
            badge.style.marginRight = 10;
            ApplyFont(badge);
            left.Add(badge);

            var title = new Label("TOWER PLACEMENT");
            title.style.fontSize = 15;
            title.style.color    = TitleGold;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 2;
            ApplyFont(title);
            left.Add(title);

            row.Add(left);

            var hammer = new Label("");
            hammer.style.fontSize = 16;
            row.Add(hammer);
            return row;
        }

        private VisualElement BuildViewport()
        {
            _viewport = new VisualElement();
            _viewport.style.height = 200;
            _viewport.style.backgroundColor = ViewportBg;
            SetBorder(_viewport, ReadoutBdr, 1);
            SetRadius(_viewport, 6);
            _viewport.style.marginBottom = 12;
            _viewport.style.alignItems     = Align.Center;
            _viewport.style.justifyContent = Justify.Center;
            return _viewport;
        }

        // axisIndex via the reset closure; slider/readout/input returned by out params.
        private VisualElement BuildAxisRow(
            string label, Color accent, float initial,
            out Slider slider, out Label readout, out TextField input,
            EventCallback<ChangeEvent<float>> onChange,
            EventCallback<ChangeEvent<string>> onInput, Action onReset)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 6;

            var name = new Label(label);
            name.style.fontSize = 11;
            name.style.color    = accent;
            name.style.width    = 96;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(name);

            slider = new Slider(-180f, 180f) { value = initial };
            slider.style.flexGrow = 1;
            slider.style.marginLeft = 4; slider.style.marginRight = 8;
            TintSlider(slider, accent);
            slider.RegisterValueChangedCallback(onChange);
            row.Add(slider);

            readout = new Label($"{Mathf.RoundToInt(initial)}°");
            readout.style.width    = 38;
            readout.style.fontSize = 11;
            readout.style.color    = TitleGold;
            readout.style.backgroundColor = ReadoutBg;
            SetBorder(readout, ReadoutBdr, 1);
            SetRadius(readout, 4);
            readout.style.unityTextAlign = TextAnchor.MiddleCenter;
            readout.style.paddingTop = 3; readout.style.paddingBottom = 3;
            row.Add(readout);

            // Editable numeric entry — type an exact angle (commits on Enter / focus-out).
            input = new TextField { value = FormatDeg(initial), isDelayed = true };
            input.style.width = 52;
            input.style.marginLeft = 4;
            input.RegisterValueChangedCallback(onInput);
            row.Add(input);

            var reset = new Button(() => onReset()) { text = "Rst" };
            reset.style.width = 26; reset.style.height = 22;
            reset.style.marginLeft = 4;
            reset.style.backgroundColor = ResetBg;
            reset.style.color = ResetTxt;
            SetBorder(reset, ReadoutBdr, 1);
            SetRadius(reset, 4);
            reset.style.fontSize = 12;
            row.Add(reset);

            return row;
        }

        /// <summary>Small section divider/label above the three per-axis scale rows.</summary>
        private VisualElement BuildScaleSectionLabel()
        {
            var label = new Label("SCALE  -  non-uniform (x multiplier)");
            label.style.fontSize = 10;
            label.style.color    = RuneStripTxt;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 1;
            label.style.marginTop    = 4;
            label.style.marginBottom = 4;
            return label;
        }

        /// <summary>
        /// One per-axis SCALE row (slider + numeric field + reset), styled to MIRROR the
        /// euler <see cref="BuildAxisRow"/> rows. Range <see cref="ScaleMin"/>..<see cref="ScaleMax"/>;
        /// readout shows the multiplier (e.g. "2.0×"). axisIndex via the reset closure.
        /// </summary>
        private VisualElement BuildScaleAxisRow(
            string label, Color accent, float initial,
            out Slider slider, out TextField input,
            EventCallback<ChangeEvent<float>> onChange,
            EventCallback<ChangeEvent<string>> onInput, Action onReset)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 6;

            var name = new Label(label);
            name.style.fontSize = 11;
            name.style.color    = accent;
            name.style.width    = 96;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(name);

            slider = new Slider(ScaleMin, ScaleMax) { value = Mathf.Clamp(initial, ScaleMin, ScaleMax) };
            slider.style.flexGrow = 1;
            slider.style.marginLeft = 4; slider.style.marginRight = 8;
            TintSlider(slider, accent);
            slider.RegisterValueChangedCallback(onChange);
            row.Add(slider);

            // "×" multiplier badge (styled like the euler ° readout) — purely decorative.
            var unit = new Label("x");
            unit.style.width    = 16;
            unit.style.fontSize = 11;
            unit.style.color    = TitleGold;
            unit.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(unit);

            // Editable numeric entry — doubles as the live readout (commits on Enter / focus-out).
            input = new TextField { value = FormatScale(initial), isDelayed = true };
            input.style.width = 52;
            input.style.marginLeft = 4;
            input.RegisterValueChangedCallback(onInput);
            row.Add(input);

            var reset = new Button(() => onReset()) { text = "Rst" };
            reset.style.width = 26; reset.style.height = 22;
            reset.style.marginLeft = 4;
            reset.style.backgroundColor = ResetBg;
            reset.style.color = ResetTxt;
            SetBorder(reset, ReadoutBdr, 1);
            SetRadius(reset, 4);
            reset.style.fontSize = 12;
            row.Add(reset);

            return row;
        }

        /// <summary>
        /// DEV: a dropdown of catalog ids read from CatalogRegistry at runtime, so the owner
        /// can SELECT the asset to orient (standalone path) rather than typing it. Picking a
        /// new id re-opens the editor on that entry's prefab. Reflection-free — the menu
        /// already references DeNelle.Core.Catalog.
        /// </summary>
        private VisualElement BuildCatalogIdRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 8;

            var name = new Label("Catalog id");
            name.style.fontSize = 11;
            name.style.color    = TitleGold;
            name.style.width    = 96;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(name);

            var ids = CollectCatalogIds();
            int idx = Mathf.Max(0, ids.IndexOf(_devOrientId));
            _catalogIdDrop = new DropdownField(ids, ids.Count > 0 ? idx : -1);
            _catalogIdDrop.style.flexGrow = 1;
            _catalogIdDrop.RegisterValueChangedCallback(OnCatalogIdPicked);
            row.Add(_catalogIdDrop);
            return row;
        }

        /// <summary>All registered catalog ids across every CatalogType (sorted, deduped).</summary>
        private static System.Collections.Generic.List<string> CollectCatalogIds()
        {
            var set = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogType t in Enum.GetValues(typeof(CatalogType)))
            {
                var list = CatalogRegistry.OfType(t);
                if (list == null) continue;
                foreach (var e in list)
                    if (e != null && !string.IsNullOrEmpty(e.id)) set.Add(e.id);
            }
            return new System.Collections.Generic.List<string>(set);
        }

        /// <summary>Pick a different catalog id from the dropdown → re-open the editor on it.</summary>
        private void OnCatalogIdPicked(ChangeEvent<string> evt)
        {
            string id = evt.newValue;
            if (string.IsNullOrEmpty(id)) return;
            var entry = CatalogRegistry.Get(id);
            string path = entry != null && !string.IsNullOrEmpty(entry.visualPrefabPath) ? entry.visualPrefabPath : id;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) { Debug.LogWarning($"[Orient] dropdown: '{id}' prefab not loadable ('{path}')."); return; }
            string name = entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : id;
            OpenDevOrient(id, prefab, name);
        }

        private VisualElement BuildInfoBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection  = FlexDirection.Row;
            bar.style.alignItems     = Align.Center;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.backgroundColor = ViewportBg;
            SetBorder(bar, ReadoutBdr, 1);
            SetRadius(bar, 6);
            bar.style.paddingTop = 6; bar.style.paddingBottom = 6;
            bar.style.paddingLeft = 8; bar.style.paddingRight = 8;
            bar.style.marginTop = 6; bar.style.marginBottom = 10;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            // Thumbnail: TowerData has no PreviewSprite — coloured-square fallback.
            var thumb = new VisualElement();
            thumb.style.width = 36; thumb.style.height = 36;
            thumb.style.backgroundColor = RuneBorder;
            SetRadius(thumb, 4);
            thumb.style.marginRight = 10;
            var thumbIcon = new Label("TWR");
            thumbIcon.style.fontSize = 20;
            thumbIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            thumbIcon.style.flexGrow = 1;
            thumb.Add(thumbIcon);
            left.Add(thumb);

            string towerName = !string.IsNullOrEmpty(_displayName)
                ? _displayName
                : (_towerData != null ? _towerData.towerName : "Tower");
            var nameLabel = new Label($"{towerName}  -  {TierLabel()}");
            nameLabel.style.fontSize = 12;
            nameLabel.style.color    = ConfirmTxt;
            ApplyFont(nameLabel);
            left.Add(nameLabel);

            bar.Add(left);

            // WO-1755: this label used to render the wallet-rail currency name after the amount —
            // the ONE RENDERED forbidden-token literal of the six offenders the WO-1740 RCA
            // measured in the rejected Play AAB's global-metadata.dat (offset 2,043,262).
            // This menu is the editor/dev OFFSET tool (BuildModeController.cs:129-131), reached
            // only by AdminOverlay reflection and AutoPilotDriver, so a rail-neutral word costs
            // nothing. The FIELD name _costSkr is untouched: it fails the gate's leading
            // word-boundary rule and renaming it would be churn with no effect on the artifact.
            var cost = new Label($"{_costSkr:F0} cost");
            cost.style.fontSize = 13;
            cost.style.color    = TitleGold;
            cost.style.unityFontStyleAndWeight = FontStyle.Bold;
            bar.Add(cost);

            return bar;
        }

        private VisualElement BuildControlsRow()
        {
            var col = new VisualElement();

            // Row 1: snap dropdown + confirm.
            var row1 = new VisualElement();
            row1.style.flexDirection  = FlexDirection.Row;
            row1.style.justifyContent = Justify.SpaceBetween;
            row1.style.alignItems     = Align.Center;
            row1.style.marginBottom   = 8;

            var snapWrap = new VisualElement();
            snapWrap.style.flexDirection = FlexDirection.Row;
            snapWrap.style.alignItems    = Align.Center;
            var snapLabel = new Label("Snap:");
            snapLabel.style.fontSize = 11;
            snapLabel.style.color    = CancelTxt;
            snapLabel.style.marginRight = 6;
            snapWrap.Add(snapLabel);

            var snapChoices = new System.Collections.Generic.List<string> { "Off", "15°", "45°", "90°" };
            var snapDrop = new DropdownField(snapChoices, SnapIndex(_snapDegrees));
            snapDrop.style.minWidth = 80;
            snapDrop.RegisterValueChangedCallback(evt =>
            {
                _snapDegrees = SnapValue(evt.newValue);
                // Re-apply snap to current values & refresh readouts.
                ApplySnapToAll();
            });
            snapWrap.Add(snapDrop);
            row1.Add(snapWrap);

            var confirm = new Button(OnConfirmClicked) { text = "Confirm Placement" };
            confirm.style.backgroundColor = ConfirmBg;
            confirm.style.color = ConfirmTxt;
            SetBorder(confirm, ConfirmBdr, 1);
            SetRadius(confirm, 6);
            confirm.style.paddingTop = 8; confirm.style.paddingBottom = 8;
            confirm.style.paddingLeft = 16; confirm.style.paddingRight = 16;
            confirm.style.fontSize = 13;
            confirm.style.unityFontStyleAndWeight = FontStyle.Bold;
            ApplyFont(confirm);
            row1.Add(confirm);

            col.Add(row1);

            // Row 2: cancel + reset rotation.
            var row2 = new VisualElement();
            row2.style.flexDirection  = FlexDirection.Row;
            row2.style.justifyContent = Justify.SpaceBetween;

            var cancel = new Button(OnCancelClicked) { text = "Cancel" };
            cancel.style.backgroundColor = CancelBg;
            cancel.style.color = CancelTxt;
            SetBorder(cancel, ReadoutBdr, 1);
            SetRadius(cancel, 6);
            cancel.style.paddingTop = 8; cancel.style.paddingBottom = 8;
            cancel.style.paddingLeft = 16; cancel.style.paddingRight = 16;
            cancel.style.fontSize = 13;
            row2.Add(cancel);

            var reset = new Button(OnResetRotationClicked) { text = "Reset Rotation" };
            reset.style.backgroundColor = ResetBg;
            reset.style.color = ResetTxt;
            SetBorder(reset, ReadoutBdr, 1);
            SetRadius(reset, 6);
            reset.style.paddingTop = 8; reset.style.paddingBottom = 8;
            reset.style.paddingLeft = 16; reset.style.paddingRight = 16;
            reset.style.fontSize = 13;
            row2.Add(reset);

            col.Add(row2);
            return col;
        }

        private VisualElement BuildRuneStrip(bool vertical)
        {
            var strip = new VisualElement();
            if (vertical)
            {
                strip.style.width = 14;
                strip.style.justifyContent = Justify.Center;
                strip.style.alignItems     = Align.Center;
            }
            else
            {
                strip.style.height = 14;
                strip.style.justifyContent = Justify.Center;
                strip.style.alignItems     = Align.Center;
                strip.style.overflow = Overflow.Hidden;
            }

            var glyphs = new Label(RepeatRunes(vertical ? 6 : 10));
            glyphs.style.fontSize = 8;
            glyphs.style.color    = RuneStripTxt;
            glyphs.style.letterSpacing = 3;
            glyphs.style.whiteSpace = WhiteSpace.NoWrap;
            if (vertical)
                glyphs.style.rotate = new StyleRotate(new Rotate(new Angle(90f, AngleUnit.Degree)));
            strip.Add(glyphs);
            return strip;
        }

        // ── Slider / snap callbacks ──────────────────────────────────────────────

        private void OnXChanged(ChangeEvent<float> evt) => SetAxis(0, evt.newValue);
        private void OnYChanged(ChangeEvent<float> evt) => SetAxis(1, evt.newValue);
        private void OnZChanged(ChangeEvent<float> evt) => SetAxis(2, evt.newValue);

        // Numeric entry → same SetAxis path (parse, fall back to current value on garbage).
        private void OnXInput(ChangeEvent<string> evt) => SetAxisFromText(0, evt.newValue, _xDeg);
        private void OnYInput(ChangeEvent<string> evt) => SetAxisFromText(1, evt.newValue, _yDeg);
        private void OnZInput(ChangeEvent<string> evt) => SetAxisFromText(2, evt.newValue, _zDeg);

        // ── Per-axis scale callbacks (slider + numeric, one set per axis) ────────
        private void OnSxChanged(ChangeEvent<float> evt) => SetScaleAxis(0, evt.newValue);
        private void OnSyChanged(ChangeEvent<float> evt) => SetScaleAxis(1, evt.newValue);
        private void OnSzChanged(ChangeEvent<float> evt) => SetScaleAxis(2, evt.newValue);

        private void OnSxInput(ChangeEvent<string> evt) => SetScaleAxisFromText(0, evt.newValue, _sxScale);
        private void OnSyInput(ChangeEvent<string> evt) => SetScaleAxisFromText(1, evt.newValue, _syScale);
        private void OnSzInput(ChangeEvent<string> evt) => SetScaleAxisFromText(2, evt.newValue, _szScale);

        private void SetScaleAxisFromText(int axis, string text, float fallback)
        {
            float v = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && parsed > 0f
                ? Mathf.Clamp(parsed, ScaleMin, ScaleMax) : fallback;
            SetScaleAxis(axis, v);
            // Push the (clamped) value back onto the matching slider handle.
            switch (axis)
            {
                case 0: _sxSlider?.SetValueWithoutNotify(_sxScale); break;
                case 1: _sySlider?.SetValueWithoutNotify(_syScale); break;
                default: _szSlider?.SetValueWithoutNotify(_szScale); break;
            }
        }

        private void SetScaleAxis(int axis, float raw)
        {
            float v = Mathf.Clamp(raw, ScaleMin, ScaleMax);
            switch (axis)
            {
                case 0: _sxScale = v; if (_sxInput != null && _sxInput.value != FormatScale(v)) _sxInput.SetValueWithoutNotify(FormatScale(v)); break;
                case 1: _syScale = v; if (_syInput != null && _syInput.value != FormatScale(v)) _syInput.SetValueWithoutNotify(FormatScale(v)); break;
                default: _szScale = v; if (_szInput != null && _szInput.value != FormatScale(v)) _szInput.SetValueWithoutNotify(FormatScale(v)); break;
            }
        }

        /// <summary>Reset a single scale axis (0=X,1=Y,2=Z) to 1.0× (identity).</summary>
        private void ResetScaleAxis(int axis)
        {
            switch (axis)
            {
                case 0: _sxScale = 1f; _sxSlider?.SetValueWithoutNotify(1f); _sxInput?.SetValueWithoutNotify(FormatScale(1f)); break;
                case 1: _syScale = 1f; _sySlider?.SetValueWithoutNotify(1f); _syInput?.SetValueWithoutNotify(FormatScale(1f)); break;
                default: _szScale = 1f; _szSlider?.SetValueWithoutNotify(1f); _szInput?.SetValueWithoutNotify(FormatScale(1f)); break;
            }
        }

        private void SetAxisFromText(int axis, string text, float fallback)
        {
            float v = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? Wrap180(parsed) : fallback;
            SetAxis(axis, v);
            // Push the (snapped/wrapped) value back onto the matching slider handle.
            switch (axis)
            {
                case 0: _xSlider?.SetValueWithoutNotify(_xDeg); break;
                case 1: _ySlider?.SetValueWithoutNotify(_yDeg); break;
                default: _zSlider?.SetValueWithoutNotify(_zDeg); break;
            }
        }

        private void SetAxis(int axis, float raw)
        {
            float snapped = SnapAngle(raw);
            switch (axis)
            {
                case 0: _xDeg = snapped; UpdateReadout(_xReadout, _xSlider, _xInput, snapped, raw); break;
                case 1: _yDeg = snapped; UpdateReadout(_yReadout, _ySlider, _yInput, snapped, raw); break;
                default: _zDeg = snapped; UpdateReadout(_zReadout, _zSlider, _zInput, snapped, raw); break;
            }
        }

        private void UpdateReadout(Label readout, Slider slider, TextField input, float snapped, float raw)
        {
            if (readout != null) readout.text = $"{Mathf.RoundToInt(snapped)}°";
            // If snapping moved the value, reflect it on the slider handle too
            // (guard against feedback by only setting when it differs).
            if (slider != null && !Mathf.Approximately(slider.value, snapped) && !Mathf.Approximately(raw, snapped))
                slider.SetValueWithoutNotify(snapped);
            // Keep the numeric field in sync with the snapped value.
            if (input != null && input.value != FormatDeg(snapped))
                input.SetValueWithoutNotify(FormatDeg(snapped));
        }

        private static string FormatDeg(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
        private static string FormatScale(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private float SnapAngle(float raw) =>
            _snapDegrees == 0 ? raw : Mathf.Round(raw / _snapDegrees) * _snapDegrees;

        private void ApplySnapToAll()
        {
            SetAxis(0, _xSlider != null ? _xSlider.value : _xDeg);
            SetAxis(1, _ySlider != null ? _ySlider.value : _yDeg);
            SetAxis(2, _zSlider != null ? _zSlider.value : _zDeg);
        }

        // ── Reset ────────────────────────────────────────────────────────────────

        /// <summary>Reset a single axis (0=X,1=Y,2=Z) to its initialRotation component.</summary>
        private void ResetAxis(int axis)
        {
            switch (axis)
            {
                case 0: _xDeg = _initialEuler.x; _xSlider?.SetValueWithoutNotify(_xDeg); if (_xReadout != null) _xReadout.text = $"{Mathf.RoundToInt(_xDeg)}°"; _xInput?.SetValueWithoutNotify(FormatDeg(_xDeg)); break;
                case 1: _yDeg = _initialEuler.y; _ySlider?.SetValueWithoutNotify(_yDeg); if (_yReadout != null) _yReadout.text = $"{Mathf.RoundToInt(_yDeg)}°"; _yInput?.SetValueWithoutNotify(FormatDeg(_yDeg)); break;
                default: _zDeg = _initialEuler.z; _zSlider?.SetValueWithoutNotify(_zDeg); if (_zReadout != null) _zReadout.text = $"{Mathf.RoundToInt(_zDeg)}°"; _zInput?.SetValueWithoutNotify(FormatDeg(_zDeg)); break;
            }
        }

        private void OnResetRotationClicked()
        {
            ResetAxis(0);
            ResetAxis(1);
            ResetAxis(2);
        }

        // ── Confirm / Cancel ─────────────────────────────────────────────────────

        private void OnConfirmClicked()
        {
            float ex = SnapAngle(_xDeg);
            float ey = SnapAngle(_yDeg);
            float ez = SnapAngle(_zDeg);

            // DEV "orient & report" path — log the dialed recipe AND apply it live so the
            // next placement uses the new orientation immediately (no rebake round-trip).
            if (!string.IsNullOrEmpty(_devOrientId))
            {
                var euler     = new Vector3(ex, ey, ez);
                var scaleAxis = new Vector3(_sxScale, _syScale, _szScale);
                ApplyOrientToCatalog(_devOrientId, euler, scaleAxis);
                ReportOrientRecipe(_devOrientId, euler, scaleAxis);
                Close();
                return;
            }

            var finalRotation = Quaternion.Euler(ex, ey, ez);
            var cb = _onConfirm;
            Close();
            cb?.Invoke(finalRotation);
        }

        /// <summary>
        /// DEV: surface the dialed orientation as a copy-pasteable Console line and
        /// append a JSON recipe to Assets/Resources/Data/orientation-recipes.json.
        /// The panel dials rotation only, so offset is (0,0,0) and scale 1 — the
        /// owner edits those in the JSON if a non-zero offset/scale is needed.
        /// </summary>
        /// <summary>
        /// Apply the dialed orientation to the live CatalogEntry so it takes effect on the
        /// NEXT placement / ghost without a rebake (StructureFactory + GhostPreview read
        /// entry.orientation; marking it manual=true is what makes them apply it). The owner
        /// still bakes the logged [OrientRecipe] into the JSON to persist it across sessions.
        /// </summary>
        private static void ApplyOrientToCatalog(string id, Vector3 euler, Vector3 scaleAxis)
        {
            var entry = CatalogRegistry.Get(id);
            if (entry == null) { Debug.LogWarning($"[Orient] apply: '{id}' not in CatalogRegistry — logged only."); return; }
            // The editor dials the FULL effective scale into the per-axis field; keep uniform
            // `scale` at 1 so EffectiveScale == scaleAxis (no double-multiply). Back-compat:
            // an all-1 scaleAxis behaves exactly like the old uniform scale=1.
            entry.orientation = new OrientationFix
            {
                corrected = true,
                manual    = true,   // human-verified → StructureFactory/GhostPreview apply it
                euler     = new[] { euler.x, euler.y, euler.z },
                offset    = new[] { 0f, 0f, 0f },
                scale     = 1f,
                scaleAxis = new[] { scaleAxis.x, scaleAxis.y, scaleAxis.z },
                note      = "build-mode orient editor"
            };
            // Owner 2026-07-08 ("should it save locally as well?"): persist the dial to the
            // local overlay store so it survives sessions/rebuilds (local wins at catalog load).
            StructureOrientationLocalStore.Upsert(id, entry.orientation);
            Debug.Log($"[Orient] applied to catalog '{id}' (live + local-saved; the [OrientRecipe] line is the bake source).");
        }

        private void ReportOrientRecipe(string id, Vector3 euler, Vector3 scaleAxis)
        {
            Vector3 offset = Vector3.zero;
            var ci = CultureInfo.InvariantCulture;

            // MUST-HAVE: copy-pasteable Console line. scale stays 1 (uniform, back-compat);
            // the per-axis stretch is in scaleAxis.
            Debug.Log(string.Format(ci,
                "[OrientRecipe] id={0} euler=({1:0.##}, {2:0.##}, {3:0.##}) offset=({4:0.##}, {5:0.##}, {6:0.##}) scale=1 scaleAxis=({7:0.###}, {8:0.###}, {9:0.###})",
                id, euler.x, euler.y, euler.z, offset.x, offset.y, offset.z, scaleAxis.x, scaleAxis.y, scaleAxis.z));

            // Owner ask 2026-07-13 ("would love a visual on screen after the offset tool"):
            // the saved recipe as an ON-SCREEN toast — neutral chrome, 8s, no denied buzz —
            // so the values are readable in a player build without the console.
            BuildFeedbackToast.ShowInfo(string.Format(ci,
                "Orient saved '{0}'  euler ({1:0.#}, {2:0.#}, {3:0.#})  scale ({4:0.##}, {5:0.##}, {6:0.##})",
                id, euler.x, euler.y, euler.z, scaleAxis.x, scaleAxis.y, scaleAxis.z));

            // BONUS: append a JSON line to the recipes file (best-effort; never throws to the UI).
            try
            {
                string json = string.Format(ci,
                    "{{ \"id\":\"{0}\", \"euler\":[{1:0.###},{2:0.###},{3:0.###}], \"offset\":[{4:0.###},{5:0.###},{6:0.###}], \"scale\":1, \"scaleAxis\":[{7:0.###},{8:0.###},{9:0.###}] }}",
                    id, euler.x, euler.y, euler.z, offset.x, offset.y, offset.z, scaleAxis.x, scaleAxis.y, scaleAxis.z);

                string dir  = Path.Combine(Application.dataPath, "Resources", "Data");
                string path = Path.Combine(dir, "orientation-recipes.json");
                Directory.CreateDirectory(dir);
                File.AppendAllText(path, json + "\n");
                Debug.Log($"[OrientRecipe] appended to {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OrientRecipe] could not write recipes file (Console line above is authoritative): {e.Message}");
            }
        }

        private void OnCancelClicked()
        {
            var cb = _onCancel;
            Close();
            cb?.Invoke();
        }

        // ── Preview ──────────────────────────────────────────────────────────────

        private void BeginPreview()
        {
            DisposePreview();
            GameObject prefab = _previewPrefab;

            _preview = new TowerPreviewCamera();
            bool ok = _preview.Begin(prefab);
            if (ok && _preview.Texture != null && _viewport != null)
            {
                _viewport.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
                _preview.SetRotation(Quaternion.Euler(_xDeg, _yDeg, _zDeg));
            }
            else
            {
                // Fallback: no Level-1 visual prefab → icon + name label.
                // TODO: live preview — wire a PreviewSprite onto TowerData and
                // render it here when the prefab is unavailable.
                DisposePreview();
                if (_viewport != null)
                {
                    _viewport.Clear();
                    var fb = new Label("(no preview)");
                    fb.style.fontSize = 18;
                    fb.style.color = TitleGold;
                    fb.style.unityTextAlign = TextAnchor.MiddleCenter;
                    fb.style.whiteSpace = WhiteSpace.Normal;
                    _viewport.Add(fb);
                }
            }
        }

        private void DisposePreview()
        {
            if (_preview != null)
            {
                _preview.Dispose();
                _preview = null;
            }
            if (_viewport != null)
                _viewport.style.backgroundImage = new StyleBackground((Texture2D)null);
        }

        // ── Show / Hide ──────────────────────────────────────────────────────────

        private void ShowPanel()
        {
            if (_document == null) return;
            _document.enabled = true;
            if (_root != null) _root.style.display = DisplayStyle.Flex;
            IsOpen = true;

            // F8-30 — register with the PanelManager arbiter (SeatingEditorOverlay pattern).
            // The handle's close action is OnCancelClicked (NOT bare Close) so an external
            // close — ESC/PauseGate CloseOpen, tutorial-skip CloseAll, arbiter swap — also
            // fires the caller's onCancel and build mode un-freezes exactly like a Cancel tap.
            // NotifyOpened can REJECT (battle-lock) — it invokes our close itself, and we
            // must not overwrite the released state, so only trace when it sticks.
            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("OrientEditor", OnCancelClicked, () => IsOpen);
            bool accepted = PanelManager.NotifyOpened(_panelHandle);
            FlowTrace.Step("Orient", accepted
                ? "panel OPEN — PanelManager slot 'OrientEditor' taken; release paths: Confirm/Cancel/ESC(CloseOpen)/CloseAll/OnDisable."
                : "panel open REJECTED by PanelManager (battle-lock) — already released via OnCancelClicked.");
        }

        // ── PanelSettings adoption (renders in builds) ───────────────────────────

        /// <summary>
        /// Adopt a sibling/scene UIDocument's PanelSettings so this code-built
        /// panel actually renders (a UIDocument with a null PanelSettings draws
        /// nothing — the documented empty-UI trap). Sorts above other panels.
        /// </summary>
        private void AdoptPanelSettings()
        {
            if (_document == null) return;
            if (_document.panelSettings != null) return;

            UIDocument hud = null, any = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                if (doc == null || doc == _document || doc.panelSettings == null) continue;
                if (any == null) any = doc;
                if (doc.gameObject.name.IndexOf("Hud", StringComparison.OrdinalIgnoreCase) >= 0) { hud = doc; break; }
            }
            var src = hud ?? any;
            if (src != null)
            {
                _document.panelSettings = src.panelSettings;
                _document.sortingOrder  = src.sortingOrder + 12;   // modal — above everything
            }
            else
            {
                Debug.LogWarning("[TowerPlacementRotateMenu] No sibling PanelSettings found — panel will not render. Add a scene UIDocument with a PanelSettings.");
            }
        }

        private void TryLoadCinzel()
        {
#if UNITY_EDITOR
            // Editor-only load of the optional Cinzel font; if absent we fall back
            // to the default serif. NO runtime download (per spec).
            _cinzel = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(FontPath);
#endif
        }

        private void ApplyFont(Label l)  { if (_cinzel != null) l.style.unityFont = _cinzel; }
        private void ApplyFont(Button b) { if (_cinzel != null) b.style.unityFont = _cinzel; }

        // ── Small helpers ────────────────────────────────────────────────────────

        private string TierLabel()
        {
            int tiers = _towerData != null && _towerData.upgrades != null ? _towerData.upgrades.Length : 0;
            return tiers > 0 ? $"TIER {ToRoman(tiers)}" : "TIER I";
        }

        private static string ToRoman(int n)
        {
            switch (n)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                default: return n.ToString();
            }
        }

        private static int SnapIndex(int snap)
        {
            switch (snap) { case 15: return 1; case 45: return 2; case 90: return 3; default: return 0; }
        }

        private static int SnapValue(string choice)
        {
            switch (choice) { case "15°": return 15; case "45°": return 45; case "90°": return 90; default: return 0; }
        }

        /// <summary>Map Unity's 0..360 euler to a slider-friendly −180..180 range.</summary>
        private static Vector3 NormalizeEuler(Vector3 e) =>
            new Vector3(Wrap180(e.x), Wrap180(e.y), Wrap180(e.z));

        private static float Wrap180(float a)
        {
            a %= 360f;
            if (a > 180f)  a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        private static string RepeatRunes(int times)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < times; i++) sb.Append(RuneGlyphs);
            return sb.ToString();
        }

        private static Color Hex(int r, int g, int b) =>
            new Color(r / 255f, g / 255f, b / 255f, 1f);

        private static void SetBorder(VisualElement ve, Color c, float w)
        {
            ve.style.borderTopColor = c; ve.style.borderRightColor = c;
            ve.style.borderBottomColor = c; ve.style.borderLeftColor = c;
            ve.style.borderTopWidth = w; ve.style.borderRightWidth = w;
            ve.style.borderBottomWidth = w; ve.style.borderLeftWidth = w;
        }

        private static void SetRadius(VisualElement ve, float r)
        {
            ve.style.borderTopLeftRadius = r; ve.style.borderTopRightRadius = r;
            ve.style.borderBottomLeftRadius = r; ve.style.borderBottomRightRadius = r;
        }

        private static void TintSlider(Slider s, Color accent)
        {
            var dragger = s.Q("unity-dragger");
            if (dragger != null) dragger.style.backgroundColor = accent;
            var tracker = s.Q("unity-tracker");
            if (tracker != null) tracker.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.35f);
        }
    }
}

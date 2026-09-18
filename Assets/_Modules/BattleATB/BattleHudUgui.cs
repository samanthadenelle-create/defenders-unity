using System;
using System.Collections.Generic;
using System.Linq;
using DeNelle.BattleATB.Engine;
using DeNelle.BattleATB.State;
using DeNelle.Core.UI;   // ElarionUiKit / ElarionUi / RpgUiCatalog — the SHARED presentation layer
using DeNelle.Core.Diagnostics;   // FlowTrace — TGVRU instrumentation
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DeNelle.BattleATB
{
    /// <summary>
    /// FF7-style Active Battle HUD implemented as pure code-built uGUI (Canvas + Image + TMP + Layouts).
    /// Matches the requested layout: Command window bottom-left (Attack/Skills/Item/Defend + dynamic skills sub),
    /// Party status bottom-right (4 slots: portrait, name, HP/MP/ATB bars+text),
    /// Top info ("The Last Stand", WAVE, active turn).
    ///
    /// Designed to replace the overly complex previous VisualElement-based BattleHud.
    /// Keeps the same callback surface (OnAction, OnControlModeToggled) for drop-in with BattleController / ATBRuntimeState.
    /// Uses existing BattleState for data (units, HP, ATB visual fill, active unit, wave, etc.).
    ///
    /// VISUAL STYLE: routes ENTIRELY through the shared presentation layer
    /// <see cref="ElarionUiKit"/> (DeNelle.Core.UI) so the combat HUD renders the SAME
    /// dark-glass + gold-rune + RPG-pack art as the town HUD / inventory / store —
    /// NO private parchment styling. Panels = ElarionUiKit.Panel (dark glass + soft
    /// gold rim); bars = ElarionUiKit.Bar(BarKind) (town-HUD ornate gauge); portraits =
    /// ElarionUiKit.Portrait + PortraitForClass; buttons = ElarionUiKit.Button. The ATB
    /// gauge keeps its charging->ready colour pop by re-tinting its fill per frame.
    ///
    /// Self-contained: creates its own ScreenSpaceOverlay Canvas + Scaler if none provided.
    /// No UXML/UIDocument — pure uGUI + the kit's procedural sprites so it works in player
    /// builds (WebGL-safe).
    /// </summary>
    public sealed class BattleHudUgui : MonoBehaviour
    {
        // ── Callbacks (same surface as old BattleHud for easy wiring in BattleController)
        public Action<BattleAction> OnAction;
        public Action<string, ControlMode> OnControlModeToggled;

        // ── Per-frame ATB colours (kept local — the kit's BarKind.Atb gives the base
        // fill, but combat re-tints the fill each frame for the charging->ready pop).
        private static readonly Color AtbFillColor = ElarionUi.Aether;                       // Aether violet (charging)
        private static readonly Color AtbReadyColor = ElarionUi.Gilt;                        // Gilt gold (ready)

        // Decorative crest glyph (shared with the kit's headers).
        private const string CrestGlyph = "*";

        // ── Real staged art (Resources/HudIcons/...) — per-class ability icons for the
        // skill menu. Class PORTRAITS now resolve through ElarionUiKit.PortraitForClass.
        // Cached so repeated skill-menu opens don't re-hit Resources.
        private static readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

        /// <summary>WebGL-safe sprite load from Resources/HudIcons/&lt;key&gt;, cached + null-safe.</summary>
        private static Sprite LoadHudIcon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_iconCache.TryGetValue(key, out var cached)) return cached;
            var sp = Resources.Load<Sprite>("HudIcons/" + key);
            _iconCache[key] = sp; // cache misses too (avoids repeat lookups)
            return sp;
        }

        /// <summary>In-battle HeroClass -> the shared kit class-portrait resolver. Routes
        /// through ElarionUiKit.PortraitForClass so combat + town render the SAME portrait
        /// (incl. the Wizard "wiard" typo fix + Healer fallback). The battle engine collapses
        /// Cleric->Mage, so Mage maps to the Wizard art.</summary>
        private static Sprite PortraitFor(HeroClass cls)
        {
            switch (cls)
            {
                case HeroClass.Knight: return ElarionUiKit.PortraitForClass("Knight");
                case HeroClass.Ranger: return ElarionUiKit.PortraitForClass("Ranger");
                case HeroClass.Mage:   return ElarionUiKit.PortraitForClass("Wizard");
                default:               return null;
            }
        }

        /// <summary>In-battle HeroClass → Resources folder + filename prefix for ability icons.</summary>
        private static string AbilityFolderFor(HeroClass cls)
        {
            switch (cls)
            {
                case HeroClass.Knight: return "Knight";
                case HeroClass.Ranger: return "Ranger";
                case HeroClass.Mage:   return "Wizard";
                default:               return null;
            }
        }

        // Per-class ability-icon assignment. The engine ability NAMES (e.g. "Arcane Bolt",
        // "Shield Slam") do NOT match the staged icon filenames (Wizard_Fireball, Knight_Charge),
        // so map deterministically by Q/W/E/R slot to a hand-picked icon. Keys are the file stem
        // AFTER the "&lt;Class&gt;/" folder (LoadHudIcon prepends "HudIcons/&lt;folder&gt;/").
        private static readonly Dictionary<HeroClass, Dictionary<AbilitySlot, string>> AbilityIconBySlot =
            new Dictionary<HeroClass, Dictionary<AbilitySlot, string>>
            {
                { HeroClass.Knight, new Dictionary<AbilitySlot, string> {
                    { AbilitySlot.Q, "Knight_Charge" },   // Shield Slam → Charge
                    { AbilitySlot.W, "knight_parry" },    // Guard       → Parry
                    { AbilitySlot.E, "Knight_Cleave" },   // Whirlwind   → Cleave
                    { AbilitySlot.R, "knight_thrust" },   // Last Stand  → Thrust
                } },
                { HeroClass.Ranger, new Dictionary<AbilitySlot, string> {
                    { AbilitySlot.Q, "Ranger_Ranged_Attack" }, // Pierce Shot   → Ranged Attack
                    { AbilitySlot.W, "Ranger_Barrage" },       // Volley        → Barrage
                    { AbilitySlot.E, "Ranger_Poison_Arrow" },  // Hunter's Mark → Poison Arrow
                    { AbilitySlot.R, "ranger_rapid_fire" },    // Rain of Arrows→ Rapid Fire
                } },
                { HeroClass.Mage, new Dictionary<AbilitySlot, string> {
                    { AbilitySlot.Q, "Wizard_Plasma" },    // Arcane Bolt → Plasma
                    { AbilitySlot.W, "Wizard_Fireball" },  // Flameblast  → Fireball
                    { AbilitySlot.E, "Wizard_Lightining" },// Frost Nova  → Lightning (only ice-ish icon absent; Lightning reads as a nova)
                    { AbilitySlot.R, "Wizard_Meteor" },    // Tempest     → Meteor
                } },
            };

        /// <summary>Resolve an ability's staged icon by class + slot (best-effort). Null when no class/match.</summary>
        private static Sprite AbilityIconFor(HeroClass cls, AbilitySlot slot)
        {
            string folder = AbilityFolderFor(cls);
            if (folder == null) return null;
            if (AbilityIconBySlot.TryGetValue(cls, out var bySlot) && bySlot.TryGetValue(slot, out var stem))
                return LoadHudIcon(folder + "/" + stem);
            return null;
        }

        // ── Roots
        private Canvas _canvas;
        private GameObject _commandPanel;
        private GameObject _partyPanel;
        private GameObject _infoPanel;
        private GameObject _skillsSubPanel;
        private GameObject _logPanel;

        // Buttons
        private Button _attackBtn;
        private Button _skillsBtn;
        private Button _itemBtn;
        private Button _defendBtn;

        // Info
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _waveText;
        private TextMeshProUGUI _turnText;

        // Battle log (last-N rolling text panel). Mirrors the engine's append-only
        // state.Log; only entries past _lastShownLogIndex are appended each Render().
        private TextMeshProUGUI _logText;
        private int _lastShownLogIndex;
        private readonly List<string> _logLines = new List<string>();
        private const int MaxLogLines = 4;

        // Party slots (4 as per spec)
        private readonly List<PartySlot> _partySlots = new List<PartySlot>();

        // Skills list (dynamic per active hero)
        private readonly List<Button> _skillButtons = new List<Button>();

        // State
        private BattleState _lastState;
        private string _activeMemberId;
        private PickKind _pendingKind = PickKind.None;
        private AbilitySlot _pendingAbility;
        private ItemKind _pendingItem;

        // Visual ATB simulation (engine is discrete; this gives the "filling" feel without touching engine)
        private readonly Dictionary<string, float> _visualAtb = new Dictionary<string, float>();
        private const float VisualAtbChargeSeconds = 3.0f;

        // WO-744 MVVM landmine 1: flag-gated read-only snapshot VM for the Skills/Item
        // submenus. Resolved ONCE at Build. When OFF (default) _useVm is false and the HUD
        // resolves abilities/items off its own held BattleState exactly as before (no VM
        // path). When ON, Render pushes the snapshot and the submenus read the VM. The
        // _visualAtb feel-sim + OnAction contract are untouched on BOTH paths.
        private BattleHudVM _vm;
        private bool _useVm;

        private enum PickKind { None, Ability, Item }

        private class PartySlot
        {
            public GameObject Root;
            public Image Portrait;
            public Image PortraitRing;
            public TextMeshProUGUI Name;
            public Image HpBar;
            public TextMeshProUGUI HpText;
            public Image MpBar;
            public TextMeshProUGUI MpText;
            public Image AtbBar;
        }

        /// <summary>Build the full FF7-style HUD. Creates its own Canvas if none supplied.</summary>
        public void Build(Canvas existingCanvas = null)
        {
            FlowTrace.Step("UI", $"BattleHudUgui.Build: existingCanvas={(existingCanvas != null ? "supplied" : "self-create")}");
            // WO-744 landmine 1: resolve the snapshot-VM flag once. OFF (default) => byte-identical legacy path.
            _useVm = DeNelle.Core.FeatureFlags.BattleHudVm;
            if (_useVm && _vm == null) _vm = new BattleHudVM();
            if (existingCanvas != null)
            {
                _canvas = existingCanvas;
            }
            else
            {
                var canvasGO = new GameObject("BattleHUD_Canvas");
                _canvas = canvasGO.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;

                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;

                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Top info
            CreateInfoPanel();

            // Bottom-left command window (classic FF7 box + 4 commands + skills sub)
            CreateCommandPanel();

            // Bottom-right party status (exactly 4 slots as specified)
            CreatePartyPanel();

            // Initial state
            _skillsSubPanel.SetActive(false);
        }

        // ── Shared-kit dressing helpers ──────────────────────────────────────
        // Every panel/card/button/bar/portrait is built from ElarionUiKit so the
        // combat HUD reads as ONE UI with town. These thin wrappers adapt the kit's
        // fraction-anchored builders to this HUD's pixel-anchored / LayoutGroup roots.

        /// <summary>Dress a pixel-anchored panel root's own Image as a kit dark-glass panel
        /// (rounded glass fill + soft gold underline rim + faint inner rim). The Image must
        /// already exist on <paramref name="panelGO"/>.</summary>
        private static void DressGlassPanel(GameObject panelGO, bool deep)
        {
            var img = panelGO.GetComponent<Image>();
            if (img != null)
            {
                img.color = deep ? ElarionUiKit.GlassDeep : ElarionUiKit.Glass;
                ElarionUiKit.ApplyRounded(img);
            }
            ElarionUiKit.AddRimUnderline(panelGO);
            ElarionUiKit.AddInnerRim(panelGO, ElarionUiKit.AccentSoft);
        }

        /// <summary>A header label (gilt crest + spaced gold title) pinned across the top of
        /// a panel root, via the shared kit Label primitive.</summary>
        private static TextMeshProUGUI PanelHeader(GameObject panelGO, string text)
        {
            var lbl = ElarionUiKit.Label(panelGO.transform, CrestGlyph + " " + text, 0f, 1f,
                                         ElarionUi.Gilt, ElarionUi.FontLabel,
                                         TextAlignmentOptions.Center, 0f, 1f, spacing: 3f, bold: true);
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = new Vector2(0, -6);
            rt.sizeDelta = new Vector2(0, 20);
            return lbl;
        }

        private GameObject CreateText(string txt, Transform parent, Vector2 anchoredPos, int fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(280, 32);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = txt;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = FontStyles.Bold;
            return go;
        }

        private void CreateInfoPanel()
        {
            _infoPanel = new GameObject("BattleInfoPanel");
            var rt = _infoPanel.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -24);
            rt.sizeDelta = new Vector2(720, 76);

            _infoPanel.AddComponent<Image>();
            DressGlassPanel(_infoPanel, deep: false);

            // Title "* The Last Stand *" — gilt crest + letter-spaced gold title (parchment-on-glass)
            var titleGO = CreateText($"{CrestGlyph}  The Last Stand  {CrestGlyph}", _infoPanel.transform, new Vector2(0, 18), ElarionUi.FontTitle, ElarionUi.Gilt, TextAlignmentOptions.Center);
            _titleText = titleGO.GetComponent<TextMeshProUGUI>();
            _titleText.characterSpacing = 6f;
            _titleText.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 34);

            // Thin gilt rule under the title (shared kit accent colour)
            var rule = new GameObject("TitleRule");
            rule.transform.SetParent(_infoPanel.transform, false);
            var ruleRt = rule.AddComponent<RectTransform>();
            ruleRt.anchoredPosition = new Vector2(0, -2);
            ruleRt.sizeDelta = new Vector2(360, 2);
            var ruleImg = rule.AddComponent<Image>();
            ruleImg.color = ElarionUiKit.Accent;
            ElarionUiKit.ApplyRounded(ruleImg);

            // WAVE — parchment cream (readable on dark glass)
            var waveGO = CreateText("WAVE 1", _infoPanel.transform, new Vector2(-228, -12), ElarionUi.FontHead, ElarionUi.Parchment, TextAlignmentOptions.Left);
            _waveText = waveGO.GetComponent<TextMeshProUGUI>();
            _waveText.characterSpacing = 2f;

            // Active Turn — muted parchment
            var turnGO = CreateText("", _infoPanel.transform, new Vector2(228, -12), ElarionUi.FontHead, ElarionUi.ParchmentDim, TextAlignmentOptions.Right);
            _turnText = turnGO.GetComponent<TextMeshProUGUI>();
        }

        private void CreateCommandPanel()
        {
            _commandPanel = new GameObject("CommandPanel");
            var rt = _commandPanel.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(30, 30);
            rt.sizeDelta = new Vector2(272, 208);

            _commandPanel.AddComponent<Image>();
            DressGlassPanel(_commandPanel, deep: false);

            // Small rune crest header on the command box (shared kit header)
            PanelHeader(_commandPanel, "COMMAND");

            // Classic FF7 vertical command list
            var vlg = _commandPanel.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 30, 10); // top pad clears the header

            _attackBtn = CreateMenuButton("Attack", OnAttackClicked);
            _skillsBtn = CreateMenuButton("Skills", OnSkillsClicked);
            _itemBtn = CreateMenuButton("Item", OnItemClicked);
            _defendBtn = CreateMenuButton("Defend", OnDefendClicked);

            // Skills sub-window (shown on Skills click, positioned to the right of main command)
            _skillsSubPanel = new GameObject("SkillsSubPanel");
            var subRt = _skillsSubPanel.AddComponent<RectTransform>();
            subRt.SetParent(_commandPanel.transform, false);
            subRt.anchorMin = new Vector2(1, 0);
            subRt.anchorMax = new Vector2(1, 0);
            subRt.pivot = new Vector2(0, 0);
            subRt.anchoredPosition = new Vector2(12, 0);
            subRt.sizeDelta = new Vector2(228, 168);

            _skillsSubPanel.AddComponent<Image>();
            DressGlassPanel(_skillsSubPanel, deep: true);

            var subVlg = _skillsSubPanel.AddComponent<VerticalLayoutGroup>();
            subVlg.childControlHeight = false;
            subVlg.childForceExpandHeight = false;
            subVlg.childControlWidth = true;
            subVlg.childForceExpandWidth = true;
            subVlg.spacing = 3;
            subVlg.padding = new RectOffset(8, 8, 8, 8);

            _skillsSubPanel.SetActive(false);

            // Battle log — a small last-N rolling text panel above the command box.
            CreateLogPanel();
        }

        /// <summary>Small dark-glass battle-log panel above the command box. Shows the
        /// last few engine log lines (state.Log), filled incrementally in Render().</summary>
        private void CreateLogPanel()
        {
            _logPanel = new GameObject("BattleLogPanel");
            var rt = _logPanel.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(30, 246); // sits just above the 208-tall command box (bottom-left)
            rt.sizeDelta = new Vector2(272, 92);

            _logPanel.AddComponent<Image>();
            DressGlassPanel(_logPanel, deep: false);

            PanelHeader(_logPanel, "LOG");

            var txtGO = new GameObject("LogText");
            txtGO.transform.SetParent(_logPanel.transform, false);
            var txtRt = txtGO.AddComponent<RectTransform>();
            txtRt.anchorMin = new Vector2(0, 0);
            txtRt.anchorMax = new Vector2(1, 1);
            txtRt.offsetMin = new Vector2(10, 8);
            txtRt.offsetMax = new Vector2(-10, -28); // clear the header crest at the top

            _logText = txtGO.AddComponent<TextMeshProUGUI>();
            _logText.text = "";
            _logText.fontSize = ElarionUi.FontMicro;
            _logText.color = ElarionUi.ParchmentDim;
            _logText.alignment = TextAlignmentOptions.BottomLeft; // newest line at the bottom
            _logText.textWrappingMode = TMPro.TextWrappingModes.Normal;
            _logText.overflowMode = TextOverflowModes.Truncate;
            _logText.raycastTarget = false;
        }

        /// <summary>One command/skill button, skinned through the shared kit (rounded glass
        /// quiet-fill + the kit's StyleButtonColors brightness feedback). Built as a sized
        /// LayoutElement child so it sits in the FF7 vertical command list; an optional ability
        /// icon sits on the left with the label inset past it.</summary>
        private Button CreateMenuButton(string label, UnityEngine.Events.UnityAction onClick, Sprite icon = null)
        {
            var go = new GameObject(label);
            go.transform.SetParent(_commandPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(240, 40);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 40;
            le.preferredHeight = 40;

            var img = go.AddComponent<Image>();
            img.color = ElarionUiKit.FillFor(ElarionUiKit.ButtonKind.Quiet);
            ElarionUiKit.ApplyRounded(img);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);
            btn.targetGraphic = img;
            ElarionUiKit.StyleButtonColors(btn);

            // Thin gilt rune rim around each command (shared kit accent line).
            ElarionUiKit.AddInnerRim(go, ElarionUiKit.AccentSoft);

            // Real ability icon (when supplied) — small square on the left, inside the
            // rim. Keeps the text label (now left-aligned + inset) as the readable
            // fallback. No icon → unchanged centred label.
            const float iconBox = 30f;
            if (icon != null)
            {
                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(go.transform, false);
                var iconRt = iconGO.AddComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0, 0.5f);
                iconRt.anchorMax = new Vector2(0, 0.5f);
                iconRt.pivot = new Vector2(0, 0.5f);
                iconRt.anchoredPosition = new Vector2(6f, 0f);
                iconRt.sizeDelta = new Vector2(iconBox, iconBox);
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            var txtGO = new GameObject("Label");
            txtGO.transform.SetParent(go.transform, false);
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = ElarionUi.FontBody;
            tmp.color = ElarionUi.Parchment;
            tmp.alignment = icon != null ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 1f;
            tmp.raycastTarget = false;
            var txtRt = txtGO.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            // Inset the label past the icon when one is present so they don't overlap.
            txtRt.offsetMin = icon != null ? new Vector2(iconBox + 12f, 0f) : Vector2.zero;
            txtRt.offsetMax = icon != null ? new Vector2(-6f, 0f) : Vector2.zero;

            return btn;
        }

        private void OnAttackClicked() => SubmitAction(new BattleAction { Kind = ActionKind.Attack });
        private void OnSkillsClicked() => ShowSkillsSubmenu();
        private void OnItemClicked() => ShowItemSubmenu();
        private void OnDefendClicked() => SubmitAction(new BattleAction { Kind = ActionKind.Defend });

        private void ShowSkillsSubmenu()
        {
            _skillsSubPanel.SetActive(true);
            foreach (Transform t in _skillsSubPanel.transform) Destroy(t.gameObject);
            _skillButtons.Clear();

            // Dynamic skills from current active member (use engine defs / catalog for the hero class).
            // WO-744 landmine 1: when the snapshot VM is bound (flag ON) the catalog resolves come
            // from the VM projection; OFF (default) uses the legacy in-View resolves byte-identically.
            IReadOnlyList<AbilityDef> abilities = _useVm && _vm != null
                ? _vm.UsableAbilities
                : GetAbilitiesForActiveHero();
            HeroClass? activeClass = _useVm && _vm != null ? _vm.ActiveHeroClass : GetActiveHeroClass();
            string activeIdForTrace = _useVm && _vm != null
                ? (_vm.ActiveUnitId ?? "<no state>")
                : (_lastState != null ? _lastState.ActiveUnitId : "<no state>");
            // TGVRU V: an empty ability list builds a Skills submenu with ZERO buttons — the player
            // taps Skills and gets a blank panel with no way out. Self-report which active unit had
            // no abilities so it's a known data gap, not an invisible dead-end.
            if (abilities == null || abilities.Count == 0)
                FlowTrace.Warn("UI", $"ShowSkillsSubmenu: 0 abilities for active unit " +
                    $"'{activeIdForTrace}' (class={(activeClass?.ToString() ?? "<none>")}) " +
                    "— Skills submenu opens EMPTY.");
            foreach (var ab in abilities)
            {
                // Real per-class ability icon (best-effort by slot); null → text-only fallback.
                Sprite icon = activeClass != null ? AbilityIconFor(activeClass.Value, ab.Slot) : null;
                var slot = ab.Slot; // capture for the closure (avoid modified-closure on the loop var)
                var b = CreateMenuButton($"{ab.Name} ({ab.Cost} MP)", () => SubmitAbility(slot), icon);
                b.transform.SetParent(_skillsSubPanel.transform, false);
                _skillButtons.Add(b);
            }
        }

        /// <summary>In-battle HeroClass of the active unit (null when none / not a hero).</summary>
        private HeroClass? GetActiveHeroClass()
        {
            var state = _lastState;
            if (state == null) return null;
            string activeId = state.ActiveUnitId;
            if (string.IsNullOrEmpty(activeId)) return null;
            var unit = state.Units.FirstOrDefault(u => u.Id == activeId);
            return unit?.HeroClass;
        }

        private List<AbilityDef> GetAbilitiesForActiveHero()
        {
            if (_lastState == null) return new List<AbilityDef>();
            var state = _lastState;  // BattleState
            string activeId = state.ActiveUnitId;
            if (string.IsNullOrEmpty(activeId)) return new List<AbilityDef>();

            var unit = state.Units.FirstOrDefault(u => u.Id == activeId);
            if (unit?.HeroClass == null) return new List<AbilityDef>();

            if (DeNelle.BattleATB.Engine.Defs.HERO_ABILITIES.TryGetValue(unit.HeroClass.Value, out var defs))
            {
                return defs.ToList();
            }
            return new List<AbilityDef>();
        }

        private void SubmitAction(BattleAction action)
        {
            OnAction?.Invoke(action);
            _skillsSubPanel.SetActive(false);
        }

        private void SubmitAbility(AbilitySlot slot)
        {
            var action = BattleAction.MakeAbility(slot);
            OnAction?.Invoke(action);
            _skillsSubPanel.SetActive(false);
        }

        /// <summary>Item submenu — mirrors <see cref="ShowSkillsSubmenu"/>: reuses the same
        /// sub-window, populating it from the battle's shared inventory (usable items only).
        /// Each chosen item routes through the SAME BattleAction path as skills.</summary>
        private void ShowItemSubmenu()
        {
            _skillsSubPanel.SetActive(true);
            foreach (Transform t in _skillsSubPanel.transform) Destroy(t.gameObject);
            _skillButtons.Clear();

            // WO-744 landmine 1: when the snapshot VM is bound (flag ON) usable items + their names
            // come from the VM projection; OFF (default) uses the legacy in-View resolves byte-identically.
            if (_useVm && _vm != null)
            {
                var items = _vm.UsableItems;
                if (items == null || items.Count == 0)
                    FlowTrace.Warn("UI", "ShowItemSubmenu: 0 usable items in inventory — Item submenu opens EMPTY.");
                foreach (var it in items)
                {
                    var kind = it.Kind;   // capture for the closure (avoid modified-closure on the loop var)
                    var b = CreateMenuButton($"{it.Name} (x{it.Count})", () => SubmitItem(kind));
                    b.transform.SetParent(_skillsSubPanel.transform, false);
                    _skillButtons.Add(b);
                }
                return;
            }

            var legacyItems = GetUsableItems();
            // TGVRU V: no usable items builds an EMPTY submenu (a blank panel, no way out).
            // Self-report so an empty inventory is a known data state, not an invisible dead-end.
            if (legacyItems == null || legacyItems.Count == 0)
                FlowTrace.Warn("UI", "ShowItemSubmenu: 0 usable items in inventory — Item submenu opens EMPTY.");
            foreach (var kv in legacyItems)
            {
                var kind = kv.Key;      // capture for the closure (avoid modified-closure on the loop var)
                int count = kv.Value;
                string name = DeNelle.BattleATB.Engine.Defs.ITEM_DEFS.TryGetValue(kind, out var def)
                    ? def.Name
                    : kind.ToString();
                var b = CreateMenuButton($"{name} (x{count})", () => SubmitItem(kind));
                b.transform.SetParent(_skillsSubPanel.transform, false);
                _skillButtons.Add(b);
            }
        }

        /// <summary>Usable items (count &gt; 0) from the active battle's shared inventory.</summary>
        private List<KeyValuePair<ItemKind, int>> GetUsableItems()
        {
            var result = new List<KeyValuePair<ItemKind, int>>();
            if (_lastState?.Inventory == null) return result;
            foreach (var kv in _lastState.Inventory)
                if (kv.Value > 0) result.Add(kv);
            return result;
        }

        private void SubmitItem(ItemKind item)
        {
            _pendingKind = PickKind.Item;
            _pendingItem = item;
            // Null target → the engine's ResolveItem falls back to the acting unit (self-heal/restore),
            // matching the AI item path; ability submit likewise passes no explicit target.
            var action = BattleAction.MakeItem(item, null);
            OnAction?.Invoke(action);
            _skillsSubPanel.SetActive(false);
        }

        private void CreatePartyPanel()
        {
            _partyPanel = new GameObject("PartyStatusPanel");
            var rt = _partyPanel.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-30, 30);
            rt.sizeDelta = new Vector2(432, 232);

            _partyPanel.AddComponent<Image>();
            DressGlassPanel(_partyPanel, deep: false);

            // Party header crest (shared kit header — cohesive with command box)
            PanelHeader(_partyPanel, "PARTY");

            var vlg = _partyPanel.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 5;
            vlg.padding = new RectOffset(10, 10, 30, 10); // top pad clears header

            _partySlots.Clear();
            for (int i = 0; i < 4; i++) // exactly as specified
            {
                var slot = CreatePartySlot();
                slot.Root.transform.SetParent(_partyPanel.transform, false);
                _partySlots.Add(slot);
            }

            // TGVRU V: the party frame must have its 4 slot widgets built or Render() has nothing
            // to fill — a "blank party panel". Assert the count + trace it so a build miss is loud.
            if (_partySlots.Count != 4)
                FlowTrace.Warn("UI", $"CreatePartyPanel: built {_partySlots.Count} party slot(s), expected 4 " +
                    "— party panel will render incomplete/blank.");
            else
                FlowTrace.Step("UI", "CreatePartyPanel: 4 party slots built.");
        }

        /// <summary>One party slot, built from the shared kit: a dark-glass card carrying a
        /// kit Portrait (circular class disc + gilt ring) and stacked kit Bars (HP/MP/ATB).
        /// Renders the SAME pieces as the town party frame.</summary>
        private PartySlot CreatePartySlot()
        {
            var go = new GameObject("PartySlot");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(412, 48);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 48;
            le.preferredHeight = 48;

            var bg = go.AddComponent<Image>();
            bg.color = ElarionUiKit.Cell;          // lighter dark-glass card on the panel
            ElarionUiKit.ApplyRounded(bg);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = false;
            hlg.childControlWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 8;
            hlg.padding = new RectOffset(6, 6, 4, 4);

            // Circular rune-framed portrait — built by the shared kit (disc + gilt ring).
            var portWrap = new GameObject("Portrait");
            portWrap.transform.SetParent(go.transform, false);
            var portRt = portWrap.AddComponent<RectTransform>();
            portRt.sizeDelta = new Vector2(40, 40);
            var portLe = portWrap.AddComponent<LayoutElement>();
            portLe.minWidth = 40; portLe.preferredWidth = 40;
            var portrait = ElarionUiKit.Portrait(portWrap.transform, null, active: false);

            // Right column: name + bars
            var right = new GameObject("RightCol");
            right.transform.SetParent(go.transform, false);
            var rightRt = right.AddComponent<RectTransform>();
            rightRt.sizeDelta = new Vector2(346, 44);
            var rightLe = right.AddComponent<LayoutElement>();
            rightLe.minWidth = 346; rightLe.preferredWidth = 346;
            var v = right.AddComponent<VerticalLayoutGroup>();
            v.childControlHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            v.childAlignment = TextAnchor.MiddleLeft;
            v.spacing = 1;

            // Name — parchment cream on dark glass (active state re-tinted in Render)
            var nameGO = CreateText("Hero", right.transform, Vector2.zero, ElarionUi.FontLabel, ElarionUi.Parchment, TextAlignmentOptions.Left);
            var nameTmp = nameGO.GetComponent<TextMeshProUGUI>();
            nameTmp.characterSpacing = 1f;

            // HP / MP / ATB rows — each a kit Bar inside a sized row container.
            var hpBar = CreateBarRow(right.transform, "HP", ElarionUiKit.BarKind.Hp, out var hpText);
            var mpBar = CreateBarRow(right.transform, "MP", ElarionUiKit.BarKind.Mp, out var mpText);
            var atbBar = CreateBarRow(right.transform, "ATB", ElarionUiKit.BarKind.Atb, out _);

            var slot = new PartySlot
            {
                Root = go,
                Portrait = portrait.image,
                PortraitRing = portrait.ring,
                Name = nameTmp,
                HpBar = hpBar,
                HpText = hpText,
                MpBar = mpBar,
                MpText = mpText,
                AtbBar = atbBar
            };
            return slot;
        }

        /// <summary>
        /// One label + a shared-kit <see cref="ElarionUiKit.Bar"/> gauge laid out in a row.
        /// The kit Bar (Well track + filled rounded fill + RPG-pack ornate frame, with an
        /// optional inline value for HP/MP) fills the sized track container, so combat renders
        /// the IDENTICAL ornate bar as town. Returns the fill Image so the caller drives
        /// fillAmount; the inline value label (HP/MP only) is returned via <paramref name="valueText"/>.
        /// </summary>
        private Image CreateBarRow(Transform parent, string label, ElarionUiKit.BarKind kind, out TextMeshProUGUI valueText)
        {
            valueText = null;
            var row = new GameObject(label + "Row");
            row.transform.SetParent(parent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(340, 13);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 13; rowLe.preferredHeight = 13;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childControlHeight = false;
            h.childControlWidth = false;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 5;

            // Label — muted parchment, readable on dark glass
            var lbl = CreateText(label, row.transform, Vector2.zero, ElarionUi.FontMicro, ElarionUi.ParchmentDim, TextAlignmentOptions.Left);
            var lblRt = lbl.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(26, 12);
            var lblLe = lbl.AddComponent<LayoutElement>();
            lblLe.minWidth = 26; lblLe.preferredWidth = 26;

            // Track container (sized) — the kit Bar fills it by fraction.
            bool hasValue = label == "HP" || label == "MP";
            float trackW = hasValue ? 196f : 250f;
            var track = new GameObject("Track");
            track.transform.SetParent(row.transform, false);
            var trackRt = track.AddComponent<RectTransform>();
            trackRt.sizeDelta = new Vector2(trackW, 9);
            var trackLe = track.AddComponent<LayoutElement>();
            trackLe.minWidth = trackW; trackLe.preferredWidth = trackW;
            trackLe.minHeight = 9; trackLe.preferredHeight = 9;

            // Shared kit Bar — fills the track container. HP/MP carry an inline value label.
            var bar = ElarionUiKit.Bar(track.transform, kind, Vector2.zero, Vector2.one, withValue: hasValue);

            if (hasValue && bar.valueLabel != null)
            {
                valueText = bar.valueLabel as TextMeshProUGUI;
            }

            return bar.fill; // caller drives fill.fillAmount
        }

        // ── Public API for BattleController to drive ─────────────────────────

        public void Render(ATBRuntimeState runtime)
        {
            var state = runtime?.Battle;
            if (state == null)
            {
                // Built-but-nothing-to-show: the HUD renders blank because there is no live battle.
                FlowTrace.Once("UI", "battlehud-render-null-state",
                    "Render: runtime/Battle is null — HUD has no state to paint (data-empty, not a build miss).");
                return;
            }
            _lastState = state;

            // WO-744 landmine 1: keep the push direction — the controller pushes Render, Render
            // pushes the read-only snapshot into the VM (flag-gated). The feel-sim below is untouched.
            if (_useVm && _vm != null) _vm.PushSnapshot(state);

            // Top info
            string activeName = "";
            if (!string.IsNullOrEmpty(state.ActiveUnitId))
            {
                var active = state.Units.FirstOrDefault(u => u.Id == state.ActiveUnitId);
                activeName = active?.Name ?? "";
            }
            // WO-1857: {0} = the acting unit's own name (catalog data, not translated here). The
            // English possessive is a GRAMMAR construct, so the whole phrase is one format row -
            // never "name" + a localized "'s Turn" fragment.
            if (_turnText)
                _turnText.text = string.IsNullOrEmpty(activeName)
                    ? ""
                    : LocalText.Format("battle.hud.active_turn", activeName);
            // Real, scaled wave from the live battle state (BattleScaling drives difficulty by it).
            // Floor at 1 so a dungeon/dev battle (Wave 0) still reads "WAVE 1" rather than "WAVE 0".
            if (_waveText) _waveText.text = "WAVE " + Mathf.Max(1, state.Wave);

            // Battle log — append only entries added since the last shown index (the engine's
            // Log is append-only; mirrors BattleController's _lastProcessedLogIndex cursor).
            AppendNewLogEntries(state);

            // Party slots (first 4 party members)
            // TGVRU V: if the slot widgets were never built, the loop below silently fills nothing —
            // the party panel reads blank. Self-report once so a missing CreatePartyPanel is visible.
            if (_partySlots == null || _partySlots.Count == 0)
                FlowTrace.Once("UI", "battlehud-no-party-slots",
                    "Render: no party slots built (CreatePartyPanel did not run) — party panel renders blank.");
            int idx = 0;
            foreach (var u in state.Units.Where(u => u.Side == Side.Party))
            {
                if (idx >= _partySlots.Count) break;
                var s = _partySlots[idx++];
                UpdateSlot(s, u);
            }

            // Highlight active member: name goes gilt + portrait ring brightens to gilt.
            foreach (var s in _partySlots)
            {
                bool isActive = s.Name && s.Name.text == activeName && !string.IsNullOrEmpty(activeName);
                if (s.Name) s.Name.color = isActive ? AtbReadyColor : ElarionUi.Parchment;
                if (s.PortraitRing != null)
                    s.PortraitRing.color = isActive
                        ? ElarionUi.Gilt
                        : new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.85f);
            }
        }

        /// <summary>Append battle-log entries added since the last shown index, keeping the
        /// last <see cref="MaxLogLines"/> lines. Resets the cursor if a fresh (shorter) log
        /// appears — e.g. a new battle reuses the same HUD instance.</summary>
        private void AppendNewLogEntries(BattleState state)
        {
            if (_logText == null || state?.Log == null) return;

            int count = state.Log.Count;
            if (_lastShownLogIndex > count) // new/replaced battle — log shrank, re-baseline
            {
                _lastShownLogIndex = 0;
                _logLines.Clear();
            }

            for (int i = _lastShownLogIndex; i < count; i++)
            {
                var entry = state.Log[i];
                if (entry != null && !string.IsNullOrEmpty(entry.Text))
                    _logLines.Add(entry.Text);
            }
            _lastShownLogIndex = count;

            if (_logLines.Count > MaxLogLines)
                _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);

            _logText.text = string.Join("\n", _logLines);
        }

        private void UpdateSlot(PartySlot slot, BattleUnit u)
        {
            if (slot.Name) slot.Name.text = u.Name ?? "???";

            // Real class portrait via the shared kit resolver (ElarionUiKit.PortraitForClass).
            // Fill the inner disc image; keep the gilt ring/frame untouched. Fall back to the
            // kit placeholder disc when the sprite is absent (pack not imported / non-hero unit).
            if (slot.Portrait != null)
            {
                Sprite portrait = (u.HeroClass != null) ? PortraitFor(u.HeroClass.Value) : null;
                if (portrait != null)
                {
                    slot.Portrait.sprite = portrait;
                    slot.Portrait.color = Color.white;     // show the art unmodified
                    slot.Portrait.preserveAspect = true;
                }
                else
                {
                    slot.Portrait.sprite = ElarionUiKit.CircleSprite;  // placeholder disc fallback
                    slot.Portrait.color = ElarionUiKit.PortraitPlaceholder;
                }
            }
            if (slot.HpText) slot.HpText.text = $"{u.Hp}/{u.MaxHp}";
            if (slot.HpBar) slot.HpBar.fillAmount = u.MaxHp > 0 ? Mathf.Clamp01((float)u.Hp / u.MaxHp) : 0;

            // Resource acts as MP
            int curRes = u.Resource;
            int maxRes = u.MaxResource;
            if (slot.MpText) slot.MpText.text = $"{curRes}/{maxRes}";
            if (slot.MpBar) slot.MpBar.fillAmount = maxRes > 0 ? Mathf.Clamp01((float)curRes / maxRes) : 0;

            // ATB — use visual simulation or unit Atb (double). Keep the charging->ready
            // colour pop by re-tinting the kit Bar's fill per frame.
            float atb = 0f;
            if (_visualAtb.TryGetValue(u.Id, out float vis)) atb = vis;
            else if (u.Atb > 0) atb = Mathf.Clamp01((float)u.Atb);
            if (slot.AtbBar)
            {
                slot.AtbBar.fillAmount = atb;
                slot.AtbBar.color = atb >= 0.99f ? AtbReadyColor : AtbFillColor;
            }
        }

        public void TickVisualAtb(ATBRuntimeState runtime, float dt)
        {
            if (runtime?.Battle == null) return;
            var state = runtime.Battle;
            foreach (var u in state.Units)
            {
                if (!u.Alive) continue;
                if (!_visualAtb.ContainsKey(u.Id)) _visualAtb[u.Id] = 0f;

                float current = _visualAtb[u.Id];
                if (u.Id == state.ActiveUnitId)
                {
                    _visualAtb[u.Id] = 1f; // pinned when acting
                }
                else
                {
                    float speed = Mathf.Max(0.5f, (float)u.Speed);
                    _visualAtb[u.Id] = Mathf.MoveTowards(current, 1f, dt / (VisualAtbChargeSeconds / speed));
                }
            }
        }

        // Helper for controller to know current active for command enabling
        public string ActiveUnitId => _lastState?.ActiveUnitId;

        // Call from controller when battle ends or to reset
        public void Reset()
        {
            _skillsSubPanel?.SetActive(false);
            _pendingKind = PickKind.None;
            _visualAtb.Clear();
            _lastShownLogIndex = 0;
            _logLines.Clear();
            if (_logText != null) _logText.text = "";
        }
    }
}

// =============================================================================
// CircleScreenPanel - the full in-game CIRCLE screen (WO-1870 Lane B).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// A DUMB SKIN over CircleScreenVM. It builds code-built uGUI through ElarionUiKit,
// switches on vm.State, and routes every tap back as a command. It reads NO game
// state, builds NO request, formats NO number and composes NO name - every one of
// those already happened in the VM (WO-1870 section 2 + the Lane A hand-back A3).
//
// CONSTRUCTION LAW (the ManageScreenPanel discipline, copied deliberately):
//   * UXML DOES NOT WORK IN BUILDS - this is code-built uGUI via ElarionUiKit.
//   * ASCII ONLY in every TMP string. LiberationSans-SDF renders anything else as
//     tofu, so "->" not an arrow and "..." not an ellipsis glyph.
//   * NEVER convey meaning by COLOUR ALONE - the owner is red/green colourblind.
//     Every role, every degraded reading, every "this is your vote" and every
//     "this is your Circle" is a WORD the VM already handed over as a locale key.
//   * Fixed-PIXEL row bands (LayoutElement preferredHeight AND rt.sizeDelta.y),
//     never fractions of a scroll column - the WO-841/852 culling root cause.
//   * Every authored band clears ElarionUiKit.MinTouchPx. The band constants below
//     are fractions of the CANVAS height and are pinned by the [circle-touch-floor]
//     case, which multiplies each one by the SMALLEST captured reference height
//     (965.4 ref px at 2670x1200) and fails anything under the floor. So the
//     smallest legal band fraction is 112/965.4 = 0.1161, and nothing here is below
//     0.13. ClampMinTouch is a rescue, not a design.
//
// LOCALIZATION LAW: not one player-facing literal. Every string resolves from a key
// the VM published or from one of the key constants below, all of which live in the
// WO-1870 frozen list (section 6) and in all 10 catalogs.
//
// ORDERING IS LOAD-BEARING (ruling 2 / [circle-name-first]): the "Claim your Remnant
// name" step is authored BEFORE anything tab-shaped in this file, because it is the
// first thing a player without a name sees.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    /// <summary>
    /// The Circle screen. Registered on <see cref="PanelId.Circle"/> and on the
    /// <c>PanelManager</c> handle "Circle", so the modal arbiter closes Circle Chat when this
    /// opens and vice versa.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CircleScreenPanel : MonoBehaviour
    {
        private const string Sys = "Circle";

        // =====================================================================
        //  KEYS THIS VIEW OWNS
        // ---------------------------------------------------------------------
        //  The VM publishes most keys itself. These are the ones only a rendered
        //  control can name. ChatDoorKey is PUBLIC on purpose: ClanChatPanel's
        //  always-visible door resolves it from here, which keeps the key inside
        //  Assets/_Modules/HUD/Circle/ where [circle-keys-referenced] looks.
        // =====================================================================

        /// <summary>The label on the always-visible Circle door inside Circle Chat (WO-1870 section 5).</summary>
        public const string ChatDoorKey = "circle.chat.door";

        /// <summary>
        /// ClanChatPanel's no-session copy when a wallet exists but no live session (WO-1875).
        /// Named here so [circle-keys-referenced] can see the frozen key under this folder.
        /// </summary>
        public const string ChatNotSignedInKey = "clanChat.notSignedIn";

        /// <summary>
        /// One-shot Circle tab select. WRITTEN only by DevScenarioIntent under QA_SCENARIO_BUILD.
        /// Open() consumes it. Not a locale key.
        /// </summary>
        public const string QaSelectTabPrefKey = "vigil.qa.circleTab";

        private const string KeyCircleWord = "common.circle";
        private const string KeyHeaderCode = "circle.header.code";
        private const string KeyHeaderRole = "circle.header.role";
        private const string KeyNameChange = "remnant.name.change";
        private const string KeyNameCancel = "remnant.name.cancel";
        private const string KeyNameFallback = "remnant.name.fallback";

        private const string KeyCreateTitle = "circle.create.title";
        private const string KeyCreateName = "circle.create.name";
        private const string KeyCreateTag = "circle.create.tag";
        // ⛔ NO JOIN-POLICY TOGGLE AND NO VAULT REGISTER FORM IN v1 (owner ruling 2026-09-18).
        // The VM can drive both (ToggleCreateOpenJoin, SubmitRegisterVault) and the server stores
        // clans.join_policy, but api/clan/join.js still requires a code, so a toggle here would
        // promise a door the server does not open yet; and the register write can refuse by NAMING
        // a signer wallet, which has never been a player-facing surface. Lane C removed
        // circle.create.openJoin and the three circle.vault.register* keys in the same breath, so
        // there is no authored-but-unrendered key left behind either.
        private const string KeyCreateSubmit = "circle.create.submit";
        private const string KeyJoinTitle = "circle.join.title";
        private const string KeyJoinCode = "circle.join.code";
        private const string KeyJoinSubmit = "circle.join.submit";

        private const string KeyVerbPromote = "circle.verb.promote";
        private const string KeyVerbDemote = "circle.verb.demote";
        private const string KeyVerbKick = "circle.verb.kick";
        private const string KeyVerbLeave = "circle.verb.leave";
        private const string KeyVerbCopyCode = "circle.verb.copyCode";
        private const string KeyVerbChat = "circle.verb.chat";
        private const string KeyVerbRefresh = "circle.verb.refresh";
        private const string KeyLeaveCancel = "circle.leave.cancel";

        private const string KeyBoardMetric = "circle.leaderboard.metric";
        private const string KeyVaultSigners = "circle.vault.signers";
        private const string KeyVigilWeight = "circle.vigil.weight";
        private const string KeyBallotPropose = "circle.ballot.propose";
        private const string KeyBallotCloses = "circle.ballot.closes";
        private const string KeyBallotClosed = "circle.ballot.closed";
        private const string KeyBallotPerks = "circle.ballot.perks";
        private const string KeyCeremonyReplay = "circle.ceremony.replay";

        // =====================================================================
        //  THE BAND TABLE - fractions of the CANVAS height, resolved to fixed px once
        // ---------------------------------------------------------------------
        //  Each name ends in BandH / RowH / CellH because that IS the authoring
        //  convention [circle-touch-floor] parses. Anything smaller than a tap target
        //  (a caption, a hint line) is NOT a band: it is anchored inside one.
        // =====================================================================
        private const float HeaderBandH = 0.22f;   // name + tag + code + role + my name
        private const float VerbRowH = 0.14f;      // copy / chat / refresh / leave
        private const float FaceRowH = 0.14f;      // the four section faces
        private const float SectionRowH = 0.13f;   // a section caption inside the list
        private const float MemberRowH = 0.15f;    // a member, with up to three verbs
        private const float InfoRowH = 0.13f;      // a two-column read-only fact
        private const float FieldRowH = 0.14f;     // a labelled input field
        private const float ActionRowH = 0.14f;    // a submit / confirm control
        private const float PreviewRowH = 0.13f;   // the live composed-name preview
        private const float OptionCellH = 0.22f;   // a ballot option tile (title + line + verb)

        private const float BandGapPx = 12f;
        private const float MinListPx = 200f;
        /// <summary>The one status sentence. NOT a tap target, so it is sized in pixels and is
        /// deliberately not an authored touch band - the floor governs what a finger must hit.</summary>
        private const float StatusLinePx = 56f;

        // =====================================================================
        //  Runtime
        // =====================================================================
        private ElarionUiKit.ObsidianModal _modal;
        private Transform _body;
        private PanelHandle _panelHandle;
        private CircleScreenVM _vm;

        private float _canvasH = 1920f;
        private float _cursorPx;

        private int _builtShape = int.MinValue;
        private Transform _listContent;
        private TextMeshProUGUI _statusLine;

        // Persistent form widgets. THEY ARE NEVER REBUILT WHILE THE PLAYER IS TYPING:
        // every setter on the VM raises Changed, so a rebuild-per-keystroke would steal
        // focus on every character. Repaint patches these in place instead.
        private TMP_InputField _nameField;
        private TextMeshProUGUI _namePreview;
        private Button _nameSubmit;
        private TMP_InputField _createNameField;
        private TMP_InputField _createTagField;
        private Button _createSubmit;
        private TMP_InputField _joinCodeField;
        private Button _joinSubmit;
        private Button _signInButton;

        /// <summary>True while the screen is up. The modal is built on open and destroyed on close.</summary>
        public bool IsOpen => _modal != null && _modal.canvas != null;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Circle", Close, () => IsOpen);
            PanelRouter.Register(PanelId.Circle, (Action)Open);
        }

        private void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.Circle, (Action)Open);
            Teardown();
        }

        /// <summary>Open if closed, close if open. The gear-drawer / chat-panel door calls Open.</summary>
        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>Build and show the screen.</summary>
        public void Open()
        {
            if (IsOpen)
            {
                ConsumeQaSelectTab();
                return;
            }
            using var _ = FlowTrace.Enter(Sys, "CircleScreenPanel.Open");

            _vm = CircleScreenVM.CreateDefault(Close);
            _vm.Changed += Repaint;
            ConsumeQaSelectTab();

            _modal = ElarionUiKit.BuildObsidianModal("CircleScreenUI", _vm.Title,
                new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.95f), Close,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "crest");

            _body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body
                : _modal.chrome.content.transform;

            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                // Battle-lock reject - never force-show. Tear the whole thing back down.
                FlowTrace.Warn(Sys, "PanelManager refused the Circle screen (a lock is held) - closing again.");
                Teardown();
                return;
            }

            Canvas.ForceUpdateCanvases();
            _canvasH = ElarionUiKit.PostScaleCanvasHeight(_body);
            if (_canvasH < 1f) _canvasH = 1920f;

            _builtShape = int.MinValue;
            Repaint();
            FlowTrace.Step(Sys, "Circle screen opened; state=" + _vm.State + " tab=" + _vm.ActiveTab);
        }

        /// <summary>Hide and destroy the screen.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            FlowTrace.Step(Sys, "Circle screen closed.");
            PanelManager.NotifyClosed(_panelHandle);
            Teardown();
        }

        private void Teardown()
        {
            if (_vm != null)
            {
                _vm.Changed -= Repaint;
                _vm.Dispose();
                _vm = null;
            }
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            _modal = null;
            _body = null;
            _listContent = null;
            _statusLine = null;
            ForgetFormWidgets();
            _builtShape = int.MinValue;
        }

        /// <summary>
        /// Consume the one-shot QA tab pref written by DevScenarioIntent. Safe before the
        /// modal exists: SelectTab raises Changed and Repaint no-ops while !IsOpen.
        /// </summary>
        private void ConsumeQaSelectTab()
        {
            if (_vm == null) return;
            string raw = null;
            try { raw = PlayerPrefs.GetString(QaSelectTabPrefKey, string.Empty); }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "QaSelectTab PlayerPrefs read threw " + ex.GetType().Name);
                return;
            }
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                PlayerPrefs.DeleteKey(QaSelectTabPrefKey);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "QaSelectTab PlayerPrefs delete threw " + ex.GetType().Name);
            }

            CircleScreenVM.CircleTab tab;
            if (string.Equals(raw, "members", StringComparison.OrdinalIgnoreCase))
                tab = CircleScreenVM.CircleTab.Members;
            else if (string.Equals(raw, "ballots", StringComparison.OrdinalIgnoreCase))
                tab = CircleScreenVM.CircleTab.Ballots;
            else if (string.Equals(raw, "vault", StringComparison.OrdinalIgnoreCase))
                tab = CircleScreenVM.CircleTab.Vault;
            else
            {
                FlowTrace.Warn(Sys, "QaSelectTab ignored unknown value.");
                return;
            }
            _vm.SelectTab(tab);
            FlowTrace.Step(Sys, "QaSelectTab -> " + tab);
        }

        private void ForgetFormWidgets()
        {
            _nameField = null;
            _namePreview = null;
            _nameSubmit = null;
            _createNameField = null;
            _createTagField = null;
            _createSubmit = null;
            _joinCodeField = null;
            _joinSubmit = null;
            _signInButton = null;
        }

        // =====================================================================
        //  RENDER - the ONE entry point, bound to vm.Changed
        // =====================================================================

        /// <summary>
        /// Repaint on every VM change. The body is REBUILT only when the SHAPE moves (the state,
        /// or the selected section); otherwise the existing widgets are patched in place, so a
        /// keystroke in a text field never destroys the field it was typed into.
        /// </summary>
        private void Repaint()
        {
            if (!IsOpen || _vm == null || _body == null) return;

            Guard.Try(Sys, "CircleScreenPanel.Repaint", () =>
            {
                int shape = ShapeOf();
                if (shape != _builtShape)
                {
                    _builtShape = shape;
                    BuildBody();
                }
                else
                {
                    PatchBody();
                }
                PaintStatusLine();
            });
        }

        private int ShapeOf() => ((int)_vm.State * 16) + (int)_vm.ActiveTab;

        // =====================================================================
        //  RULING 2 - THE CLAIM-YOUR-REMNANT-NAME STEP.
        // ---------------------------------------------------------------------
        //  ⛔ AUTHORED FIRST ON PURPOSE. It is the first thing a nameless player sees,
        //  and [circle-name-first] asserts that ordering against this file's own text.
        //  The player claims a FIRST NAME only - one token, the live server rule of 3..16
        //  [A-Za-z0-9_] (api/_lib/username-policy.js) that the VM mirrors - and the game
        //  composes the shown name with their Circle. The preview below is the VM's own
        //  compose call, never a concatenation written here.
        // =====================================================================
        private void BuildNeedsName()
        {
            var title = ListRow(PreviewRowH);
            KeyLabel(title, _vm.NamePromptKey, ElarionUi.FontTitle, ElarionUi.Gold,
                TextAlignmentOptions.Center);

            var hint = ListRow(PreviewRowH);
            KeyLabel(hint, _vm.NameHintKey, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Center);

            var field = ListRow(FieldRowH);
            KeyLabel(field, _vm.NameFieldKey, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, 0.02f, 0.30f);
            _nameField = MakeInputField(field, CircleScreenVM.NameMaxLength,
                new Vector2(0.32f, 0.05f), new Vector2(0.98f, 0.95f));
            _nameField.SetTextWithoutNotify(_vm.NameDraft ?? string.Empty);
            _nameField.onValueChanged.AddListener(OnNameDraftTyped);

            var preview = ListRow(PreviewRowH);
            _namePreview = KeyLabel(preview, null, ElarionUi.FontBody, ElarionUi.Parchment,
                TextAlignmentOptions.Center);

            var submit = ListRow(ActionRowH);
            _nameSubmit = ElarionUiKit.Button(submit, Resolve(_vm.NameSubmitKey),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.06f, 0.05f), new Vector2(0.60f, 0.95f), OnSubmitName);
            if (_vm.HasDisplayName)
            {
                ElarionUiKit.Button(submit, Resolve(KeyNameCancel), ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.64f, 0.05f), new Vector2(0.98f, 0.95f), OnCancelName);
            }

            PatchNeedsName();
        }

        private void PatchNeedsName()
        {
            if (_namePreview != null)
                _namePreview.text = CircleScreenVM.ComposeDisplayName(_vm.NameDraft, _vm.CircleName);
            if (_nameSubmit != null) _nameSubmit.interactable = _vm.CanSubmitName && !_vm.IsBusy;
        }

        private void OnNameDraftTyped(string typed)
        {
            _vm.SetNameDraft(typed);
        }

        private void OnSubmitName()
        {
            FlowTrace.Step(Sys, "verb: claim Remnant name (first-name token, composed by the VM).");
            _vm.SubmitDisplayName();
        }

        private void OnCancelName()
        {
            FlowTrace.Step(Sys, "verb: cancel the name edit and go back to the Circle.");
            _vm.CancelEditName();
        }

        private void OnEditName()
        {
            FlowTrace.Step(Sys, "verb: change my Remnant name.");
            _vm.EditDisplayName();
        }

        // =====================================================================
        //  BODY - one branch per CircleState
        // =====================================================================

        /// <summary>
        /// ⛔ THE BAND LAW, AND WHY ALMOST NOTHING IS A FIXED BAND.
        /// ---------------------------------------------------------------------
        /// Bands are absolutely stacked from the top of the body and NOTHING CLIPS THEM, so a
        /// fixed stack taller than the well paints outside the frame - silently, and worst on the
        /// smallest device. The measured well is small: ManageScreenPanel.cs records FrameCore's
        /// body resolving to ~533 reference px at 2670x1200, and that screen's own band table is
        /// written around it. Five touch-floor bands (5 x ~130px) already exceed it.
        ///
        /// So the FIXED set is deliberately minimal - the section faces (which must stay reachable
        /// while the list scrolls) and the one status line - and EVERYTHING ELSE, including the
        /// header, the verb row and both forms, lives in the scrolling column. The fixed heights
        /// are SUMMED, subtracted from the MEASURED well, and the list takes the remainder; if the
        /// remainder falls under the floor we say so IN PIXELS and shrink deliberately, which is
        /// the ManageScreenPanel law rather than a silent overflow.
        /// </summary>
        private void BuildBody()
        {
            ForgetFormWidgets();
            _listContent = null;
            _statusLine = null;
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);

            _cursorPx = 0f;

            // ---- band 1 (fixed, InCircle only): the four section faces ----
            if (_vm.State == CircleScreenVM.CircleState.InCircle) BuildFaceRow();

            // ---- band 2 (flexible): the scrolling column, sized from the MEASURED well ----
            float wellPx = PanelBodyHeightPx();
            float fixedPx = _cursorPx + StatusLinePx + BandGapPx;
            float remaining = wellPx - fixedPx;
            if (remaining < MinListPx)
            {
                FlowTrace.Warn(Sys, "Circle body well is " + wellPx.ToString("0") + "px and the fixed bands " +
                    "take " + fixedPx.ToString("0") + "px, leaving " + remaining.ToString("0") +
                    "px for the list - under the " + MinListPx.ToString("0") + "px floor. Shrinking to the " +
                    "floor deliberately; the list scrolls, so nothing is lost, but the well is tighter than " +
                    "this screen's band table assumes.");
                remaining = MinListPx;
            }
            BuildListHost(remaining);

            switch (_vm.State)
            {
                case CircleScreenVM.CircleState.NeedsName:
                    BuildNeedsName();
                    break;
                case CircleScreenVM.CircleState.NoWallet:
                case CircleScreenVM.CircleState.Loading:
                case CircleScreenVM.CircleState.Unreachable:
                case CircleScreenVM.CircleState.NotSignedIn:
                    BuildNoticeOnly();
                    break;
                case CircleScreenVM.CircleState.NotInCircle:
                    BuildCreateOrJoin();
                    break;
                default:
                    BuildSectionContent();
                    break;
            }

            // ---- band 3 (fixed): the ONE status line. Not a tap target, so it is sized in
            //      pixels rather than against the touch floor - the floor governs what a finger
            //      must hit, not what a sentence must fit.
            _statusLine = KeyLabel(BandPx(_body, StatusLinePx), null, ElarionUi.FontLabel,
                ElarionUi.Parchment, TextAlignmentOptions.Center);
        }

        private void PatchBody()
        {
            switch (_vm.State)
            {
                case CircleScreenVM.CircleState.NeedsName:
                    PatchNeedsName();
                    break;
                case CircleScreenVM.CircleState.NotInCircle:
                    PatchCreateOrJoin();
                    break;
                case CircleScreenVM.CircleState.InCircle:
                    BuildSectionContent();
                    break;
                case CircleScreenVM.CircleState.NotSignedIn:
                    if (_signInButton != null) _signInButton.interactable = _vm.CanSignIn;
                    break;
            }
        }

        /// <summary>
        /// No wallet / still loading / could not reach the server / not signed in. ONE sentence,
        /// plus the one face the player can use. The sentence itself is the VM's key - a blank
        /// panel is the failure mode CLAUDE.md section 12 forbids.
        /// WO-1875: NotSignedIn is GOLD SIGN IN (the minting read) with Refresh beside it.
        /// Colour is never meaning alone - the word SIGN IN is the cue, gold is the emphasis.
        /// </summary>
        private void BuildNoticeOnly()
        {
            // The key is chosen by the STATE, not left to whatever the last tab happened to set.
            // Loading has no sentence of its own on purpose - the screen is about to answer, and
            // inventing a "please wait" string would need an 11th catalog row to say nothing.
            string key = _vm.ErrorKey;
            if (string.IsNullOrEmpty(key))
            {
                if (_vm.State == CircleScreenVM.CircleState.NoWallet) key = "circle.error.noWallet";
                else if (_vm.State == CircleScreenVM.CircleState.NotSignedIn) key = "circle.error.notSignedIn";
                else if (_vm.State == CircleScreenVM.CircleState.Unreachable) key = "circle.error.unreachable";
            }

            var band = ListRow(SectionRowH);
            KeyLabel(band, key, ElarionUi.FontBody, ElarionUi.Parchment,
                TextAlignmentOptions.Center);

            var actions = ListRow(ActionRowH);
            if (_vm.State == CircleScreenVM.CircleState.NotSignedIn)
            {
                _signInButton = ElarionUiKit.Button(actions, Resolve(CircleScreenVM.SignInFaceKey),
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.02f, 0.05f), new Vector2(0.58f, 0.95f), OnSignIn);
                if (_signInButton != null) _signInButton.interactable = _vm.CanSignIn;
                ElarionUiKit.Button(actions, Resolve(KeyVerbRefresh), ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.62f, 0.05f), new Vector2(0.98f, 0.95f), OnRefresh);
            }
            else
            {
                ElarionUiKit.Button(actions, Resolve(KeyVerbRefresh), ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.28f, 0.05f), new Vector2(0.72f, 0.95f), OnRefresh);
            }
        }

        // =====================================================================
        //  NOT IN A CIRCLE - create, or join by code
        // ---------------------------------------------------------------------
        //  RULING 4: there is no stake precondition on this screen and none may be added.
        //  Joining is gated on the CODE SHAPE alone, which is what the VM's CanSubmitJoin
        //  computes and what [circle-join-ungated] pins.
        // =====================================================================
        private void BuildCreateOrJoin()
        {
            KeyLabel(ListRow(SectionRowH), KeyCreateTitle, ElarionUi.FontTitle,
                ElarionUi.Gold, TextAlignmentOptions.Left, 0.02f, 0.98f);

            var nameRow = ListRow(FieldRowH);
            KeyLabel(nameRow, KeyCreateName, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, 0.02f, 0.30f);
            _createNameField = MakeInputField(nameRow, CircleScreenVM.CircleNameMaxLength,
                new Vector2(0.32f, 0.05f), new Vector2(0.98f, 0.95f));
            _createNameField.SetTextWithoutNotify(_vm.CreateName ?? string.Empty);
            _createNameField.onValueChanged.AddListener(OnCreateNameTyped);

            var tagRow = ListRow(FieldRowH);
            KeyLabel(tagRow, KeyCreateTag, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, 0.02f, 0.30f);
            _createTagField = MakeInputField(tagRow, CircleScreenVM.CircleTagMaxLength,
                new Vector2(0.32f, 0.05f), new Vector2(0.62f, 0.95f));
            _createTagField.SetTextWithoutNotify(_vm.CreateTag ?? string.Empty);
            _createTagField.onValueChanged.AddListener(OnCreateTagTyped);

            KeyLabel(ListRow(PreviewRowH), _vm.CreateHintKey, ElarionUi.FontLabel,
                ElarionUi.ParchmentDim, TextAlignmentOptions.Left, 0.02f, 0.98f);

            var createRow = ListRow(ActionRowH);
            _createSubmit = ElarionUiKit.Button(createRow, Resolve(KeyCreateSubmit),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), OnSubmitCreate);

            KeyLabel(ListRow(SectionRowH), KeyJoinTitle, ElarionUi.FontTitle,
                ElarionUi.Gold, TextAlignmentOptions.Left, 0.02f, 0.98f);

            var codeRow = ListRow(FieldRowH);
            KeyLabel(codeRow, KeyJoinCode, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, 0.02f, 0.30f);
            _joinCodeField = MakeInputField(codeRow, CircleScreenVM.CircleCodeLength,
                new Vector2(0.32f, 0.05f), new Vector2(0.72f, 0.95f));
            _joinCodeField.SetTextWithoutNotify(_vm.JoinCode ?? string.Empty);
            _joinCodeField.onValueChanged.AddListener(OnJoinCodeTyped);

            KeyLabel(ListRow(PreviewRowH), _vm.JoinHintKey, ElarionUi.FontLabel,
                ElarionUi.ParchmentDim, TextAlignmentOptions.Left, 0.02f, 0.98f);

            var joinRow = ListRow(ActionRowH);
            _joinSubmit = ElarionUiKit.Button(joinRow, Resolve(KeyJoinSubmit),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.06f, 0.05f), new Vector2(0.60f, 0.95f), OnSubmitJoin);
            ElarionUiKit.Button(joinRow, Resolve(KeyVerbRefresh), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.64f, 0.05f), new Vector2(0.98f, 0.95f), OnRefresh);

            PatchCreateOrJoin();
        }

        private void PatchCreateOrJoin()
        {
            if (_createSubmit != null) _createSubmit.interactable = _vm.CanSubmitCreate && !_vm.IsBusy;
            if (_joinSubmit != null) _joinSubmit.interactable = _vm.CanSubmitJoin && !_vm.IsBusy;
            if (_joinCodeField != null && !string.Equals(_joinCodeField.text, _vm.JoinCode, StringComparison.Ordinal))
            {
                // The VM upper-cases the draft; echo its normalised form back without re-raising.
                _joinCodeField.SetTextWithoutNotify(_vm.JoinCode ?? string.Empty);
            }
        }

        private void OnCreateNameTyped(string typed) => _vm.SetCreateName(typed);
        private void OnCreateTagTyped(string typed) => _vm.SetCreateTag(typed);
        private void OnJoinCodeTyped(string typed) => _vm.SetJoinCode(typed);

        private void OnSubmitCreate()
        {
            FlowTrace.Step(Sys, "verb: create a Circle.");
            _vm.SubmitCreate();
        }

        private void OnSubmitJoin()
        {
            FlowTrace.Step(Sys, "verb: join a Circle by code (no stake precondition - ruling 4).");
            _vm.SubmitJoin();
        }

        // =====================================================================
        //  IN A CIRCLE - header, verbs, the four section faces, the list
        // =====================================================================
        /// <summary>
        /// The header is a SCROLLED row, not a fixed band, and that is what keeps it honest: it is
        /// rebuilt with the rest of the column on every VM change, so a claimed name, a role that
        /// moved or the member count after a join lands immediately. A fixed header painted once
        /// in BuildBody would have gone stale until the state or the section changed.
        /// </summary>
        private void BuildHeader()
        {
            var head = ListRow(HeaderBandH);

            KeyLabel(head, KeyCircleWord, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, 0.02f, 0.40f, 0.78f, 0.98f);

            var title = MakeText(head, ElarionUi.FontTitle, ElarionUi.Gold,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0.50f), new Vector2(0.52f, 0.80f));
            title.text = _vm.CircleName;

            var tag = MakeText(head, ElarionUi.FontBody, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.53f, 0.50f), new Vector2(0.61f, 0.80f));
            tag.text = _vm.CircleTag;

            if (!string.IsNullOrEmpty(_vm.VigilWordKey))
            {
                var vigilWord = MakeText(head, ElarionUi.FontTitle, ElarionUi.Gold,
                    TextAlignmentOptions.Right, new Vector2(0.50f, 0.78f), new Vector2(0.98f, 0.98f));
                vigilWord.text = Resolve(_vm.VigilWordKey);
            }

            var codeLabel = MakeText(head, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0.26f), new Vector2(0.48f, 0.48f));
            codeLabel.text = Resolve(KeyHeaderCode) + " " + _vm.CircleCode;

            var roleLabel = MakeText(head, ElarionUi.FontLabel, ElarionUi.Parchment,
                TextAlignmentOptions.Left, new Vector2(0.50f, 0.26f), new Vector2(0.98f, 0.48f));
            roleLabel.text = Resolve(KeyHeaderRole) + " " + Resolve(_vm.MyRoleLabelKey);

            var countLabel = MakeText(head, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0.02f), new Vector2(0.48f, 0.24f));
            countLabel.text = _vm.MemberCountText;

            var policyLabel = MakeText(head, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.50f, 0.02f), new Vector2(0.98f, 0.24f));
            policyLabel.text = Resolve(_vm.JoinPolicyLabelKey);

            var mine = MakeText(head, ElarionUi.FontBody, ElarionUi.Parchment,
                TextAlignmentOptions.Right, new Vector2(0.62f, 0.50f), new Vector2(0.98f, 0.80f));
            if (_vm.HasDisplayName) mine.text = _vm.MyDisplayNameText;
            else mine.text = Resolve(KeyNameFallback);
        }

        private void BuildVerbRow()
        {
            var row = ListRow(VerbRowH);

            ElarionUiKit.Button(row, Resolve(KeyVerbCopyCode), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.01f, 0.06f), new Vector2(0.24f, 0.94f), OnCopyCode);
            ElarionUiKit.Button(row, Resolve(KeyVerbChat), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.26f, 0.06f), new Vector2(0.49f, 0.94f), OnOpenChat);
            ElarionUiKit.Button(row, Resolve(KeyVerbRefresh), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.51f, 0.06f), new Vector2(0.74f, 0.94f), OnRefresh);
            ElarionUiKit.Button(row, Resolve(KeyNameChange), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.76f, 0.06f), new Vector2(0.99f, 0.94f), OnEditName);

            // LEAVE is a two-step verb and the confirm is a SENTENCE, not a red button.
            var leaveRow = ListRow(ActionRowH);
            if (_vm.LeaveConfirmArmed)
            {
                KeyLabel(leaveRow, _vm.LeaveConfirmKey, ElarionUi.FontLabel, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, 0.02f, 0.54f);
                ElarionUiKit.Button(leaveRow, Resolve(KeyVerbLeave), ElarionUiKit.ButtonKind.Danger,
                    new Vector2(0.56f, 0.06f), new Vector2(0.76f, 0.94f), OnConfirmLeave);
                ElarionUiKit.Button(leaveRow, Resolve(KeyLeaveCancel), ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.78f, 0.06f), new Vector2(0.99f, 0.94f), OnCancelLeave);
            }
            else
            {
                ElarionUiKit.Button(leaveRow, Resolve(KeyVerbLeave), ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.72f, 0.06f), new Vector2(0.99f, 0.94f), OnRequestLeave);
            }
        }

        /// <summary>
        /// The four section faces. The ACTIVE one is named in a caption under the row as well as
        /// tinted, because a tint alone is not a readable state (the owner is red/green
        /// colourblind).
        /// </summary>
        private void BuildFaceRow()
        {
            var row = Band(_body, FaceRowH);
            var faces = _vm.Tabs;
            if (faces == null) return;

            int n = Mathf.Max(1, faces.Count);
            int i = 0;
            Guard.TryEach(Sys, "CircleScreenPanel.BuildFaceRow", faces, face =>
            {
                float w = 1f / n;
                float x0 = (i * w) + 0.005f;
                float x1 = ((i + 1) * w) - 0.005f;
                i++;
                var kind = face.IsActive ? ElarionUiKit.ButtonKind.Gold : ElarionUiKit.ButtonKind.Quiet;
                var captured = face;
                ElarionUiKit.Button(row, Resolve(face.LabelKey), kind,
                    new Vector2(x0, 0.06f), new Vector2(x1, 0.94f), () => OnSelectSection(captured));
            });

        }

        /// <summary>The active section, named in WORDS under the faces - a tint is not a state.</summary>
        private void BuildSectionCaption()
        {
            var active = ActiveFace();
            KeyLabel(ListRow(SectionRowH), active != null ? active.LabelKey : null,
                ElarionUi.FontBody, ElarionUi.Gold, TextAlignmentOptions.Left, 0.02f, 0.98f);
        }

        private CircleScreenVM.TabVM ActiveFace()
        {
            var faces = _vm.Tabs;
            if (faces == null) return null;
            for (int i = 0; i < faces.Count; i++) if (faces[i].IsActive) return faces[i];
            return null;
        }

        private void OnSelectSection(CircleScreenVM.TabVM face)
        {
            if (face == null) return;
            FlowTrace.Step(Sys, "section switched to " + face.Id);
            if (face.Activate != null) face.Activate();
            else _vm.SelectTab(face.Id);
        }

        // =====================================================================
        //  THE SCROLLING SECTION BODY
        // =====================================================================

        private void BuildListHost(float heightPx)
        {
            var band = BandPx(_body, heightPx);

            var viewport = ElarionUiKit.AddImage(band, "Viewport",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Color(0f, 0f, 0f, 0f));
            viewport.AddComponent<RectMask2D>();
            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.spacing = BandGapPx;
            layout.padding = new RectOffset(0, 0, (int)BandGapPx, (int)BandGapPx);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            _listContent = content.transform;
        }

        /// <summary>
        /// The whole in-Circle column, rebuilt on every VM change. THAT IS THE POINT: the leave
        /// confirm, the header facts and the roster all move without the screen's shape changing,
        /// and a shape-keyed rebuild alone would never have repainted them - the Leave verb would
        /// have armed and nothing would have appeared.
        /// ⚠ Known and accepted for v1: a rebuild returns the scroll position to the top.
        /// </summary>
        private void BuildSectionContent()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--) Destroy(_listContent.GetChild(i).gameObject);

            BuildHeader();
            BuildVerbRow();
            BuildSectionCaption();

            switch (_vm.ActiveTab)
            {
                case CircleScreenVM.CircleTab.Members: BuildMembers(); break;
                case CircleScreenVM.CircleTab.Leaderboard: BuildLeaderboard(); break;
                case CircleScreenVM.CircleTab.Vault: BuildVault(); break;
                default: BuildBallots(); break;
            }
        }

        private void BuildMembers()
        {
            var rows = _vm.Members;
            if (rows == null || rows.Count == 0)
            {
                EmptyLine(_vm.MembersEmptyKey);
                return;
            }

            Guard.TryEach(Sys, "CircleScreenPanel.BuildMembers", rows, m =>
            {
                var row = ListRow(MemberRowH);

                var who = MakeText(row, ElarionUi.FontBody, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, new Vector2(0.02f, 0.52f), new Vector2(0.58f, 0.96f));
                who.text = m.DisplayNameText;

                var facts = MakeText(row, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                    TextAlignmentOptions.Left, new Vector2(0.02f, 0.04f), new Vector2(0.58f, 0.50f));
                facts.text = Resolve(m.RoleLabelKey) + "   " + m.TenureText + "   " + Resolve(m.DegradedLabelKey);

                float x = 0.60f;
                if (m.CanPromote) x = VerbChip(row, x, KeyVerbPromote, m.Promote);
                if (m.CanDemote) x = VerbChip(row, x, KeyVerbDemote, m.Demote);
                if (m.CanKick) VerbChip(row, x, KeyVerbKick, m.Kick);
            });
        }

        private float VerbChip(Transform row, float x0, string key, Action verb)
        {
            const float w = 0.13f;
            ElarionUiKit.Button(row, Resolve(key), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(x0, 0.10f), new Vector2(x0 + w, 0.90f), () =>
                {
                    FlowTrace.Step(Sys, "member verb tapped: " + key);
                    if (verb != null) verb();
                });
            return x0 + w + 0.01f;
        }

        private void BuildLeaderboard()
        {
            var rows = _vm.Leaderboard;
            if (rows == null || rows.Count == 0)
            {
                EmptyLine(_vm.LeaderboardEmptyKey);
                return;
            }

            Guard.TryEach(Sys, "CircleScreenPanel.BuildLeaderboard", rows, r =>
            {
                var row = ListRow(InfoRowH);

                var rank = MakeText(row, ElarionUi.FontBody, ElarionUi.Gold,
                    TextAlignmentOptions.Left, new Vector2(0.02f, 0.05f), new Vector2(0.12f, 0.95f));
                rank.text = r.RankText;

                var who = MakeText(row, ElarionUi.FontBody, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, new Vector2(0.13f, 0.05f), new Vector2(0.55f, 0.95f));
                who.text = r.Name + "  " + r.Tag + "  " + Resolve(r.MineLabelKey);

                var metric = MakeText(row, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                    TextAlignmentOptions.Left, new Vector2(0.56f, 0.05f), new Vector2(0.80f, 0.95f));
                metric.text = Resolve(KeyBoardMetric) + " " + r.MetricText;

                var count = MakeText(row, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                    TextAlignmentOptions.Left, new Vector2(0.81f, 0.05f), new Vector2(0.98f, 0.95f));
                count.text = r.MemberCountText;
            });
        }

        /// <summary>
        /// READ-ONLY (owner ruling 2026-09-18). The leader-only register form is NOT rendered in
        /// v1 even though the VM can drive it - see the Lane B hand-back for the three keys that
        /// therefore stay unreferenced.
        /// </summary>
        private void BuildVault()
        {
            if (!_vm.HasVault)
            {
                EmptyLine(_vm.VaultEmptyKey);
                return;
            }

            var rows = _vm.VaultRows;
            if (rows != null)
            {
                Guard.TryEach(Sys, "CircleScreenPanel.BuildVault", rows, r =>
                {
                    var row = ListRow(InfoRowH);
                    KeyLabel(row, r.LabelKey, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                        TextAlignmentOptions.Left, 0.02f, 0.48f);
                    var value = MakeText(row, ElarionUi.FontBody, ElarionUi.Parchment,
                        TextAlignmentOptions.Left, new Vector2(0.50f, 0.05f), new Vector2(0.98f, 0.95f));
                    value.text = r.ValueText;
                });
            }

            var signers = _vm.VaultSignerShortTexts;
            if (signers == null || signers.Count == 0) return;

            KeyLabel(ListRow(SectionRowH), KeyVaultSigners, ElarionUi.FontBody, ElarionUi.Gold,
                TextAlignmentOptions.Left, 0.02f, 0.98f);
            Guard.TryEach(Sys, "CircleScreenPanel.BuildVaultSigners", signers, s =>
            {
                var row = ListRow(InfoRowH);
                var value = MakeText(row, ElarionUi.FontLabel, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, new Vector2(0.04f, 0.05f), new Vector2(0.98f, 0.95f));
                value.text = s;
            });
        }

        /// <summary>
        /// RULING 3. Only the narrative title and description of an option are rendered. The
        /// server's own options[].effect carries a stat magnitude and this View has NO binding
        /// for it - BallotOptionRowVM.EffectText is declared always-empty by the VM and is never
        /// read here. [circle-no-stat-copy] is what keeps that true.
        /// </summary>
        private void BuildBallots()
        {
            if (_vm.CanReplayVigil)
            {
                var replay = ListRow(ActionRowH);
                ElarionUiKit.Button(replay, Resolve(_vm.ReplayVigilKey ?? KeyCeremonyReplay),
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), OnReplayVigil);
            }

            if (!string.IsNullOrEmpty(_vm.VigilWordKey))
            {
                var wordRow = ListRow(SectionRowH);
                var word = MakeText(wordRow, ElarionUi.FontTitle, ElarionUi.Gold,
                    TextAlignmentOptions.Left, new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.95f));
                word.text = Resolve(_vm.VigilWordKey);
            }

            var weight = ListRow(InfoRowH);
            KeyLabel(weight, KeyVigilWeight, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, 0.02f, 0.48f);
            var weightValue = MakeText(weight, ElarionUi.FontBody, ElarionUi.Parchment,
                TextAlignmentOptions.Left, new Vector2(0.50f, 0.05f), new Vector2(0.80f, 0.95f));
            weightValue.text = _vm.VigilWeightText;
            var degraded = MakeText(weight, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.81f, 0.05f), new Vector2(0.98f, 0.95f));
            degraded.text = Resolve(_vm.VigilDegradedKey);

            var tiers = _vm.BallotTiers;
            if (tiers != null)
            {
                Guard.TryEach(Sys, "CircleScreenPanel.BuildBallotTiers", tiers, t =>
                {
                    var row = ListRow(MemberRowH);

                    var head = MakeText(row, ElarionUi.FontBody, ElarionUi.Parchment,
                        TextAlignmentOptions.Left, new Vector2(0.02f, 0.52f), new Vector2(0.72f, 0.96f));
                    head.text = t.TierText + "   " + t.ThresholdText + "   " + Resolve(t.LockedLabelKey);

                    var slate = MakeText(row, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                        TextAlignmentOptions.Left, new Vector2(0.02f, 0.04f), new Vector2(0.72f, 0.50f));
                    slate.text = SlateOf(t);

                    if (!t.CanPropose) return;
                    var captured = t;
                    ElarionUiKit.Button(row, Resolve(KeyBallotPropose), ElarionUiKit.ButtonKind.Gold,
                        new Vector2(0.74f, 0.10f), new Vector2(0.98f, 0.90f), () =>
                        {
                            FlowTrace.Step(Sys, "verb: propose a ballot at tier " + captured.Tier);
                            if (captured.Propose != null) captured.Propose();
                            else _vm.ProposeTier(captured.Tier);
                        });
                });
            }

            if (!_vm.HasBallot)
            {
                EmptyLine(_vm.BallotsEmptyKey);
                BuildPerks();
                return;
            }

            var timing = ListRow(InfoRowH);
            var timingText = MakeText(timing, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f));
            if (_vm.BallotClosed) timingText.text = Resolve(KeyBallotClosed);
            else timingText.text = LocalText.Format(KeyBallotCloses, _vm.BallotClosesAtIso);

            var options = _vm.BallotOptions;
            if (options != null)
            {
                Guard.TryEach(Sys, "CircleScreenPanel.BuildBallotOptions", options, o =>
                {
                    var cell = ListRow(OptionCellH);

                    var title = MakeText(cell, ElarionUi.FontBody, ElarionUi.Parchment,
                        TextAlignmentOptions.Left, new Vector2(0.02f, 0.66f), new Vector2(0.72f, 0.97f));
                    title.text = o.TitleText;

                    var body = MakeText(cell, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                        TextAlignmentOptions.Left, new Vector2(0.02f, 0.30f), new Vector2(0.72f, 0.64f));
                    body.text = o.DescriptionText;

                    var tally = MakeText(cell, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                        TextAlignmentOptions.Left, new Vector2(0.02f, 0.03f), new Vector2(0.72f, 0.28f));
                    tally.text = o.WeightText + "   " + o.VotersText + "   " + Resolve(o.MyVoteLabelKey);

                    if (!o.CanVote) return;
                    var captured = o;
                    ElarionUiKit.Button(cell, Resolve("circle.ballot.vote"), ElarionUiKit.ButtonKind.Gold,
                        new Vector2(0.74f, 0.28f), new Vector2(0.98f, 0.72f), () =>
                        {
                            FlowTrace.Step(Sys, "verb: vote on the open ballot.");
                            if (captured.Vote != null) captured.Vote();
                            else _vm.Vote(captured.OptionId);
                        });
                });
            }

            var turnout = ListRow(InfoRowH);
            var turnoutText = MakeText(turnout, ElarionUi.FontLabel, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Left, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f));
            turnoutText.text = _vm.TurnoutText;

            if (_vm.HasResult)
            {
                var result = ListRow(InfoRowH);
                KeyLabel(result, _vm.ResultReasonKey, ElarionUi.FontLabel, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, 0.02f, 0.48f);
                var won = MakeText(result, ElarionUi.FontBody, ElarionUi.Gold,
                    TextAlignmentOptions.Left, new Vector2(0.50f, 0.05f), new Vector2(0.98f, 0.95f));
                won.text = _vm.ResultWinningOptionTitleText;
            }

            BuildPerks();
        }

        private void BuildPerks()
        {
            var perks = _vm.Perks;
            if (perks == null || perks.Count == 0) return;

            KeyLabel(ListRow(SectionRowH), KeyBallotPerks, ElarionUi.FontBody, ElarionUi.Gold,
                TextAlignmentOptions.Left, 0.02f, 0.98f);

            Guard.TryEach(Sys, "CircleScreenPanel.BuildPerks", perks, p =>
            {
                var row = ListRow(InfoRowH);
                var text = MakeText(row, ElarionUi.FontLabel, ElarionUi.Parchment,
                    TextAlignmentOptions.Left, new Vector2(0.04f, 0.05f), new Vector2(0.98f, 0.95f));
                text.text = p.TitleText;
            });
        }

        /// <summary>
        /// The server's own published slate for a tier, titles only. RULING 3 again: the
        /// catalogue's effect strings never reach a label.
        /// </summary>
        private static string SlateOf(CircleScreenVM.BallotTierVM tier)
        {
            var options = tier != null ? tier.CatalogOptions : null;
            if (options == null || options.Count == 0) return string.Empty;
            var parts = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] != null) parts.Add(options[i].TitleText);
            }
            return string.Join("   ", parts);
        }

        // =====================================================================
        //  Verbs that live on the frame rather than a row
        // =====================================================================

        private void OnReplayVigil()
        {
            string door = typeof(VigilCeremonyPanel).Name;
            FlowTrace.Step(Sys, "verb: watch the last vigil via " + door);
            if (_vm.ReplayVigil != null) _vm.ReplayVigil();
            else PanelRouter.Open(PanelId.CeremonyOfVigil, "replay");
        }

        private void OnRefresh()
        {
            FlowTrace.Step(Sys, "verb: refresh the Circle screen.");
            _vm.RefreshAll();
        }

        private void OnSignIn()
        {
            FlowTrace.Step(Sys, "verb: sign in (the one minting Circle read).");
            _vm.SignIn();
        }

        private void OnCopyCode()
        {
            FlowTrace.Step(Sys, "verb: copy the invite code.");
            _vm.CopyCode();
        }

        private void OnOpenChat()
        {
            FlowTrace.Step(Sys, "verb: open Circle Chat from the Circle screen.");
            _vm.OpenChat();
        }

        private void OnRequestLeave()
        {
            FlowTrace.Step(Sys, "verb: leave requested - arming the confirm, calling nothing.");
            _vm.RequestLeave();
        }

        private void OnCancelLeave()
        {
            FlowTrace.Step(Sys, "verb: leave cancelled.");
            _vm.CancelLeave();
        }

        private void OnConfirmLeave()
        {
            FlowTrace.Step(Sys, "verb: leave confirmed.");
            _vm.ConfirmLeave();
        }

        // =====================================================================
        //  The ONE feedback line. A refusal is never silent and never a blank panel.
        // =====================================================================
        private void PaintStatusLine()
        {
            if (_statusLine == null) return;

            if (!string.IsNullOrEmpty(_vm.ErrorKey))
            {
                if (!string.Equals(_lastTracedError, _vm.ErrorKey, StringComparison.Ordinal))
                {
                    _lastTracedError = _vm.ErrorKey;
                    FlowTrace.Warn(Sys, "refusal surfaced to the player: " + _vm.ErrorKey);
                }
                _statusLine.color = ElarionUi.Parchment;
                _statusLine.text = Resolve(_vm.ErrorKey);
                return;
            }

            _lastTracedError = null;
            if (!string.IsNullOrEmpty(_vm.ToastKey))
            {
                _statusLine.color = ElarionUi.Gold;
                _statusLine.text = Resolve(_vm.ToastKey);
                return;
            }

            _statusLine.text = string.Empty;
        }

        private string _lastTracedError;

        // =====================================================================
        //  Layout primitives
        // =====================================================================

        private float PanelBodyHeightPx()
        {
            var rt = _body as RectTransform;
            float h = rt != null ? rt.rect.height : 0f;
            if (h > 1f) return h;
            return _canvasH * 0.7f;
        }

        /// <summary>
        /// One band, stacked under the previous one. FIXED PIXELS: the fraction is resolved
        /// against the canvas height exactly once per build, and the band owns that height on
        /// both the RectTransform and its LayoutElement.
        /// </summary>
        private RectTransform Band(Transform host, float fraction)
            => BandPx(host, fraction * _canvasH);

        /// <summary>The same stacker, given the height directly in reference pixels.</summary>
        private RectTransform BandPx(Transform host, float heightPx)
        {
            float px = Mathf.Max(1f, heightPx);
            var go = new GameObject("Band", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, px);
            rt.anchoredPosition = new Vector2(0f, -_cursorPx);
            _cursorPx += px + BandGapPx;
            return rt;
        }

        /// <summary>A row inside the scrolling section body, sized in fixed pixels.</summary>
        private RectTransform ListRow(float fraction)
        {
            float px = Mathf.Max(1f, fraction * _canvasH);
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_listContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, px);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = px;
            le.minHeight = px;
            le.flexibleHeight = 0f;
            return rt;
        }

        private void EmptyLine(string key)
        {
            KeyLabel(ListRow(SectionRowH), key, ElarionUi.FontBody, ElarionUi.ParchmentDim,
                TextAlignmentOptions.Center, 0.04f, 0.96f);
        }

        private TextMeshProUGUI KeyLabel(Transform host, string key, int size, Color color,
            TextAlignmentOptions align, float x0 = 0.03f, float x1 = 0.97f,
            float y0 = 0.02f, float y1 = 0.98f)
        {
            var t = MakeText(host, size, color, align, new Vector2(x0, y0), new Vector2(x1, y1));
            t.text = Resolve(key);
            return t;
        }

        private static TextMeshProUGUI MakeText(Transform parent, int size, Color color,
            TextAlignmentOptions align, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.enableAutoSizing = true;
            t.fontSizeMin = 18f;
            t.fontSizeMax = size;
            t.text = string.Empty;
            return t;
        }

        /// <summary>
        /// A single-line text field over a rounded well. The kit has no input-field builder, so
        /// this mirrors RedeemCodePanel.MakeInputField (the standing precedent) rather than
        /// inventing a second shape.
        /// </summary>
        private static TMP_InputField MakeInputField(Transform parent, int characterLimit,
            Vector2 min, Vector2 max)
        {
            var host = new GameObject("Field", typeof(Image), typeof(TMP_InputField));
            host.transform.SetParent(parent, false);
            var rt = (RectTransform)host.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = host.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            ElarionUiKit.ApplyRounded(bg);

            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(host.transform, false);
            var art = (RectTransform)areaGo.transform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(14f, 4f);
            art.offsetMax = new Vector2(-14f, -4f);

            var text = ElarionUiKit.Label(areaGo.transform, string.Empty, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.Left, 0f, 1f);

            var field = host.GetComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.textViewport = art;
            field.textComponent = text;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.text = string.Empty;
            return field;
        }

        /// <summary>
        /// Key -> player copy. A null or empty key resolves to nothing, which is how the VM says
        /// "this fact is not true right now" for every label-key field it publishes
        /// (DegradedLabelKey, MineLabelKey, MyVoteLabelKey, LockedLabelKey).
        /// </summary>
        private static string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return new LocalizedText(key).Resolve();
        }
    }
}

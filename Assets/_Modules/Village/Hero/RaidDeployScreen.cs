// =============================================================================
// RaidDeployScreen — the PRE-raid tactical deploy screen (screen 3 of
// docs/RAID_TROOP_UI.md). Code-built uGUI (NO UXML — UXML does not render in
// player builds, project hard rule), routed through the SHARED presentation kit
// (DeNelle.Core.UI.ElarionUiKit) so it reads as the SAME designed game as the
// town HUD / PartyShopPanelMvvm / TroopTrainingPanel: dark-wood + gold framing, gold serif
// header, framed portraits, scroll list, and a big glowing DEPLOY CTA.
// -----------------------------------------------------------------------------
// MIRRORS PartyShopPanelMvvm / TroopTrainingPanel: BuildModalCanvas (sortingOrder 31050 —
// one band ABOVE RaidSelectionScreen so the deploy screen sits over the grid) +
// tap-outside Scrim + a framed dark-glass panel + a Header.
//
// LAYOUT (portrait) — WO-1519 (owner: "can we make this screen pop?"):
//   Header   "RAID: <displayName>" + the difficulty WORD + gold diamonds + the clock.
//   Left     three DISJOINT bands: YOUR FORCES, the party medallion row, and the ARMY
//            band (vm.ArmyBandText — its OWN row, which is the WO-1464 deploy-screen
//            defect: the cap line used to print on top of the portraits), then the
//            scrollable troop-card list from ArmyStorage.GetDeployable().
//   Right    three DISJOINT bands: the ENEMY BASE HERO CARD (boss emblem + name + Power
//            and Recon as two big numerals), the SPOILS chip row (wood / iron / gold
//            icons + numbers), then the SCOUT REPORT well.
//            ⛔ THERE IS NO ECHO GUIDE BAND HERE ANY MORE — owner ruling 2026-09-06
//            20:24, WO-1519 §2B: "Remove it from the deploy screen." The FEATURE is not
//            cut — EchoGuideService, its 24 memory lines and EchoWorldPresence's spoken
//            return all stay, and NoteExpeditionTarget is still called from OnDeploy so
//            the Echo still has somewhere to remember. What left is ONE SURFACE.
//   ⛔ EVERY BAND IS A LITERAL IN THE TABLE BELOW (BandsFor), and the table — not a copy
//   of its numbers in a suite — is what RaidDeployLayoutRegression MEASURES.
//   Bottom   WO-1403: the footer is BOUND to vm.Fielded. Zero troops -> ONE wide
//            primary, TRAIN TROOPS, a door to Manage > Troops (BEGIN ASSAULT is not
//            drawn). Troops > 0 -> EDIT ARMY (same door) + BEGIN ASSAULT ->
//            SceneRouter.GoRaid(def.sceneName). ("Army Ready?" / the "Auto Recommend"
//            stub are retired: a question on a button and a toast-only verb.)
//
// Open(SceneConfigDef) / Close() API; RaidSelectionScreen taps a card -> Open(def).
//
// TODO (out of first-pass scope, noted for the next increment):
//  - Live "Estimated Clear Time" that recomputes as troops are added/removed (this
//    pass shows the config's recommended/2-star band statically).
//  - WO-839 #3 built the SCOUT REPORT intel band (honest config facts: walls /
//    gates / garrison / boss, via vm.ScoutReport). Still TODO: the deeper analysis
//    (AA-tower density / choke vs open) driving the soft RPS + a meaningful
//    Auto-Recommend comp.
//  - Per-troop quantity sliders + deploy slots (this pass shows owned counts; the
//    in-raid deploy tray handles the actual unit placement).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;   // StarRatingRow (tofu-proof star row)

namespace DeNelle.Village.Hero
{
    public sealed class RaidDeployScreen : MonoBehaviour
    {
        private GameObject _ui;
        private RaidDeployVM _vm;                       // owns the party/army/power math (no GameState in the View)

        // UIF-01: single-modal arbiter handle (opening this closes the raid grid; back/ESC dismisses it).
        private PanelHandle _panelHandle;
        private RectTransform _troopListArea;          // list region inside the body zone
        private ElarionUiKit.ScrollZoneHandle _scroll; // kit fit-or-scroll handle (§1.14)

        // Cached self-instance so the static entry never FindObjectsByType-scans the scene.
        private static RaidDeployScreen _instance;

        private const float RowHeightPx = 60f;
        private const float RowGapPx    = 3f;

        // =====================================================================
        //  WO-1519 — THE GEOMETRY SURFACE. Exposed so an oracle can MEASURE it.
        // =====================================================================
        //  Until this lane the deploy screen's layout was six `private const float`
        //  literals, and the only thing judging them was a REGEX over this file
        //  (RaidDeployUiRegression [deploy-bands-disjoint] read
        //  "private const float GuideBandY0 = <literal>f;" out of the source text).
        //  That is the duplicated-state shape CLAUDE.md §2/§5/§16 each describe in
        //  their own words: rename a constant and the oracle stops judging anything,
        //  silently, while still reporting OK. The sibling screen already solved this —
        //  RaidSelectionScreen.CardBands is a LIVE table an oracle iterates — and this
        //  is the same cure applied to the door one file away.
        //
        //  THE LAW EVERY BAND MUST CLEAR, and it is not a preference:
        //  ElarionUiKit.FitSingleLine auto-sizes DOWN to ElarionUiKit.FontFloor (30),
        //  and TMP's Ellipsis overflow CULLS THE WHOLE LINE when the floor's line height
        //  exceeds the rect — a too-thin band renders NOTHING, not something small
        //  (ElarionUiKitObsidian.cs:3096-3110). The post-layout relax guard is a RUNTIME
        //  component and does not run in an edit-mode headless capture, so it cannot be
        //  the answer either. The pixel a 30 pt line needs is
        //  RaidSelectionScreen.NeedPx(30) = 38.6 — READ FROM THE SIBLING SCREEN'S OWN
        //  FUNCTION rather than copied, because a second copy of that arithmetic is the
        //  very failure this table exists to end.
        //
        //  ⚠ THE OLD BUDGET WAS UNDER THAT LAW AND SAID SO IN ITS OWN COMMENT. The
        //  WO-1385/1403 banner claimed "every single-line row >= 36 px so the 30 px
        //  FontFloor seats WITHOUT the runtime relax guard". 36 < 38.6. Those rows were
        //  one measurement away from rendering blank in exactly the headless capture the
        //  acceptance criteria are read off. Every band below is >= 39 ref px on the
        //  411 ref px phone body, and RaidDeployLayoutRegression measures it rather than
        //  trusting this sentence.
        // ---------------------------------------------------------------------
        //  THE BUDGET (fractions OF THE BODY ZONE; ~411 ref px at 2670x1200):
        //    LEFT                                   RIGHT
        //     forces   0.902-1.000  ( 40 px)         enemy    0.504-0.998  (203 px)
        //     party    0.662-0.892  ( 95 px)         spoils   0.394-0.492  ( 40 px)
        //     army     0.548-0.648  ( 41 px)         scout    0.000-0.382  (157 px)
        //     troops   0.000-0.532  (219 px)
        //  The two columns are INDEPENDENT stacks — a band only has to be disjoint from
        //  its own column's neighbours, which is why Column is on the struct.
        // =====================================================================

        /// <summary>The panel's anchors, exposed so the layout oracle measures the REAL panel
        /// rather than a typed copy of these two vectors.</summary>
        public static readonly Vector2 PanelAnchorMin = new Vector2(0.10f, 0.05f);
        public static readonly Vector2 PanelAnchorMax = new Vector2(0.90f, 0.95f);

        // =====================================================================
        //  THE BODY THIS SCREEN ACTUALLY GETS — the number the seat law is judged at.
        // =====================================================================
        //  ⚠ THIS IS A RECORDED MEASUREMENT, AND IT IS NOT THE SAME AS THE SIBLING
        //  SCREEN'S. RaidSelectionScreen derives its well from
        //  ElarionUiKit.ZonesFor(FrameCore) directly, and an oracle can call
        //  RaidSelectionScreen.ComputeWellBand to reproduce it. THIS panel cannot be
        //  reproduced that way: BuildObsidianPanel applies a CLOSE-BAND RESERVATION on
        //  top of the zones (ElarionUiKit.cs:611-676) that depends on the live canvas
        //  height, and ZonesFor / FrameZones / DefaultCloseZone are all PRIVATE to the
        //  kit. An oracle that used ComputeWellBand here would measure a 534 ref px body
        //  on the owner's surface while the screen really gets 411 — every band would
        //  clear the seat law in the suite and some would render blank on her phone.
        //  Measuring the wrong thing and reporting a pass is worse than not measuring.
        //
        //  SO THE FRACTION IS RECORDED HERE, WITH ITS PROVENANCE, AND IT IS THE FLOOR:
        //  the reservation lifts the footer to 0.217-0.347 and the body floor to 0.362
        //  under a body top of 0.835 -> 0.473 of the panel. That is the WO-1385 reading
        //  of this panel's own BuildObsidianPanel FlowTrace line, which prints
        //  "bodyYMin <before>-><after> ... reservedYMin=<n>" on EVERY framed panel build —
        //  so it is re-provable from any fresh capture, by grepping that one line, and
        //  does not have to be believed on this comment's word.
        //
        //  It is used as a FLOOR, never as "the" body: a bigger body only makes every
        //  band taller, so a layout that seats at 0.473 seats everywhere. On the owner's
        //  2670x1200 that floor is 411 ref px, the tightest of the four surfaces
        //  (1920x1080 gives 460, 2340x1080 gives 417, portrait gives 817).
        //
        //  ⛔ IF THE KIT EVER EXPOSES THE RESERVATION AS A PURE FUNCTION, DELETE THIS AND
        //  CALL IT. A recorded number is the second-best answer; it is here only because
        //  the first-best is private, and widening shared kit visibility is not something
        //  a player-facing lane gets to smuggle in (ARCHITECTURE_PRINCIPLES).
        public const float MinBodyFracOfPanel = 0.473f;

        /// <summary>Column key on a band — bands only have to clear their own stack.</summary>
        public const string ColumnLeft  = "left";
        public const string ColumnRight = "right";

        /// <summary>
        /// How far the whole layout drops on the PROCEDURAL chrome path (no FrameCore
        /// sub-header zone), where the badge/stars/clock meta row keeps the legacy body-top
        /// strip <see cref="MetaStripY0"/>..1.0 and everything else must clear it.
        /// </summary>
        public const float FallbackShift = 0.060f;

        /// <summary>The legacy body-top meta strip, used ONLY on the fallback path.</summary>
        public const float MetaStripY0 = 0.945f;

        // ── LEFT column (sub-header path; the fallback subtracts FallbackShift) ──
        private const float ForcesY0 = 0.902f, ForcesY1 = 1.000f;
        private const float PartyY0  = 0.662f, PartyY1  = 0.892f;
        private const float ArmyY0   = 0.548f, ArmyY1   = 0.648f;
        private const float ListY0   = 0.000f, ListY1   = 0.532f;

        // ── RIGHT column ──
        private const float ScoutBandY0  = 0.000f, ScoutBandY1  = 0.382f;
        private const float SpoilsBandY0 = 0.394f, SpoilsBandY1 = 0.492f;
        private const float EnemyBandY0  = 0.504f, EnemyBandY1  = 0.998f;

        // ── Column x-extents (WO-839 #3 seam, unchanged) ──
        private const float LeftColX1  = 0.49f;
        private const float RightColX0 = 0.51f;

        /// <summary>One authored band of the deploy body: which column it stacks in, its
        /// fractions, and the font its text must seat (0 = a container with no text of its
        /// own, exempt from the seat law but not from the disjointness law).</summary>
        public readonly struct DeployBand
        {
            public readonly string Name; public readonly string Column;
            public readonly float Y0, Y1; public readonly int FontPt;
            public DeployBand(string name, string column, float y0, float y1, int fontPt)
            { Name = name; Column = column; Y0 = y0; Y1 = y1; FontPt = fontPt; }
            /// <summary>Reference px this band gives its row on a body of <paramref name="bodyPx"/>.</summary>
            public float HavePx(float bodyPx) => (Y1 - Y0) * bodyPx;
            /// <summary>Reference px the row's text needs before TMP culls the whole line.
            /// A fitted label shrinks to ElarionUiKit.FontFloor, so the demand is the FLOOR's
            /// line, never the authored size.</summary>
            public float NeedsPx =>
                FontPt <= 0 ? 0f
                : RaidSelectionScreen.NeedPx(Mathf.Min(FontPt, Mathf.RoundToInt(ElarionUiKit.FontFloor)));
        }

        /// <summary>
        /// THE BODY'S BAND TABLE, live — RaidDeployLayoutRegression iterates THIS, so a
        /// renamed constant moves the oracle with it instead of blinding it.
        /// <paramref name="hasSubHeader"/> false = the procedural chrome path, where every
        /// band drops by <see cref="FallbackShift"/> to clear the legacy meta strip.
        /// </summary>
        public static DeployBand[] BandsFor(bool hasSubHeader)
        {
            float d = hasSubHeader ? 0f : FallbackShift;
            return new[]
            {
                new DeployBand("troops", ColumnLeft,  ListY0,          ListY1 - d,          0),
                new DeployBand("army",   ColumnLeft,  ArmyY0 - d,      ArmyY1 - d,          ElarionUi.FontLabel),
                new DeployBand("party",  ColumnLeft,  PartyY0 - d,     PartyY1 - d,         0),
                new DeployBand("forces", ColumnLeft,  ForcesY0 - d,    ForcesY1 - d,        ElarionUi.FontLabel),
                new DeployBand("scout",  ColumnRight, ScoutBandY0,     ScoutBandY1 - d,     ElarionUi.FontMicro),
                new DeployBand("spoils", ColumnRight, SpoilsBandY0 - d, SpoilsBandY1 - d,   ElarionUi.FontLabel),
                new DeployBand("enemy",  ColumnRight, EnemyBandY0 - d, EnemyBandY1 - d,     ElarionUi.FontLabel),
            };
        }

        // ── The ENEMY BASE hero card's interior (fractions OF THE CARD) ──────────
        // Boss emblem down the left; header, boss name and the two big numerals stacked
        // down the right. On the 203 ref px card every text row is >= 38.6 px, the height
        // RaidSelectionScreen.NeedPx(FontFloor=30) demands before TMP culls the line.
        private const float CardHdrY0   = 0.776f, CardHdrY1   = 0.980f;   // 41 px
        private const float CardNameY0  = 0.562f, CardNameY1  = 0.766f;   // 41 px
        private const float CardStatY0  = 0.222f, CardStatY1  = 0.552f;   // 67 px - the numerals
        private const float CardCapY0   = 0.010f, CardCapY1   = 0.212f;   // 41 px - their captions
        /// <summary>The RpgUi sprite ROLE the boss emblems live under
        /// (Resources/RpgUi/emblem/, mirrored by RpgUiImporter). RpgUiCatalog has no named
        /// const for it — the role is the folder name, and this is the one place this screen
        /// says it.</summary>
        private const string EmblemRole = "emblem";
        private const float CardEmblemX0 = 0.030f, CardEmblemX1 = 0.280f;
        private const float CardTextX0   = 0.300f, CardTextX1   = 0.980f;
        private const float CardStatMidX = 0.640f;   // the split between the two stat columns

        /// <summary>The hero card's own stacked bands (fractions of the card), for the same
        /// measured treatment as the body's. All four sit in one column.</summary>
        public static DeployBand[] EnemyCardBands()
        {
            return new[]
            {
                new DeployBand("card-captions", ColumnRight, CardCapY0,  CardCapY1,  ElarionUi.FontMicro),
                new DeployBand("card-stats",    ColumnRight, CardStatY0, CardStatY1, ElarionUi.FontHead),
                new DeployBand("card-boss",     ColumnRight, CardNameY0, CardNameY1, ElarionUi.FontLabel),
                new DeployBand("card-header",   ColumnRight, CardHdrY0,  CardHdrY1,  ElarionUi.FontLabel),
            };
        }

        // ── The party row's interior (fractions OF THE ROW) ──────────────────────
        // WO-1519 §2.3: the medallion grows (the 20:14 frame's portraits were "TINY") and
        // the class word owns a real row under it instead of the 11 px sliver WO-1385 found.
        private const float PartyNameY0  = 0.000f, PartyNameY1 = 0.450f;   // 43 px of a 95 px row
        private const float PartyPlateY0 = 0.470f, PartyPlateY1 = 1.000f;  // 50 px
        private const float PartyPlateHalfW = 0.040f;                      // was 0.028 (WO-1385)

        /// <summary>The party row's two stacked bands (fractions of the row).</summary>
        public static DeployBand[] PartyRowBands()
        {
            return new[]
            {
                new DeployBand("party-name",  ColumnLeft, PartyNameY0,  PartyNameY1,  ElarionUi.FontMicro),
                new DeployBand("party-plate", ColumnLeft, PartyPlateY0, PartyPlateY1, 0),
            };
        }

        // ── Entry ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Self-healing static entry: finds or creates a host GameObject carrying a
        /// RaidDeployScreen and opens it for <paramref name="def"/> (the raid the
        /// player tapped on the selection grid).
        /// </summary>
        public static void Open(SceneConfigDef def)
        {
            if (def == null)
            {
                // WO-1110 §2 — a lone Debug.LogWarning gave the PLAYER nothing: the deploy
                // screen simply never opened. Trace it AND say so on screen, so a missing def
                // never reads as an unresponsive UI.
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "RaidDeployScreen.Open(null) - no SceneConfigDef, the deploy screen cannot open.");
                ElarionUiKit.ShowToast("That raid could not be opened.",
                    ElarionUiKit.ToastTone.Danger);
                return;
            }
            var existing = _instance;
            if (existing == null)
            {
                var host = new GameObject("RaidDeployScreen");
                existing = host.AddComponent<RaidDeployScreen>();   // Awake caches _instance
            }
            existing.OpenInternal(def);
        }

        private void Awake()
        {
            if (_instance == null) _instance = this;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void OpenInternal(SceneConfigDef def)
        {
            Close();

            // WO-823 Phase E — the screen's ONE window onto readiness, taken ONCE here.
            // ArmyReadiness.Compute(GameState) is the single readiness formula in the game; this
            // is the only place that can see the live save (the in-flight Train-channel slots and
            // GameState.EverCompletedRaid), so the snapshot is taken here and HANDED to the VM,
            // which derives Fielded / ShowAssault / PrimaryCtaLabel from it. The View fetches; it
            // decides nothing — no predicate, no re-derived count, and no second opinion on "may
            // this player raid" (RaidEntryGate / RaidSelectionScreen stay that authority).
            // No GameState (headless / AutoPilot) -> Compute returns the WO-813/WO-820
            // never-false-block snapshot with zero slots, and never throws.
            var st = DeNelle.Core.State.GameStateService.Instance != null
                ? DeNelle.Core.State.GameStateService.Instance.State : null;
            var readiness = DeNelle.Village.ArmyReadiness.Compute(st);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy readiness snapshot: deployableSlots=" + readiness.DeployableSlots +
                " queued=" + readiness.QueuedSlots + " required=" + readiness.RequiredSlots +
                " cap=" + readiness.CapSlots + " ready=" + readiness.Ready +
                " firstRaidSoftGate=" + readiness.FirstRaidSoftGate);

            // VM FIRST — it resolves the army roster + party + troop facts from GameState/
            // TroopCatalog, so this View never touches either.
            _vm = RaidDeployVM.CreateDefault(def, readiness, Close);

            // 31050: one band above RaidSelectionScreen (31000) so deploy sits over the grid.
            _ui = ElarionUiKit.BuildModalCanvas("RaidDeployScreenUI", 31050);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: Close);

            // WO-562: canonical obsidian chrome (black + gold trim + shared Close) replaces
            // PanelFramed + a per-panel "X" Danger button. The chrome's header zone carries the
            // ONE title (UI matrix 2026-07-03: the extra procedural Header was a duplicate);
            // BuildHeader now adds only the badge / stars / target-time sub-row.
            // WO-714 P10: a raw id is never player-visible — missing displayName routes
            // through the ONE kit formatter.
            string raidName = !string.IsNullOrEmpty(_vm.DisplayNameRaw)
                ? _vm.DisplayNameRaw
                : (!string.IsNullOrEmpty(_vm.RaidId) ? ElarionUiKit.SpacedDisplayName(_vm.RaidId) : "Raid");
            // WO-1462 — THE BACKDROP IS THE KIT'S DEFAULT AND THIS SCREEN NOW TAKES IT.
            // This call used to pass `withBackdrop: false`, the exact D3 defect WO-1442 fixed on
            // the sibling RaidSelectionScreen five days earlier. The reasoning is identical and
            // is repeated here because the two doors are separate files: this panel has NO other
            // opaque layer. BuildObsidianPanel builds `chrome.content` at alpha 0 by design;
            // MedievalUiSkin.ApplyShell re-asserts alpha 0 on it; and the shell sprite it swaps
            // in (UI/ElarionMedieval/frames/modal-frame-16x9) is HOLLOW — alpha 0 at every
            // interior sample. Three transparent layers, so the town's resource read bled
            // straight through the deploy panel. The Scrim is a screen-wide 0.85 veil, not a
            // panel backing, and it rides the open fade, so it is not a substitute.
            // ⛔ DO NOT PASS `withBackdrop: false` BACK INTO THIS CALL — the kit's named
            // "Backdrop" (a 0.94-alpha plate, ElarionUiKit.cs:568,573-579) is the ONE layer
            // designed for this, and a bespoke opaque quad here would be a second authority.
            // Pinned by RaidSelectionLayoutRegression case S7:deploy-opaque-backdrop.
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, "RAID: " + raidName,
                PanelAnchorMin, PanelAnchorMax, Close,
                frameName: RpgUiCatalog.FrameCore);

            // WO-714 W4: ALL content drops into the FACTORY zones (chrome.layout.body /
            // .footer) — the kit owns the close-band reservation and the frame-filigree
            // margins, so the years of hand-tuned "dodge the medallion / dodge the Close"
            // panel fractions below are retired. Fractions in the builders are OF THE ZONE.
            var body = chrome.layout != null && chrome.layout.body != null
                ? (Transform)chrome.layout.body : chrome.content.transform;
            var footer = chrome.layout != null && chrome.layout.footer != null
                ? (Transform)chrome.layout.footer : body;
            // WO-839 #1: FrameCore now carves a thin SUB-HEADER band (right of the medallion
            // socket, under the title) -- the badge / stars / target-time meta row seats there
            // so the body top is no longer a second stacked header row. Null on the
            // procedural fallback path (frame art absent); builders then keep the legacy
            // body-top strip.
            var subHeader = chrome.layout != null && chrome.layout.subHeader != null
                ? (Transform)chrome.layout.subHeader : null;

            // WO-714 P8: the ONE shared open ease (scale target = the panel rect).
            ElarionUiKit.AttachPanelOpenFx(_ui,
                chrome.root != null ? chrome.root.transform as RectTransform : null);

            BuildHeader(subHeader, body);

            // LEFT column — Your Forces (party row + troop list + cap indicator).
            BuildLeftColumn(body, hasSubHeader: subHeader != null);

            // RIGHT column — battle preview + estimated clear time + summary + scout report.
            BuildCenterColumn(body, hasSubHeader: subHeader != null);

            // FOOTER action strip — Auto Recommend + the big glowing DEPLOY CTA.
            BuildDeployBar(footer);

            // UIF-01: join the single-modal arbiter (closes the raid grid it opened over; back/ESC
            // routes here). A battle-lock rejection tears this down (handle.Close) and returns.
            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("Raid Deploy", Close, () => _ui != null);
            if (!PanelManager.NotifyOpened(_panelHandle))
                return;

            Debug.Log($"[RaidDeployScreen] Opened for raid '{_vm.RaidId}' -> scene '{_vm.SceneName}'.");
        }

        // Meta row (difficulty badge + star row + target time). WO-839 #1: on the frame
        // path this seats in the chrome's SUB-HEADER band -- beside/below the title, right
        // of the medallion socket -- so it never stacks a second header row into the body
        // top and the pill can never collide with the medallion. Fallback (procedural
        // chrome, no sub-header zone): the legacy body-top strip.
        private void BuildHeader(Transform subHeader, Transform body)
        {
            Transform host = subHeader != null ? subHeader : body;
            // Fractions OF THE HOST: the sub-header band uses (near) full-height rows; the
            // body fallback keeps the legacy thin top strip.
            float y0 = subHeader != null ? 0.06f : 0.945f;
            float y1 = subHeader != null ? 0.94f : 1.00f;

            // WO-1519 §2.5 - THE COLOURED DIFFICULTY PILL IS RETIRED, and the reason is a
            // rule this project already holds: the owner is red/green colourblind, and the pill
            // carried its distinction in HUE ALONE (DifficultyColor mapped Regular -> Affordable
            // green, Hard -> amber, Extreme -> Danger red). To her, Regular and Extreme were the
            // same grey plate behind the same word. The WORD was already doing all the work; the
            // plate was doing none of it, and it was the loudest thing in the header.
            // ⛔ DO NOT REINSTATE A TINTED PLATE HERE. If difficulty ever needs a second channel,
            // it takes shape, weight or another word - never a colour. (DifficultyColor itself is
            // deleted with this call, so there is no helper left to quietly re-wire.)
            var badgeLbl = ElarionUiKit.Label(host, DifficultyLabel(_vm != null ? _vm.Difficulty : null),
                y0, y1, ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left,
                0.00f, 0.17f, bold: true);
            ElarionUiKit.FitSingleLine(badgeLbl);
            badgeLbl.raycastTarget = false;

            // Tofu fix (2026-07-02): ★ (U+2605) is in NO project SDF font (scanned —
            // zero m_Unicode:9733 hits), so the old "★★★" text rendered as boxes in
            // builds. Procedural gold diamonds instead (EndStateView's pattern via
            // the shared StarRatingRow), then a plain font-safe "Target:" label.
            StarRatingRow.Build(host, 3, 3, 0.19f, y0, 0.28f, y1, sizePx: 12f);
            // Honesty: this is the LIVE raid clock (3★ under-time), not a longer decorative target.
            var timeLbl = ElarionUiKit.Label(host, "Clock: " + FormatTime(_vm != null ? _vm.TargetTime : 0f),
                y0, y1, ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, 0.305f, 0.75f, bold: true);
            ElarionUiKit.FitSingleLine(timeLbl);
            timeLbl.raycastTarget = false;
        }

        // LEFT — Your Forces: hero + companions portrait row, then the scrollable
        // troop-card list grouped by TroopDefId, then the army-cap indicator.
        // WO-839 #1: with the meta row gone to the chrome sub-header, the left column
        // starts at the top of the body (spec: "YOUR FORCES" from body yMax ~= 0.90).
        // The fallback (no sub-header) keeps the legacy offsets below the body-top row.
        private void BuildLeftColumn(Transform body, bool hasSubHeader)
        {
            // WO-1519: every fraction comes off the ONE band table, so this builder and the
            // measuring oracle can never be reading different numbers.
            var bands = BandsFor(hasSubHeader);
            var forces = BandNamed(bands, "forces");
            var party  = BandNamed(bands, "party");
            var army   = BandNamed(bands, "army");
            var list   = BandNamed(bands, "troops");

            var lbl = ElarionUiKit.Label(body, "YOUR FORCES", forces.Y0, forces.Y1, ElarionUi.Gilt,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, 0.00f, LeftColX1, bold: true);
            ElarionUiKit.FitSingleLine(lbl);
            lbl.raycastTarget = false;

            // Hero + Companions portrait row.
            BuildPartyRow(body, party.Y0, party.Y1);

            // =============================================================
            //  THE ARMY BAND — WO-1519 §2.3 + the deploy half of WO-1464.
            // =============================================================
            // This was a bare label sharing the party row's airspace, and on the owner's
            // 20:14 frame it printed "Army: 10 / 10 slots" ON TOP of the Grom/Sylas
            // portraits. It now owns a band of its own, on a plate, with the state word
            // composed by the VM (vm.ArmyBandText - "ARMY 10 / 10 - FULL"), and the
            // disjointness is MEASURED rather than eyeballed.
            // ⛔ The View does not decide "full": ArmyFull is the VM's, from the same
            // used/cap read the legacy ArmyCapText line uses.
            var armyPlate = ElarionUiKit.Well(body,
                new Vector2(0.00f, army.Y0), new Vector2(LeftColX1, army.Y1));
            var armyPlateImg = armyPlate.GetComponent<Image>();
            if (armyPlateImg != null) armyPlateImg.raycastTarget = false;
            var capLbl = ElarionUiKit.Label(armyPlate.transform,
                _vm != null ? _vm.ArmyBandText : "ARMY -", 0.00f, 1.00f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center,
                0.04f, 0.96f, bold: true);
            ElarionUiKit.FitSingleLine(capLbl);
            capLbl.raycastTarget = false;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy army band '" + (_vm != null ? _vm.ArmyBandText : "ARMY -") + "' full=" +
                (_vm != null && _vm.ArmyFull) + " (own band " + army.Y0.ToString("0.###") + ".." +
                army.Y1.ToString("0.###") + " - WO-1464: it used to print over the party row).");

            // Troop list region (left half of the body zone, below the army band). The body
            // zone's bottom already sits ABOVE the footer + Close band (factory reservation,
            // WO-714 P6) — the old hand-computed 0.19 Close-dodge is retired.
            var listGo = new GameObject("TroopListArea", typeof(RectTransform));
            listGo.transform.SetParent(body, false);
            _troopListArea = listGo.GetComponent<RectTransform>();
            _troopListArea.anchorMin = new Vector2(0.00f, list.Y0);
            _troopListArea.anchorMax = new Vector2(LeftColX1, list.Y1);
            _troopListArea.offsetMin = Vector2.zero;
            _troopListArea.offsetMax = Vector2.zero;

            BuildTroopList();
        }

        /// <summary>Look one band out of the table by name. A missing name is a programming
        /// error, not a player-visible one, so it traces and answers a zero band rather than
        /// throwing a whole screen away.</summary>
        private static DeployBand BandNamed(DeployBand[] bands, string name)
        {
            if (bands != null)
                for (int i = 0; i < bands.Length; i++)
                    if (bands[i].Name == name) return bands[i];
            DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                "deploy band '" + name + "' is not in BandsFor() - the layout table and a builder " +
                "have drifted apart; that band will render at zero height.");
            return new DeployBand(name, ColumnLeft, 0f, 0f, 0);
        }

        // Hero + companions as small framed class portraits across the top of the
        // left column. The hero's class = GameState.HeroClass; companions = the class
        // strings in PartyMemberIds. (Hero is added explicitly so the row always shows
        // the player even before any companion has joined.)
        // ⚠ THE OLD NOTE HERE WAS RETIRED BY THE RULING IT WAS WAITING ON (2026-08-21).
        // It read: "WO-774.0 FORWARD NOTE (spectator-model ruling PENDING …): when the
        // owner/Grok ruling lands, the hero LEAVES RAID SCENES ENTIRELY and this
        // hero+companion party row is expected to be REPLACED by a troop-loadout row."
        // The ruling landed the OTHER WAY. WO-1109 (2026-08-16, commit 256fa9ee3) shipped
        // Option A — CARRY: SceneRouter.GoRaid now detaches the REAL hero, DontDestroyOnLoad's
        // it across the load and re-homes it at the raid's baked HeroStartPoint_PlayerSpawn.
        // (Before that, every raid silently ran the EMERGENCY pill-hero, which had no
        // HeroAbilities at all — Q/W/E/R were dead in every raid ever played.) So the hero
        // is IN the raid, with its real class body and abilities, and this party row is
        // CORRECT rather than provisional. A seat acting on the retired note would have
        // deleted a row that now reflects who actually fights.
        // Still true, and still the reason this stays thin: do NOT deepen hero-in-raid
        // coupling on this SCREEN — the carry is SceneRouter's, not the deploy UI's.
        private void BuildPartyRow(Transform body, float rowY0, float rowY1)
        {
            var classes = _vm != null ? _vm.PartyClasses : new List<string>();

            // Row host — left half of the body zone, below "YOUR FORCES".
            var rowHost = new GameObject("PartyRow", typeof(RectTransform));
            rowHost.transform.SetParent(body, false);
            var rr = rowHost.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0.00f, rowY0);
            rr.anchorMax = new Vector2(LeftColX1, rowY1);
            rr.offsetMin = Vector2.zero;
            rr.offsetMax = Vector2.zero;

            int n = classes.Count;
            if (n == 0) return;
            float slot = Mathf.Min(0.30f, 0.92f / Mathf.Max(n, 1));
            float rowWidth = slot * n;
            float rowStart = (1f - rowWidth) * 0.5f;
            // WO-1385 #2 (owner screenshot: "Thrain" / "Grom" painted OVER the olive plates).
            // The old niche was the whole slot width (225 px) by 0.18-0.98 of a 62 px row, and
            // the name sat in the 11 px left under it -- a FontMicro glyph cannot seat in 11 px,
            // so it overflowed upward onto the plate.
            // WO-1519 §2.3 (owner 20:14: the portraits read "TINY"): the row deepens to 95 ref
            // px and the plate grows with it -- 0.040 half-width (60 px) by 0.470-1.000 (50 px)
            // -- while the name keeps a REAL 43 px row of its own underneath. Both fractions come
            // off PartyRowBands(), which the layout oracle measures, so "bigger" is a number the
            // suite can hold this file to rather than an adjective in a commit message.
            // Plate is decorative (no Button, raycast off), so the touch floor does not apply.
            var rowBands = PartyRowBands();
            var plateBand = BandNamed(rowBands, "party-plate");
            var nameBand  = BandNamed(rowBands, "party-name");
            for (int i = 0; i < n; i++)
            {
                string cls = classes[i];
                float x0 = rowStart + i * slot + 0.015f;
                float x1 = rowStart + (i + 1) * slot - 0.015f;
                float cx = (x0 + x1) * 0.5f;

                // Framed portrait niche -- the PLATE, above the label.
                var niche = ElarionUiKit.Niche(rowHost.transform,
                    new Vector2(cx - PartyPlateHalfW, plateBand.Y0),
                    new Vector2(cx + PartyPlateHalfW, plateBand.Y1));
                niche.GetComponent<Image>().raycastTarget = false;
                var portrait = ElarionUiKit.Portrait(niche.transform, ElarionUiKit.PortraitForClass(cls), active: i == 0);

                // Name BELOW the portrait plate (the canon companion name for the class).
                var nameLbl = ElarionUiKit.Label(rowHost.transform, _vm != null ? _vm.CompanionName(cls) : cls,
                    nameBand.Y0, nameBand.Y1, ElarionUi.Parchment,
                    ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, x0, x1, bold: true);
                ElarionUiKit.FitSingleLine(nameLbl);
                nameLbl.raycastTarget = false;
            }
        }

        // The scrollable troop list, grouped by TroopDefId (one row per troop type the
        // player owns + can deploy), each row = icon glyph + name + owned count.
        private void BuildTroopList()
        {
            ClearTroopList();

            var troops = _vm != null ? _vm.Troops : null;
            if (troops == null || troops.Count == 0)
            {
                // Empty state sits directly on the list area (a stretched label inside the
                // scroll column reports height 0 under the kit's childControlHeight:false law).
                ElarionUiKit.Label(_troopListArea, "No troops trained yet. Visit the Barracks.", 0.3f, 0.7f,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center);
                return;
            }

            // WO-714 W4: the ONE kit scroll zone (§1.14) replaces the hand-rolled
            // viewport/content/fitter plumbing — screens add no scroll plumbing of their own.
            _scroll = ElarionUiKit.MakeScrollZone(_troopListArea, spacing: RowGapPx, padding: 3);
            foreach (var item in troops)
                CreateTroopRow(_scroll.content, item);

            FinalizeScroll();
        }

        private void CreateTroopRow(Transform parent, DeNelle.Core.UI.Mvvm.ItemVM item)
        {
            string troopDefId = item.Id;
            int owned = item.Price;   // owned count carried on Price by the VM
            // WO-714 P10: a raw troopDefId is never player-visible.
            string name = !string.IsNullOrEmpty(item.Name)
                ? item.Name : ElarionUiKit.SpacedDisplayName(troopDefId);

            var row = new GameObject("TroopRow_" + troopDefId, typeof(Image));
            row.transform.SetParent(parent, false);
            // Kit scroll-column row law (MakeScrollZone runs childControlHeight:false): rows
            // carry their own height via sizeDelta, not a LayoutElement.
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, RowHeightPx);
            var rowImg = row.GetComponent<Image>();
            rowImg.color = ElarionUiKit.Cell;
            ElarionUiKit.ApplyRounded(rowImg);

            // Canonical troop portrait; keep the compact role glyph only as the honest
            // missing-asset fallback (WO-1294).
            var well = ElarionUiKit.AddImage(row.transform, "IconWell",
                new Vector2(0.03f, 0.15f), new Vector2(0.20f, 0.85f), new Color(0f, 0f, 0f, 0.30f));
            var wellImage = well.GetComponent<Image>();
            wellImage.raycastTarget = false;
            var portrait = RpgUiCatalog.Get(RpgUiCatalog.RoleTroop, item.IconName);
            if (portrait != null)
            {
                wellImage.sprite = portrait;
                wellImage.preserveAspect = true;
                wellImage.color = Color.white;
            }
            else
            {
                string glyph = _vm != null ? _vm.RoleGlyph(troopDefId) : "MEL";
                var ic = ElarionUiKit.Label(well.transform, glyph, 0f, 1f, ElarionUi.Gilt,
                    ElarionUi.FontHead, TMPro.TextAlignmentOptions.Center, 0f, 1f, bold: true);
                ic.raycastTarget = false;
            }

            var nameLbl = ElarionUiKit.Label(row.transform, name, 0.45f, 0.95f, ElarionUi.Parchment,
                ElarionUi.FontBody, TMPro.TextAlignmentOptions.Left, 0.23f, 0.98f, bold: true);
            nameLbl.raycastTarget = false;
            // §1.14 fit-never-truncate: a long troop name shrinks, never clips, at phone aspect.
            ElarionUiKit.FitSingleLine(nameLbl);

            var ownedLbl = ElarionUiKit.Label(row.transform, "x" + owned, 0.05f, 0.5f, ElarionUi.Affordable,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, 0.23f, 0.98f, bold: true);
            ownedLbl.raycastTarget = false;
        }

        // =====================================================================
        //  RIGHT — the ENEMY BASE hero card, the SPOILS chips, the SCOUT REPORT.
        //  WO-1519 (owner 2026-09-06 20:14, on the deploy frame: "can we make this
        //  screen pop?"). Fractions come off BandsFor(); the budget and the law it
        //  clears are documented once, at the table, not again here.
        // ---------------------------------------------------------------------
        //  WHAT CHANGED AND WHY, in the owner's own terms:
        //   * "Nothing on the screen is sized by its importance." The decision she is
        //     about to make is WHICH CAMP, WITH WHAT. So ENEMY BASE stops being two
        //     thin stat lines and becomes a HERO CARD: the boss's emblem at plate size,
        //     his name, and Power / Recon as two BIG numerals with captions under them.
        //     203 ref px instead of 72 — it is the loudest thing in the column, which
        //     is what it should have been.
        //   * SPOILS leave the prose. Four lines of scout text ending in "Spoils: ~1800
        //     wood, ~1100 iron, ~2200 gold" is a sentence the eye has to parse; three
        //     icon+number chips are a glance. Same numbers, same producer (the VM's
        //     SpoilsChips and its SpoilsLine are one estimate), rendered through the
        //     kit's existing ElarionUiKit.CostRow so this screen adds no chip authority
        //     of its own.
        //   * The SCOUT REPORT well therefore paints vm.ScoutIntel (three lines), not
        //     vm.ScoutReport (four) — a projection of one list, never a second report.
        //     vm.ScoutReport is untouched because RaidDeployZeroArmyRegression
        //     [zero-army-spoils] pins its shape, and a pin is not weakened to make a
        //     layout change easier.
        //   * ⛔ THE ECHO GUIDE BAND IS GONE (owner ruling 20:24, WO-1519 §2B). She
        //     asked "what is the Echo Guide even bringing to the table?", was told that
        //     by her OWN scope fence (WO-1380: no stat, no yield, no combat effect) it
        //     brings one memory line, and ruled: "Remove it from the deploy screen."
        //     Its 148 px is where the hero card's extra height came from. NOTHING about
        //     the Echo Guide FEATURE is deleted — see OnDeploy, which still calls
        //     EchoGuideService.NoteExpeditionTarget so EchoWorldPresence has a place to
        //     remember when it brings the Echo back after the battle.
        // =====================================================================
        private void BuildCenterColumn(Transform body, bool hasSubHeader)
        {
            var bands = BandsFor(hasSubHeader);
            var enemy  = BandNamed(bands, "enemy");
            var spoils = BandNamed(bands, "spoils");
            var scout  = BandNamed(bands, "scout");

            BuildEnemyHeroCard(body, enemy);
            BuildSpoilsChips(body, spoils);

            // WO-1519 §2B — THE PROOF OF A REMOVAL, in the capture. A band that is simply
            // not built leaves no evidence that it was deliberately not built, and the next
            // seat reading a frame with no Echo Guide on it cannot tell a ruling from a bug.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy composes NO Echo Guide block (owner ruling 2026-09-06 20:24, WO-1519 " +
                "section 2B). EchoGuideService, its 24 memory lines and EchoWorldPresence are " +
                "untouched; NoteExpeditionTarget still fires on BEGIN ASSAULT.");

            // WO-839 #3: SCOUT REPORT intel band fills the previously bare lower band.
            // Strict MVVM: the View renders the VM's lines verbatim — honest config facts
            // only (walls / gates / garrison / boss; never the cosmetic reward fields the
            // loot math ignores). (The property it reads is now vm.ScoutIntel, not
            // vm.ScoutReport; see the note on the read itself for why.)
            // WO-1519: the well is 157 ref px and paints THREE lines (vm.ScoutIntel) — the
            // spoils moved out to the chip row above, so the header takes a real 41 px row
            // (0.740-1.000) and the block below it holds 3 x 38.6 px at the 30 px FontFloor
            // with no reliance on the runtime relax guard.
            var intel = ElarionUiKit.Well(body, new Vector2(RightColX0, scout.Y0), new Vector2(1.00f, scout.Y1));
            intel.GetComponent<Image>().raycastTarget = false;
            var intelHdr = ElarionUiKit.Label(intel.transform, "SCOUT REPORT", 0.740f, 1.00f,
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f, bold: true);
            ElarionUiKit.FitSingleLine(intelHdr);
            intelHdr.raycastTarget = false;
            // vm.ScoutIntel, NOT vm.ScoutReport — the same list minus its spoils tail, which
            // the chips now carry. Saying a number twice on one screen is the duplicated-state
            // smell, and this file's own WO-1385 banner already rejected a third "Troops N"
            // for exactly that reason.
            var report = _vm != null ? _vm.ScoutIntel : null;
            string intelText = report != null && report.Count > 0
                ? string.Join("\n", report) : "No scout intel available.";
            // 2026-09-05 - THE BLOCK IS 0.05-0.96 OF THE WELL, NOT 0.08-0.92, and the 13% is
            // load-bearing. On the fresh capture the 4th line read "Spoils if you win: ~1800
            // wood, ~1100 iron," at 1920x1080 and "...~1100 iron, ~22" at 2670x1200 - the gold
            // amount CLIPPED, on the one screen that answers "is this raid worth it". The well
            // is budgeted for four lines (no wrapping into a fifth), so the two levers are the
            // prefix (RaidDeployVM.SpoilsPrefix lost "if you win", eleven characters) and this
            // width. Together they turn a 0-character overrun into ~4 characters of slack on
            // the longest live line, "Spoils: ~4000 wood, ~2400 iron, ~6500 gold". The kit's
            // Well draws an inner rim, so 0.05/0.96 is as wide as the block can sit without
            // touching it.
            var intelLbl = ElarionUiKit.Label(intel.transform, intelText, 0.00f, 0.730f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.TopLeft, 0.05f, 0.96f);
            ElarionUiKit.FitBlock(intelLbl);
            intelLbl.raycastTarget = false;
        }

        // =====================================================================
        //  THE ENEMY BASE HERO CARD — WO-1519 §2.2.
        // =====================================================================
        //  ⚠ THE WO ASKED FOR "the boss portrait from Portraits/". THAT FOLDER HAS NO
        //  BOSS PORTRAIT — measured, not assumed: Assets/Resources/Portraits/ holds
        //  buildings, walls and one companion (Sylas.png); a search for necromancer /
        //  orc / warlord / berserker across it returns nothing. Building the card on
        //  that path would have shipped an empty plate on every camp.
        //  WHAT DOES EXIST AND DOES RENDER: the RpgUi EMBLEM pack —
        //  Assets/Resources/RpgUi/emblem/Necromancer.png, opened and looked at
        //  2026-09-06 (a full-colour dripping skull, not a blank or a magenta). All four
        //  authored camps' bosses ("orc-necromancer" x2, "necromancer" x2) resolve to it
        //  through vm.BossEmblemName. WO-1509's missing-albedo finding is about the
        //  Necromancer FBX — a 3D model on a different pipeline — and does not touch
        //  this sprite; that was the open question §3 told this lane to settle, and it
        //  is settled by opening the file.
        //  The camp/scene ART the WO also sketches does NOT exist either (no per-camp
        //  image anywhere under Resources, and RaidSelectionScreen loads only frame
        //  sprites for its cards), so the card is boss-led rather than scene-led. A
        //  camp-art pipeline is a separate lane, and this card has a place to hang it.
        //  Fallback chain, honest at every step: the boss emblem, else the shared combat
        //  crest, else a quiet gilt plate. Never a blank, never a lie about a boss.
        // =====================================================================
        private void BuildEnemyHeroCard(Transform body, DeployBand band)
        {
            var card = ElarionUiKit.Well(body, new Vector2(RightColX0, band.Y0), new Vector2(1.00f, band.Y1));
            card.GetComponent<Image>().raycastTarget = false;

            // The boss's face, at plate size down the card's left. Decorative: raycast off.
            var emblem = ElarionUiKit.AddImage(card.transform, "BossEmblem",
                new Vector2(CardEmblemX0, CardCapY0), new Vector2(CardEmblemX1, CardNameY1),
                new Color(1f, 1f, 1f, 0.95f));
            var emblemImg = emblem.GetComponent<Image>();
            string emblemName = _vm != null ? _vm.BossEmblemName : "";
            Sprite art = !string.IsNullOrEmpty(emblemName)
                ? RpgUiCatalog.Get(EmblemRole, emblemName) : null;
            if (art == null) art = UiStyle.Icon("combat", "crest", "shield", "emblem");
            if (art != null)
            {
                emblemImg.sprite = art;
                emblemImg.preserveAspect = true;
            }
            else
            {
                // No art in this build: a quiet gilt plate keeps the framed read, and the
                // capture records WHICH key found nothing rather than leaving a mute square.
                emblemImg.color = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.18f);
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "deploy hero card: no emblem art for boss '" + (emblemName ?? "(none)") +
                    "' and no combat crest either - falling back to a gilt plate.");
            }
            emblemImg.raycastTarget = false;

            var hdr = ElarionUiKit.Label(card.transform, "ENEMY BASE", CardHdrY0, CardHdrY1,
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left,
                CardTextX0, CardTextX1, bold: true);
            ElarionUiKit.FitSingleLine(hdr);
            hdr.raycastTarget = false;

            // The boss NAME, or the honest camp sentence when a camp authors no boss. The
            // card never claims a boss it does not have.
            string boss = _vm != null ? _vm.BossDisplayName : "";
            var nameLbl = ElarionUiKit.Label(card.transform,
                !string.IsNullOrEmpty(boss) ? boss : "Scout the camp",
                CardNameY0, CardNameY1, ElarionUi.Parchment, ElarionUi.FontLabel,
                TMPro.TextAlignmentOptions.Left, CardTextX0, CardTextX1, bold: true);
            ElarionUiKit.FitSingleLine(nameLbl);
            nameLbl.raycastTarget = false;

            // The two BIG numerals. Value on top at FontHead, its caption under it — the
            // NUMBER is what the eye lands on, and the caption tells it what it read. The
            // owner is colourblind, so the hierarchy is carried by SIZE and POSITION, which
            // survive a greyscale copy of the capture intact.
            int power = _vm != null ? _vm.PowerRating : 0;
            string est = _vm != null ? FormatTime(_vm.EstClearTime) : "--:--";
            BuildCardStat(card.transform, "Power", power.ToString(), "POWER", CardTextX0, CardStatMidX);
            BuildCardStat(card.transform, "Recon", "~" + est, "RECON", CardStatMidX, CardTextX1);
        }

        // One big numeral + its caption, both fitted, both inside their own band.
        private static void BuildCardStat(Transform card, string name, string value, string caption,
                                          float x0, float x1)
        {
            var val = ElarionUiKit.Label(card, value, CardStatY0, CardStatY1,
                ElarionUi.Gilt, ElarionUi.FontHead, TMPro.TextAlignmentOptions.Center,
                x0, x1, bold: true);
            val.name = "Stat_" + name;
            ElarionUiKit.FitSingleLine(val);
            val.raycastTarget = false;

            var cap = ElarionUiKit.Label(card, caption, CardCapY0, CardCapY1,
                ElarionUi.ParchmentDim, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center,
                x0, x1, bold: true);
            cap.name = "StatCaption_" + name;
            ElarionUiKit.FitSingleLine(cap);
            cap.raycastTarget = false;
        }

        // =====================================================================
        //  THE SPOILS CHIPS — WO-1519 §2.4.
        // =====================================================================
        //  Three icon+number chips through ElarionUiKit.CostRow, the kit's EXISTING
        //  icon+amount row (CostFormat.cs) — this screen adds no chip authority of its
        //  own, and CostRow already carries the WO-1060 childControlWidth law that keeps
        //  a three-part row from spilling onto its neighbour.
        //  The numbers are the VM's SpoilsChips: WO-1402's estimate through WO-1402's
        //  rounding, the same pair vm.SpoilsLine produces its sentence from. Not parsed
        //  out of that sentence — a string is an output of the producer, not a source.
        //  ⚠ NOT CAP-AWARE OR REPEAT-AWARE YET (WO-1461, READY not landed). The chips
        //  quote what the selection row quotes; the "~" says estimate, and the WO's
        //  "the number shown is the number that will bank" acceptance is OWED, not met.
        // =====================================================================
        private void BuildSpoilsChips(Transform body, DeployBand band)
        {
            var plate = ElarionUiKit.Well(body, new Vector2(RightColX0, band.Y0), new Vector2(1.00f, band.Y1));
            var plateImg = plate.GetComponent<Image>();
            if (plateImg != null) plateImg.raycastTarget = false;

            var chips = _vm != null ? _vm.SpoilsChips : null;
            if (chips == null || chips.Count == 0)
            {
                // Never an empty plate: a camp whose estimate pays nothing says so in words.
                var none = ElarionUiKit.Label(plate.transform, "Spoils unknown", 0.00f, 1.00f,
                    ElarionUi.ParchmentDim, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f);
                ElarionUiKit.FitSingleLine(none);
                none.raycastTarget = false;
                return;
            }

            var parts = new List<(string conceptId, string word, int amount)>(chips.Count);
            foreach (var c in chips) parts.Add((c.ConceptId, c.Word, c.Amount));
            ElarionUiKit.CostRow(plate.transform, DeNelle.Core.UI.CostFormat.Parts(parts),
                new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f),
                ElarionUi.Parchment, prefix: "SPOILS", fontPx: 24f);
        }

        // =====================================================================
        //  WO-1519 §2B — THE ECHO GUIDE BAND USED TO LIVE HERE. IT IS GONE.
        // =====================================================================
        //  Owner, of the block on her 2026-09-06 20:14 frame:
        //      "what is the Echo Guide even bringing to the table?"
        //  Told that by her OWN 2026-09-04 scope fence (WO-1380: no stat, no yield, no
        //  combat effect — narrative only) it brings one memory line, she ruled at 20:24:
        //      "Remove it from the deploy screen."
        //
        //  DELETED FROM THIS FILE: BuildGuideBand, RefreshGuideBand, OnCycleGuide, the
        //  _guideNameLabel / _guideMemoryLabel fields, GuideTextX0/X1 and the
        //  GuideBandY0/Y1 constants. Their ~148 ref px is what the ENEMY BASE hero card
        //  grew into.
        //
        //  ⛔ THIS IS ONE SURFACE REMOVED, NOT A FEATURE CUT — do not "finish the job":
        //   * EchoGuideService and its 24 authored memory lines STAY, and
        //     EchoGuideMemoryRegression's scope fence [no-effect] stays UNTOUCHED. It was
        //     not weakened to make this removal easier, and it must not be.
        //   * EchoWorldPresence is still the single appearance owner (WO-1108 Lane B) and
        //     still SPEAKS the memory line aloud when it brings the Echo back after the
        //     battle — which was always the payoff; the band was a preview of it.
        //   * OnDeploy still calls EchoGuideService.NoteExpeditionTarget, so the Echo
        //     still knows where it was taken. Removing THAT would silence the world beat.
        //
        //  ⚠ WHAT THIS LEAVES OPEN, stated rather than hidden: guide SELECTION now has no
        //  UI home. WO-1519 §2B says it "can live on the Echoes screen instead"; that
        //  screen is not built in this lane, so between this change and that one the
        //  player keeps whichever Guide the service defaults to (Corvin). WO-1380's
        //  acceptance "Guide selection EXISTS" is therefore OWED, and
        //  EchoGuideMemoryRegression's [tappable] case records it as owed rather than
        //  pretending either that the picker is here or that it never mattered.

        // =====================================================================
        //  WO-1403 (owner ruling 2026-09-05, merged UI review section 2 #2 - "Zero-army-
        //  assault? Default NO"): the footer is BOUND to vm.Fielded.
        // ---------------------------------------------------------------------
        //  RETIRED HERE: the WO-839 #6 `GateDeployAtZeroTroops` flag ("OWNER CONFIRM
        //  pending" since 2026-08-09 - default false = scouting with 0 troops) and the
        //  `ReadinessSlots()` HELPER that read the snapshot at paint time. The SNAPSHOT
        //  ITSELF IS NOT RETIRED and did not move out of this file's flow: WO-823 Phase E's
        //  ArmyReadiness.Compute(GameState) is now taken ONCE in OpenInternal and handed to
        //  the VM, which is what vm.Fielded / vm.ShowAssault below are derived from. What
        //  went away is a View-side predicate, not the readiness input. The ruling the flag
        //  waited on has landed
        //  the OTHER way: a new player's first deploy screen must send them to TRAIN,
        //  not let them lose ("creating reason to raid is big"). The 07:02 capture had
        //  a full-size live BEGIN ASSAULT under "No troops trained yet. Visit the
        //  Barracks." - the loudest button said attack, the sentence said go elsewhere,
        //  and neither was a door.
        //
        //  Phase E's concern (a headcount disagreeing with slot-weighted readiness) cannot
        //  reopen here: vm.Fielded IS the snapshot's DeployableSlots, so there is no second
        //  number to disagree with. The ONLY question this footer asks of it is "zero or
        //  not" - never Snapshot.Ready, which would be a second raid gate. Above zero the
        //  screen still decides nothing about readiness - RaidEntryGate / the selection
        //  grid remain the one authority on "may this player raid".
        //
        //  Words carry the state (vm.PrimaryCtaLabel), one mechanism per door:
        //    Fielded == 0 -> ONE wide primary TRAIN TROOPS -> Manage > Troops. BEGIN
        //                    ASSAULT is NOT DRAWN (not greyed, not "SCOUT ONLY": the
        //                    Heartfire charge is spent at raid entry, and a second
        //                    button on a ~113 px footer only splits the one tap the
        //                    player should make).
        //    Fielded  > 0 -> EDIT ARMY (Quiet, the same Manage > Troops door) + BEGIN
        //                    ASSAULT (BuildObsidianButton Yellow, WO-1385 #4).
        //  Both branches seat every CTA at the canonical height (WO-1075). Pinned by
        //  RaidDeployUiRegression [deploy-bar-kit-button] and by the WO-1403 suite
        //  RaidDeployZeroArmyRegression [zero-army-footer].
        // =====================================================================
        private void BuildDeployBar(Transform footer)
        {
            int fielded = _vm != null ? _vm.Fielded : 0;
            bool showAssault = _vm != null && _vm.ShowAssault;
            // The WO-1403 decision line, emitted ONCE before the branch so a capture records
            // what the footer decided and on what number - the two branch traces below say
            // what was then drawn. The label comes from the VM (PrimaryCtaLabel), never from
            // a literal re-derived here: the trace and the button can never disagree. The
            // required/ready tail is the readiness snapshot the number CAME FROM, so a capture
            // can tell "0 slots" apart from "not Ready yet" without a second run.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy footer fielded=" + fielded + " primary=" +
                (_vm != null ? _vm.PrimaryCtaLabel : RaidDeployVM.PrimaryTrainLabel) +
                " required=" + (_vm != null ? _vm.Readiness.RequiredSlots : 0) +
                " ready=" + (_vm != null && _vm.Readiness.Ready));

            if (showAssault)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "deploy primary='" + RaidDeployVM.PrimaryAssaultLabel + "' fielded=" + fielded +
                    " secondary='EDIT ARMY'");

                // EDIT ARMY (was "Army Ready?" - a question on a button, and a toast). A verb
                // that does what it says: the Troops tab is where the army is trained/upgraded.
                var editBtn = ElarionUiKit.Button(footer, "EDIT ARMY", ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.00f, 0.50f), new Vector2(0.28f, 0.50f), OnEditArmy);
                SeatFooterCtaAtCanonicalHeight(editBtn);

                // DEPLOY -- the primary CTA. WO-1385 #4 (owner 2026-09-04, Seeker screenshot:
                // "yuck"): the WO-839 DeployGlow halo -- a flat gilt image slab behind the
                // button -- is GONE. The primary emphasis comes from the kit's own primary face
                // -- BuildObsidianButton Yellow -- on the SAME row geometry as the Quiet button
                // (y 0.50/0.50 + SeatFooterCtaAtCanonicalHeight). No second image, no second
                // style. WO-932: "BEGIN ASSAULT" -- distinct from in-raid ground DROP of troops.
                var deployBtn = ElarionUiKit.BuildObsidianButton(footer, "BEGIN ASSAULT",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                    new Vector2(0.32f, 0.50f), new Vector2(0.985f, 0.50f), OnDeploy);
                SeatFooterCtaAtCanonicalHeight(deployBtn);
                if (deployBtn != null) deployBtn.interactable = _vm != null && _vm.CanDeploy;
                return;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy primary='" + RaidDeployVM.PrimaryTrainLabel + "' reason=zero-army (BEGIN ASSAULT not drawn; " +
                "the empty-army sentence in the troop list is the explanation)");
            // ONE wide primary - the full row the two buttons would have shared, same face,
            // same seat. The troop list's "No troops trained yet. Visit the Barracks." stays
            // as the explanation; this is the door that sentence never was.
            var trainBtn = ElarionUiKit.BuildObsidianButton(footer, "TRAIN TROOPS",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.00f, 0.50f), new Vector2(0.985f, 0.50f), OnTrainTroops);
            SeatFooterCtaAtCanonicalHeight(trainBtn);
        }

        // WO-1075: footer height changes with aspect, so a vertical fraction can fall below
        // the mobile touch floor. Pin both actions to the canonical pixel height instead.
        private static void SeatFooterCtaAtCanonicalHeight(Button button)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            float half = ElarionUiKit.CanonCtaHeight * 0.5f;
            rt.offsetMin = new Vector2(rt.offsetMin.x, -half);
            rt.offsetMax = new Vector2(rt.offsetMax.x, half);
        }

        // WO-1403: the ONE door from this screen to the Barracks - Manage on the Troops tab
        // (the WO-1389 context open, ManageScreenPanel.Open(string) "Troops"). Close FIRST,
        // then open, exactly as BuildCollectionBrowser's Defense door does (BuildCollection-
        // Browser.cs:173-176, read 2026-09-05): the close-to-nothing ARMS the WO-1400 return
        // door the Journey deck set when it handed off to Raids, and the Manage open that
        // follows KEEPS it - PanelManager.cs:374 emits
        //   "return door '<name>' KEPT - '<name>' opened before it fired"
        // - so closing Manage later lands back on the deck, not on the bare HUD.
        private void OpenTroopsDoor(string from)
        {
            string raidId = _vm != null ? _vm.RaidId : "(none)";
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy " + from + " -> Manage tab 'Troops' (raid='" + raidId + "'; deploy screen closed first, " +
                "the deck's WO-1400 return door is kept by the arbiter)");
            Close();
            if (!PanelRouter.Open(PanelId.Manage, "Troops"))
            {
                // PanelRouter has already FlowTrace.Fail'd the why; the player still needs a word.
                ElarionUiKit.ShowToast("The Barracks could not be opened.", ElarionUiKit.ToastTone.Danger);
            }
        }

        private void OnTrainTroops() => OpenTroopsDoor("TRAIN TROOPS");

        private void OnEditArmy() => OpenTroopsDoor("EDIT ARMY");

        // =====================================================================
        //  WO-1640 ITEM B — A TOAST FIRED FROM THIS PANEL MUST DRAW ABOVE IT.
        // ---------------------------------------------------------------------
        //  The panel's canvas is BuildModalCanvas("RaidDeployScreenUI", 31050) with
        //  overrideSorting = true (see OpenInternal), and since WO-1462 it carries the
        //  kit's opaque 0.94-alpha Backdrop. ElarionUiKit.ShowToast builds its own
        //  ScreenSpaceOverlay canvas at sortingOrder 720 by default
        //  (ElarionUiKitConformance.cs:394, applied :411) — 720 UNDER 31050, behind an
        //  opaque plate. Both canvases are ScreenSpaceOverlay (ElarionUiKit.cs:104 and
        //  ElarionUiKitConformance.cs:409-411), so the comparison is a plain sortingOrder
        //  one and the toast loses it every time.
        //
        //  DEVICE PROOF, not inference (Builds/device-frames/2026-09-10_raid_logcat_stream.txt):
        //    :25546  [Flow:UI] kit toast -> 'Outmatched: 9 defenders against your 8. Tap
        //            BEGIN ASSAULT again to march anyway.' tone=Info
        //    :25547  [Flow:Raid] BEGIN ASSAULT: outmatched camp - asked once, did NOT refuse.
        //  ...and 2026-09-10_0607_arena_01_entry.png, the staging screen after that tap:
        //  the face is lit and NO confirm text appears anywhere. The WO-1542 two-tap step
        //  is an owner ruling and is UNTOUCHED here — what is fixed is that the player
        //  never saw the sentence that explains it.
        //
        //  ⛔ THIS IS NOT A KIT CHANGE. sortingOrder is ALREADY a caller-supplied optional
        //  on ShowToast, so raising it HERE moves this screen's toasts and nothing else in
        //  the game. The WO's shape 2 ("raise the toast overlay's sorting") was called out
        //  as kit-wide and needing its own pins; this is that fix confined to one caller.
        //
        //  WHY 31400 AND NOT MORE: it clears every modal panel in the game
        //  (highest measured 2026-09-10: 31300, PlayerDeckWorkspace's
        //  BuildModalCanvas(WorkspaceName + "Canvas", 31300)) and stays BELOW
        //  BugReportToastCanvas / Village2VictoryBanner at 32000, which must keep the top
        //  band. Pinned by RaidDeployUiRegression [deploy-toast-above-modal], which reads
        //  BOTH numbers out of this file and fails if the relation inverts.
        //
        //  ⚠ NOT PROVEN by this lane: whether the 0607 frame's toast was on screen and
        //  occluded, or had already expired. The sorting arithmetic above is proven; that
        //  particular frame's timing is not, and the WO says not to claim it.
        private const int ToastSortingOrder = 31400;

        /// <summary>The confirm sentence is ~80 characters — exactly the 480x76 default
        /// card's stated two-line capacity (ElarionUiKitConformance.cs:390-397), past which
        /// a third line draws OUTSIDE the plate. It is also a QUESTION, so it gets longer
        /// than the 2.2 s default to be read and acted on.</summary>
        private const float ConfirmToastWidth = 760f, ConfirmToastHeight = 132f;
        private const float ConfirmToastLife = 5.0f;

        private void OnDeploy()
        {
            // WO-1640 §5.1 — ARRIVAL. Every refusal branch below traced; arrival did not, so
            // "tap not received" and "tap received and swallowed" were indistinguishable
            // except by inference. This line is the difference (CLAUDE.md §12).
            string tapRaid = _vm != null ? _vm.RaidId : "(no vm)";
            string tapScene = _vm != null ? _vm.SceneName : "(no vm)";
            bool tapNeedsConfirm = _vm != null && _vm.NeedsOutmatchConfirm;
            int tapFielded = _vm != null ? _vm.Fielded : -1;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "BEGIN ASSAULT tap received: raid='" + tapRaid + "' scene='" + tapScene +
                "' fielded=" + tapFielded + " needsOutmatchConfirm=" + tapNeedsConfirm +
                // ⛔ THIS CHAIN MIRRORS THE BRANCHES BELOW IN ORDER, ALL OF THEM. A trace that
                // names a branch the code did not take is worse than one that names none
                // (CLAUDE.md §12) — the scene-not-in-build refusal is a real branch and it is
                // here for that reason. IsSceneInBuild is a pure Build-Settings read.
                " -> branch=" + (_vm == null ? "no-vm"
                    : string.IsNullOrEmpty(tapScene) ? "no-scene"
                    : !DeNelle.Core.SceneRouter.IsSceneInBuild(tapScene) ? "scene-not-in-build"
                    : tapFielded <= 0 ? "zero-army"
                    : tapNeedsConfirm ? "outmatch-confirm" : "march"));

            if (_vm == null)
            {
                ElarionUiKit.ShowToast("Raid briefing is not ready.", ElarionUiKit.ToastTone.Danger,
                    sortingOrder: ToastSortingOrder);
                return;
            }
            if (string.IsNullOrEmpty(_vm.SceneName))
            {
                ElarionUiKit.ShowToast("This raid has no battleground yet.", ElarionUiKit.ToastTone.Danger,
                    sortingOrder: ToastSortingOrder);
                Debug.LogWarning("[RaidDeployScreen] DEPLOY: empty sceneName.");
                return;
            }
            if (!DeNelle.Core.SceneRouter.IsSceneInBuild(_vm.SceneName))
            {
                // WO-932 Phase 2: honest under-construction — never silent strand.
                ElarionUiKit.ShowToast(
                    "Raid under construction — battleground not in this build.",
                    ElarionUiKit.ToastTone.Danger, sortingOrder: ToastSortingOrder);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                    $"BEGIN ASSAULT refused: scene '{_vm.SceneName}' not in Build Settings.");
                return;
            }
            // WO-1403: BEGIN ASSAULT is not drawn at zero, so this is the belt behind the
            // braces (a stale handle, a re-entrant tap). The VM's Deploy() refuses too; this
            // copy exists only to give the player a word instead of a dead tap.
            if (_vm.Fielded <= 0)
            {
                ElarionUiKit.ShowToast("No troops trained yet. Visit the Barracks.", ElarionUiKit.ToastTone.Danger,
                    sortingOrder: ToastSortingOrder);
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "BEGIN ASSAULT tapped with fielded=0 - refused (WO-1403 ruling); the button should not " +
                    "have been drawn.");
                return;
            }
            // WO-1542 - THE OUTMATCH CONFIRM (owner ruling 2026-09-06, "add the confirm toast").
            // It ASKS ONCE and never refuses: the VM latches the acknowledgement, so this branch
            // cannot fire twice and the next tap marches whatever the numbers say. The VM owns
            // both the predicate and the words (RaidDeployVM.NeedsOutmatchConfirm / OutmatchToast,
            // which read RaidSelectionVM's one producer); this View only shows the string.
            // STOP - this is NOT a readiness gate. CanDeploy, ShowAssault and Deploy() are
            // untouched, and HeartfireRegression PIN F must stay green (WO-1379 / WO-1403).
            if (_vm.NeedsOutmatchConfirm)
            {
                _vm.AcknowledgeOutmatch();
                // WO-1640 ITEM B: above the panel (ToastSortingOrder), on a card big enough
                // for the whole sentence, and held long enough to read and act on. The words
                // are still the VM's — this View composes none of them, and RaidSelectionVM's
                // one producer is untouched, so the sentence still names BEGIN ASSAULT and the
                // face it names still says BEGIN ASSAULT.
                ElarionUiKit.ShowToast(_vm.OutmatchToast, ElarionUiKit.ToastTone.Info,
                    lifeSeconds: ConfirmToastLife, sortingOrder: ToastSortingOrder,
                    cardWidth: ConfirmToastWidth, cardHeight: ConfirmToastHeight);
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "BEGIN ASSAULT: outmatched camp - asked once, did NOT refuse. The next tap marches. " +
                    "The confirm is drawn at sortingOrder " + ToastSortingOrder + ", above this panel's own " +
                    "modal band (WO-1640) - before that it took the kit default and was painted under the " +
                    "0.94-alpha backdrop.");
                return;
            }

            // WO-1380: remember WHERE the Guide is being taken, so the Echo has something to
            // remember when EchoWorldPresence brings it back after the battle. Records an id
            // only — it grants nothing and gates nothing, and a target with no authored lines
            // just leaves the Echo silent (warned by the catalog, never a wrong line).
            DeNelle.Village.World.Camps.EchoGuideService.NoteExpeditionTarget(_vm.RaidId, "BEGIN ASSAULT");

            string name = !string.IsNullOrEmpty(_vm.DisplayNameRaw) ? _vm.DisplayNameRaw : _vm.RaidId;
            ElarionUiKit.ShowToast("Assaulting " + name + "…", ElarionUiKit.ToastTone.Info,
                sortingOrder: ToastSortingOrder);
            Debug.Log($"[RaidDeployScreen] BEGIN ASSAULT -> SceneRouter.GoRaid('{_vm.SceneName}').");
            // SHARED CONTRACT: the VM loads the raid scene; the in-raid deploy tray handles
            // the actual unit placement.
            _vm.Deploy();
        }

        // ── Data helpers ───────────────────────────────────────────────────────

        // (Army roster / party / deployable-count / power-rating all moved to RaidDeployVM —
        //  the View no longer reads GameState / TroopCatalog.)

        // ⛔ DifficultyColor IS DELETED (WO-1519 §2.5). It mapped Regular -> green,
        // Hard -> amber, Extreme -> red, and the owner is red/green colourblind: to her
        // those were one grey plate behind three different words, so the plate carried
        // NO information and the word carried all of it. Do not restore a hue-only
        // difficulty channel; DifficultyLabel below is the whole signal, beside the
        // diamonds. (Kept as a comment rather than a dead method so a future seat sees
        // the ruling instead of an unused helper begging to be re-wired.)
        private static string DifficultyLabel(string difficulty)
        {
            if (string.IsNullOrEmpty(difficulty)) return "Regular";
            string d = difficulty.Trim();
            return char.ToUpper(d[0]) + (d.Length > 1 ? d.Substring(1).ToLowerInvariant() : "");
        }

        private static string FormatTime(float seconds)
        {
            if (seconds <= 0f) return "--:--";
            int total = Mathf.RoundToInt(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        // ── Scroll list — the kit scroll zone owns all plumbing (WO-714 W4) ────

        private void FinalizeScroll()
        {
            if (_scroll == null || _scroll.content == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
        }

        private void ClearTroopList()
        {
            _scroll = null;
            if (_troopListArea == null) return;
            for (int i = _troopListArea.childCount - 1; i >= 0; i--)
            {
                var c = _troopListArea.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }
        }

        public void Close()
        {
            // UIF-01: release the arbiter slot (no-op if already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            _vm?.Dispose();
            _vm = null;
            // WO-714 P8: eased fade/scale-out through the ONE kit FX (falls back to an
            // immediate Destroy when the FX is absent / not playing).
            if (_ui != null) ElarionUiKit.ClosePanelWithFx(_ui);
            _ui = null;
            _troopListArea = null;
            _scroll = null;
        }

        private void OnDestroy()
        {
            // UIF-01: don't leak the arbiter slot if destroyed while open (scene unload).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            _vm?.Dispose();
            _vm = null;
            if (_instance == this) _instance = null;
            if (_ui != null) Destroy(_ui);
        }
    }
}

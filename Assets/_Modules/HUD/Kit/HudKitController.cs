// =============================================================================
// HudKitController — assembles the HUD kit: factory widgets in the actionable
// areas, model-bound, posture-occupied from hud-areas.json (P23 HUDKIT).
// (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 §3.3/§3.4 as A4 area/posture rows.)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD.Kit
//
// MVVM LAW (§5): every widget is built by the ElarionUiKit factory and bound to
// a Core.HudModel model's Changed event — zero raw widget construction, zero
// state pulls, zero .Instance/reflection reads in this file. Commands fire the
// owner VillageHudController's UnityEvents (Village bridges subscribe those) or
// the Core HudCommands sink (Village registers handlers).
//
// THE §0 FELT FIXES delivered here (mechanism in-line at each site):
//   • HP 9/145 renders ~6%  — vitals = BuildNameplate(Player): the §1.1 fill
//     contract (non-null sprite + fillAmount-only) replaces the sprite-less
//     Filled images of BattleHud9Zone.FillBarLeft (:1701) / VillageHudController.
//   • MP live               — bound to HeroVitalsModel (producer follows
//     HeroHealth.Instance); kills the frozen fillAmount=1f (:1878) + SetMana
//     no-op (:2906).
//   • Dead target clears    — BuildTargetFrame.Bind: TargetModel !HasTarget =>
//     total Clear() (fixes the :549 early-return).
//   • Wave chrome law       — waveBlock exists only in the calm(town) row and
//     self-gates to between-waves phases; countdown shows only when real.
//   • No resource flashing  — CurrencyChip.SetAmount count-tweens; no colour
//     flash exists in the component (owner rule enforced by the factory).
//   • 4 round move buttons  — BuildControllerCluster -> HudMoveInput (replaces
//     the square D-pad + VirtualDPadLean).
//   • Talk button appears   — availability from PostureSignals.TalkAvailable
//     (Core static; the stale one-shot reflection push is retired), consumed via
//     HudActionBarModel (WO-835: the face packs in/out, no dim, no hole).
//   • raid-"x"/harvest-"Y"  — not rebuilt (earns-its-place: no verified
//     backing feature surface); Pi sign-in stays off the HUD (Title-gated).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;   // WO-1225: N0 grouping on the measured reward delta
using UnityEngine;
using UnityEngine.SceneManagement;   // heartStatus scene gate (see ApplyHeartSceneGate)
using UnityEngine.UI;
using TMPro;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.HUD;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;

namespace DeNelle.HUD.Kit
{
    /// <summary>Builds, binds and posture-occupies every kit widget (see header).</summary>
    public sealed class HudKitController : MonoBehaviour
    {
        private HudAreasHost _host;
        private PostureEvaluator _evaluator;
        private HudAreasConfig _config;
        private VillageHudController _owner;   // command events (Village bridges subscribe them)
        private IHudModel _models;

        // widget id -> root (registry the occupancy rows drive).
        private readonly Dictionary<string, GameObject> _widgets =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        // live handles
        private ElarionUiKit.PartyNameplateHandle _vitals;   // WO-432 shared HP/MP plate (+07-06 XP strip)
        private bool _xpStripBound;                          // one-shot FlowTrace on first XP bind

        // WO-997 §3b: mana-bar legibility. OnVitals now records a TARGET fill (from the
        // model's exact floats when present) and Update() eases the shown fill toward it,
        // so regen reads as motion instead of whole-point steps. A DOWNWARD jump (a spend)
        // arms a brief BRIGHTEN flash on the fill image — brightness, never a hue swap
        // (owner is red/green colourblind). -1 target = nothing bound yet (first push snaps).
        private float _manaFillTarget = -1f;                 // 0..1 fill the bar should reach
        private float _manaFillShown  = -1f;                 // 0..1 fill currently painted
        private float _manaFlashUntil;                       // unscaled time the spend flash ends
        private Color _manaFillBaseColor = Color.white;      // the BUILT fill colour, restored after a flash
        private bool  _manaFillBaseCaptured;
        private const float ManaFillLerpSpeed  = 9f;         // exponential ease rate (~0.11s to 63%)
        private const float ManaSpendThreshold = 0.02f;      // fill drop that counts as a spend
        private const float ManaFlashSeconds   = 0.25f;      // spend flash duration

        // WO-1104 — KILL REWARD VISIBILITY (owner felt-test 2026-08-16, verbatim: "I couldn't
        // tell when I killed in the field. I couldn't tell if it awarded anything... whether
        // it's simply just a flashing on the bar"). Every XP gain is MEASURED here from the
        // model's own state delta (never from an amount some producer claims it granted), then
        // presented two ways: the XP strip BRIGHTENS (flash) and a "+N XP" readout pops just
        // under the plate. Repeat gains inside the merge window ADD INTO the live readout, so a
        // five-kill fight visibly climbs to a bigger number than a one-kill fight — that
        // climbing total IS the "more enemies = more experience" read the owner could not see.
        // Number + brightness, never hue (red/green colourblind law).
        private bool  _xpPrevValid;                          // a baseline has been captured
        private int   _xpPrevXp, _xpPrevToNext, _xpPrevLevel;
        private int   _xpGainRunning;                        // merged total currently displayed
        private float _xpGainLastTime;                       // unscaled time of the last merge
        private float _xpGainHoldUntil;                      // unscaled time the readout starts fading
        private float _xpFlashUntil;                         // unscaled time the strip flash ends
        private Color _xpFillBaseColor = Color.white;        // BUILT strip colour, restored after a flash
        private bool  _xpFillBaseCaptured;
        private const float XpGainMergeSeconds = 1.5f;       // repeat gain inside this window merges
        private const float XpGainHoldSeconds  = 1.6f;       // readable hold before the fade
        private const float XpGainFadeSeconds  = 0.45f;      // fade-out duration
        private const float XpFlashSeconds     = 0.35f;      // strip brighten duration

        // ── WO-1225 — the gold acknowledgement that a modal cannot occlude ────────────────
        // WO-1213 proved its toast fired and the owner still saw nothing: EchoUnlockDialogue
        // opened 3 ms later at sortingOrder 31020 behind a full-screen scrim, over a toast at
        // 720. Owner ruling 2026-08-26: "can it show streamers and +1000 showing to gold?
        // counting up animation?" -- so the acknowledgement moves onto the persistent gold chip.
        //
        // ⛔ THE COUNT-UP MUST NOT LIE. Nothing here ever renders the amount the grant path
        // ASKED for. A raise merely ARMS a window; the number shown is the MEASURED delta
        // between two EconomyModel pushes, and the count runs to the MEASURED post-grant
        // balance. Same discipline as NoteXpGain above, and as Enemy.cs's rolled-vs-credited
        // kill grant: a shortfall WARNS and shows the smaller, true number.
        private bool  _goldPrevValid;                        // an economy baseline has been captured
        private long  _goldPrev;                             // last MEASURED balance pushed by the model
        private bool  _goldCelebrateArmed;                   // a raise is waiting for its wallet push
        private float _goldCelebrateUntil;                   // unscaled time the armed window expires
        private long  _goldCelebrateRequested;               // ORACLE ONLY -- never rendered
        private string _goldCelebrateReason = "";
        private Vector2 _goldCelebrateOrigin;                // screen point the headline flies from
        // The LOOK-BACK half: the grant path credits the wallet BEFORE it raises, and the economy
        // push is synchronous, so the measured move is usually already behind us when the raise
        // lands. These record the last measured gain so it can still be acknowledged.
        private long  _goldLastGainFrom, _goldLastGainTo, _goldLastGainDelta;
        private float _goldLastGainTime;
        private bool  _goldLastGainConsumed = true;              // nothing to acknowledge at boot
        private const float GoldCelebrateWindowSeconds   = 2.5f; // grant push must arrive inside this
        private const float GoldCelebrateLookbackSeconds = 1.5f; // ...or have landed this recently
        private const float GoldCelebrateCountSeconds    = 1.15f;// chip count matches the readout's

        private ElarionUiKit.CurrencyChipHandle _wisdomChip;
        private ElarionUiKit.PartyNameplateHandle _heartPlate;   // WO-432: Heart of Elarion on the shared plate
        private TMP_Text _heartObjectiveLabel;
        // WO-1407: the objective line is STATE, resolved in Core (HeartObjectiveCopy.Resolve)
        // from the posture rail + the army seam. These are the change-detect inputs of the last
        // paint, so the per-frame poll repaints (and traces) only on a transition.
        private bool _heartObjectiveHostile;
        private bool _heartObjectiveCapablePainted;
        private PostureSignals.RaidLockReason _heartObjectiveLockPainted = PostureSignals.RaidLockReason.None;
        private int _heartObjectiveArmyVersionPainted = -1;
        private bool _heartObjectiveHostilePainted;
        private bool _heartObjectiveEverPainted;

        // WO-1379 HEARTFIRE - the flame row + rekindle line under the Heart of Elarion
        // plate. Repainted from the Core posture rail (PostureSignals.SetHeartfire), the
        // same cheap poll as the collectors chip; the View derives NOTHING.
        private RectTransform _heartfireFlameHost;
        private readonly List<Image> _heartfireFlameSlots = new List<Image>(3);
        // The flame sprite is resolved from Resources ONCE (see ResolveHeartfireFlameSprite):
        // the repaint runs on every count change and must never touch Resources.
        // _heartfireFlameSpriteMissing latches the MISS, so a missing key costs one lookup and
        // one trace line for the whole session, not one per repaint.
        private static Sprite _heartfireFlameSprite;
        private static bool _heartfireFlameSpriteMissing;
        private TMP_Text _heartfireLabel;
        /// <summary>WO-1384: the rekindle line ("Heartfire is full" / "Heartfire rekindles in
        /// m:ss") on its OWN row under the marks row. It used to be the second line of
        /// _heartfireLabel, which forced two lines into a one-line band and shrank both.</summary>
        private TMP_Text _heartfireRekindleLabel;
        private int _heartfireLitPainted = -1;
        private int _heartfireMaxPainted = -1;
        private long _heartfireSecondsPainted = -1L;
        private ElarionUiKit.TargetFrameHandle _targetFrame;
        private ElarionUiKit.CastBarHandle _castBar;
        private ElarionUiKit.ActionSlotHandle[] _abilitySlots;
        // WO-917 Phase B: click closures are built once while loadout state changes at runtime.
        private bool[] _abilitySlotEquipped;
        private ElarionUiKit.SoftGlowCooldown[] _abilityGlows;   // WO-611: soft under-glow cooldown (combat HUD only)
        private ElarionUiKit.LockCrosshairHandle _lockBadge;     // WO-611: animated target lock crosshair (combat HUD only)
        private ElarionUiKit.ActionSlotHandle[] _assignableSlots;
        private ElarionUiKit.ActionSlotHandle _itemSlot;
        private ElarionUiKit.ObsidianModal _itemPicker;
        private PanelHandle _itemPickerPanelHandle;
        private WorldHold.Handle _itemPickerHold;
        private Button _itemHealButton;
        private Button _itemManaButton;
        private TMP_Text _itemHealLabel;
        private TMP_Text _itemManaLabel;
        private bool _itemUseInFlight;
        private ElarionUiKit.ActionSlotHandle[] _playerStatusSlots;
        private ElarionUiKit.ActionSlotHandle[] _enemyStatusSlots;
        private const int StatusSlotCount = 6;
        private ElarionUiKit.ActionSlotHandle _attackSlot;
        // WO-611 ATTACK PILL rect, in fractions of the actionRail zone. Shared with
        // CombatArcLayout611 (below): the Q/W/E/R medallion arc is computed FROM this
        // pill rect at layout time, so pill and arc can never drift apart again
        // (capture 2026-07-06 battle_hud.png — see BuildAbilityRow).
        internal const float Pill611X0 = 0.30f, Pill611Y0 = 0.02f;
        internal const float Pill611X1 = 0.99f, Pill611Y1 = 0.30f;
        private ElarionUiKit.CurrencyChipHandle[] _resChips;      // expanded row
        private TMP_Text[] _cappedResourceValues;                 // Wood / Iron / Stone current of capacity
        private ElarionUiKit.CurrencyChipHandle _resGoldOnly;     // collapsed variant
        private GameObject _resExpandedRow;
        private TMP_Text _resHintLabel;   // WO-1221: the collapsed chip's "+N more" hint
        // WO-1435: the rail chips whose vertical offset is DERIVED from the resource panel's
        // laid-out height rather than authored. _buildersClearance stays null in shipping builds
        // (its chip's build call is retired — see BuildQueueStatusChip).
        private HudRailClearance _collectorsClearance, _buildersClearance;
        private Button _fleeButton, _startWaveButton;
        // ── WO-835 action bar (owner architecture law 2026-08-02): the bottom bar renders
        // ONLY the applicable buttons, packed + centered. Every predicate (Talk in range,
        // Raids capable, Map onboarded, Upgrade focus, posture set) lives in the Core
        // HudActionBarModel; this View holds the button GameObjects and renders the array
        // it is passed (ApplyActionBar), NOTHING else. The old Update() gate polls
        // (talk dim / raids dim / map hole-hide / Quests<->Upgrade relabel) are RETIRED
        // from this class — moved into the model.
        private HudActionBarModel _barModel;
        private readonly GameObject[] _barButtons = new GameObject[HudActionBarModel.ButtonCount];
        private readonly RectTransform[] _barButtonRects = new RectTransform[HudActionBarModel.ButtonCount];
        private GameObject _peacefulDockRoot;
        private GameObject _combatDockRoot;
        private ElarionUiKit.ActionSlotHandle[] _adaptiveCombatSlots;
        // Constant per-button width (owner default: never resize a face as context
        // changes) sized so the 7-button MAX packs the ActionBar zone exactly; smaller
        // sets keep the SAME width and the group centers ((1 - gap*(max-1)) / max).
        private const float BarGap = 0.01f;

        // WO-911 (ruling Q10+Q13) — the section-3b FLAGGED check, settled AT SOURCE.
        // ---------------------------------------------------------------------------------
        // The question was whether a 6-face bar built on 7-slot geometry leaves a dead trailing
        // slot. Read together, HudActionBarModel and ApplyActionBar answer it: the bar renders
        // HudActionBarModel.Active (a LIST whose length varies), NOT ButtonCount, and
        // ApplyActionBar CENTRES the group (x = (1 - groupW) / 2). A shorter set therefore cannot
        // leave a hole or a trailing gap — it just occupies less width, centred.
        //
        // So the fix is not "drop ButtonCount to 6": that const is the ENUM-IDENTITY bound and the
        // face arrays are indexed by ordinal (Upgrade = 6, with Map kept dormant at 4), so lowering
        // it would put Upgrade out of bounds. The number that genuinely went 7 -> 6 is the maximum
        // number of faces that can be VISIBLE at once, and that is what the slot width must derive
        // from. Both literals are now derived from HudActionBarModel.MaxVisibleFaces, so the next
        // face added or removed cannot silently overflow or under-fill the zone again.
        private const float BarSlotW =
            (1f - BarGap * (HudActionBarModel.MaxVisibleFaces - 1)) / HudActionBarModel.MaxVisibleFaces;
        private const float BarY0 = 0.10f, BarY1 = 0.95f;
        /// <summary>WO-1144 — the wave block's OWN band, in FIXED reference px, hung below the
        /// Status crown so it can never share a rect with the compass again (see BuildWaveBlock).
        /// 128 = MinTouchPx(112) for the Start Wave CTA + 16 px of margin, so the 0.03..0.93 CTA
        /// band resolves to 115 px - clear OF the touch floor rather than exactly on it (a band
        /// authored to land on the floor to the decimal fails the moment anyone nudges either
        /// number). Fixed pixels, never a fraction: the whole defect was a fraction band that
        /// collapses to ~140 ref px in landscape.</summary>
        public const float WaveBandHeightPx = 128f;
        // Raids dim visuals (WO-820 semantics preserved: dim toward Disabled, never
        // uninteractable — the tap still reaches the drillmaster redirect). The DECISION
        // (RaidsDimmed) comes from the model; only the built-colour restore lives here.
        private Image _raidsButtonImage;          // targetGraphic face (built colour cached)
        private TMP_Text _raidsButtonLabel;
        private Color _raidsImageBuiltColor = Color.white;
        private Color _raidsLabelBuiltColor = Color.white;
        private TMP_Text _fleeLabel;
        private TMP_Text _waveLabel, _waveCountdown;
        private ElarionUiKit.BarHandle _waveProgress;
        private GameObject _waveBlockRoot;
        private ElarionUiKit.NameplateHandle[] _cycleRows;
        private string[] _cycleIds;
        private ElarionUiKit.SlideDockHandle _slideDock;   // WO-439: left slide-out (Chat/Ranks/Music/Settings)
        private HudCompassWidget _compass;
        private HudMinimapWidget _minimap;   // WO-828 — null when ff.minimap is OFF
        // WO-778: persistent Builders/Training status chip (CoC-feel; polls ObsidianQueueGate.Status).
        private TMP_Text _queueChipLabel;
        private QueueRailView _queueRail;     // WO-864: the CoC card rail replaces the WC3 text rows
        private RectTransform _queueRailMount;   // the Builders EXPANDED section (collapsed by default)
        private int _queueStatusVersion = -1;
        private int _queueRailSyncFrames;        // post-expand re-sync countdown (see SetRailSection)

        // WO-1027 — the Manage face carries the SESSION-SHAPE numeral ("Manage - 2 of 3 idle").
        // The View only paints it; the words are decided in Core (HudActionBarModel), exactly as
        // the Raids dim tell above. Zero predicates here.
        private TMP_Text _manageButtonLabel;

        // WO-900 §4 — the AMBIENT collector tell. The diegetic tell (CollectorStackView) lives on
        // the building; this chip is the one you can read from anywhere in town, with no modal.
        private TMP_Text _collectorsChipLabel;
        private int _collectorStatusVersion = -1;

        private static readonly string[] PeacefulDockLabelKeys =
        {
            HudStrings.KeyNavBuild,
            HudStrings.KeyNavTalk,
            HudStrings.KeyNavHero,
            HudStrings.KeyNavJourney,
            HudStrings.KeyNavManage,
        };
        private readonly TMP_Text[] _peacefulDockLabels = new TMP_Text[5];
        private bool _localTextSubscribed;

        // WO-1515 sec.2B (owner ruling 2026-09-06 20:05) - THE ATTACK REPORT CHIP.
        // "the only way to get to the defense report is buried under settings then realm.
        //  should be on screen as a button if there is a report that is incoming".
        // The BAND is what gets toggled, never the registered root: the root belongs to
        // ApplyPosture / hud-areas.json occupancy, and two owners calling SetActive on one
        // object is how a widget ends up permanently off in exactly one posture. Posture says
        // WHERE the chip may live; the unread predicate says WHETHER it is there.
        private TMP_Text _defenseChipLabel;
        private RectTransform _defenseChipBand;
        private int _defenseChipKey = -1;
        /// <summary>WO-1670b (owner ruling 2026-09-10 12:55) — the resource-panel state that was
        /// folded into the LAST visibility decision. It is a second input to the SAME change
        /// detector, never a second visibility writer: the model's Key does not move when the
        /// player opens the resource panel, so without this the throttled early-return would
        /// hold the chip on screen over the expanded panel forever.</summary>
        private bool _defenseChipPanelWasOpen;
        private float _defenseChipPollTimer;
        /// <summary>How often the unread predicate is evaluated, seconds. The Builders and
        /// Collectors chips compare a published Version int per frame; the defence ledger
        /// publishes no version, and DefenseReportChipModel.Current walks the retained ring
        /// (re-normalising up to MaxRetained records), so this one is THROTTLED instead. A
        /// report lands at most once per assault - half a second is invisible to the player
        /// and costs nothing on the frame path.</summary>
        private const float DefenseChipPollSeconds = 0.5f;

        // ── RIGHT RAIL, ONE CHIP STYLE (owner ruling 2026-08-05) ────────────────────
        // "I love the builder screen on the right. However, it should be minimized like
        //  everything else, like echoes until needed. The echoes, the builders, and the
        //  resources should all be styled similarly. So they're all the same until you
        //  click and open and expand them."
        // The three rail sections open one at a time; this is the whole arbiter state.
        private enum RailSection { None = 0, Builders = 1, Resources = 2 }
        private RailSection _railOpen = RailSection.None;

        // model subscriptions (for teardown)
        private readonly List<Action> _unsubscribe = new List<Action>();

        private bool _startWaveAvailable;

        // ⭐ WO-1221 - THE RAIL IS A TOGGLE, NOT A TIMED PEEK (owner ruling 2026-08-26).
        // It used to be `float _chipsExpandUntil` - a tap set `Time.unscaledTime + 6f` and the
        // rail closed itself six seconds later. That timer was never ruled; it was simply what
        // WO-440 happened to build, and a player checking whether she can afford something is
        // very often slower than six seconds. The owner ruled it OUT explicitly: tap the gold
        // chip -> the rail opens and STAYS open; tap again -> it closes.
        // ⛔ Do not reintroduce a duration here. The rail still closes itself when its OPENER
        // goes away (build / modal / battle occupancy) - see the LateTick gate - which is the
        // WO-1205 invariant "the panel can never outlive its opener" and is a different rule.
        private bool _resChipsExpanded;

        // WO-1221 — post-expand MEASURED verify (see TickResourceExpandVerify).
        // Frames remaining in the settle poll. WO-976's travelling rule: measure AFTER layout
        // settles, and POLL — do not guess a frame count. The observed ceiling elsewhere is 8.
        private const int ResExpandVerifyMaxFrames = 8;
        private int _resExpandVerifyFrames;

        // heartStatus scene gate (ApplyHeartSceneGate): the hub test is cached per ACTIVE
        // SCENE (Scene.name allocates a string, and the gate runs on every occupancy apply
        // AND every frame's availability poll), and the decision is logged only when it
        // FLIPS, so a per-frame poll cannot spam the trace.
        private int _heartGateSceneHandle = int.MinValue;
        private bool _heartGateIsHub;
        private string _heartGateSceneName = string.Empty;
        // WO-1436 — the HUB-ONLY widget set. Widgets whose MEANING is the town, listed in
        // hud-areas.json rows that legitimately also fire outside a hub. Each gets the exact
        // heartStatus treatment: the ROW stays authored (it is not wrong), and a scene test
        // sits ON TOP of it. One flip-state per widget (this REPLACES the single
        // _heartGateLogged int, which only ever tracked heartStatus).
        private static readonly string[] HubOnlyWidgets = { "heartStatus", "waveBlock" };
        private readonly int[] _hubOnlyLogged = { -1, -1 };

        // Why-it-is-hub-only, printed in the gate's own trace so a capture explains itself.
        private static readonly string[] HubOnlyWhy =
        {
            "the Heart is village-only",
            "waves attack the TOWN; a raid/dungeon has no wave to count down",
        };

        /// <summary>Build the whole kit under a fresh HudAreasHost.</summary>
        public static HudKitController Create(VillageHudController owner)
        {
            var host = HudAreasHost.Create(owner != null ? owner.transform : null);
            var kit = host.gameObject.AddComponent<HudKitController>();
            kit._owner = owner;
            kit._host = host;
            kit._evaluator = host.gameObject.AddComponent<PostureEvaluator>();
            kit._config = HudAreasConfig.Load();
            kit._models = CoreServices.HudModel;
            kit.BuildWidgets();
            kit.BindModels();
            kit._evaluator.PostureChanged += kit.ApplyPosture;
            kit.ApplyPosture(kit._evaluator.Posture);
            FlowTrace.Step("HudKit", "kit assembled: " + kit._widgets.Count + " widgets, posture " +
                           HudPostureKeys.Key(kit._evaluator.Posture));
            return kit;
        }

        /// <summary>The whole-HUD visibility seam (VillageHudController.SetHudVisible adapter).</summary>
        public void SetHudVisible(bool visible)
        {
            if (_host == null || _host.Group == null) return;
            _host.Group.alpha = visible ? 1f : 0f;
            _host.Group.interactable = visible;
            _host.Group.blocksRaycasts = visible;
        }

        /// <summary>Start-Wave availability push (StartWaveHudBridge -> VillageHudController adapter).</summary>
        public void SetStartWaveAvailable(bool available)
        {
            // StartWaveHudBridge owns the gameplay/onboarding predicate and pushes the
            // already-gated availability. This view only renders that model input.
            _startWaveAvailable = available;
            if (_startWaveButton != null) _startWaveButton.gameObject.SetActive(_startWaveAvailable);
        }

        /// <summary>Repair-prompt push adapter — the shared toast PLUS a factory Repair
        /// button firing RepairConfirmRequested (the WallRepairHudBridge contract; the
        /// old prompt's Cancel is the toast simply expiring).</summary>
        public void ShowRepairToast(string wallLabel, float damagePercent)
        {
            var card = ShowToast(ElarionUiKit.ToastTone.Danger,
                (string.IsNullOrEmpty(wallLabel) ? "A wall" : wallLabel) +
                " is damaged (" + Mathf.RoundToInt(damagePercent) + "%)", lifetime: 6f);
            if (card == null) return;
            ElarionUiKit.BuildObsidianButton(card.transform, "Repair",
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.70f, 0.12f), new Vector2(0.97f, 0.88f), () =>
                {
                    if (_owner != null) _owner.RepairConfirmRequested?.Invoke();
                    Destroy(card);
                });
        }

        // =====================================================================
        //  REPAIR PROMPT — the ACTIONABLE surface (WallRepairHudBridge contract)
        // ---------------------------------------------------------------------
        //  WO/F8 2026-08-24 ("Purple shader says repair but no option to repair").
        //  The owner selected a damaged structure, the world marker turned violet
        //  and its label read "Repair?" (RepairHighlight.ApplyColor), and NOTHING
        //  actionable appeared. Root cause, from the DEVICE log (not inference —
        //  Logs/device/2026-08-20-equip.log:4580831, repeated on every Bind()):
        //
        //    W/Unity: [WallRepairHudBridge] One or more HUD repair-prompt methods
        //             were not found on 'DeNelle.HUD.VillageHudController'.
        //
        //  WallRepairHudBridge.ResolveHudHandles looks the HUD up BY REFLECTION
        //  (the Village asmdef may not reference DeNelle.HUD) for exactly:
        //      ShowRepairPrompt(string,int,bool) / HideRepairPrompt() /
        //      ShowRepairFeedback(string,bool)
        //  The HUD only ever had ShowRepairPrompt(string,FLOAT) and NO
        //  ShowRepairFeedback at all, so GetMethod returned null for two of the
        //  three, OnPromptShown's `_hudShowPrompt?.Invoke(...)` was a silent NO-OP,
        //  and the selection could never be confirmed. A reflection seam with no
        //  compile-time check and no test drifted, and the ONLY detector left was
        //  the owner's eyes — pinned now by RepairHudContractRegression.
        //
        //  This is a PROMPT, not a toast: it does NOT self-expire. A prompt that
        //  timed out would leave the marker selected with nothing to press again,
        //  which is the reported symptom returning on a delay. It closes only on
        //  Repair, on Cancel, or on HideRepairPrompt (PromptHidden).
        // =====================================================================

        private GameObject _repairPromptCard;

        /// <summary>
        /// Shows the repair prompt for the currently-selected structure.
        /// <paramref name="subtitle"/> is the fully-composed line the controller
        /// hands over verbatim (e.g. "Repair the North Gate? Cost: 12 wood, 4 iron")
        /// — the materials cost travels IN the text, so the HUD never prices anything.
        /// When <paramref name="affordable"/> is false the Repair button is present but
        /// NOT interactable, so the player can read the price they cannot yet meet
        /// instead of the prompt silently vanishing.
        /// </summary>
        public void ShowRepairPrompt(string subtitle, bool affordable)
        {
            HideRepairPrompt();

            var mount = _host != null ? _host.Mount(HudArea.Feedback) : null;
            if (mount == null)
            {
                FlowTrace.Fail("HudKit",
                    "repair prompt NOT shown: HudArea.Feedback mount is null — the selected " +
                    "structure has no way to be confirmed. subtitle='" + (subtitle ?? "") + "'");
                return;
            }

            var parts = ElarionUiKit.ToastCard(mount,
                affordable ? ElarionUiKit.ToastTone.Gold : ElarionUiKit.ToastTone.Danger,
                accentLeft: true, align: TextAnchor.MiddleLeft);
            var rt = (RectTransform)parts.card.transform;
            // Four readable detail lines plus phone-sized actions. The old 12%-high toast seat
            // forced the full structure/cost sentence behind the buttons on narrow screens.
            rt.anchorMin = new Vector2(0.08f, 0.66f);
            rt.anchorMax = new Vector2(0.92f, 0.94f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            parts.label.text = subtitle ?? "";
            parts.label.horizontalOverflow = HorizontalWrapMode.Wrap;
            parts.label.verticalOverflow = VerticalWrapMode.Overflow;
            parts.label.resizeTextForBestFit = true;
            parts.label.resizeTextMinSize = 18;
            parts.label.resizeTextMaxSize = 28;
            var labelRt = (RectTransform)parts.label.transform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.57f, 1f);
            labelRt.offsetMin = new Vector2(22f, 12f);
            labelRt.offsetMax = new Vector2(-10f, -12f);

            bool rebuild = !string.IsNullOrEmpty(subtitle) &&
                           subtitle.IndexOf("Rebuild cost:", System.StringComparison.Ordinal) >= 0;
            string actionCopy = rebuild ? "Rebuild structure" : "Repair structure";

            var repairBtn = ElarionUiKit.BuildObsidianButton(parts.card.transform, actionCopy,
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.59f, 0.53f), new Vector2(0.97f, 0.91f), () =>
                {
                    if (_owner != null) _owner.RepairConfirmRequested?.Invoke();
                    HideRepairPrompt();
                });
            if (repairBtn != null) repairBtn.interactable = affordable;

            ElarionUiKit.BuildObsidianButton(parts.card.transform, "Cancel",
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.59f, 0.09f), new Vector2(0.97f, 0.47f), () =>
                {
                    if (_owner != null) _owner.RepairCancelRequested?.Invoke();
                    HideRepairPrompt();
                });

            _repairPromptCard = parts.card;
            FlowTrace.Step("HudKit",
                "repair prompt SHOWN (affordable=" + affordable + "): " + (subtitle ?? ""));
        }

        /// <summary>Dismisses the repair prompt (PromptHidden / confirm / cancel).</summary>
        public void HideRepairPrompt()
        {
            if (_repairPromptCard == null) return;
            Destroy(_repairPromptCard);
            _repairPromptCard = null;
            FlowTrace.Step("HudKit", "repair prompt hidden");
        }

        /// <summary>Repair result / refusal message (WallRepairController.FeedbackShown).</summary>
        public void ShowRepairFeedback(string message, bool isError)
        {
            ShowToast(isError ? ElarionUiKit.ToastTone.Danger : ElarionUiKit.ToastTone.Confirm,
                      message ?? "");
        }

        /// <summary>
        /// Wave-clear push adapter — routes the old no-op banner through the shared toast.
        /// ⚠ WO-1309: REACHABLE ONLY FROM VillageHudController.ShowWaveClearBanner, which is
        /// itself caller-less on purpose — the wave-clear announcement is the end-state modal.
        /// The `enemiesDefeated` sentence below is what rendered the owner's crystal balance as
        /// "400 foes defeated"; the lie was at the CALL SITE, not here, and that call site is
        /// cut (WaveFeedbackDirector.OnWaveCleared). Left intact, not deleted, so the seam is
        /// available if a real per-wave kill count is ever authored — sourced from
        /// WaveManager's payout record, never from a wallet.
        /// </summary>
        public void ShowWaveClearToast(int waveNumber, int enemiesDefeated, string flavourLine)
        {
            string line = "Wave " + waveNumber + " cleared! " +
                          (enemiesDefeated > 0 ? enemiesDefeated + " foes defeated. " : "") +
                          (flavourLine ?? "");
            ShowToast(ElarionUiKit.ToastTone.Confirm, line.TrimEnd());
        }

        private GameObject ShowToast(ElarionUiKit.ToastTone tone, string text, float lifetime = 3.5f)
        {
            var mount = _host != null ? _host.Mount(HudArea.Feedback) : null;
            if (mount == null) return null;
            var parts = ElarionUiKit.ToastCard(mount, tone, accentLeft: true, align: TextAnchor.MiddleCenter);
            var rt = (RectTransform)parts.card.transform;
            // ⭐ WO-1219 - THE ONE RESERVED TOAST ZONE, centred above the action bar.
            // Every transient toast on this screen lands here, whichever module raised it, so a
            // toast can never again be authored against a corner whose contents its own module
            // cannot see (that is how the Repair All card came to sit on the minimap, the region
            // status line AND the gear at once - tmp/shield-seat-101829.png). The seat is DATA
            // now: HudLayoutBands.ToastZone, shared with DeNelle.Village and DeNelle.Dungeons.
            HudLayoutBands.ApplyToastZone(rt);
            parts.label.text = text ?? "";
            Destroy(parts.card, lifetime);
            FlowTrace.Step("HudKit", "toast: " + text);
            return parts.card;
        }

        // =====================================================================
        // WIDGET CONSTRUCTION — factory-only (§5).
        // =====================================================================

        private void BuildWidgets()
        {
            Transform pool = transform;   // widgets are reparented into areas on ApplyPosture

            // ── vitals: WO-432 shared BuildPartyNameplate (name + HP + MP) ──
            // withXpStrip (owner 07-06): thin gold XP-to-next-level strip under HP/MP, built on
            // the SHARED plate path so it renders in BOTH CombatHud611 flag states (a vitals
            // fact, not combat chrome). Bound from HeroVitalsModel in OnVitals.
            // WO-1219: the plate's seat inside the Vitals mount is authored ONCE, in
            // HudLayoutBands (the left column's one owner) - it is no longer a magic 0.35f here.
            // The mount now spans the plate band AND the SKILL chip band beneath it, so the two
            // are exclusive sub-rects rather than a plate with a chip tucked under its skirt.
            _vitals = ElarionUiKit.BuildPartyNameplate(pool, "Hero",
                new Vector2(HudLayoutBands.HeroPlateInVitals.xMin, HudLayoutBands.HeroPlateInVitals.yMin),
                new Vector2(HudLayoutBands.HeroPlateInVitals.xMax, HudLayoutBands.HeroPlateInVitals.yMax),
                withXpStrip: true);
            if (FeatureFlags.CombatHud611)
            {
                // WO-611 (mockup v8): HP/MP bars RECESSED in an inset WELL inside the plate — a darker
                // sub-panel (#06080b @ 50%) with a 1px darker TOP edge (the inset-shadow read), wrapping
                // both bar rows (StatBars spans 0.06..0.94 x, 0.08..0.55 y in BuildPartyNameplate) so
                // the bars never touch the plate edge. Explicit colours — the kit Well() (Track black
                // @45% + BlinkChrome-gated rim) washed out against the ornate plate in the 07-05 capture.
                // First sibling = above the plate face (the root's own Image) but below name + StatBars.
                var vitalsWell = ElarionUiKit.AddImage(_vitals.Root.transform, "VitalsWell",
                    new Vector2(0.02f, 0.02f), new Vector2(0.99f, 0.60f),
                    new Color(0.024f, 0.031f, 0.043f, 0.50f), rounded: true);
                vitalsWell.GetComponent<Image>().raycastTarget = false;
                var wellTop = ElarionUiKit.AddImage(vitalsWell.transform, "TopEdge",
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Color(0f, 0f, 0f, 0.55f), rounded: false);
                var wtRt = (RectTransform)wellTop.transform;
                wtRt.pivot = new Vector2(0.5f, 1f);
                wtRt.sizeDelta = new Vector2(-4f, 1.5f);   // 1px-ish darker top edge, inset from the corners
                wtRt.anchoredPosition = Vector2.zero;
                wellTop.GetComponent<Image>().raycastTarget = false;
                vitalsWell.transform.SetAsFirstSibling();
            }
            Register("playerNameplate", WrapAsWidget("playerNameplate", _vitals.Root.gameObject));
            BuildStatusRow(pool, "playerBuffRow", out _playerStatusSlots);

            // Owner F8 07-06: the standalone under-plate XP bar is GONE — it rendered frameless
            // (its HudBarXp frame sprite never drew) and duplicated the in-plate XP strip inside
            // the Knight nameplate (ElarionUiKitNameplate), which is THE one XP display. The
            // hud-areas.json "xpBar" occupancy row is now inert (ApplyPosture iterates only
            // registered widgets), so no data change is required.

            // Owner F8 07-06: Wisdom = skill points; the chip's icon art is a known gap, so
            // "434" read as an unlabeled naked number. "SKILL" text tag is ALWAYS visible
            // (colorblind law: icon + TEXT, never icon-or-nothing).
            //
            // ⭐ WO-1219 - THE CHIP GETS WIDTH, NOT A SHORTER WORD (owner ruling, 2026-08-26).
            // The device captured "SK... 177" (tmp/screen-103219.png): the chip's own sub-rect
            // was 0.02..0.34 x / 0.00..0.16 y of a Vitals mount that was 0.320 x 0.185 of screen,
            // i.e. ~220 x 29 REFERENCE units at 2670x1200 - shorter than the kit's FontFloor of
            // 30, so the tag could not even render at the legibility floor before FitSingleLine
            // ellipsised it. The band now comes from HudLayoutBands.SkillChipInVitals: ~243 x 50
            // units, its OWN exclusive band under the hero plate, sized so "SKILL" (~93 units at
            // FontMicro) and a six-digit amount both fit whole before any autoshrink.
            // ⛔ Do not solve a truncation by shortening the authored string - a two-glyph stub in
            // front of a number is a naked number with noise on it, which is what this tag exists
            // to prevent.
            // Locked adaptive-HUD ruling: skill points live only in Hero -> Skills.

            // ── status: wave block (calm(town), between waves only) + heart ──
            BuildWaveBlock(pool);
            // WO-432: the Heart of Elarion status — a tree-of-life glyph + "Elarion" label
            // ABOVE its own gold bar, occupying the HeartStatus area (left, below the hero
            // nameplate). Reads as the world-tree/heart status, NOT a second hero HP bar.
            BuildHeartStatus(pool);

            // targetCycle: up to 4 compact enemy rows -> HudCommands.CycleSelect.
            BuildTargetCycle(pool);

            // ── system: flee ──
            // 3-settings-doors -> 1 (owner cosmetic flag A, 2026-07-24): the top-right "Menu"
            // TEXT button was REMOVED here. It reached the same card the LEFT gold-gear
            // slide-dock's Settings tab already opens, so it was a duplicate door (WO-1399: that
            // tab now opens the REAL Settings via SettingsGate; Help is a row inside it). The
            // single settings entry point is now the left gear (BuildSlideDock: Chat/Leaderboard/
            // Music/Settings/Pause). hud-areas.json "settingsButton" rows go inert automatically
            // (posture only iterates REGISTERED widgets). Flee stays below.

            // Two-tap arm/confirm (anti-misfire, carried from the retired BattleArenaHud
            // flee button): first tap arms for 2s ("Flee?"), second tap inside the window
            // actually flees; the window expiring disarms back to "Flee".
            _fleeButton = ElarionUiKit.BuildObsidianButton(pool, CombatHudText.ResolveFlee(false),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Red,
                new Vector2(0.10f, 0.05f), new Vector2(0.98f, 0.48f), OnFleeTapped);
            _fleeLabel = _fleeButton.GetComponentInChildren<TMP_Text>();
            Register("fleeButton", WrapAsWidget("fleeButton", _fleeButton.gameObject));

            // ── targetInfo: target frame + cast telegraph ──
            _targetFrame = ElarionUiKit.BuildTargetFrame(pool, new Vector2(0f, 0.35f), new Vector2(1f, 1f));
            if (FeatureFlags.CombatHud611)
            {
                // WO-611 (mockup v8): gold "Lv N" right beside the enemy name. The Blink prefab path
                // (MODE 1) has NO *level* text child (FindDeep "level" -> null), so TargetFrameHandle.
                // Set()'s write had nowhere to land — the 07-05 capture showed no slot. Give the
                // handle a label; MODE 2 already has one, which is re-tinted to the ratified gold.
                // WO-1232: the slot's CONTENT is now the authored BOSS/ELITE word, never a number.
                var gold611 = new Color(0.831f, 0.686f, 0.353f, 1f);   // #d4af5a

                // Capture 2026-07-06 (battle_hud.png): the enemy PORTRAIT circle overhung the
                // plate's LEFT edge. Proven in the prefab bytes: TargetNameplate.prefab's
                // TargetIcon is CENTRE-anchored at a FIXED (-191.1, -0.8) offset, 90x90 px,
                // authored on a 480px-wide root — but InstantiateBlinkPrefab STRETCHES the root
                // to the status-zone rect (~410 px at 720p), so the fixed offset lands the
                // circle past the plate (icon left edge -236 < -205 half-width). Re-anchor it
                // FRACTIONALLY inside the plate (inset left column, aspect-true) so it can
                // never leave the plate at any width. No-op when absent (MODE 2 has none).
                var portrait611 = FindTargetPortrait611(_targetFrame.root.transform);
                if (portrait611 != null)
                {
                    var prt = (RectTransform)portrait611.transform;
                    prt.anchorMin = new Vector2(0.03f, 0.14f);
                    prt.anchorMax = new Vector2(0.21f, 0.86f);
                    prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
                    portrait611.preserveAspect = true;
                }

                // TITLE ROW (capture 2026-07-06: "! Orcish Raid" clipped MID-WORD and the gold
                // "Lv 4" overlapped the name/plate edge). Proven causes: the prefab's TargetName
                // is a FIXED 259px centre-anchored box that never reflows with the stretched
                // plate and had no kit fit (MODE 1 skipped FitSingleLine), and the previous Lv
                // label was a FULL OVERLAY of that same box. Fix: one measured row — a container
                // on fractional plate anchors (right of the portrait column); the name takes the
                // LEFT ~72% with the §1.14 bounded auto-size/ellipsize (ElarionUiKit.FitSingleLine,
                // the Forge-title law) and the gold slot takes the RIGHT slice. The name therefore
                // ellipsizes BEFORE it can touch it, and neither can reach the plate edge.
                // WO-1232: that gold slot now carries the AUTHORED classification WORD (BOSS /
                // ELITE / nothing), not a "Lv N" number - the number was maxHp/25 and is removed.
                if (_targetFrame.name != null)
                {
                    var nameRt = (RectTransform)_targetFrame.name.transform;
                    var titleRow = new GameObject("TitleRow611", typeof(RectTransform));
                    var rowRt = (RectTransform)titleRow.transform;
                    rowRt.SetParent(nameRt.parent, false);
                    rowRt.SetSiblingIndex(nameRt.GetSiblingIndex());   // keep the name's draw order
                    rowRt.anchorMin = new Vector2(0.24f, 0.72f);
                    rowRt.anchorMax = new Vector2(0.88f, 0.97f);
                    rowRt.offsetMin = Vector2.zero; rowRt.offsetMax = Vector2.zero;

                    nameRt.SetParent(rowRt, false);
                    nameRt.anchorMin = Vector2.zero;
                    nameRt.anchorMax = new Vector2(0.72f, 1f);
                    nameRt.offsetMin = Vector2.zero; nameRt.offsetMax = Vector2.zero;
                    _targetFrame.name.alignment = TextAlignmentOptions.MidlineLeft;
                    ElarionUiKit.FitSingleLine(_targetFrame.name);

                    if (_targetFrame.badge == null)
                    {
                        _targetFrame.badge = ElarionUiKit.Label(rowRt, "", 0f, 1f, gold611,
                            ElarionUi.FontLabel, TextAlignmentOptions.MidlineRight, 0.74f, 1f, bold: true);
                    }
                    else
                    {
                        // MODE 2 built its own badge slot at the plate's far left — move it in.
                        var lvRt = (RectTransform)_targetFrame.badge.transform;
                        lvRt.SetParent(rowRt, false);
                        lvRt.anchorMin = new Vector2(0.74f, 0f);
                        lvRt.anchorMax = Vector2.one;
                        lvRt.offsetMin = Vector2.zero; lvRt.offsetMax = Vector2.zero;
                        _targetFrame.badge.alignment = TextAlignmentOptions.MidlineRight;
                    }
                    ElarionUiKit.FitSingleLine(_targetFrame.badge);   // §1.14 — BOSS never spills either
                }
                else if (_targetFrame.badge == null)
                {
                    // Neither build mode should reach here (both guarantee a name label) —
                    // fall back to a plate-anchored badge slot clear of the bar rects.
                    _targetFrame.badge = ElarionUiKit.Label(_targetFrame.root.transform, "",
                        0.72f, 0.97f, gold611, ElarionUi.FontLabel,
                        TextAlignmentOptions.MidlineRight, 0.60f, 0.88f, bold: true);
                }
                _targetFrame.badge.color = gold611;
                _targetFrame.badge.raycastTarget = false;

                // 3-state LOCK BADGE chip (crosshair art + uppercase UNLOCKED/LOCKING/LOCKED word),
                // top-right of the plate beside the badge word; driven from TargetModel in Update().
                _lockBadge = ElarionUiKit.BuildLockCrosshairBadge(_targetFrame.root.transform,
                    new Vector2(0.72f, 0.02f), new Vector2(0.99f, 0.34f));
            }
            Register("targetFrame", WrapAsWidget("targetFrame", _targetFrame.root));
            BuildStatusRow(pool, "enemyBuffRow", out _enemyStatusSlots);

            _castBar = ElarionUiKit.BuildCastBar(pool, 1, new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.30f));
            Register("castBar", WrapAsWidget("castBar", _castBar.root));

            // ── actionRail: static W/E/R class kit (WO-609 — bottom-right) ──
            BuildAbilityRow(pool);

            // ── actionBar: hotswap extras + one paused Item picker ──
            BuildAssignableSkillRow(pool);
            BuildPotionSlots(pool);

            // ── actionRail: the big basic-attack slot ──
            if (FeatureFlags.CombatHud611)
            {
                // WO-611: oblong stadium ATTACK PILL, gold-trimmed, bottom-right thumb anchor.
                // Rect = the shared Pill611* constants — the Q/W/E/R arc derives from them.
                _attackSlot = ElarionUiKit.BuildAttackPill(pool,
                    new Vector2(Pill611X0, Pill611Y0), new Vector2(Pill611X1, Pill611Y1), HudCommands.Attack);
                // WO-899 §3: "attack" now leads the fallback chain. It resolves (concept-icons.json)
                // to RpgUi/abilities/attack_sword — the SAME energy-sword artwork, but circle-masked
                // with transparent corners. The old lead concept "energy-sword" resolves to
                // icons/icon_energy_sword, which is a FULLY OPAQUE RECTANGLE: it painted its own grey
                // background square onto the pill, which is the "pasted sprite / amateur" read the
                // owner reported. Kept as the fallback so a missing file still shows a sword.
                var atkIcon = UiStyle.Icon("attack", "energy-sword", "sword", "melee");
                if (atkIcon != null) _attackSlot.SetIcon(atkIcon);
            }
            else
            {
                _attackSlot = ElarionUiKit.BuildActionSlot(pool,
                    new Vector2(0.22f, 0.02f), new Vector2(0.98f, 0.44f), HudCommands.Attack);
                var atkIcon = UiStyle.Icon("attack", "sword", "melee");
                if (atkIcon != null) _attackSlot.SetIcon(atkIcon);
            }
            Register("attackButton", WrapAsWidget("attackButton", _attackSlot.root));

            // resource chips (expanded row) + collapsed gold-only variant (tap-expand).
            BuildResourceChips(pool);

            // ⚠ OWNER RULING 2026-08-07 — THE BUILDERS CHIP IS RETIRED.
            // Verbatim: "with the manage section in the hud we can remove the open builders queue
            // on right side of hud in town ... since it has a natural home ... i had them expanded
            // in manage tab". The Manage/Queues screen now shows every line (Defense / Buildings /
            // Troops / Research) with progress bars, Finish-Now, cancel+refund and bump - so the
            // right-column chip is duplicate furniture on the busiest edge of the screen.
            //
            // This SUPERSEDES WO-911 ruling Q10/Q13, which kept the chip as a status glance after
            // retiring its double-tap door. That ruling's real intent - exactly ONE Queues entry -
            // is unchanged and now simply lands on the bar's Manage face alone.
            //
            // BuildQueueStatusChip and its QueueRailView are LEFT INTACT below, unreferenced: the
            // rail is the shared component the Manage screen also hosts, and the chip is two lines
            // from returning if the owner wants it back. Deleting them would make a reversal a
            // rewrite.
            // BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)

            // WO-900 §4 — THE AMBIENT COLLECTOR TELL takes the band the retired Builders chip
            // left free (HudArea.QueueStatus). ⚠ It is NOT the chip coming back: it carries no
            // queue state, opens no queue door, and the "exactly ONE Queues entry" rule is
            // untouched (the bar's Manage face is still it). The player's question here is a
            // different one — "has a collector stopped earning while I was not looking?" — and
            // today the only answer is the wallet number quietly failing to move.
            BuildCollectorsChip(pool);

            // WO-1515 sec.2B - the ATTACK REPORT chip. Built ALWAYS, shown only while a report is
            // unread (the band starts inactive). Building it conditionally would mean the HUD
            // had to be rebuilt when an assault ends, and a chip that only exists after a
            // rebuild is a chip that does not appear on the frame the report lands.
            BuildDefenseReportChip(pool);

            // ── town action bar (WO-835 APPLICABILITY REPACK): Build / Talk / Bag /
            // Raids / Map / Quests / Upgrade ──
            // The bar is no longer a fixed-divisor row. The Core HudActionBarModel computes
            // the ordered ACTIVE set from the context signals (posture, talk range, raid
            // capability, Onboarded, building focus) and ApplyActionBar() renders EXACTLY
            // that array — constant button width, group centered, no holes (owner
            // 2026-08-02: "the visible array should only be the ones active"). Buttons are
            // built here at a placeholder slot and positioned by the render pass; each keeps
            // its own widget id so the hud-areas.json occupancy rows still own the roots.
            // Queues stays retired (owner 2026-08-01): the right-column Builders chip is the
            // one Queues entry (ObsidianQueueRegression 7c enforces).
            var slot0Min = new Vector2(0f, BarY0);
            var slot0Max = new Vector2(BarSlotW, BarY1);

            var build = ElarionUiKit.BuildObsidianButton(pool, "Build",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                slot0Min, slot0Max,
                () =>
                {
                    if (SwallowedByCloseGrace("Build face")) return;   // WO-1393
                    if (_owner != null) _owner.BuildRequested?.Invoke();
                });
            // Carry-over (WO-T2 working-tree intent): the tutorial spotlight target.
            TutorialHighlightRegistry.Register("hud.build_button", (RectTransform)build.transform);
            RegisterBarButton(ActionBarButtonId.Build, "buildButton", build);

            var talk = ElarionUiKit.BuildObsidianButton(pool, "Talk",
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                slot0Min, slot0Max, () =>
                {
                    if (SwallowedByCloseGrace("Talk face")) return;   // WO-1393
                    FlowTrace.Step("HudKit", "Talk tapped -> HudCommands.Talk + TalkRequested");
                    HudCommands.Talk();
                    if (_owner != null) _owner.TalkRequested?.Invoke();   // legacy bridge compat
                });
            // WO-835: Talk HIDES (repacks out) when no NPC is in range — the model drops it
            // from the array; the old dim-to-0.45 CanvasGroup treatment is retired.
            RegisterBarButton(ActionBarButtonId.Talk, "talkButton", talk);

            var bag = ElarionUiKit.BuildObsidianButton(pool, "Hero",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                slot0Min, slot0Max, () =>
                {
                    // Owner 07-06 "Clicking bag doesnt do anything" (RCA log-proven): the two
                    // events below had ZERO live listeners in Main_Castle_Overworld (HeroEquipHud
                    // is scene-whitelisted and never spawned). Route through PanelRouter — the
                    // scene-independent Core opener HeroInventoryController registers at boot.
                    // The legacy events still fire for any listener that DOES exist (hub scenes).
                    if (SwallowedByCloseGrace("Hero face")) return;   // WO-1393
                    FlowTrace.Step("HudKit", "Hero tapped -> PanelRouter.Open(HeroDeck)");
                    PanelRouter.Open(PanelId.HeroDeck);
                });
            // WO-1340 — hop 1 of the spend-a-talent-point teach. Same registry contract as
            // hud.build_button above. The face is labelled "Hero" and opens PanelId.HeroDeck;
            // the highlight id says hero_button for that reason, while the ActionBarButtonId
            // stays Bag because the ORDINAL is load-bearing (CLAUDE.md §7 - the face arrays
            // are indexed by ordinal, so the enum member is never renumbered or renamed).
            TutorialHighlightRegistry.Register("hud.hero_button", (RectTransform)bag.transform);
            RegisterBarButton(ActionBarButtonId.Bag, "bagButton", bag);

            // Queues entry (owner 2026-08-01): the bar's Queues button was RETIRED — the
            // right-column Builders chip (BuildQueueStatusChip, QueueStatus band above the
            // resources dock) already taps into ObsidianQueueGate.RequestToggle and is the
            // one Queues entry. ObsidianQueueRegression 7c enforces the retirement.

            // RAIDS (owner F8 2026-07-30 "there is no raid option"): the raid loop's only HUD
            // door was the OLD VillageHudController crossed-swords icon — the kit rendered no
            // raid widget at all. Kit button -> Core RaidEntryGate -> Village RaidEntryBridge
            // -> RaidSelectionScreen (whose Open carries the WO-813 zero-troops safety net).
            // Base word from the model (WO-1008) so the live label and the model's dim-state
            // labels can never drift apart.
            var raids = ElarionUiKit.BuildObsidianButton(pool, HudActionBarModel.RaidsBaseLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                slot0Min, slot0Max, () =>
                {
                    if (SwallowedByCloseGrace("Raids face")) return;   // WO-1393
                    FlowTrace.Step("HudKit", "Raids tapped -> RaidEntryGate.RequestOpen");
                    RaidEntryGate.RequestOpen();
                });
            // WO-835 §3d (owner default 1): Raids HIDES when the player cannot raid at all
            // (no barracks / no troops / flag off — the model's RaidCapable input). The
            // WO-820 full-army gate is PRESERVED on a visible face: dim toward Disabled,
            // never uninteractable, so the tap still reaches the drillmaster redirect.
            // Capture the face image + label with their BUILT colours so ApplyRaidsDim can
            // restore exactly what the kit built.
            RegisterBarButton(ActionBarButtonId.Raids, "raidsButton", raids);
            _raidsButtonImage = raids != null ? raids.targetGraphic as Image : null;
            _raidsButtonLabel = raids != null ? raids.GetComponentInChildren<TMP_Text>(true) : null;
            if (_raidsButtonImage != null) _raidsImageBuiltColor = _raidsButtonImage.color;
            if (_raidsButtonLabel != null) _raidsLabelBuiltColor = _raidsButtonLabel.color;
            // WO-1219: the SECOND bar face that carries a second line (the WO-1008 slot numerals).
            // BuildObsidianButton armed FitSingleLine, which is right for Build/Bag/Quests and
            // WRONG here - no-wrap + ellipsis is exactly what produced the captured "Raids ...",
            // and what it ellipsised away was the NUMBERS, i.e. the whole colourblind-safe tell.
            // Identical treatment to the Manage face below, for identical reasons (WO-1144).
            if (_raidsButtonLabel != null) ElarionUiKit.FitBlock(_raidsButtonLabel);

            // MAP — ⚠ NO LONGER A BAR FACE (WO-911, owner ruling Q10+Q13, 2026-08-06).
            // Taking Map off the bar is half of how it went 7 -> 6 faces without needing an
            // 8th slot. CORRECTED 2026-09-05 (WO-1396): this note used to say the map was "now
            // reached from the Bag tab row" - that Bag route shipped behind a default-OFF flag
            // and was never offered, so it was false against the default. The Realm Map's ONE
            // public door is the Journey deck's "Realm Map" card (PlayerDeckWorkspace), which
            // routes through PanelRouter to RealmMapPanel (registered by DeNelle.Village at boot).
            // Nothing is built here, so no widget is registered under "mapButton" and the
            // hud-areas.json calm(town) row has no Map entry. ActionBarButtonId.Map stays DORMANT
            // at ordinal 4 (never masked in) so the other faces keep their indices.

            // QUESTS (WO-835 §3c): its OWN always-in-town face — the 07-06 Quests<->Upgrade
            // relabel hijack is retired (owner: "allows quests to be active more often").
            var quests = ElarionUiKit.BuildObsidianButton(pool, "Journey",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                slot0Min, slot0Max, OnQuestsAction);
            RegisterBarButton(ActionBarButtonId.Quests, "questButton", quests);

            // MANAGE — the RE-POINTED Upgrade face (WO-911, ruling Q10+Q13).
            // -----------------------------------------------------------------
            // Same enum value (ActionBarButtonId.Upgrade = 6), same widget id
            // ("upgradeButton"), same hud-areas.json row — RE-POINTED, NOT ADDED. That is what
            // dissolves the 8th-face problem: no enum extension, no ButtonCount bump, no new
            // canonical row. Only the LABEL and the DESTINATION change.
            //
            // It is no longer a context face: the model now packs it in whenever the town bar is
            // up, because it is the single door onto all three production lines. Gating it on a
            // focused building is exactly the undiscoverability WO-911 exists to remove.
            //
            // WO-1027: the base word comes from the model so the live label and the model's
            // session-shape labels can never drift apart (the Raids precedent, line ~567).
            var manage = ElarionUiKit.BuildObsidianButton(pool, HudActionBarModel.ManageBaseLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                slot0Min, slot0Max, OnManageAction);
            RegisterBarButton(ActionBarButtonId.Upgrade, "upgradeButton", manage);
            _manageButtonLabel = manage != null ? manage.GetComponentInChildren<TMP_Text>(true) : null;
            // WO-1144: this is the ONE bar face that carries a second line (the WO-1027 idle
            // numeral). BuildObsidianButton armed FitSingleLine, which is right for every other
            // face and WRONG here — no-wrap + ellipsis is exactly what produced the captured
            // "Manag...". The face is ~110 ref px tall, so two lines at the 30 px legibility
            // floor cost nothing; FitBlock keeps the same bounded auto-size and the same floor
            // and uses the height already reserved. The BuildRailChip precedent, same reasoning.
            if (_manageButtonLabel != null) ElarionUiKit.FitBlock(_manageButtonLabel);

            BuildAdaptivePeacefulDock(pool);
            BuildAdaptiveCombatDock(pool);
            // WO-1672 — the third posture dock. Built unconditionally like the other two; which
            // one the player sees is DATA, not an `if`: ApplyPosture activates whichever dock the
            // posture's hud-areas.json actionBar row names (calm(town) -> peacefulDock,
            // calm(explore) -> outsideDock, hostile -> combatDock).
            BuildAdaptiveOutsideDock(pool);

            // ── moveCluster -> HudMoveInput ──
            if (FeatureFlags.CombatHud611)
            {
                // WO-899 §1 (owner felt-test): the boxy steel D-PAD is replaced by a clean
                // ANALOG STICK — a base ring + a knob that tracks the thumb and emits a
                // CONTINUOUS -1..1 deflection (magnitude = distance/radius). Same gate, same
                // widget id, same HudMoveInput.Set contract, so HeroLocomotion is untouched.
                //
                // BuildVirtualDPad is kept as the FALLBACK (not as the flag-OFF branch — the
                // flag-OFF path must stay byte-identical to the shipping 4-round-button
                // cluster, WO-611 law). It is used only if the stick's guarded construction
                // failed, so the move widget can never be missing.
                var stick = ElarionUiKit.BuildAnalogStick(pool, new Vector2(0.5f, 0.5f), HudMoveInput.Set);
                if (stick == null || stick.root == null)
                {
                    FlowTrace.Warn("HudKit", "analog stick unavailable -- falling back to the WO-611 virtual D-pad.");
                    stick = ElarionUiKit.BuildVirtualDPad(pool, new Vector2(0.5f, 0.5f), HudMoveInput.Set);
                }
                Register("moveCluster", WrapAsWidget("moveCluster", stick.root));
            }
            else
            {
                // THE FOUR ROUND BUTTONS (§1.11).
                var cluster = ElarionUiKit.BuildControllerCluster(pool, new Vector2(0.5f, 0.5f), HudMoveInput.Set);
                Register("moveCluster", WrapAsWidget("moveCluster", cluster.root));
            }

            // ── dock: WO-439 LEFT slide-out (Chat/Leaderboard/Music/Settings), gear tab ──
            // (hidden entirely in build mode via the occupancy rows, same "chatDock" widget id).
            BuildSlideDock(pool);

            // ── status: the COMMON compass widget (navigation cue) ──
            // The reusable kit compass — cardinal heading + gold objective/region-gate
            // bearing + red enemy ticks. Placed by the hud-areas.json "compass" rows into
            // the top-centre Status area in BOTH calm(town) and calm(explore). Presentation
            // reads the world through provider delegates (wired below); owns no state.
            _compass = HudCompassWidget.Create(pool);
            WireCompassProviders(_compass);
            Register("compass", WrapAsWidget("compass", _compass.gameObject));

            // ── minimap: the "you are here" plate (WO-828) ──
            // The compass answers WHICH WAY; this answers WHERE. Same three providers,
            // re-wired rather than shared, so neither widget can break the other by
            // caching a stale hero. Placed by the hud-areas.json "minimap" rows into the
            // left-column Minimap band in calm(town) + calm(explore) only.
            //
            // Flag-OFF builds NOTHING (not a hidden widget): a minimap that is off should
            // cost zero, and an unregistered id is simply absent from every occupancy row.
            // Locked adaptive-HUD ruling: no minimap is constructed on the player HUD.

            // ── the Night Market card: the store's PERMANENT face (WO-1335) ──
            BuildNightMarketCard(pool);

            // ── feedback: the CombatTextLayer marker (its own capped/pooled canvas) ──
            var fb = new GameObject("FeedbackLayerMarker", typeof(RectTransform));
            fb.transform.SetParent(pool, false);
            if (Application.isPlaying) { var _ = CombatTextLayer.Instance; }   // ensure the layer exists
            Register("feedbackLayer", fb);
        }

        // =====================================================================
        //  WO-1335 — THE NIGHT MARKET CARD: the store's PERMANENT face on the HUD.
        // ---------------------------------------------------------------------
        //  Owner ruling 2026-09-03, twice, in her own words:
        //    "the realm store is hidden away needs a permanent face on hud"
        //    "can you take the realm store card from settings > night market and anchor it
        //     smaller to left side on hud"
        //
        //  ⭐ THIS IS THE EXISTING CARD, RE-SITED - NOT A SECOND STORE WIDGET. She named it, so
        //  it is taken rather than reinterpreted: the same authored art key `realm-store` and the
        //  same obsidian card treatment PlayerDeckWorkspace.BuildCard gives the "Realm Store"
        //  route inside settings > Night Market. What "smaller" removes is the deck card's
        //  secondary purpose line ("Browse clearly priced realm offers"): on a 320 x 156 HUD
        //  element a second line of body copy is decoration that costs the title its size, and
        //  the title is the part that carries the meaning.
        //
        //  ⭐ WO-1384 - "THE SHINING GEM" (owner felt-test 2026-09-04: "it needs to be the shining
        //  gem, it should draw attention to it so it above all stands out"). The captured card was
        //  a dark 272 x 132 thumbnail reading "NIGHT MA..." and indistinguishable from the FLAG
        //  chip beneath it. It stands out now by SIZE, FRAME and LIGHT - never by hue alone, the
        //  owner is red/green colourblind (CLAUDE.md section 7):
        //    SIZE  - HudLayoutBands.NightMarketCardWidthPx/HeightPx grew to 320 x 156, the
        //            largest control in the column (gear 112 x 112, FLAG chip 120 x 84).
        //    FRAME - WO-1384b (owner 2026-09-04 23:59, after seeing build 355952: "instead of
        //            just dropping a yellow box around the store ... can we round the edges and
        //            have a chasing soft color changing vfx, subtle but inviting?"). The flat
        //            rectangular gold AddImage frame is GONE. The card is now ROUNDED - the
        //            button's own Image is the kit RoundedSprite at NightMarketCornerRadiusPx and
        //            carries a Mask, so the opaque card art is clipped to the rounded shape (the
        //            RoundIconMask precedent, ElarionUiKit.cs:3768) - and it wears a soft RING:
        //            "NightMarketCardRing", the same RoundedSprite one radius step larger, pushed
        //            NightMarketRingPx outside the card. The masked art covers the ring's middle,
        //            so what remains visible is a true rounded band with rounded INNER and OUTER
        //            corners - geometry a single hollow 9-slice cannot produce.
        //    CHASE - "NightMarketCardComet0..2": three RadialGlowSprite blobs (head + two tail)
        //            that ride the card's perimeter once every NightMarketGlowKnobs.LapSec, half
        //            spilling past the edge and half hidden under the opaque art, so they read as
        //            a rim light travelling round the card. The ring and the comets DRIFT through
        //            the warm palette (gold -> amber -> rose -> gold) at NightMarketGlowKnobs.
        //            AlphaPct. Driven from THIS class's existing Update via AnimateNightMarketGlow
        //            (no second Update owner, no particle system on the HUD canvas); the first 60
        //            frames are Stopwatch-sampled and reported ONCE as
        //            "[Flow:Store] aurora cost <ms>ms/frame (sampled 60 frames)" - the perf pin.
        //    LIGHT - ElarionUiKit.RadialGlowSprite, the kit's one bloom primitive (the same aura
        //            StorePackCard mounts behind every pack, StorePackCard.cs:689), tinted gold
        //            behind the ring. Its rim is alpha 0, so the overshoot past the band is a
        //            transparent halo; the OPAQUE card stays inside the band's neighbours.
        //    WORD  - "NIGHT MARKET" on ONE line, never truncated: the label plate widened to
        //            x 0.30..0.97 and the fit floor stays the kit's 20 px hard floor.
        //  Colourblind law (CLAUDE.md section 7): SIZE, ROUNDING and glow MOTION carry the
        //  standout; the hue drift is decoration on top of them, never the only cue.
        //
        //  ⛔ ONE DESTINATION, TWO DOORWAYS (WO-1164). This opens PanelId.RealmStore - the SAME
        //  door RealmStoreVendor walks the player through and the same one PackStoreBootstrap
        //  registers. It is a second CALLER, never a second store. PanelRouter.Open returns FALSE
        //  when no opener is registered, so the refusal is reported rather than swallowed: an
        //  unchecked call looks to the player like a broken store and to us like nothing happened.
        //
        //  ⛔ THE BOTTOM ACTION BAR IS NOT TOUCHED. ButtonCount stays 7, no ordinal is renumbered
        //  and the dormant Map ordinal is left alone (CLAUDE.md §7 - the face arrays are indexed
        //  by ordinal). The owner explicitly chose a left-side CARD over a bar face.
        //
        //  ⛔ AND IT DOES NOT TOUCH THE MOVEMENT STICK. Its band comes from
        //  HudLayoutBands.ResolveNightMarketCard - the column's one authority - which seats it in
        //  the Minimap mount, bottoming out at y 0.483 of screen against the gear row's top at
        //  0.473 and the MoveCluster mount's top edge at 0.330. Covering the stick would break the
        //  game's only movement control, so the seat is DERIVED from the shared table and asserted
        //  by the oracle, never eyeballed.
        // =====================================================================

        /// <summary>WO-1384b: the card's corner radius in reference units. The button Image is
        /// the kit RoundedSprite at this radius and masks the art to it.</summary>
        private const float NightMarketCornerRadiusPx = 18f;
        /// <summary>WO-1384b: the soft ring's thickness in reference units, drawn OUTSIDE the
        /// card band (translucent, so a neighbour band under its tail is not occluded). 6 units
        /// on every side; the band keeps a 9.7-unit gap to the Heart plate above and a 10-unit
        /// gap to the gear below at 2670x1200 (HudLayoutBands), so the ring never enters a
        /// neighbour's band.</summary>
        private const float NightMarketRingPx = 6f;
        /// <summary>WO-1384b: comet head diameter, then the two tail blobs, reference units.</summary>
        private static readonly float[] NightMarketCometSizePx = { 64f, 52f, 40f };
        /// <summary>WO-1384b: each comet's alpha as a fraction of the knob alpha (head, tails).</summary>
        private static readonly float[] NightMarketCometAlphaScale = { 1f, 0.6f, 0.35f };
        /// <summary>WO-1384b: each comet's lag behind the head along the perimeter (lap fraction).</summary>
        private static readonly float[] NightMarketCometLag = { 0f, 0.035f, 0.07f };
        /// <summary>WO-1384b: the ring reads at this fraction of the knob alpha so the comets
        /// stay the brighter, moving element.</summary>
        private const float NightMarketRingAlphaScale = 0.8f;
        /// <summary>WO-1384b: how many frames the cost sample covers before the ONE trace line.</summary>
        private const int NightMarketGlowSampleFrames = 60;
        /// <summary>WO-1384b: the warm palette's two non-kit tones. ElarionUi has Gold; amber and
        /// rose are authored here, once, and only reachable through the palette mask.</summary>
        private static readonly Color NightMarketAmber = new Color(0.95f, 0.55f, 0.18f, 1f);
        private static readonly Color NightMarketRose  = new Color(0.86f, 0.42f, 0.50f, 1f);

        /// <summary>
        /// WO-1384b - THE THREE FEEL KNOBS, in ONE place. TUNABLE (WO-1384b): wire to the
        /// RemoteTunables rail - `hud.nightMarketGlowLapSec` (default 5),
        /// `hud.nightMarketGlowAlphaPct` (default 35), `hud.nightMarketGlowPaletteMask` (default
        /// gold|amber|rose = 7). The rail lane overwrites these statics from its int table; the
        /// animator reads them EVERY frame, so an overwrite lands live with no rebuild. Until
        /// the rail is wired they hold the shipping defaults below.
        /// </summary>
        public static class NightMarketGlowKnobs
        {
            /// <summary>hud.nightMarketGlowLapSec shipping default.</summary>
            public const float NightMarketGlowLapSecDefault = 5f;
            /// <summary>hud.nightMarketGlowAlphaPct shipping default.</summary>
            public const float NightMarketGlowAlphaPctDefault = 35f;
            /// <summary>hud.nightMarketGlowPaletteMask shipping default: Gold|Amber|Rose.</summary>
            public const int NightMarketGlowPaletteMaskDefault = PaletteGold | PaletteAmber | PaletteRose;

            public const int PaletteGold  = 1;
            public const int PaletteAmber = 2;
            public const int PaletteRose  = 4;

            /// <summary>Seconds for one lap of the perimeter. Clamped to 1..60 at read.</summary>
            public static float LapSec = NightMarketGlowLapSecDefault;
            /// <summary>Peak alpha of the ring/comets in percent. Clamped to 0..100 at read.</summary>
            public static float AlphaPct = NightMarketGlowAlphaPctDefault;
            /// <summary>Bit mask of palette stops (PaletteGold | PaletteAmber | PaletteRose).
            /// An empty mask resolves to Gold alone (logged once), never to nothing.</summary>
            public static int PaletteMask = NightMarketGlowPaletteMaskDefault;
        }

        // WO-1384b live pieces. Null when the card was not built (no obsidian button) or the
        // kit sprites failed; AnimateNightMarketGlow early-outs on null.
        private Image _nightMarketRing;
        private Image[] _nightMarketComets;
        private RectTransform[] _nightMarketCometRts;
        private int _nightMarketGlowSampled;
        private long _nightMarketGlowTicks;
        private static readonly System.Diagnostics.Stopwatch s_nightMarketGlowWatch = new System.Diagnostics.Stopwatch();
        private static readonly List<Color> s_nightMarketPalette = new List<Color>(3);
        /// <summary>WO-1384: the aura's reach past the card as a fraction of the card, per axis.
        /// The radial sprite is alpha 1 at centre and 0 at its rim, so only the transparent tail
        /// crosses the band edge.</summary>
        private const float NightMarketAuraReachX = 0.08f;
        private const float NightMarketAuraReachY = 0.16f;
        /// <summary>WO-1384: the aura's peak alpha (gold-tinted). Kept under the pack cards'
        /// art radial so the halo lights the card rather than washing the art beside it.</summary>
        private const float NightMarketAuraAlpha = 0.55f;
        /// <summary>WO-1384: the label plate's left edge as a fraction of the card (was 0.36).
        /// realm-store.png is illustration-left with an EMPTY text plate right, so widening the
        /// plate leftward costs a sliver of illustration and buys the whole word one line.
        /// <para>⚠ WO-1466 (2026-09-06) — 0.30 -> 0.20, and the REASON is the one the oracle was
        /// blind to. `Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png` renders the caption
        /// **"THE NIGHT MA..."** in CAPITALS, while canon-strings authors `storeWordmark` as the
        /// mixed-case *"The Night Market"*. The same frame proves the transform is not this
        /// card's: <see cref="AddDockTab"/> passes "Leaderboard"/"Music"/"Settings"/"Realm"/
        /// "Pause" and the capture paints MUSIC / SETTINGS / REALM / PAUSE - every obsidian
        /// button face in this HUD renders upper-case. HudLabelFitRegression [night-market-
        /// standout] 11c measured the MIXED-CASE string and reported the upper-case width as a
        /// NOTE, so the oracle could be green while the player saw a cut word. Both 11c and 12e
        /// now measure the upper-case form (the glyphs actually drawn), and the plate is widened
        /// to carry it: (0.97 - 0.20) * 320 * 0.92 = 226.7 ref px against 197.2 before.
        /// ⛔ The CARD is NOT widened (WO-1466 §3) and the authored copy is NOT retyped -
        /// canon-strings is the owner's call and a second copy of the name is forbidden
        /// (StoreNameSingleSourceRegression). Only the plate inside the card moves.</para></summary>
        private const float NightMarketLabelPlateX0 = 0.20f;
        /// <summary>WO-1662: the card title's font CEILING, reference px. Named rather than typed
        /// twice at the call site, because <see cref="HudLabelFitRegression"/> 11c/12e parse it out
        /// of this file to model the TWO-LINE block they now measure — a hand-copied 26 in the
        /// oracle would be the duplicated state CLAUDE.md documents four times over. The FLOOR is
        /// deliberately NOT named here: it is <c>ElarionUiKit.FontHardFloor</c>, and FitBlock clamps
        /// to it structurally (ElarionUiKitObsidian.cs:3083).</summary>
        private const float NightMarketLabelMaxPx = 26f;

        private void BuildNightMarketCard(Transform pool)
        {
            // WO-1384b TUNABLE: the three feel knobs come off the RemoteTunables rail HERE, the one
            // place the card is built, before alpha0 below samples the holder. Int() never throws
            // and answers the shipping default on every failure path (no row, offline, malformed),
            // so an unreachable server lands exactly the constants NightMarketGlowKnobs already
            // holds. The clamps are the ones AnimateNightMarketGlow applies (lap 1..60, alpha
            // 0..100, mask 0..7), so a wild row becomes the nearest legal value, never a frozen
            // or invisible ring; an empty mask is left to ResolveNightMarketPalette, which
            // resolves it to Gold alone and logs once. Guarded: a throw here must never cost the
            // store its HUD face. Traced ONCE per distinct triple (CLAUDE.md section 12).
            Guard.Try("Store", "read the Night Market glow knobs from the RemoteTunables rail", () =>
            {
                int lap = Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyHudNightMarketGlowLapSec), 1, 60);
                int alphaPct = Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyHudNightMarketGlowAlphaPct), 0, 100);
                int mask = Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyHudNightMarketGlowPaletteMask), 0, 7);
                NightMarketGlowKnobs.LapSec = lap;
                NightMarketGlowKnobs.AlphaPct = alphaPct;
                NightMarketGlowKnobs.PaletteMask = mask;
                FlowTrace.Once("Store", "nightmarket-glow-knobs:" + lap + ":" + alphaPct + ":" + mask,
                    "Night Market glow knobs from rail: lap=" + lap + " alpha=" + alphaPct + " mask=" + mask);
            });

            var root = new GameObject("NightMarketCard", typeof(RectTransform));
            root.transform.SetParent(pool, false);
            var rt = (RectTransform)root.transform;

            // Hung from the mount's TOP-LEFT at a FIXED reference size, which is the same shape
            // ResolveNightMarketCard resolves. ⚠ Fixed pixels, never a fraction of the mount: a
            // fraction changes its aspect with the device, and both the card art's 2.055:1 ratio
            // and the 112-unit touch floor are stated in pixels (HudLayoutBands' own header rule).
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(HudLayoutBands.NightMarketCardWidthPx,
                                       HudLayoutBands.NightMarketCardHeightPx);

            // WO-1384 LIGHT: the kit's radial bloom, gold-tinted, behind everything else on the
            // card. Built FIRST so it stays the bottom sibling. Null-checked exactly as the kit
            // demands (a null sprite would draw a white quad).
            var auraSprite = ElarionUiKit.RadialGlowSprite;
            if (auraSprite != null)
            {
                var aura = ElarionUiKit.AddImage(root.transform, "NightMarketCardAura",
                    new Vector2(-NightMarketAuraReachX, -NightMarketAuraReachY),
                    new Vector2(1f + NightMarketAuraReachX, 1f + NightMarketAuraReachY),
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, NightMarketAuraAlpha),
                    rounded: false);
                var auraImage = aura.GetComponent<Image>();
                auraImage.sprite = auraSprite;
                auraImage.type = Image.Type.Simple;
                auraImage.preserveAspect = false;
                auraImage.raycastTarget = false;
            }
            else
            {
                // Never silent (CLAUDE.md section 12): the frame + size still carry the standout.
                FlowTrace.Warn("Store", "HUD Night Market card: RadialGlowSprite is null - the card " +
                                        "ships with its gold frame but no aura this session.");
            }

            // WO-1384b RING: the kit's rounded 9-slice, one radius step larger than the card and
            // pushed NightMarketRingPx outside it. The masked art drawn later covers its middle,
            // so the visible remainder is a soft rounded band. Its colour drifts through the
            // palette from AnimateNightMarketGlow. AddImage(rounded:true) already applied the
            // sprite; ApplyRounded(img, radius) only moves the 9-slice scale.
            float alpha0 = Mathf.Clamp(NightMarketGlowKnobs.AlphaPct, 0f, 100f) * 0.01f;
            var ring = ElarionUiKit.AddImage(root.transform, "NightMarketCardRing",
                Vector2.zero, Vector2.one,
                new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, alpha0 * NightMarketRingAlphaScale),
                rounded: true);
            var ringRt = (RectTransform)ring.transform;
            ringRt.offsetMin = new Vector2(-NightMarketRingPx, -NightMarketRingPx);
            ringRt.offsetMax = new Vector2(NightMarketRingPx, NightMarketRingPx);
            _nightMarketRing = ring.GetComponent<Image>();
            _nightMarketRing.raycastTarget = false;
            if (_nightMarketRing.sprite != null)
                ElarionUiKit.ApplyRounded(_nightMarketRing, NightMarketCornerRadiusPx + NightMarketRingPx);
            else
                FlowTrace.Warn("Store", "HUD Night Market card: RoundedSprite is null - the ring and the " +
                                        "card corners are square this session (flat quads).");

            // WO-1384b CHASE: three soft blobs (head + two tail) on the card's perimeter. Built
            // BEFORE the button so the opaque art hides their inner half - a rim light, not a
            // spotlight. Sized in reference units, anchored to the card's top-left like the card
            // itself, positioned every frame by AnimateNightMarketGlow.
            if (auraSprite != null)
            {
                _nightMarketComets = new Image[NightMarketCometSizePx.Length];
                _nightMarketCometRts = new RectTransform[NightMarketCometSizePx.Length];
                for (int i = 0; i < NightMarketCometSizePx.Length; i++)
                {
                    var comet = ElarionUiKit.AddImage(root.transform, "NightMarketCardComet" + i,
                        new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b,
                                  alpha0 * NightMarketCometAlphaScale[i]),
                        rounded: false);
                    var crt = (RectTransform)comet.transform;
                    crt.pivot = new Vector2(0.5f, 0.5f);
                    crt.sizeDelta = new Vector2(NightMarketCometSizePx[i], NightMarketCometSizePx[i]);
                    crt.anchoredPosition = Vector2.zero;
                    var cimg = comet.GetComponent<Image>();
                    cimg.sprite = auraSprite;
                    cimg.type = Image.Type.Simple;
                    cimg.preserveAspect = false;
                    cimg.raycastTarget = false;
                    _nightMarketComets[i] = cimg;
                    _nightMarketCometRts[i] = crt;
                }
            }
            else
            {
                // The aura branch above already warned; the ring still carries the colour drift.
                _nightMarketComets = null;
                _nightMarketCometRts = null;
            }
            FlowTrace.Step("Store", "HUD Night Market card (WO-1384b): rounded r=" + NightMarketCornerRadiusPx +
                                    "px, ring " + NightMarketRingPx + "px, comets=" +
                                    (_nightMarketComets == null ? 0 : _nightMarketComets.Length) +
                                    ", lap=" + NightMarketGlowKnobs.LapSec + "s alpha=" +
                                    NightMarketGlowKnobs.AlphaPct + "% paletteMask=" + NightMarketGlowKnobs.PaletteMask);

            // WO-1398: the word is the store's OWN name from canon-strings (storeWordmark), the
            // same row PackStore titles itself with - never a literal typed here. Traced by
            // HudStrings.StoreFaceLabel ("store face label=... site=hud-card").
            var button = ElarionUiKit.BuildObsidianButton(root.transform, HudStrings.StoreFaceLabel("hud-card"),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                Vector2.zero, Vector2.one, OpenNightMarket);
            if (button == null)
            {
                FlowTrace.Warn("Store", "HUD Night Market card: the obsidian button factory returned " +
                                        "null - the permanent store face is absent this session.");
                Register("nightMarketCard", WrapAsWidget("nightMarketCard", root));
                return;
            }
            button.gameObject.name = "NightMarketCardButton";

            // The authored card face, exactly as the deck card loads it.
            var art = Resources.Load<Sprite>("UI/ElarionMedieval/cards/realm-store");
            var cardImage = button.GetComponent<Image>();
            if (art != null && cardImage != null)
            {
                // WO-1384b ROUNDING: the button's own Image becomes the rounded stencil. The
                // RoundIconMask precedent (ElarionUiKit.cs:3768): white, showMaskGraphic OFF, so
                // it paints nothing itself and clips every child (art, label plate) to the
                // rounded shape. raycastTarget stays TRUE - this Image is what catches the tap.
                // A null RoundedSprite leaves a square stencil (the ring branch above warned).
                cardImage.color = Color.white;
                ElarionUiKit.ApplyRounded(cardImage, NightMarketCornerRadiusPx);
                var mask = button.gameObject.GetComponent<Mask>();
                if (mask == null) mask = button.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                var surface = ElarionUiKit.AddImage(button.transform, "NightMarketCardSurface",
                    Vector2.zero, Vector2.one, Color.white, false);
                surface.transform.SetAsFirstSibling();
                var artImage = surface.GetComponent<Image>();
                artImage.sprite = art;
                artImage.type = Image.Type.Simple;
                artImage.preserveAspect = false;   // the band already carries the art's own aspect
                artImage.raycastTarget = false;
                button.targetGraphic = artImage;
                // An illustrated card is a complete surface: never SpriteSwap it to a blank face.
                button.transition = Selectable.Transition.ColorTint;
            }
            else if (art == null)
            {
                // Never silent (CLAUDE.md §12). The card still works as a worded button.
                FlowTrace.Warn("Store", "HUD Night Market card: UI/ElarionMedieval/cards/realm-store " +
                                        "did not load - falling back to the plain obsidian face.");
            }

            // ⛔ THE WORD, ON ITS OWN PLATE. The owner is red/green colourblind (CLAUDE.md §7), so
            // the card may never be identifiable by its artwork's hue alone. The word is the
            // store's own canon name (storeWordmark, WO-1398) - the same words the store titles
            // itself with - on a dark plate so it reads over any part of the illustration.
            var face = button.GetComponentInChildren<TMP_Text>(true);
            if (face != null)
            {
                // WO-1384: plate x0 widened (0.36 -> NightMarketLabelPlateX0) so "NIGHT MARKET"
                // measures inside it at the 20 px floor on one line - the captured "NIGHT MA..."
                // was the plate being 153 units wide for a word that needs more at any legible
                // size. Pinned by HudLabelFitRegression [night-market-standout], which MEASURES
                // the word's glyph advances against this plate.
                var plate = ElarionUiKit.AddImage(button.transform, "NightMarketCardLabelPlate",
                    new Vector2(NightMarketLabelPlateX0, 0.46f), new Vector2(0.97f, 0.92f),
                    new Color(0f, 0f, 0f, .66f), false);
                var plateImage = plate.GetComponent<Image>();
                if (plateImage != null) plateImage.raycastTarget = false;

                var faceRt = face.rectTransform;
                faceRt.SetParent(plate.transform, false);
                faceRt.anchorMin = new Vector2(0.04f, 0.02f);
                faceRt.anchorMax = new Vector2(0.96f, 0.98f);
                faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;
                face.alignment = TextAlignmentOptions.Center;
                face.color = ElarionUi.Gold;
                face.fontStyle = FontStyles.Bold;
                face.fontSize = NightMarketLabelMaxPx;
                // ⭐ WO-1662 — THE TITLE WRAPS TO TWO LINES. IT DID NOT FIT ON ONE, AT ANY ASPECT.
                // ---------------------------------------------------------------------
                // CAPTURED (Builds/wave7-capture1, HEAD a89603a7a, all THREE landscape targets and
                // all six HUD panel builds - the card is authored in fixed px, so the defect is
                // aspect-independent):
                //   [glyph-oracle] TEXT TRUNCATED [AdaptiveHudPeaceful_2670x1200] '.../
                //   NightMarketCardLabelPlate/Label' ("THE NIGHT MARKET") draws 12 of 14 printable
                //   glyphs. (x -976.5..-749.8 ...) at font 20 [autosize 20..26, enabled=True]
                //   overflow=Ellipsis
                // ⚠ font 20 IS the hard floor: autosize had already bottomed out on FontHardFloor
                // and ellipsised anyway. There was nothing left underneath, so a smaller font was
                // never available as a remedy.
                //
                // WHY ONE LINE COULD NOT WORK, in numbers (glyph advances summed from the committed
                // font assets exactly as MeasureLineWidthPx sums them; the label rect derived from
                // the authored fractions below is 226.7 x 68.9 ref px, which the capture's x/y bands
                // reproduce to the tenth - so this is a model of the render, not of itself):
                //   font_body   (Alata Regular)      "THE NIGHT MARKET" @20 = 177.6 px   <- what the
                //                                     two existing pins measured, and why they passed
                //   font_title  (Merriweather Bold)  same string  @20 = 214.7 px   <- what is DRAWN
                //   + characterSpacing 2                          ~= 221 px
                //   + TMP faux-bold (font_title.asset:2968 boldSpacing 7)  ~= 243 px  -> OVER by ~17
                // The drawn face is Title/bold/spaced because BuildObsidianButton ends on
                // MedievalUiSkin.ApplyButton (ElarionUiKitObsidian.cs:688), which sets
                // fontStyle |= Bold, characterSpacing = 2 and EnsureFont(FontRole.Title)
                // (MedievalUiSkin.cs:88-91). This slice overrides the SIZE afterwards; it never
                // resets the role or the spacing, and it must not - that is the shared button skin.
                //
                // TWO LINES, AT THE CEILING, WITH ROOM TO SPARE:
                //   band 68.9 px tall; two Title lines at 26 px need 2 x 26 x 1.2570 = 65.4 px
                //     (font_title m_LineHeight 80.448 / m_PointSize 64 = 1.2570)
                //   widest wrapped line "THE NIGHT" @26 = 154.5 px raw in a 226.7 px rect - ~30%
                //     margin, which absorbs the bold + spacing terms the one-line form could not.
                // So the word gets BIGGER (26, not the bottomed-out 20), which is also WO-1384's
                // owner ruling: "night market ... needs to be the shining gem ... it should stand
                // out". No font floor is lowered and no player-facing string is shortened - the
                // wordmark is canon storeWordmark (WO-1398), and shortening it is an owner ruling.
                //
                // ⚠ THIS REPLACES A ONE-LINE CONTRACT THAT WAS PINNED. HudLabelFitRegression 12e
                // required the kit's no-wrap single-line fitter here, verbatim, and 12e now pins
                // the OPPOSITE - so the two tokens it greps for are deliberately NOT spelled out
                // anywhere in this slice, not even in prose: a comment that happens to contain the
                // pinned string is indistinguishable from code to a source-text oracle.
                // That pin and 11c are
                // re-pointed in the SAME change (CLAUDE.md §15), onto the font actually drawn. The
                // one-line rule traced to the WO-1384b implementation, not to an owner sentence -
                // grep of WORK_ORDER_1384 finds no wrap ruling. FitBlock sets Normal wrapping +
                // Truncate itself and clamps minSize at FontHardFloor (ElarionUiKitObsidian.cs:3083),
                // so the floor is structural here rather than passed by hand.
                ElarionUiKit.FitBlock(face, ElarionUiKit.FontHardFloor, NightMarketLabelMaxPx);
            }

            Register("nightMarketCard", WrapAsWidget("nightMarketCard", root));
        }

        /// <summary>
        /// WO-1384b: one frame of the Night Market card's rim light. Reads the three knobs every
        /// call (so a tunables overwrite lands live), moves the three comets along the card's
        /// perimeter and drifts the ring + comet colours through the palette. PURE PRESENTATION:
        /// nothing here decides anything about the store. Unscaled time, like every other HUD
        /// animation in this class (timeScale must never freeze or speed the HUD chrome).
        /// Cost: 4 Image colour writes + 3 anchoredPosition writes per frame; the animator's own
        /// CPU is Stopwatch-sampled over the first NightMarketGlowSampleFrames frames and traced
        /// ONCE - the canvas rebuild those dirty graphics trigger is outside this sample and is
        /// what the device screenrecord + Player.log frame time judge.
        /// </summary>
        private void AnimateNightMarketGlow()
        {
            if (_nightMarketRing == null) return;
            if (!_nightMarketRing.gameObject.activeInHierarchy) return;   // occupancy hid the card

            bool sampling = _nightMarketGlowSampled < NightMarketGlowSampleFrames;
            if (sampling) s_nightMarketGlowWatch.Restart();

            float lap = Mathf.Clamp(NightMarketGlowKnobs.LapSec, 1f, 60f);
            float alpha = Mathf.Clamp(NightMarketGlowKnobs.AlphaPct, 0f, 100f) * 0.01f;
            float t = Mathf.Repeat(Time.unscaledTime / lap, 1f);

            // Ring: a slow drift, a third of a lap behind the comet head so the two never
            // read as one flat tint.
            var ringColor = NightMarketPaletteColor(t + 0.33f);
            ringColor.a = alpha * NightMarketRingAlphaScale;
            _nightMarketRing.color = ringColor;

            if (_nightMarketComets != null)
            {
                float w = HudLayoutBands.NightMarketCardWidthPx;
                float h = HudLayoutBands.NightMarketCardHeightPx;
                for (int i = 0; i < _nightMarketComets.Length; i++)
                {
                    var img = _nightMarketComets[i];
                    var crt = _nightMarketCometRts[i];
                    if (img == null || crt == null) continue;
                    float ti = Mathf.Repeat(t - NightMarketCometLag[i], 1f);
                    crt.anchoredPosition = NightMarketPerimeterPoint(ti, w, h);
                    var c = NightMarketPaletteColor(ti);
                    c.a = alpha * NightMarketCometAlphaScale[i];
                    img.color = c;
                }
            }

            if (sampling)
            {
                s_nightMarketGlowWatch.Stop();
                _nightMarketGlowTicks += s_nightMarketGlowWatch.ElapsedTicks;
                _nightMarketGlowSampled++;
                if (_nightMarketGlowSampled == NightMarketGlowSampleFrames)
                {
                    double msPerFrame = _nightMarketGlowTicks * 1000.0 /
                                        System.Diagnostics.Stopwatch.Frequency / NightMarketGlowSampleFrames;
                    string line = "aurora cost " + msPerFrame.ToString("F3", CultureInfo.InvariantCulture) +
                                  "ms/frame (sampled " + NightMarketGlowSampleFrames + " frames)";
                    FlowTrace.Once("Store", "aurora-cost", line);
                    if (msPerFrame > 1.0)
                        FlowTrace.Warn("Store", line + " - OVER the 1 ms/frame budget (WO-1384b)");
                }
            }
        }

        /// <summary>
        /// WO-1384b: a point on the card's perimeter, clockwise from the top-left, in the card
        /// root's local space (pivot top-left: x 0..w, y 0..-h). Corner rounding is ignored on
        /// purpose - an 18-unit radius under a 64-unit soft blob is invisible, and the straight
        /// path is four branches with no trig.
        /// </summary>
        private static Vector2 NightMarketPerimeterPoint(float t, float w, float h)
        {
            float p = 2f * (w + h);
            float s = Mathf.Repeat(t, 1f) * p;
            if (s < w)         return new Vector2(s, 0f);
            if (s < w + h)     return new Vector2(w, -(s - w));
            if (s < 2f * w + h) return new Vector2(w - (s - w - h), -h);
            return new Vector2(0f, -(p - s));
        }

        /// <summary>
        /// WO-1384b: the palette colour at lap fraction <paramref name="u"/>: the active stops
        /// (from NightMarketGlowKnobs.PaletteMask, in gold -> amber -> rose order) blended
        /// smoothly in a loop. An empty mask resolves to Gold, once-logged, never to nothing.
        /// </summary>
        private static Color NightMarketPaletteColor(float u)
        {
            var stops = s_nightMarketPalette;
            stops.Clear();
            int mask = NightMarketGlowKnobs.PaletteMask;
            if ((mask & NightMarketGlowKnobs.PaletteGold) != 0)  stops.Add(ElarionUi.Gold);
            if ((mask & NightMarketGlowKnobs.PaletteAmber) != 0) stops.Add(NightMarketAmber);
            if ((mask & NightMarketGlowKnobs.PaletteRose) != 0)  stops.Add(NightMarketRose);
            if (stops.Count == 0)
            {
                FlowTrace.Once("Store", "aurora-palette-empty",
                    "hud.nightMarketGlowPaletteMask=" + mask + " selects no palette stop - falling back to Gold");
                stops.Add(ElarionUi.Gold);
            }
            if (stops.Count == 1) return stops[0];
            float seg = Mathf.Repeat(u, 1f) * stops.Count;
            int i = Mathf.Min((int)seg, stops.Count - 1);
            float f = Mathf.SmoothStep(0f, 1f, seg - i);
            return Color.Lerp(stops[i], stops[(i + 1) % stops.Count], f);
        }

        /// <summary>
        /// The HUD Night Market card's command. Shaped after RealmStoreVendor.Open deliberately:
        /// PanelRouter.Open returns FALSE when no opener is registered, and an unchecked call would
        /// look to the player like the store is broken and to us like nothing happened.
        /// </summary>
        private void OpenNightMarket()
        {
            if (SwallowedByCloseGrace("Night Market card")) return;
            Guard.Try("Store", "open the Night Market from the HUD card", () =>
            {
                if (PanelRouter.Open(PanelId.RealmStore))
                    FlowTrace.Step("Store", "HUD Night Market card opened PanelId.RealmStore.");
                else
                    FlowTrace.Fail("Store",
                        "PanelRouter.Open(PanelId.RealmStore) returned FALSE from the HUD card - the " +
                        "PackStoreBootstrap opener is not registered in this scene.");
            });
        }

        // =====================================================================
        //  WO-1393 (2026-09-05) - THE CLOSE-FRAME GRACE, CONSULTED BY EVERY HUD TAP HANDLER.
        // ---------------------------------------------------------------------
        //  PROVEN (docs/qa/UI_REVIEW_2026-09-05/11-research-upgrade-door.png): a tap issued as
        //  Manage was closing landed on the Night Market card beneath it and opened the store -
        //  no "research locked door" line, a store instead. PanelManager now stamps the close
        //  frame (PanelManager.CloseGraceUntilFrame = Time.frameCount + 1 in NotifyClosed); the
        //  HUD - the layer UNDER every modal - drops any tap that arrives inside that window.
        //  One frame, one trace line, and only here: panels never consult it, so a tap on a
        //  panel that is still open is untouched. Pinned by ModalArbiterRegistrationRegression
        //  [close-frame-grace].
        // =====================================================================
        private static bool SwallowedByCloseGrace(string face)
        {
            if (!PanelManager.InCloseGrace) return false;
            FlowTrace.Step("HUD", "tap swallowed: panel closed this frame (grace) - " + face +
                " on frame " + Time.frameCount + ", grace until " + PanelManager.CloseGraceUntilFrame +
                " (WO-1393)");
            return true;
        }

        // Wire the compass' presentation-only world readers. DeNelle.HUD keeps its
        // "HUD -> Core only" edge, so the hero/seam/enemy transforms are resolved by
        // REFLECTION against the DeNelle.Village types (the same loose-reflection seam
        // HudKit already uses for the jukebox / DailyQuest bridges). The compass polls
        // these on a ~4 Hz throttle, so the FindObjects scans never hit the hot path.
        private static void WireCompassProviders(HudCompassWidget compass)
        {
            if (compass == null) return;
            var hero = MakeHeroProvider();
            compass.HeroProvider = hero;
            compass.ObjectiveProvider = MakeSeamObjectiveProvider(hero);
            compass.EnemyProvider = MakeEnemyProvider();
        }

        // WO-828: the minimap reads the SAME three world facts as the compass — where the
        // hero is, where the objective is, where the threats are — so it is wired from the
        // same three factories rather than from a second, drifting copy of the reflection.
        // Each widget gets its OWN closures (its own hero cache and its own enemy buffer):
        // sharing one buffer between two widgets polling on different timers is exactly how
        // one widget ends up reading a list the other is mid-rebuild.
        private static void WireMinimapProviders(HudMinimapWidget minimap)
        {
            if (minimap == null) return;
            var hero = MakeHeroProvider();
            minimap.HeroProvider = hero;
            minimap.ObjectiveProvider = MakeSeamObjectiveProvider(hero);
            minimap.EnemyProvider = MakeEnemyProvider();
        }

        // ── the three shared provider factories (loose reflection, HUD -> Core edge kept) ──
        // DeNelle.HUD may not reference DeNelle.Village (§5), so the Village types are
        // resolved by name. A null type is NOT an error here — it is the legitimate
        // "this scene has no Village assembly loaded" case, and every provider degrades
        // to "nothing to show" rather than throwing into the HUD.

        private static Func<Transform> MakeHeroProvider()
        {
            var heroT = Type.GetType("DeNelle.Village.HeroLocomotion, DeNelle.Village");
            Transform heroCache = null;
            return () =>
            {
                if ((heroCache == null || !heroCache) && heroT != null)
                {
                    var o = UnityEngine.Object.FindAnyObjectByType(heroT) as Component;
                    heroCache = o != null ? o.transform : null;
                }
                return heroCache;
            };
        }

        // Nearest region-gate seam crossing (HeroLinkCrossing markers) to the hero =
        // "where do I go" — points at the gate to leave town, and the way home in the open.
        private static Func<Vector3?> MakeSeamObjectiveProvider(Func<Transform> heroProvider)
        {
            var linkT = Type.GetType("DeNelle.Village.HeroLinkCrossing, DeNelle.Village");
            return () =>
            {
                if (linkT == null) return (Vector3?)null;
                var hero = heroProvider != null ? heroProvider() : null;
                if (hero == null || !hero) return (Vector3?)null;
                Vector3 hp = hero.position;
                float best = float.MaxValue; Vector3? bestPos = null;
                var found = UnityEngine.Object.FindObjectsByType(linkT, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var o in found)
                {
                    if (o is Component c && c != null)
                    {
                        float d = (c.transform.position - hp).sqrMagnitude;
                        if (d < best) { best = d; bestPos = c.transform.position; }
                    }
                }
                return bestPos;
            };
        }

        private static Func<IReadOnlyList<Transform>> MakeEnemyProvider()
        {
            var enemyT = Type.GetType("DeNelle.Village.Enemy, DeNelle.Village");
            var enemyBuf = new List<Transform>();
            return () =>
            {
                enemyBuf.Clear();
                if (enemyT == null) return enemyBuf;
                var found = UnityEngine.Object.FindObjectsByType(enemyT, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var o in found)
                    if (o is Component c && c != null) enemyBuf.Add(c.transform);
                return enemyBuf;
            };
        }

        // A stretch wrapper so every widget occupies its area mount uniformly.
        private GameObject WrapAsWidget(string id, GameObject content)
        {
            var wrap = new GameObject("Widget_" + id, typeof(RectTransform));
            wrap.transform.SetParent(transform, false);
            var rt = (RectTransform)wrap.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            content.transform.SetParent(wrap.transform, false);
            return wrap;
        }

        private void Register(string id, GameObject root)
        {
            _widgets[id] = root;
            root.SetActive(false);   // occupancy rows switch widgets on
        }

        // WO-835: capture a bar face for the model-driven render pass. The widget wrap
        // (occupancy-owned root) registers exactly as before; the INNER button starts
        // hidden — ApplyActionBar() activates + positions exactly the model's array
        // (the WO-826 Map inner-button precedent, generalized to every face).
        private void RegisterBarButton(ActionBarButtonId id, string widgetId, Button button)
        {
            _barButtons[(int)id] = button.gameObject;
            _barButtonRects[(int)id] = (RectTransform)button.transform;
            Register(widgetId, WrapAsWidget(widgetId, button.gameObject));
            button.gameObject.SetActive(false);
        }

        private void BuildWaveBlock(Transform pool)
        {
            // Labels + progress + Start Wave (all factory pieces).
            // WO-432: NO olive Panel() slab — a bare transparent container so the wave
            // labels/progress/button read cleanly against the scene (kill the block bg).
            _waveBlockRoot = new GameObject("WaveBlock", typeof(RectTransform), typeof(Image));
            _waveBlockRoot.transform.SetParent(pool, false);
            var wbrt = (RectTransform)_waveBlockRoot.transform;
            // ── WO-1144: THE WAVE BLOCK GETS ITS OWN BAND, HUNG BELOW THE STATUS CROWN ──
            //
            // PROVEN CAUSE (2026-08-22 headed fleet, break_24_error.png, all 8 runs): "Wave 1" and
            // "Next wave in 45s" were painted THROUGH the compass strip, with "Start Now" jammed
            // against its bottom edge. Nothing was mis-anchored. hud-areas.json puts BOTH
            // "compass" AND "waveBlock" in the calm(town) `status` area, this root stretched
            // 0..1 across that mount, and HudCompassWidget's strip occupies y 0.34-1.00 of the
            // SAME mount — so the two widgets were authored into one rect and the wave labels
            // (0.49-0.99) landed inside the strip by construction. Two live elements, one band.
            //
            // And the band cannot hold both: HudArea.Status is a HEIGHT FRACTION (0.845-0.990),
            // which is 278 ref px on the 1080x1920 portrait reference but collapses to ~140 ref
            // px in landscape (at 2670x1200 the scaler resolves the canvas to 2148x965). A
            // compass strip plus two label rows plus a bar plus a MinTouchPx(112) CTA has never
            // fitted in 140 px — which is also why the old Start Wave button resolved to ~46 ref
            // px tall, 66 px UNDER the touch floor, invisibly (ClampMinTouch no-ops pre-layout,
            // when rect.height is still 0).
            //
            // So: the compass keeps the Status mount to itself, and the wave stack hangs from the
            // mount's BOTTOM EDGE in FIXED REFERENCE PIXELS — disjoint from the crown by
            // construction, at every aspect, rather than by two fraction stacks agreeing. The
            // band it hangs into is free in calm(town): HudArea.TargetInfo (0.660-0.840) has no
            // occupants in that posture, and x stays inside the Status column (0.34-0.66), clear
            // of Vitals/HeartStatus/Minimap on the left and System/QueueStatus on the right.
            //
            // The stack inside it is LANDSCAPE-SHAPED (labels left, CTA right) because that is
            // where the room is: a vertical stack would need ~230 ref px of height it does not
            // have, while the Status column is 688 ref px WIDE at the capture aspect.
            wbrt.anchorMin = new Vector2(0f, 0f);
            wbrt.anchorMax = new Vector2(1f, 0f);
            wbrt.pivot = new Vector2(0.5f, 1f);          // top edge pinned to the mount's bottom edge
            wbrt.sizeDelta = new Vector2(0f, WaveBandHeightPx);
            wbrt.anchoredPosition = Vector2.zero;
            var wavePlate = _waveBlockRoot.GetComponent<Image>();
            var wavePlateSprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (wavePlateSprite != null)
            {
                wavePlate.sprite = wavePlateSprite;
                wavePlate.type = Image.Type.Simple;
                wavePlate.preserveAspect = false;
                wavePlate.color = Color.white;
            }
            else wavePlate.color = new Color(0.03f, 0.035f, 0.045f, 0.94f);
            wavePlate.raycastTarget = false;
            // Labels + progress occupy the LEFT ~58% of the band; the CTA owns the right ~37%.
            // (F8 2026-07-08 lesson kept: every label band below is tall enough to seat its line —
            // the guard FAIL that started this stack was "0 visible glyphs, rect 333x25".)
            _waveLabel = ElarionUiKit.Label(_waveBlockRoot.transform, "", 0.50f, 0.96f,
                ElarionUi.Parchment, ElarionUi.FontHead, TextAlignmentOptions.Center, 0.02f, 0.60f, bold: true);
            _waveLabel.enableAutoSizing = true;
            _waveLabel.fontSizeMin = 22f;
            _waveLabel.fontSizeMax = 30f;
            _waveCountdown = ElarionUiKit.Label(_waveBlockRoot.transform, "", 0.16f, 0.48f,
                ElarionUi.Gilt, ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.02f, 0.60f, bold: true);
            _waveCountdown.enableAutoSizing = true;
            _waveCountdown.fontSizeMin = 18f;
            _waveCountdown.fontSizeMax = 24f;
            _waveProgress = ElarionUiKit.BuildObsidianBar(_waveBlockRoot.transform,
                ElarionUiKit.ObsidianBarKind.Stat, new Vector2(0.03f, 0.08f), new Vector2(0.59f, 0.18f),
                withValue: false, framed: false);
            // y 0.03-0.93 of the 128 px band == 115 ref px, clear of ElarionUiKit.MinTouchPx (112),
            // so the CTA is authored ABOVE the floor rather than relying on ClampMinTouch to rescue
            // it after layout (it cannot: rect.height is still 0 when the button is built, which is
            // exactly how the old ~46 px Start Wave button shipped un-flagged).
            _startWaveButton = ElarionUiKit.BuildObsidianButton(_waveBlockRoot.transform, "Start Wave",
                ElarionUiKit.ObsidianButtonStyle.Style2, ElarionUiKit.ObsidianButtonColor.Green,
                new Vector2(0.63f, 0.03f), new Vector2(1.00f, 0.93f),
                () => { if (_owner != null) _owner.StartWaveRequested?.Invoke(); });
            MedievalUiSkin.ApplyButton(_startWaveButton, primary: true);
            var startWaveLabel = _startWaveButton != null
                ? _startWaveButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (startWaveLabel != null)
            {
                startWaveLabel.fontSizeMin = 20f;
                startWaveLabel.fontSizeMax = 30f;
            }
            // Carry-over (WO-T2 working-tree intent): the tutorial spotlight target.
            TutorialHighlightRegistry.Register("hud.wave_button", (RectTransform)_startWaveButton.transform);
            _startWaveButton.gameObject.SetActive(false);
            Register("waveBlock", WrapAsWidget("waveBlock", _waveBlockRoot));
        }

        // =====================================================================
        // THE RIGHT RAIL — ONE COLLAPSED CHIP STYLE, THREE INSTANCES
        // ---------------------------------------------------------------------
        // Owner ruling 2026-08-05 (verbatim at the _railOpen field). The device
        // review (docs/qa/UI_REVIEW_2026-08-05_seeker.md, findings 11 + 13 + P2
        // "three different right edges") measured three DIFFERENT treatments on
        // one column: Builders drew as a large permanently-expanded gold-bordered
        // panel, Echoes as a small dark chip, Resources as BOTH a chip AND a
        // headered panel that repeated the word and overlapped the chip.
        //
        // The fix is a single authored chip: same height (AT the touch floor),
        // same width, same Style1/Gray obsidian face, same right edge, collapsed
        // by default; the expanded section hangs below it and only ONE section is
        // ever open (SetRailSection). Every number below is FIXED REFERENCE PIXELS,
        // never a fraction of the parent band — the WO-841/WO-852 defect class is a
        // sub-MinTouchPx fraction band that ClampMinTouch then grows symmetrically
        // about its centre, closing the gap to its neighbour.
        //
        // The width + gutter deliberately MATCH the third chip, which this class
        // does not own: EchoUnlockFeedback.cs:56-60 authors EchoChipWidthPx = 220
        // and insets it ElarionUi.PadPanel * 3 (54 ref px) from the SCREEN edge.
        // HudRailGutter (bottom of this file) reproduces that screen-relative inset
        // for chips whose parent is an AREA mount, so all three share one edge.
        /// <summary>Collapsed chip height — authored AT the tap floor, so ClampMinTouch
        /// has nothing to grow (growth is what pushed WO-868's chip off-screen).</summary>
        private const float RailChipHeightPx = ElarionUiKit.MinTouchPx;   // 112
        /// <summary>Collapsed chip width — == EchoUnlockFeedback.EchoChipWidthPx.</summary>
        private const float RailChipWidthPx = 220f;
        // ── THE PLATE IS NOT THE BAND (WO-1642 item B, owner 2026-09-10: the town chrome
        //    "feels incomplete and not polished") ───────────────────────────────────────────
        // MedievalUiSkin.ApplyButton paints the kit plate with Image.Type.Simple
        // (MedievalUiSkin.cs:70), so the 2172x724 sprite STRETCHES across the whole chip band —
        // and that sprite's own ink occupies only the upper-middle 59% of its height. Measured off
        // Assets/Resources/UI/ElarionMedieval/buttons/button-normal-empty.png (alpha > 32):
        // rows 105..533 of 724 = 0.145..0.736 of the band, its dark core 0.174..0.709. Confirmed
        // against the shipped frame Builds/device-frames/2026-09-10_0602_town.png: the Echoes chip
        // is authored at band centre 0.475 of a 1200-px-tall screen (device band 560.4..699.6) and
        // its plate's dark core measures rows 585..658 there, against 584.6..659.1 predicted.
        //
        // ⛔ SO A LABEL ANCHORED TO THE BAND IS NOT ON THE PLATE. BuildObsidianButton's label call
        // (ElarionUiKitObsidian.cs:683) reads Label(parent, text, y0: 0f, y1: 1f, …, x0: 0.04f,
        // x1: 0.96f) — ⚠ THE 0.04/0.96 PAIR IS x, NOT y (ElarionUiKit.cs:1905-1907 declares
        // y0/y1 BEFORE the optional x0/x1), which is also where DefenseReportLayoutRegression's
        // 0.92 label inset comes from. So the label spans the FULL 112 px band vertically —
        // 16.2 px ABOVE the plate's top edge and 29.6 px BELOW its bottom one. A one-word chip
        // never notices. The three-line "ATTACK / REPORT / HELD" in that same frame does:
        // FitBlock filled the 112 px band (3 lines at ~27 px) and the first and last lines painted
        // onto the town wall at either end of the plate, the harness's own RULE 1 class
        // (UICaptureLaunch.cs:5851-5872). It was never a collision with the Echoes chip.
        private const float RailPlateInkTopFrac = 0.145f;
        private const float RailPlateInkBottomFrac = 0.736f;
        /// <summary>Breathing room inside the plate's ornate rim, reference px. Small on purpose:
        /// the band it leaves must still seat TWO lines at the chip's 22 px fit floor.</summary>
        private const float RailPlateInsetPx = 3f;
        /// <summary>Gap between a chip and its expanded section. WO-1435 made it `internal`
        /// (was `private`) so <see cref="HudRailClearance"/> holds the SAME gap when it seats a
        /// chip below a panel — a second "6f" typed into the clearance component would be the
        /// duplicated state this whole ticket is about.</summary>
        internal const float RailGapPx = 6f;
        /// <summary>Expanded-section width. Shares the chip's right edge, grows LEFT.</summary>
        private const float RailPanelWidthPx = 420f;
        /// <summary>THE shared rail gutter: distance from the SCREEN's right edge to every
        /// chip and every expanded panel. == the Echoes chip's authored inset, so the one
        /// rail element this class cannot edit lands on the same edge.</summary>
        internal const float RailGutterPx = ElarionUi.PadPanel * 3f;   // 54
        // Expanded resource rows hang BELOW the gold chip (owner mockup WO-1221). They are
        // display-only — the gold chip is the one tap target and is AUTHORED at
        // RailChipHeightPx (WO-1660, see BuildResourceChips). ⚠ THIS LINE READ "stays >=
        // MinTouchPx via ClampMinTouch" until 2026-09-10, and that was the defect wearing the
        // clothes of a design note: relying on the clamp is what let the band ship 8.5 px
        // under the floor for eight months. 4×MinTouchPx cannot physically fit under ActionRail
        // top on the captured 2670x1200 Seeker (~326 ref px to the screen bottom), so the rows
        // match the gold chip's WIDTH and use a compact readable height. 56×4 + 5×3 = 239
        // ref px, which seats under the 112 px gold chip without dropping Wood/Iron
        // off the bottom.
        private const float ResRowHeightPx = 56f;
        private const float ResRowGapPx = 5f;
        /// <summary>WO-1221: height of the collapsed chip's "+N more" hint tag, reference px.
        /// ⛔ THIS BAND MUST SEAT THE 30 px FontFloor LINE — DO NOT LOWER IT (WO-1658).
        /// It was 26f, and the device measured exactly that: 2026-09-10_0943_363722_logcat.txt
        /// reports `TextFitGuard '+4' [.../CurrencyChip_Gold/Label]: rect 398x26 lineFactor 1.15
        /// — floor 30 -> 21 (0 post-check iterations), fontSize now 23`. The always-on HUD rail's
        /// hint was shipping at 23 px, 7 px under the owner's legibility floor, and no capture
        /// could ever show it (headless captures never enter Play mode, so the guard never runs
        /// and every gated PNG rendered it at 30 — WO-1652's subject).
        /// THE ARITHMETIC, so a later edit can re-derive it instead of copying this number:
        /// the guard relaxes when `floor(h / lineFactor) - 1 &lt; FontFloor`, so a band is legal
        /// only at `h >= (FontFloor + 1) * lineFactor` = 31 * 1.1499 = 35.65 px. lineFactor is
        /// the FONT's own `faceInfo.lineHeight / pointSize` — Assets/Resources/Localization/
        /// Fonts/ElarionLocaleFallback.asset carries 73.59375 / 64 = 1.1499, which is the 1.15
        /// the device logged. 38f is that threshold plus the WO-1658 minBand margin
        /// ((FontFloor + 1) * lineFactor + 2 = 37.65).
        /// ⚠ AND THE OLD "grew rect 26px -> 26px" LINE IS NOT A DRIVEN-RECT FAILURE. This rect is
        /// sizeDelta-driven (anchorMin.y == anchorMax.y below), so the guard's offset write DID
        /// take: its minBand uses FontHardFloor and lands at (20+1)*1.1499+2 = 26.148, a deficit
        /// of 0.148 px that `(int)` truncates back to 26 in the message. The grow worked; it was
        /// aiming at the HARD floor, which is not the goal.
        /// Growing downward is safe: the hint is SetActive(false) whenever the expanded stack is
        /// open (see SetResourcePanelOpen), so the extra extent never overlaps ResRow_0.</summary>
        private const float ResHintHeightPx = 38f;

        // WO-778: the always-visible CoC-style Builders chip — busy count, tap opens
        // the WORK QUEUE. Player copy: "Builders"/"Training" — never "Obsidian".
        // ASCII-only, text-encoded state (never colour-only).
        // WO-864: the expanded section is a CoC-style CARD RAIL (QueueRailView).
        // 2026-08-05: that rail is now COLLAPSED BY DEFAULT — the owner keeps the
        // panel she likes, it just starts minimized like its two neighbours.
        private void BuildQueueStatusChip(Transform pool)
        {
            var root = new GameObject("QueueStatusChip", typeof(RectTransform));
            root.transform.SetParent(pool, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            // THE shared collapsed chip (identical build to the Resources chip below).
            // ⭐ WO-1435: this 0f is a RESTING BASE, not a position — same treatment as the
            // Collectors chip, for the same reason (it shares the QueueStatus band that sits under
            // the variable-height resource panel). ⚠ THIS CHIP CANNOT COLLIDE TODAY, and the proof
            // is at source, not an inference: the call site is commented out at
            // `// BuildQueueStatusChip(pool);` in Build(), so nothing here ever runs, and
            // SessionShapeRegression Case7_OneDoor FAILS the build if that retirement line
            // disappears. ⚠ WO-1667a: that was NOT TRUE until 2026-09-10 — Case7 searched for the
            // SHORT form, which THIS VERY COMMENT also contains, so the prose satisfied the pin and
            // the line could be replaced by a live call while the gate stayed green. It is anchored
            // on the FULL retirement text now. It is wired anyway because the owner ruling that retired it also
            // said the chip is "two lines from returning" — un-retiring it must not silently
            // re-ship this defect, nor land it on top of the Collectors chip that took its band.
            RectTransform buildersBand;
            var chip = BuildRailChip(rrt, "BuildersChip", "Builders", 0f,
                                     OnBuildersChipTapped, out buildersBand);
            if (buildersBand != null)
            {
                _buildersClearance = buildersBand.gameObject.AddComponent<HudRailClearance>();
                _buildersClearance.BaseYFromTopPx = 0f;
                _buildersClearance.AddSource(_resGoldOnly != null && _resGoldOnly.root != null
                    ? (RectTransform)_resGoldOnly.root.transform : null);
                // Build order between the two chips is not fixed, so each registers with whichever
                // of the pair already exists (see the mirror of this in BuildCollectorsChip).
                if (_collectorsClearance != null)
                    _buildersClearance.AddSource((RectTransform)_collectorsClearance.transform);
            }
            // Wrap + bounded auto-size come from BuildRailChip (see the note there) — the chip
            // reports the SAME string it always did (FormatQueueChip), it just no longer has to
            // ellipsize the Train count off the end of a narrower face.
            _queueChipLabel = chip != null ? chip.GetComponentInChildren<TMP_Text>(true) : null;
            // WO-1012 P3 (TIMERS beat): the FTUE spotlights this chip for its one line on
            // build timers — same registry contract as hud.build_button (line ~490 above).
            if (chip != null)
                TutorialHighlightRegistry.Register("hud.builders_chip", (RectTransform)chip.transform);

            // The Builder card rail. The SHARED component (DeNelle.Core.UI.QueueRailView) —
            // the Work Queue modal hosts the very same one, so the two surfaces can never
            // show a different queue visual. This host supplies only the mount; the rail
            // owns its own chrome, card anatomy and cheap tick. Height comes from the
            // component (HeightOf), never a guessed literal, so the section can never
            // reserve more band than the cards occupy.
            _queueRailMount = RailBand(rrt, "QueueRailMount",
                RailChipHeightPx + RailGapPx,
                QueueRailView.HeightOf(QueueRailView.Options.Default),
                RailPanelWidthPx);
            _queueRail = QueueRailView.Build(_queueRailMount, DeNelle.Core.Jobs.ChannelId.Builder,
                QueueRailView.Options.Default);
            _queueRailMount.gameObject.SetActive(false);   // collapsed by default

            Register("queueStatusChip", WrapAsWidget("queueStatusChip", root));
        }

        // ⚠ WO-911 (ruling Q10, 2026-08-06) — THE CHIP IS NO LONGER A DOOR.
        // ---------------------------------------------------------------------
        // The 2026-08-01 rule was "there is exactly ONE Queues entry". That rule is intact; what
        // changed is WHICH entry. The bar's re-pointed Manage face is now the single door, so the
        // chip SURVIVES as a STATUS GLANCE ONLY (count + timer + the inline peek rail) and its
        // second tap no longer raises ObsidianQueueGate.RequestToggle. Leaving both would give the
        // player two doors and the "one Queues entry" rule nothing left to mean.
        //
        // This also retires B4: the only way into the queue used to be an undiscoverable
        // DOUBLE-TAP on a status chip (ObsidianQueueHud.OpenWorkQueue had zero live callers).
        //
        // The chip's own oracle row (queueStatusChip in hud-areas.json) is unaffected.
        private void OnBuildersChipTapped()
        {
            if (SwallowedByCloseGrace("Builders chip")) return;   // WO-1393
            // Plain toggle: tap to peek the inline card rail, tap again to collapse it.
            if (_railOpen == RailSection.Builders)
            {
                SetRailSection(RailSection.None);
                FlowTrace.Step("HudKit", "Builders chip collapsed (status glance only — the Manage bar face is the door).");
                return;
            }
            SetRailSection(RailSection.Builders);
        }

        // =====================================================================
        //  WO-900 §4 — THE AMBIENT COLLECTOR CHIP
        // =====================================================================
        // "We need to somehow convey to the player when capacity is full" (owner, 2026-08-04).
        // §3 delivered the DIEGETIC tell on the building (CollectorStackView: the pile, the
        // near-full band, the "N/20", the "!"). This is the AMBIENT half: a right-column glance
        // that answers the same question from across town, with no modal open.
        //
        // It reuses the shared rail chip (BuildRailChip -> ElarionUiKit obsidian face), so it
        // inherits the MinTouchPx (112) floor and matches the Resources chip it sits beside.
        // The tap is the EXISTING command: CollectorStatusGate.RequestCollectAll() carries it to
        // Village, which answers with ResourceCollectorService.CollectAll(). No new collect verb.
        //
        // ⚠ COPY LAW: this chip says "Collectors", never "Storage" — "Storage"/"Bank"/current-max
        // is the WALLET's word (WO-857), and the player must never meet two different notions of
        // "full" on one screen.
        private void BuildCollectorsChip(Transform pool)
        {
            var root = new GameObject("CollectorsChip", typeof(RectTransform));
            root.transform.SetParent(pool, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            // ⭐ WO-1435 — THIS CHIP'S y IS DERIVED, AND THE 0f IS A RESTING BASE, NOT A POSITION.
            // The QueueStatus band sits directly under the ActionRail band that hosts the resource
            // panel, and that panel's height is `kinds.Length * ResRowHeightPx + ...` — a FUNCTION
            // of its row count, growing downward. Pinned at a constant 0f, this chip landed inside
            // it: at the owner's 2670x1200 the four-row panel occupies ~143..382 ref px from the
            // screen top and the chip occupied 241..353, i.e. entirely within it, burying the STONE
            // row's number (owner felt-test 2026-09-06, build 2026.09.06.358161). ⛔ Do NOT replace
            // this 0f with a bigger literal that happens to clear four rows — see HudRailClearance
            // for why a second hand-maintained number is the same bug one resource later.
            RectTransform collectorsBand;
            var chip = BuildRailChip(rrt, "CollectorsChip", HudStrings.Get(HudStrings.KeyCollectorsTitle), 0f,
                                     OnCollectorsChipTapped, out collectorsBand);
            var clearance = collectorsBand != null
                ? collectorsBand.gameObject.AddComponent<HudRailClearance>() : null;
            if (clearance != null)
            {
                clearance.BaseYFromTopPx = 0f;
                if (_resGoldOnly != null && _resGoldOnly.root != null)
                    clearance.AddSource((RectTransform)_resGoldOnly.root.transform);
                else
                    FlowTrace.Warn("HudKit", "collectors chip clearance has NO source - the gold " +
                                             "resource chip did not build, so the Harvest chip will " +
                                             "rest at its base offset and cannot know where the " +
                                             "expanded panel ends (WO-1435).");
                _collectorsClearance = clearance;
                // A dormant Builders chip (retired 2026-08-07, call site commented out) would share
                // this exact band, so if it is ever un-retired it must clear THIS chip too. Wiring
                // it here rather than at its build site keeps the two build orders independent.
                if (_buildersClearance != null) _buildersClearance.AddSource((RectTransform)clearance.transform);
            }
            else
            {
                FlowTrace.Warn("HudKit", "collectors chip built with NO clearance band - its y is " +
                                         "back to a constant and it can sit on top of the expanded " +
                                         "resource panel (WO-1435).");
            }
            _collectorsChipLabel = chip != null ? chip.GetComponentInChildren<TMP_Text>(true) : null;
            if (_collectorsChipLabel == null)
                FlowTrace.Warn("HudKit", "collectors chip built without a label - the ambient collector " +
                                         "tell will show nothing (the kit returned no button/text).");

            Register("collectorsChip", WrapAsWidget("collectorsChip", root));
            BindLocalizedHudCopy();
        }

        private void OnCollectorsChipTapped()
        {
            if (SwallowedByCloseGrace("Harvest chip")) return;   // WO-1393
            if (!CollectorStatusGate.HasSubscriber)
            {
                // A boot race (tapped before the Village publisher installs) must not read as a
                // broken button — say so in the trace rather than swallowing the tap silently.
                FlowTrace.Warn("HudKit", "collectors chip tapped with NO Village listener - " +
                                         "CollectorStatusPublisher has not installed yet.");
                return;
            }
            FlowTrace.Step("HudKit", "collectors chip tapped -> CollectorStatusGate.RequestCollectAll");
            CollectorStatusGate.RequestCollectAll();
        }

        // =====================================================================
        //  WO-1515 sec.2B/2D - THE ATTACK REPORT CHIP (owner ruling 2026-09-06 20:05)
        // =====================================================================
        // "the only way to get to the defense report is buried under settings then realm.
        //  should be on screen as a button if there is a report that is incoming"
        //
        // It is the SAME BuildRailChip the Builders and Collectors chips use, so it inherits
        // the 220x112 face, the MinTouchPx floor and the shared rail gutter rather than
        // authoring a fourth geometry. What is different is that it is CONDITIONAL: the band
        // is inactive whenever DefenseReportChipModel says Visible is false, because WO-1515
        // sec.3 forbids a permanent chip ("a fifth status glance competing with the four that
        // earn their place"). Not greyed, not empty-stated - absent.
        //
        // STOP: THE VIEW DECIDES NOTHING. Visibility and the words both come from
        // DeNelle.Core.HudModel.DefenseReportChipModel; this method wires a face and a tap.
        // Settings -> Realm stays the ARCHIVE door (SettingsController.cs:748) - this is a
        // second CALLER of PanelId.DefenseReport, never a second screen.
        private void BuildDefenseReportChip(Transform pool)
        {
            var root = new GameObject("DefenseReportChip", typeof(RectTransform));
            root.transform.SetParent(pool, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            // NOTE WO-1435 applies here too: the 0f is a RESTING BASE, not a position. This chip
            // shares the QueueStatus band with the Collectors chip, which itself sits under the
            // variable-height resource panel - so its y is DERIVED by HudRailClearance from the
            // rects above it. A literal that happens to clear today's four resource rows is the
            // same bug one resource later.
            RectTransform band;
            var chip = BuildRailChip(rrt, "DefenseReportChip",
                                     DefenseReportChipModel.TitleLine, 0f,
                                     OnDefenseReportChipTapped, out band);
            _defenseChipBand = band;

            if (band != null)
            {
                var clearance = band.gameObject.AddComponent<HudRailClearance>();
                clearance.BaseYFromTopPx = 0f;
                if (_resGoldOnly != null && _resGoldOnly.root != null)
                    clearance.AddSource((RectTransform)_resGoldOnly.root.transform);
                // Stack under the Collectors chip that already holds this band, and under the
                // Builders chip if it is ever un-retired. Registering with whichever of the pair
                // exists keeps the three build orders independent (the same mirror the other two
                // chips keep with each other).
                if (_collectorsClearance != null) clearance.AddSource((RectTransform)_collectorsClearance.transform);
                if (_buildersClearance != null) clearance.AddSource((RectTransform)_buildersClearance.transform);
                band.gameObject.SetActive(false);   // conditional: the poll turns it on
            }
            else
            {
                FlowTrace.Warn("HudKit", "WO-1515: the attack report chip built with NO band - the " +
                                         "unread-report door cannot be shown or hidden, so the defence " +
                                         "report is back to Settings -> Realm only.");
            }

            _defenseChipLabel = chip != null ? chip.GetComponentInChildren<TMP_Text>(true) : null;
            if (_defenseChipLabel != null)
            {
                // TWO LINES, and the fit has to change with them. BuildRailChip arms
                // FitSingleLine (NoWrap + Ellipsis), which is a WIDTH fit: a hard "\n" survives
                // NoWrap and the label never shrinks to make its second line fit - that is
                // verbatim the RCA of this same WO's list-row overlap. "ATTACK REPORT BREACHED"
                // cannot seat on one ~202px line above the kit's legibility floor either, and
                // the half that would ellipsize away is the OUTCOME WORD, which is the only part
                // that survives greyscale. So: FitBlock (wrap + bounded auto-size + truncate).
                //
                // WO-1642: ...but fitted to THE PLATE, not to the band. SeatOnRailPlate must run
                // BEFORE FitBlock - TMP auto-sizes against the rect it has, so a label re-seated
                // afterwards keeps a size chosen for the old, taller box. The copy is untouched:
                // DefenseReportChipModel still composes "ATTACK REPORT\nHELD" and nothing here
                // shortens a player-facing string.
                SeatOnRailPlate(_defenseChipLabel, "defenseReportChip");
                ElarionUiKit.FitBlock(_defenseChipLabel, 22f, 30f);
            }
            else
            {
                FlowTrace.Warn("HudKit", "WO-1515: the attack report chip built without a label - " +
                                         "the outcome word cannot be painted (the kit returned no button/text).");
            }

            // Reset the repaint key with the widgets. Without this a HUD rebuild would leave the
            // band inactive while the key still matched the live model, and the tick would early
            // out forever - an unread report with no door, which is the whole defect.
            _defenseChipKey = -1;
            _defenseChipPollTimer = DefenseChipPollSeconds;   // evaluate on the first tick, not in 0.5s

            Register("defenseReportChip", WrapAsWidget("defenseReportChip", root));
        }

        /// <summary>The door. PanelRouter.Open returns FALSE when nothing registered the panel,
        /// and an unchecked call would leave the player tapping a chip that does nothing - say
        /// so in the trace rather than swallowing it (the Night Market card precedent).</summary>
        private void OnDefenseReportChipTapped()
        {
            if (SwallowedByCloseGrace("Attack report chip")) return;   // WO-1393
            if (!PanelRouter.Open(PanelId.DefenseReport))
            {
                FlowTrace.Warn("HudKit", "WO-1515: attack report chip tapped but PanelId.DefenseReport " +
                                         "has no opener registered - DefenseReportPanel is not in this " +
                                         "scene, so the only remaining door is Settings -> Realm.");
                return;
            }
            FlowTrace.Step("HudKit", "attack report chip tapped -> PanelRouter.Open(DefenseReport).");
        }

        /// <summary>
        /// The conditional repaint. THROTTLED (see DefenseChipPollSeconds) and change-detected
        /// on the model's own Key PLUS the resource-panel state, so a town frame costs one float
        /// compare in the common case. PURE PRESENTATION: every branch below reads a field of the
        /// snapshot or a HUD-local view flag.
        /// <para>⭐ WO-1670b — OWNER RULING 2026-09-10 12:55: *hide the ATTACK REPORT chip while
        /// the resource panel is expanded; it returns on collapse.* WHY IT IS DECIDED HERE AND
        /// NOWHERE ELSE: this method holds the ONLY <c>SetActive</c> on <c>_defenseChipBand</c> in
        /// the whole codebase, and WO-1515's own header says why that matters ("a second object is
        /// how a widget ends up permanently off in exactly one posture"). A hide written from
        /// <see cref="SetResourcePanelOpen"/> would be a SECOND writer racing this one on the next
        /// throttled tick — the chip would flicker back on 0.5 s later. So the panel state becomes
        /// an INPUT to this decision; SetResourcePanelOpen only RINGS the tick, exactly as it
        /// already rings HudRailClearance.MarkDirty().</para>
        /// <para>⚠ The chip is HIDDEN, not moved. WO-1670 measured the alternative: with the panel
        /// open, HudRailClearance derives this chip DOWN out of the QueueStatus mount to
        /// y 0.4667..0.3507 at four resource rows (0.2244 at six), straight through the Echoes
        /// chip's band. No authored fraction can fix that — the depth is a runtime function of
        /// kinds.Length — which is why the ruling is visibility, not geometry.</para>
        /// </summary>
        private void TickDefenseReportChip()
        {
            if (_defenseChipBand == null && _defenseChipLabel == null) return;

            _defenseChipPollTimer += Time.unscaledDeltaTime;
            if (_defenseChipPollTimer < DefenseChipPollSeconds) return;
            _defenseChipPollTimer = 0f;

            var snap = DefenseReportChipModel.Current;
            bool panelOpen = _resChipsExpanded;
            // Both inputs guard the early-out. The model's Key does NOT move when the player
            // toggles the resource panel, so keying on it alone would never re-evaluate.
            if (snap.Key == _defenseChipKey && panelOpen == _defenseChipPanelWasOpen) return;
            bool panelEdge = panelOpen != _defenseChipPanelWasOpen;
            _defenseChipKey = snap.Key;
            _defenseChipPanelWasOpen = panelOpen;

            // The model still owns WHETHER there is a report to show; the panel only SUPPRESSES
            // an otherwise-visible chip. snap.Visible is never overwritten, so a report that
            // lands while the panel is open is still unread and still appears on collapse.
            bool wantVisible = snap.Visible && !panelOpen;

            if (_defenseChipLabel != null && wantVisible) _defenseChipLabel.text = snap.Caption;
            if (_defenseChipBand != null && _defenseChipBand.gameObject.activeSelf != wantVisible)
                _defenseChipBand.gameObject.SetActive(wantVisible);

            if (panelEdge)
                FlowTrace.Step("HudKit", "WO-1670b attack report chip " +
                                         (panelOpen ? "HIDDEN" : "SHOWN") +
                                         " on the resource panel " + (panelOpen ? "EXPAND" : "COLLAPSE") +
                                         " edge (owner ruling 12:55) - model says visible=" + snap.Visible +
                                         ", so on screen=" + wantVisible + ". The report is not consumed; " +
                                         "it returns on collapse while it is still unread.");

            FlowTrace.Step("HudKit", "WO-1515 attack report chip: visible=" + snap.Visible +
                                     " onScreen=" + wantVisible + " panelOpen=" + panelOpen +
                                     " unread=" + snap.UnreadCount +
                                     " face='" + (snap.Caption ?? string.Empty).Replace("\n", " / ") + "'.");
        }

        /// <summary>
        /// "Collectors 2/3 full" + the action line. TEXT-ENCODED STATE ONLY — the owner is
        /// red/green colourblind, so the chip never leans on a tint to say "full"; the count and
        /// the word carry it. Two short lines, matching the sibling rail chip (the chip is
        /// MinTouchPx tall, so the second line costs nothing and survives a narrow face far
        /// better than one wrapped line would).
        /// Never the word "Storage" (WO-900 §4 copy law).
        ///
        /// WO-1144 — THE WORDS MOVED TO canon-strings.json AND GOT SHORTER, and the reason is a
        /// measurement, not taste. The 2026-08-22 headed fleet captured this chip reading
        /// "Tap to collec" — a word sliced mid-glyph, in all 8 runs. The chip is 220 ref px wide
        /// by law (== EchoUnlockFeedback.EchoChipWidthPx; three rail chips share one right edge),
        /// so its label rect is ~202 ref px, and "Tap to collect" measures ~214 ref px at
        /// ElarionUiKit.FontFloor (30). It could not fit at ANY legible size, and the old 85 %
        /// branch ("85% - tap to collect", 20 chars) was worse. Line 1 already wraps to two lines
        /// inside the 112 px chip, so the action line has exactly ONE line of ~202 px to live in.
        /// ⛔ The fix is never a smaller font (FontFloor is a floor) and never a wider chip (the
        /// shared rail edge is canon) — it is FEWER CHARACTERS, authored in canon-strings.json
        /// where HudLabelFitRegression can measure them against this exact box.
        /// </summary>
        private static string FormatCollectorChip(CollectorStatusGate.CollectorStatus s)
        {
            // WO-1194: the three resource lines own the storage state. This button owns
            // only the existing collect-all action, so it remains the verb "Harvest".
            return HudStrings.Get(HudStrings.KeyCollectorsTitle);
                // The load-bearing tell: a full collector has STOPPED EARNING, and the fix is one
                // tap. Line 1 has already said "N/M full", so the bare imperative is the whole of
                // what is left to say — which is fortunate, because it is also all that fits.
                // (Cross-WO: once the bank gets a headroom check, WO-857 replaces this line with
                // a "bank full" variant when the collect cannot bank — flagged in both WOs so
                // neither surface ships a lie. Keep that variant SHORT too.)
        }

        /// <summary>THE one collapsed rail chip. Echoes, Builders and Resources are the same
        /// object with a different word on it: Style1/Gray obsidian face, fixed 220x112 ref px,
        /// pinned to the shared rail gutter. Fixed pixels only (WO-841).</summary>
        private Button BuildRailChip(RectTransform parent, string name, string label,
                                     float yFromTopPx, Action onTap)
        {
            RectTransform ignored;
            return BuildRailChip(parent, name, label, yFromTopPx, onTap, out ignored);
        }

        /// <summary>Same chip, handing back the fixed-pixel BAND it was seated in. WO-1435 needs
        /// the band (not the button) because the band is what carries the rail's y — and reaching
        /// it by <c>GetChild(0)</c> or by re-deriving the "<c>&lt;name&gt;Band</c>" string at the
        /// call site would be a second copy of a name this method already owns.</summary>
        private Button BuildRailChip(RectTransform parent, string name, string label,
                                     float yFromTopPx, Action onTap, out RectTransform band)
        {
            band = RailBand(parent, name + "Band", yFromTopPx, RailChipHeightPx, RailChipWidthPx);
            var btn = ElarionUiKit.BuildObsidianButton(band, label,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                Vector2.zero, Vector2.one, onTap);
            if (btn == null)
            {
                FlowTrace.Warn("HudKit", "rail chip '" + name + "' did not build - the kit returned no button");
                return null;
            }
            btn.gameObject.name = "RailChip_" + name;
            // BuildObsidianButton arms FitSingleLine (no-wrap + ellipsis), which is right for a
            // wide bar face and WRONG here: the chip is 220 ref px wide because that is the width
            // of the Echoes chip it must match, and "Builders 1/2 | Train 3" cannot seat 22
            // characters on one 200 px line above the kit's 30 px legibility floor — it would
            // ellipsize the Train count away, and the collapsed chip is the only HUD surface that
            // reports it. The chip is 112 px TALL, so the label wraps instead: FitBlock keeps the
            // same bounded auto-size and legibility floor, uses the height we already reserved,
            // and nothing is clipped or dropped. Single-word chips ("Resources") are unaffected.
            MedievalUiSkin.ApplyButton(btn, primary: true);
            var lbl = btn.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null)
            {
                lbl.fontSizeMin = 22f;
                lbl.fontSizeMax = 30f;
                ElarionUiKit.FitSingleLine(lbl, 22f, 30f);
            }
            return btn;
        }

        /// <summary>A FIXED-PIXEL band pinned to the TOP-RIGHT of <paramref name="parent"/> and
        /// snapped onto the shared rail gutter by <see cref="HudRailGutter"/>. The kit's anchor
        /// helpers take fractions; rail chrome must not (WO-841) — a fraction band can resolve
        /// under MinTouchPx, and ClampMinTouch then grows it about its centre into its neighbour.</summary>
        private static RectTransform RailBand(RectTransform parent, string name,
            float yFromTopPx, float heightPx, float widthPx)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(widthPx, heightPx);
            rt.anchoredPosition = new Vector2(0f, -yFromTopPx);
            go.AddComponent<HudRailGutter>();
            return rt;
        }

        /// <summary>
        /// Re-seat a rail chip's label onto the kit plate's INK band, in REFERENCE PIXELS
        /// (WO-1623/WO-1628 idiom: pivot first, then offsets; never a share of the parent).
        /// <para>Why this exists at all is written at <see cref="RailPlateInkTopFrac"/>: the plate
        /// stretches across the band but its ink does not, so the kit's full-height label anchors
        /// hand a multi-line caption all 112 px of the band inside a plate that shows ~66.
        /// Collapsing the y anchors onto the band's TOP edge and hanging a fixed px band off it
        /// means the fitter measures the surface the player can actually see.</para>
        /// <para>WHAT THE MEASUREMENT SAYS THIS BAND SEATS (so it is decided, not hoped):
        /// 112 * 0.736 - 3 - (112 * 0.145 + 3) = <b>60.2 ref px</b>. The shipped label carries the
        /// TITLE role - MedievalUiSkin.ApplyButton ends on EnsureFont(FontRole.Title) - whose asset
        /// (Resources/RpgUi/font/font_title.asset, Merriweather Bold) declares m_LineHeight 80.448
        /// at m_PointSize 64, i.e. 1.257 em. So at the chip's 22 px fit floor TWO lines need 55.3 px
        /// and fit; THREE need 82.9 px and cannot. Width agrees: summing that asset's glyph
        /// advances, "ATTACK REPORT" is 190.7 ref px at 22 px (196.4 with the skin's
        /// characterSpacing 2) inside the label's 220 * 0.92 = 202.4 px rect, so the title seats
        /// on ONE line. The caption therefore resolves to two lines at the floor - the font is
        /// never driven under it, and no player-facing string is shortened.</para>
        /// <para>⚠ THE WIDTH MARGIN IS 6.0 px (196.4 of 202.4, ~3%) AND IT IS THE ONE NUMBER THE
        /// CAPTURE MUST CONFIRM. Kerning can only shrink it further, but if "ATTACK REPORT" ever
        /// failed to seat on one line the caption would go back to three, exceed this band and be
        /// TRUNCATED at the floor instead of overflowing - a worse failure than the one being
        /// fixed. The cheap remedy if that happens is width, and the room exists: the plate's own
        /// ink spans x 0.021..0.978 of the band = 210.5 px. Not taken here, because width is not
        /// implicated by the captured defect (the escape is at the plate's TOP and BOTTOM) and
        /// DefenseReportLayoutRegression's budget models this rect EXACTLY at 220 * 0.92 - moving
        /// the inset would silently make that oracle wrong in the optimistic direction.</para>
        /// </summary>
        private static void SeatOnRailPlate(TMP_Text label, string who)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            if (rt == null) return;

            float top = RailChipHeightPx * RailPlateInkTopFrac + RailPlateInsetPx;
            float bottom = RailChipHeightPx * RailPlateInkBottomFrac - RailPlateInsetPx;
            if (bottom - top < ElarionUi.PadPanel)
            {
                FlowTrace.Warn("HudKit", "WO-1642: the plate band for '" + who + "' resolved to " +
                                         (bottom - top).ToString("F1") + " ref px - too thin to seat a " +
                                         "caption, so the label keeps the kit's own anchors.");
                return;
            }

            string before = rt.anchorMin.y.ToString("F2") + ".." + rt.anchorMax.y.ToString("F2");
            rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
            rt.pivot = new Vector2(rt.pivot.x, 1f);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
            rt.offsetMin = new Vector2(rt.offsetMin.x, -bottom);

            FlowTrace.Step("HudKit", "WO-1642 '" + who + "' label seated on the PLATE: " +
                                     top.ToString("F1") + ".." + bottom.ToString("F1") + " ref px from the " +
                                     "band top (" + (bottom - top).ToString("F1") + " px tall), retiring the " +
                                     "kit's y anchors " + before + " which spanned the whole " +
                                     RailChipHeightPx.ToString("F0") + " px band.");
        }

        /// <summary>THE rail arbiter: at most ONE expanded section, ever. Reused by the Builders
        /// chip, the Resources chip and the calm(explore) tap-expand window, so no caller can
        /// stack two panels on the right column.</summary>
        private void SetRailSection(RailSection section)
        {
            bool builders = section == RailSection.Builders;
            if (_railOpen == section &&
                (_queueRailMount == null || _queueRailMount.gameObject.activeSelf == builders)) return;

            _railOpen = section;
            if (_queueRailMount != null && _queueRailMount.gameObject.activeSelf != builders)
                _queueRailMount.gameObject.SetActive(builders);
            // Repaint what was hidden. QueueRailView.Sync() takes its cheap text-only path
            // unless the measured width moved — and on the FIRST expand the rail's rect has
            // never been through a layout pass (it was built and deactivated in one frame),
            // so sync AGAIN one frame later, once the fixed-px band has resolved. Without
            // that second pass the cards would keep a zero-width shape until the publisher's
            // next 1 s tick happened to move the version.
            if (builders && _queueRail != null) { _queueRail.Sync(); _queueRailSyncFrames = 2; }
            else _queueRailSyncFrames = 0;

            // WO-1205 re-points WO-1194: the capped-resource lines are NOT ambient furniture.
            // The owner ruled the rail back to gold-only-until-tapped, so this arbiter no
            // longer force-opens the resource panel; the tap window is its only opener.
            // (Opening another rail section still leaves the resource panel to its own state.)

            FlowTrace.Step("HudKit", "right rail: expanded section = " + section +
                           " (one open at a time; the other two stay collapsed)");
        }

        private void ToggleRailSection(RailSection section) =>
            SetRailSection(_railOpen == section ? RailSection.None : section);

        // "Builders 1/2" (+ " | Training N"). NO TIMER — WO-864 bug 1: the chip used to
        // print the soonest countdown AND the job row printed the same value right under
        // it, so the owner saw "3m 13s" twice, stacked. Exactly ONE surface owns a
        // countdown now, and it is the card that the countdown belongs to.
        //
        // WO-1407: the words moved to Core (BuildersChipCopy.Format) so the regression can read
        // them with a fixture; this View only relays. The chip now SAYS idle ("Builders idle 2")
        // instead of "Builders 0/2" - a status glance that reports nothing when nothing is
        // building is indistinguishable from a missing chip (the merged review row 6).
        // The two-line shape ("\nTrain N", never " | ") and its 2026-08-05 numeral-1 reasoning
        // travelled with the words - read them there.
        private static string FormatQueueChip(ObsidianQueueGate.WorkQueueStatus s)
        {
            return BuildersChipCopy.Format(s);
        }

        // ── WO-1384: THE HEART PLATE'S ROWS, top to bottom, as fractions of _heartPlate.Root ──
        // Read by HudLabelFitRegression [heartfire-inside-plate] as literals, so a band that
        // leaves the plate or overlaps a neighbour is a red gate, not a felt-test report. The
        // plate is 0.96 of HudLayoutBands.HeartMount = 125 ref units at 2670x1200 (140 at
        // 1920x1080), so every row below seats its font floor times the 1.2 line factor with
        // room over: name 27.5 units (floor 20 x 1.2 = 24), objective 23.8 (its kit-clamped
        // floor is 18 -> 21.6), Heartfire 27.5 (24), rekindle 23.8 (21.6).
        private const float HeartNameBandY0 = 0.74f;
        private const float HeartNameBandY1 = 0.96f;
        private const float HeartObjectiveBandY0 = 0.53f;
        private const float HeartObjectiveBandY1 = 0.72f;
        private const float HeartfireBandY0 = 0.29f;
        private const float HeartfireBandY1 = 0.51f;
        private const float HeartfireRekindleBandY0 = 0.08f;
        private const float HeartfireRekindleBandY1 = 0.27f;
        /// <summary>The rows' shared x band inside the plate (inside the visible frame).</summary>
        private const float HeartRowX0 = 0.05f;
        private const float HeartRowX1 = 0.95f;
        /// <summary>The plate's name size - and, by the owner's ruling, the Heartfire row's.</summary>
        private const float HeartNameFontMin = 20f;
        private const float HeartNameFontMax = 26f;
        // (Literals, not aliases of the name constants: the regression pin parses these as
        // numbers and asserts HeartfireFontMin >= HeartNameFontMin itself.)
        private const float HeartfireFontMin = 20f;
        private const float HeartfireFontMax = 26f;
        /// <summary>The objective line and the rekindle line: the plate's small text.
        /// (FitSingleLine clamps the floor up to ElarionUiKit.FontHardFloor, so 16 resolves
        /// to a fixed 18 - stated here as authored, the kit owns the clamp.)</summary>
        private const float HeartObjectiveFontMin = 16f;
        private const float HeartObjectiveFontMax = 18f;
        /// <summary>WO-1419 owner-selected runtime sprite and greyscale-safe slot treatment.</summary>
        private const string HeartfireFlameSpritePath = "ItemIcons/cons_emberfire_bomb";
        private const float HeartfireFlameLitAlpha = 1.0f;
        private const float HeartfireFlameSpentAlpha = 0.25f;
        private const float HeartfireFlameSpentGray = 0.55f;
        /// <summary>One flame matches the marks row's 26px line height; slots keep a fixed gutter.</summary>
        private const float HeartfireFlameIconPx = 26f;
        private const float HeartfireFlameGapPx = 4f;
        private const float HeartfireFlameX1 = 0.32f;
        private const float HeartfireLabelX0 = 0.34f;

        // WO-432: Heart of Elarion status cluster — a tree-of-life glyph + "Elarion" caption
        // sitting ABOVE its own gold Heart bar, so the whole widget reads as the world-tree /
        // heart status (occupied into the HeartStatus area on the left, below the nameplate)
        // and can never be mistaken for a second hero HP bar. Factory-only (§5).
        private void BuildHeartStatus(Transform pool)
        {
            var root = new GameObject("HeartStatus", typeof(RectTransform));
            root.transform.SetParent(pool, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // Tree-of-life glyph (OUR icon via the concept resolver; hidden if the art is
            // absent — the nameplate carries the "Elarion" caption) marking this as the
            // world tree's status.
            var mark = new GameObject("HeartMark", typeof(Image));
            mark.transform.SetParent(root.transform, false);
            var mrt = (RectTransform)mark.transform;
            mrt.anchorMin = new Vector2(0.02f, 0.30f); mrt.anchorMax = new Vector2(0.15f, 0.95f);
            mrt.offsetMin = Vector2.zero; mrt.offsetMax = Vector2.zero;
            var markImg = mark.GetComponent<Image>();
            markImg.preserveAspect = true; markImg.raycastTarget = false;
            var markSprite = UiStyle.Icon("tree");
            // The adjacent campfire/tree glyph duplicated the explicit Heart of Elarion label
            // and read as a separate unexplained control. Keep the object for prefab/capture
            // compatibility, but retire its player-facing rendering.
            mark.SetActive(false);

            // WO-432: the Heart of Elarion now renders on the SHARED PartyNameplate builder
            // (name = "Heart of Elarion" + a single HP bar). Only HealthFill is used; the mana row is
            // hidden so it reads as the world-tree/heart status, never a second hero MP bar.
            // (ASCII name; the old "♥" heart glyph tofu'd on the build font.)
            _heartPlate = ElarionUiKit.BuildPartyNameplate(root.transform, HeartObjectiveCopy.Title,
                new Vector2(0.02f, 0.02f), new Vector2(0.99f, 0.98f));
            // ── WO-1384: FOUR ROWS, ONE PLATE, EVERY BAND STATED ONCE ──────────────────
            // Owner felt-test 2026-09-04 (Seeker, build 355905): "there is something under the
            // Heart of Elarion, but i cannot read it its too small on screen". The capture
            // (docs/qa/seeker-hud-left-2026-09-04.png) shows "[*] [*] [*]  Heartfire" drawn
            // ACROSS the plate's bottom edge at the plate's smallest size. The cause was
            // geometric, not a font choice: the Heartfire label carried TWO lines (marks row +
            // rekindle line) inside a band 0.04..0.32 of an 83-unit plate = 23 units, so the fit
            // guard relaxed the font to seat two lines in one line's height and the centred
            // block still overflowed the rect - half of it below the plate.
            //
            // The fix follows the owner's ruling verbatim - "grow the plate, never shrink the
            // text": HudLayoutBands.HeartMount grew 0.090 -> 0.135 of screen (the cluster root
            // this plate fills), and the rows below are stated as constants so the regression
            // pin reads the SAME numbers this code lays out (HudLabelFitRegression
            // [heartfire-inside-plate]). All four rows sit inside y 0.08..0.96 of the plate,
            // which is inside the visible frame of the content-panel sprite.
            if (_heartPlate.NameLabel != null)
            {
                var nameRt = _heartPlate.NameLabel.rectTransform;
                nameRt.anchorMin = new Vector2(nameRt.anchorMin.x, HeartNameBandY0);
                nameRt.anchorMax = new Vector2(nameRt.anchorMax.x, HeartNameBandY1);
            }
            // WO-1407: line 2 carries STATE (Barracks hint / train count / wave line) - the words
            // are resolved in Core (HeartObjectiveCopy) and painted by RepaintHeartObjective; the
            // literal sentence no longer lives in this View. The row stays ONE fitted line in the
            // WO-1384 band (the band seats exactly one 18 px line - a FitBlock wrap here would
            // Truncate the second line away silently, while the single-line fit ellipsises
            // visibly; HudLabelFitRegression [heart-objective-state] MEASURES the widest state
            // string inside the row so neither happens).
            _heartObjectiveLabel = ElarionUiKit.Label(_heartPlate.Root.transform,
                string.Empty, HeartObjectiveBandY0, HeartObjectiveBandY1,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft,
                HeartRowX0, HeartRowX1);
            _heartObjectiveLabel.enableAutoSizing = true;
            ElarionUiKit.FitSingleLine(_heartObjectiveLabel, HeartObjectiveFontMin, HeartObjectiveFontMax);
            RepaintHeartObjective(force: true);

            // ── WO-1379 HEARTFIRE ────────────────────────────────────────────────────
            // Canon docs/CREATIVE_CANON_ELARION_2026-09-04.md section 4 draws three flames
            // around the Heart symbol with the rekindle timer beneath, so this is the one
            // right home for it: the town HUD's Heart of Elarion plate, which is already
            // occupied into calm(town) by hud-areas.json. No new widget id, no new
            // occupancy row - and the player sees the count WITHOUT opening the raid grid,
            // which is the acceptance criterion.
            //
            // ⛔ COLOUR AND ICON TREATMENT ARE THE OWNER'S CALL, NOT THE IMPLEMENTER'S
            // WO-1419 records that owner call: ItemIcons/cons_emberfire_bomb is the mark.
            // Lit/spent differ by opacity and neutral-grey fill, not hue; FlameRow survives only
            // in the trace path. The rekindle line remains on its own row beneath the icons/count.
            // WO-1419: real flame Images replace the player-facing ASCII brackets. The slot
            // treatment differs in opacity and fill luminance, so it survives greyscale.
            _heartfireFlameSlots.Clear();
            var flameHostGo = new GameObject("HeartfireFlameSlots", typeof(RectTransform));
            _heartfireFlameHost = (RectTransform)flameHostGo.transform;
            _heartfireFlameHost.SetParent(_heartPlate.Root.transform, false);
            _heartfireFlameHost.anchorMin = new Vector2(HeartRowX0, HeartfireBandY0);
            _heartfireFlameHost.anchorMax = new Vector2(HeartfireFlameX1, HeartfireBandY1);
            _heartfireFlameHost.offsetMin = Vector2.zero;
            _heartfireFlameHost.offsetMax = Vector2.zero;
            _heartfireLabel = ElarionUiKit.Label(_heartPlate.Root.transform,
                 string.Empty, HeartfireBandY0, HeartfireBandY1,
                 ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft,
                 HeartfireLabelX0, HeartRowX1, bold: true);
            _heartfireLabel.enableAutoSizing = true;
            ElarionUiKit.FitSingleLine(_heartfireLabel, HeartfireFontMin, HeartfireFontMax);
            _heartfireRekindleLabel = ElarionUiKit.Label(_heartPlate.Root.transform,
                string.Empty, HeartfireRekindleBandY0, HeartfireRekindleBandY1,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft,
                HeartRowX0, HeartRowX1);
            _heartfireRekindleLabel.enableAutoSizing = true;
            ElarionUiKit.FitSingleLine(_heartfireRekindleLabel, HeartObjectiveFontMin, HeartObjectiveFontMax);
            RepaintHeartfire(force: true);
            if (_heartPlate.NameLabel != null)
            {
                _heartPlate.NameLabel.fontSizeMin = HeartNameFontMin;
                _heartPlate.NameLabel.fontSizeMax = HeartNameFontMax;
            }
            var heartHealthRow = _heartPlate.HealthFill != null
                ? _heartPlate.HealthFill.transform.parent : null;
            if (heartHealthRow != null) heartHealthRow.gameObject.SetActive(false);
            if (_heartPlate.ManaFill != null)
            {
                _heartPlate.ManaFill.fillAmount = 0f;
                var manaBg = _heartPlate.ManaFill.transform.parent;   // ManaBackground row
                if (manaBg != null) manaBg.gameObject.SetActive(false);
            }

            Register("heartStatus", WrapAsWidget("heartStatus", root));
        }

        /// <summary>
        /// Approved calm-state dock: one stable housing with four touch-first medallions.
        /// Navigation remains routed through the existing authoritative seams; this method owns
        /// presentation and labels only.
        /// </summary>
        private void BuildAdaptivePeacefulDock(Transform pool)
        {
            _peacefulDockRoot = new GameObject("AdaptivePeacefulDock", typeof(RectTransform));
            _peacefulDockRoot.transform.SetParent(pool, false);
            var rootRt = (RectTransform)_peacefulDockRoot.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

            ElarionUiKit.BuildActionBarHousing(_peacefulDockRoot.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f));

            // WO-1319 — the five faces are laid out in REFERENCE PIXELS by a live solver, not
            // by 1/5 fractions of a mount that is only 46% of a canvas whose local width
            // collapses with the aspect. See HudDockSlotLayout / DeNelle.Core.UI.HudDockLayout.
            _peacefulDockLayout = _peacefulDockRoot.AddComponent<HudDockSlotLayout>();
            _peacefulDockLayout.Configure(rootRt, PeacefulDockSlotY0, PeacefulDockSlotY1,
                HudAreasHost.ActionBarRightHeadroomRatio, HudDockLayout.GapFraction);

            // WO-1605 — display copy is localized while icon IDs remain stable authored data.
            // BuildPeacefulDockSlot receives both explicitly, so translated copy can never change
            // which emblem is loaded. That is not tidiness: the sheet the
            // owner authors reads BUILD/TALK/HERO/MANAGE across the top with JOURNEY beneath, while
            // the bar shows BUILD/TALK/HERO/JOURNEY/MANAGE — a position-indexed slice silently
            // swaps the last two, and both faces still look plausible, so nobody catches it.
            // The extra ids below are the LEGACY pack fallbacks, kept so a face whose authored art
            // has not landed yet renders exactly what it renders today.
            BuildPeacefulDockSlot(0, "build", HudStrings.KeyNavBuild, new[] { "hammer" }, () =>
            {
                if (_owner != null) _owner.BuildRequested?.Invoke();
            });
            BuildPeacefulDockSlot(1, "talk", HudStrings.KeyNavTalk, new[] { "speech", "dialogue" }, () =>
            {
                HudCommands.Talk();
                if (_owner != null) _owner.TalkRequested?.Invoke();
            });
            BuildPeacefulDockSlot(2, "hero", HudStrings.KeyNavHero, new[] { "helmet", "sword" }, () =>
            {
                if (!PanelRouter.Open(PanelId.HeroDeck))
                    FlowTrace.Warn("HudKit", "Hero workspace opener not registered");
            });
            BuildPeacefulDockSlot(3, "journey", HudStrings.KeyNavJourney, new[] { "compass", "quest" }, OnQuestsAction);
            BuildPeacefulDockSlot(4, "manage", HudStrings.KeyNavManage, new[] { "banner", "shield" }, OnManageAction);

            Register("peacefulDock", WrapAsWidget("peacefulDock", _peacefulDockRoot));
            BindLocalizedHudCopy();
        }

        /// <summary>
        /// WO-1467 — MEASUREMENT HOOK, and nothing else. It builds the dock exactly as the
        /// runtime does (it calls the one builder above) and hands back the same
        /// <c>_peacefulDockRoot</c> the runtime registers.
        ///
        /// WHY IT EXISTS. Until this ticket the dock the player actually touches was covered
        /// only by SOURCE-TEXT LINT, while two suites pinned <c>HudActionBarModel</c> — a model
        /// <see cref="BindActionBar"/> never subscribes once this dock exists, because it returns
        /// early on <c>_peacefulDockRoot != null</c>. A lint cannot see a face that moved, was
        /// re-ordered, or lost its caption. <c>HudActionBarRegression.CheckMeasuredPeacefulDock</c>
        /// calls this on a throwaway instance in batch mode, walks the returned tree and asserts
        /// the faces it FINDS — count, captions, left-to-right order, touch floor and label fit.
        ///
        /// It is not a second build path: it has no live caller, adds no state, and must never
        /// grow one. Reflection on the private builder was the alternative and is worse — a rename
        /// would silently turn the oracle off instead of failing to compile.
        /// </summary>
        public GameObject BuildPeacefulDockProbe(Transform pool)
        {
            BuildAdaptivePeacefulDock(pool);
            return _peacefulDockRoot;
        }

        // WO-1319 — the peaceful dock's vertical band, named once and shared with the live
        // solver. (The horizontal 1/5 slicing that used to sit beside them is GONE: it is what
        // collapsed under ElarionUiKit's touch floor at a narrow aspect and printed the five
        // captions as one overlapping run. HudDockSlotLayout owns x now.)
        private const float PeacefulDockSlotY0 = 0.08f;
        private const float PeacefulDockSlotY1 = 0.94f;
        private HudDockSlotLayout _peacefulDockLayout;

        /// <summary>
        /// One calm-dock medallion. <paramref name="iconKey"/> is stable authored-data identity;
        /// <paramref name="labelKey"/> resolves the live translated caption. <paramref
        /// name="iconFallbacks"/> are the older pack concepts, tried in order only when the
        /// caption's own art is absent, which is what keeps the bar looking exactly as it does
        /// today until authored art is dropped in. A null icon is NOT an error and never blanks the
        /// face: the kit's medallion keeps its own look and the live caption still names it.
        /// </summary>
        private void BuildPeacefulDockSlot(int index, string iconKey, string labelKey,
                                           string[] iconFallbacks, Action command)
        {
            BuildDockSlot(_peacefulDockRoot, _peacefulDockLayout, _peacefulDockLabels,
                          index, PeacefulDockFaceCount, iconKey, labelKey, null,
                          iconFallbacks, command);
        }

        /// <summary>How many faces the CALM dock builds. Named once, used by the build-time x
        /// seed. ⛔ This is NOT the authority on what the bar ships — HudActionBarRegression
        /// .CheckMeasuredPeacefulDock builds the dock and COUNTS it (CLAUDE.md §7).</summary>
        private const int PeacefulDockFaceCount = 5;
        /// <summary>WO-1672 — how many faces the OUTSIDE dock builds. BUILD and TALK are not
        /// among them; see BuildAdaptiveOutsideDock for why they are absent, not disabled.</summary>
        private const int OutsideDockFaceCount = 4;

        /// <summary>
        /// The ONE calm/outside dock medallion builder. <paramref name="literalCaption"/> wins over
        /// <paramref name="labelKey"/> when non-null (the ITEM face, which has no HudStrings key
        /// yet — see WO-1672's follow-up note); everything else is unchanged from the five-face
        /// path this was hoisted out of, so the peaceful dock builds byte-for-byte as before.
        /// </summary>
        private ElarionUiKit.ActionSlotHandle BuildDockSlot(
            GameObject dockRoot, HudDockSlotLayout layout, TMP_Text[] labelSink,
            int index, int count, string iconKey, string labelKey, string literalCaption,
            string[] iconFallbacks, Action command)
        {
            if (dockRoot == null) return null;
            string caption = literalCaption ?? HudStrings.Get(labelKey);
            // The authored emblem sheet is asked FIRST and by name; the pack fallbacks below are
            // only reached when her art cannot be resolved. Which one answered decides how the
            // medallion is dressed, so the two lookups stay separate.
            var authored = UiStyle.AuthoredIcon(iconKey);
            var icon = authored != null
                ? authored
                : UiStyle.Icon(iconKey, iconFallbacks ?? System.Array.Empty<string>());
            if (icon == null)
                FlowTrace.Throttle("HudKit", "dock-icon-miss:" + iconKey, 30f,
                    "calm dock face '" + caption + "' resolved NO icon art (key '" + iconKey +
                    "') - the medallion keeps its kit look and the live caption still names it");
            // Build-time seed only: an equal share of the mount, so a dock that somehow never
            // gets a layout pass still renders in a sane shape. HudDockSlotLayout overwrites
            // these x anchors with absolute reference-pixel positions on the first LateUpdate
            // and on every surface change after (a browser window drag is a shipping event).
            const float gap = HudDockLayout.GapFraction;
            float width = (1f - gap * (count + 1)) / count;
            float x0 = gap + index * (width + gap);
            var slot = ElarionUiKit.BuildActionSlot(dockRoot.transform,
                new Vector2(x0, PeacefulDockSlotY0), new Vector2(x0 + width, PeacefulDockSlotY1), command);
            // Keep the slot as an equal-width quarter of the shared dock.  Only its medallion
            // artwork is square; constraining the slot itself makes Unity centre all four roots
            // on the same point and the last one (MANAGE) visually covers the others.
            ElarionUiKit.StyleAsRoundMedallion(slot);
            slot.SetIcon(icon);
            // WO-1359 — her emblems ARE medallions (own socket, own gold ring, four diamond points
            // proud of the circle). Dressing one in the kit's medallion too would draw a second
            // ring around hers, clip the points at the round stencil and stretch a 386x411 emblem
            // square. Only when authored art actually answered does the kit step back; a pack
            // fallback keeps the kit medallion it has always had.
            if (authored != null) ElarionUiKit.PresentAuthoredEmblem(slot);
            slot.SetCaption(caption);
            if (labelSink != null && index >= 0 && index < labelSink.Length)
                labelSink[index] = slot.caption;
            // WO-1319 acceptance 2 — the caption's degradation is AUTHORED, not incidental.
            // SetCaption leaves the kit default (word-wrap on, autosize floor 6f), so a caption
            // that outgrew its face either re-flowed or shrank to an illegible smear. The shared
            // kit's own single-line fitter is the right answer and already exists: NoWrap +
            // bounded autosize + Ellipsis, floored at FontHardFloor(20) so "JOURNEY" becomes
            // "JOUR..." rather than 6pt mush or a word painted over its neighbour. The label can
            // now never be wider than its slot, whatever the solver hands it.
            if (slot.caption != null)
                ElarionUiKit.FitSingleLine(slot.caption, ElarionUiKit.FontHardFloor, ElarionUi.FontMicro);
            // WO-1671 — the caption band draws BELOW the housing art, onto the town terrain: the
            // owner's device frames measure BUILD 2.12:1, TALK 2.73:1, HERO 2.97:1, JOURNEY 1.87:1,
            // MANAGE 2.16:1 against what is actually behind them (2670x1200, PIL, 2026-09-10) - all
            // five under the 3:1 floor. The kit's obsidian plate goes under each word. The caption
            // rect is NOT changed by this, so the solved slot geometry is untouched: HudDockLayout
            // .Solve(count, mountW, maxTrack, gap) takes no caption or height term at all.
            var captionPlate = ElarionUiKit.AddCaptionPlate(slot);
            if (slot.button != null) ElarionUiKit.ClampMinTouch(slot.button);
            if (layout != null)
                layout.AddSlot((RectTransform)slot.root.transform, slot.caption, captionPlate);
            return slot;
        }

        // ═══ WO-1672 — THE OUTSIDE DOCK ═══════════════════════════════════════════
        //
        // OWNER RULING, verbatim (2026-09-10 12:16): "when you are outside the castle should not
        // be the peaceful UI, not combat, but should not be able to build or talk but can use
        // items still."
        //
        // ⚠ THE SIGNAL ALREADY EXISTS AND THE HUD ALREADY COMPUTES IT — that is the finding, and
        // it is why this ticket did not need a new world seam invented for it:
        //   HudContextEvaluator.IsInTownRing (Assets/_Modules/Village/HUD/HudContextEvaluator.cs
        //   :202-214) tests the hero's horizontal distance from the world origin against
        //   TownRadius 60 m (:74) with 8 m hysteresis (:75), polled every 0.2 s;
        //   -> HudContextResolver.Resolve (Core/HudModel/HudContextResolver.cs:41-48) turns it
        //      into HudContext.Town vs HudContext.Overworld;
        //   -> PostureEvaluator.Derive (HUD/Kit/PostureEvaluator.cs:143-145) turns THAT into
        //      HudPosture.CalmTown vs HudPosture.CalmExplore, and traces every transition at
        //      PostureEvaluator.cs:78-80 ("posture calm(town)->calm(explore)"). No new trace is
        //      added here: adding a second one would be two owners of the same event.
        // What was MISSING is only the last hop: hud-areas.json listed the SAME "peacefulDock"
        // widget under calm(town) and calm(explore), so the dock was byte-identical on both sides
        // of a boundary the HUD had already crossed. This dock is what calm(explore) gets instead.
        //
        // ⛔ BUILD AND TALK ARE **ABSENT**, NOT DISABLED, AND THE CHOICE IS FORCED:
        //  1. The oracle cannot tell a disabled face from a live one. CheckMeasuredPeacefulDock
        //     finds faces with GetComponentInChildren<Button>(true) — includeInactive, and
        //     DELIBERATELY so, because Register() deactivates the whole dock root. A
        //     SetActive(false) BUILD face still counts as a face; only a face that was never
        //     constructed can be asserted absent.
        //  2. A dimmed face must carry a WORD or a NUMBER saying why (HudActionBarModel.cs:270-285
        //     — the owner is red/green colourblind, so a greyed medallion signals nothing to her).
        //     No such copy is ruled, and inventing it would be a design decision this lane does
        //     not own.
        //  3. The touch floor is the scarcest thing on this bar (MinSlotPx 112; HudDockLayout
        //     already degrades to icon-only when five faces will not fit). Spending two of them on
        //     controls that cannot fire is the opposite of what the narrow-surface ladder is for.
        // If the owner wants them present-but-dead instead, that is a one-line change here plus a
        // ruled word for each — flagged in the WO, not decided here.
        //
        // HERO / JOURNEY / MANAGE are UNRULED and therefore carried over from the peaceful dock
        // exactly as they are, in the same relative order. ITEM is appended on the right, where
        // the combat dock already puts it (BuildAdaptiveCombatDock slot 5), so the item face never
        // moves between postures. Both of those are flagged for the owner in the WO.
        private void BuildAdaptiveOutsideDock(Transform pool)
        {
            _outsideDockRoot = new GameObject("AdaptiveOutsideDock", typeof(RectTransform));
            _outsideDockRoot.transform.SetParent(pool, false);
            var rootRt = (RectTransform)_outsideDockRoot.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

            ElarionUiKit.BuildActionBarHousing(_outsideDockRoot.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f));

            // Same live reference-pixel solver as the other two docks (WO-1319). Four faces, so
            // it solves WIDER slots than the calm dock at every surface — never narrower.
            _outsideDockLayout = _outsideDockRoot.AddComponent<HudDockSlotLayout>();
            _outsideDockLayout.Configure(rootRt, PeacefulDockSlotY0, PeacefulDockSlotY1,
                HudAreasHost.ActionBarRightHeadroomRatio, HudDockLayout.GapFraction);

            BuildDockSlot(_outsideDockRoot, _outsideDockLayout, _outsideDockLabels, 0,
                OutsideDockFaceCount, "hero", HudStrings.KeyNavHero, null,
                new[] { "helmet", "sword" }, () =>
                {
                    if (!PanelRouter.Open(PanelId.HeroDeck))
                        FlowTrace.Warn("HudKit", "Hero workspace opener not registered");
                });
            BuildDockSlot(_outsideDockRoot, _outsideDockLayout, _outsideDockLabels, 1,
                OutsideDockFaceCount, "journey", HudStrings.KeyNavJourney, null,
                new[] { "compass", "quest" }, OnQuestsAction);
            BuildDockSlot(_outsideDockRoot, _outsideDockLayout, _outsideDockLabels, 2,
                OutsideDockFaceCount, "manage", HudStrings.KeyNavManage, null,
                new[] { "banner", "shield" }, OnManageAction);
            // "can use items still" — the SAME picker the combat dock opens (OpenItemPicker), with
            // the same stack badge and the same WO-1468 in-medallion seat, so the face the player
            // learned in combat behaves identically out here. The caption is the literal "ITEM"
            // because HudStrings has no item key today (AllKeys, HudStrings.cs:128-139) and the
            // combat face is the same literal — minting a key belongs to the localization lane and
            // is flagged in the WO rather than done half-way here.
            _outsideItemSlot = BuildDockSlot(_outsideDockRoot, _outsideDockLayout,
                _outsideDockLabels, 3, OutsideDockFaceCount, "potion", null, "ITEM",
                new[] { "consumable", "bag" }, OpenItemPicker);
            if (_outsideItemSlot != null)
            {
                _outsideItemSlot.showZero = true;
                ElarionUiKit.StyleAsStackBadge(_outsideItemSlot);
                SeatStackBadgeInMedallion(_outsideItemSlot);
            }

            Register("outsideDock", WrapAsWidget("outsideDock", _outsideDockRoot));
            FlowTrace.Once("HudKit", "outside-dock-built",
                "WO-1672: outside dock built with " + OutsideDockFaceCount + " faces " +
                "(HERO/JOURNEY/MANAGE/ITEM). BUILD and TALK are NOT CONSTRUCTED - the owner ruled " +
                "they must not be usable outside the castle, and an absent face is the only kind " +
                "the measured-dock oracle can tell apart from a live one.");
        }

        /// <summary>WO-1672 — the measurement hook for the outside dock, the exact twin of
        /// <see cref="BuildPeacefulDockProbe"/>: it calls the ONE builder and returns the same root
        /// the runtime registers. No live caller, no state, and it must never grow one.</summary>
        public GameObject BuildOutsideDockProbe(Transform pool)
        {
            BuildAdaptiveOutsideDock(pool);
            return _outsideDockRoot;
        }

        private GameObject _outsideDockRoot;
        private HudDockSlotLayout _outsideDockLayout;
        private ElarionUiKit.ActionSlotHandle _outsideItemSlot;
        private readonly TMP_Text[] _outsideDockLabels = new TMP_Text[OutsideDockFaceCount];
        /// <summary>Localization keys for the outside dock, index-aligned with
        /// <c>_outsideDockLabels</c>. The ITEM slot is NULL on purpose: it carries a literal today
        /// (see BuildAdaptiveOutsideDock) and the refresh loop skips nulls rather than blanking it.</summary>
        private static readonly string[] OutsideDockLabelKeys =
        {
            HudStrings.KeyNavHero,
            HudStrings.KeyNavJourney,
            HudStrings.KeyNavManage,
            null,
        };

        private void BindLocalizedHudCopy()
        {
            if (_localTextSubscribed) return;
            LocalText.Changed += RefreshLocalizedHudCopy;
            _unsubscribe.Add(() => LocalText.Changed -= RefreshLocalizedHudCopy);
            _localTextSubscribed = true;
            RefreshLocalizedHudCopy();
        }

        private void RefreshLocalizedHudCopy()
        {
            for (int i = 0; i < _peacefulDockLabels.Length && i < PeacefulDockLabelKeys.Length; i++)
                if (_peacefulDockLabels[i] != null)
                    _peacefulDockLabels[i].text = HudStrings.Get(PeacefulDockLabelKeys[i]);
            // WO-1672 — the outside dock's carried-over faces retranslate with the calm ones. A
            // null key is the ITEM literal and is SKIPPED, never resolved: HudStrings.Get on an
            // unknown key would blank the only word on that face.
            for (int i = 0; i < _outsideDockLabels.Length && i < OutsideDockLabelKeys.Length; i++)
                if (_outsideDockLabels[i] != null && OutsideDockLabelKeys[i] != null)
                    _outsideDockLabels[i].text = HudStrings.Get(OutsideDockLabelKeys[i]);
            if (_collectorsChipLabel != null)
                _collectorsChipLabel.text = HudStrings.Get(HudStrings.KeyCollectorsTitle);
            if (_heartPlate.NameLabel != null)
                _heartPlate.NameLabel.text = HeartObjectiveCopy.Title;
            RepaintHeartObjective(force: true);
            RepaintHeartfire(force: true);
            if (_fleeLabel != null)
                _fleeLabel.text = CombatHudText.ResolveFlee(Time.unscaledTime < _fleeArmedUntil);
        }

        /// <summary>Approved active-combat dock: Attack, held Block, three live assignable skills,
        /// and the atomic paused Item picker in one stable six-medallion housing.</summary>
        private void BuildAdaptiveCombatDock(Transform pool)
        {
            _combatDockRoot = new GameObject("AdaptiveCombatDock", typeof(RectTransform));
            _combatDockRoot.transform.SetParent(pool, false);
            var rootRt = (RectTransform)_combatDockRoot.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            ElarionUiKit.BuildActionBarHousing(_combatDockRoot.transform, Vector2.zero, Vector2.one);

            // WO-1319 — the combat dock sits in the SAME ActionBar mount and sliced it into SIX
            // fractions, so at the owner's aspect it carried the identical defect one posture
            // away (six 91px slots is worse than five). Same solver, same ladder, its own gap.
            _combatDockLayout = _combatDockRoot.AddComponent<HudDockSlotLayout>();
            _combatDockLayout.Configure(rootRt, CombatDockSlotY0, CombatDockSlotY1,
                HudAreasHost.ActionBarRightHeadroomRatio, HudDockLayout.CombatGapFraction);

            _adaptiveCombatSlots = new ElarionUiKit.ActionSlotHandle[6];
            _adaptiveCombatSlots[0] = BuildCombatDockSlot(0, "ATTACK",
                UiStyle.Icon("attack", "energy-sword", "sword"), HudCommands.Attack);
            _adaptiveCombatSlots[1] = BuildCombatDockSlot(1, "BLOCK",
                UiStyle.Icon("block", "shield", "defense"), null);
            if (_adaptiveCombatSlots[1] != null && _adaptiveCombatSlots[1].root != null)
                _adaptiveCombatSlots[1].root.AddComponent<HudBlockPressRelay>();
            _adaptiveCombatSlots[2] = BuildCombatDockSlot(2, "EMPTY",
                UiStyle.Icon("skill", "ability"), () => HudCommands.AssignableCast(0));
            _adaptiveCombatSlots[3] = BuildCombatDockSlot(3, "EMPTY",
                UiStyle.Icon("skill", "ability"), () => HudCommands.AssignableCast(1));
            _adaptiveCombatSlots[4] = BuildCombatDockSlot(4, "EMPTY",
                UiStyle.Icon("skill", "ability"), () => HudCommands.AssignableCast(2));
            _adaptiveCombatSlots[5] = BuildCombatDockSlot(5, "ITEM",
                UiStyle.Icon("potion", "consumable", "bag"), OpenItemPicker);
            if (_adaptiveCombatSlots[5] != null)
            {
                _adaptiveCombatSlots[5].showZero = true;
                ElarionUiKit.StyleAsStackBadge(_adaptiveCombatSlots[5]);
                SeatStackBadgeInMedallion(_adaptiveCombatSlots[5]);
            }

            Register("combatDock", WrapAsWidget("combatDock", _combatDockRoot));
        }

        // ═══ WO-1468 — THE CHARGE BADGE SITS ON THE MEDALLION, NOT ON THE CELL ═════
        //
        // EVIDENCE: Builds/ui-capture/AdaptiveHudCombat_2670x1200.png shows the ITEM charge "0"
        // outside the bar's frame, up and to the RIGHT of the round ITEM face; the owner's device
        // build 358574 shows the same escape with a "7". Two aspects, one defect.
        //
        // PROVEN CAUSE, from the kit source (read, not inferred):
        //  * ElarionUiKit.StyleAsStackBadge anchors the plate to the SLOT ROOT at pivot (1,1)
        //    with a 3 px inset - i.e. the top-right corner of the CELL;
        //  * ElarionUiKit.StyleAsRoundMedallion puts the visible art in a child called
        //    "MedallionBounds", anchored (0, 0.20)..(1, 1) with an AspectRatioFitter in
        //    FitInParent at ratio 1 - a SQUARE inscribed in the top 80% of the cell, centred.
        // The cell is WIDER than it is tall (six faces across the ActionBar mount), so the
        // square is narrower than the cell and the cell's top-right corner is well outside it.
        // The badge was never "mis-offset": it was anchored to the right rect.
        //
        // ⚠ AND THIS IS WHY THE OBVIOUS TEST WOULD HAVE PASSED. "The badge is inside the slot
        // rect" is TRUE today, and so is "inside the ActionBarHousing rect" - the housing
        // stretches the whole mount. A containment case written against either rect is green
        // while the player sees the digit outside the frame. The rect that matches what the eye
        // calls "the frame" is the MEDALLION, so that is the rect the badge is seated in and the
        // rect the regression measures.
        //
        // THE SEAT: anchor the plate's top-right corner at the 45-degree point of the inscribed
        // circle - 0.5 + 0.5/sqrt(2) on both axes of the medallion square - and let the fixed
        // 52x40 plate extend inward toward the centre. Corner-anchored inside the face, exactly
        // as WO-1468 asks, and a FRACTION rather than a pixel offset because the medallion's side
        // is resolved by the AspectRatioFitter at runtime and differs per aspect (WO-1468 §3
        // forbids a one-resolution offset). The plate keeps its fixed 52x40 px size, so the
        // digit's legibility is unchanged.
        // PUBLIC + STATIC deliberately: HudUiRegression [stack-badge-inside-medallion] builds a
        // real slot and calls THIS method, so the oracle measures the shipping seat rather than a
        // re-typed copy of it. DeNelle.EditorRegression.asmdef does reference DeNelle.HUD.
        public static void SeatStackBadgeInMedallion(ElarionUiKit.ActionSlotHandle slot)
        {
            if (slot == null || slot.root == null) return;
            var plate = slot.root.transform.Find(StackBadgeObjectName) as RectTransform;
            if (plate == null)
            {
                FlowTrace.Warn("HudKit", "WO-1468: no '" + StackBadgeObjectName + "' under '" +
                                         slot.root.name + "' - StyleAsStackBadge did not run, so there " +
                                         "is no badge to seat. The charge count is NOT being drawn.");
                return;
            }
            var bounds = slot.root.transform.Find(MedallionBoundsObjectName) as RectTransform;
            if (bounds == null)
            {
                // No round face on this slot: the cell IS the face, and the kit's corner seat is
                // already inside it. Named, never silent - a future restyle that drops the
                // medallion should read this line rather than wonder why the seat moved.
                FlowTrace.Once("HudKit", "stackbadge-no-medallion:" + slot.root.name,
                               "WO-1468: '" + slot.root.name + "' has no " + MedallionBoundsObjectName +
                               " (square cell, not a medallion) - the kit's corner seat is kept.");
                return;
            }
            plate.SetParent(bounds, false);
            plate.anchorMin = new Vector2(StackBadgeMedallionAnchor, StackBadgeMedallionAnchor);
            plate.anchorMax = plate.anchorMin;
            plate.pivot = new Vector2(1f, 1f);
            plate.anchoredPosition = Vector2.zero;
            FlowTrace.Step("HudKit", "WO-1468: charge badge re-seated inside the medallion of '" +
                                     slot.root.name + "' at anchor " + StackBadgeMedallionAnchor);
        }

        /// <summary>ElarionUiKit.StyleAsStackBadge's plate object name - matched here, never
        /// re-typed as a literal at a call site.</summary>
        private const string StackBadgeObjectName = "StackBadge";
        /// <summary>ElarionUiKit.StyleAsRoundMedallion's square art container.</summary>
        private const string MedallionBoundsObjectName = "MedallionBounds";
        /// <summary>WO-1468: where the badge's top-right CORNER sits inside the medallion square.
        /// 0.5 + 0.5 / sqrt(2) = 0.8536 is the 45-degree point of the circle inscribed in that
        /// square, so the corner lands ON the rim and the plate extends inward. Derived, not
        /// tuned - and a fraction, because the square's side is resolved per aspect by the
        /// AspectRatioFitter.</summary>
        public const float StackBadgeMedallionAnchor = 0.85355339f;

        // WO-1319 — the combat dock's vertical band + live solver, exactly as the peaceful one.
        private const float CombatDockSlotY0 = 0.06f;
        private const float CombatDockSlotY1 = 0.95f;
        private HudDockSlotLayout _combatDockLayout;

        private ElarionUiKit.ActionSlotHandle BuildCombatDockSlot(int index, string caption,
            Sprite icon, Action command)
        {
            // Build-time seed only — HudDockSlotLayout owns x from the first LateUpdate on.
            const int count = 6;
            const float gap = HudDockLayout.CombatGapFraction;
            float width = (1f - gap * (count + 1)) / count;
            float x0 = gap + index * (width + gap);
            var slot = ElarionUiKit.BuildActionSlot(_combatDockRoot.transform,
                new Vector2(x0, CombatDockSlotY0), new Vector2(x0 + width, CombatDockSlotY1), command);
            ElarionUiKit.StyleAsRoundMedallion(slot);
            slot.SetIcon(icon);
            slot.SetCaption(caption);
            // Same authored caption degradation as the peaceful dock: NoWrap + bounded autosize
            // + Ellipsis, floored at FontHardFloor. Long skill names shorten, never spill.
            if (slot.caption != null)
                ElarionUiKit.FitSingleLine(slot.caption, ElarionUiKit.FontHardFloor, ElarionUi.FontMicro);
            if (slot.button != null) ElarionUiKit.ClampMinTouch(slot.button);
            if (_combatDockLayout != null)
                _combatDockLayout.AddSlot((RectTransform)slot.root.transform, slot.caption);
            return slot;
        }

        private void BuildAbilityRow(Transform pool)
        {
            var row = new GameObject("AbilityRow", typeof(RectTransform));
            row.transform.SetParent(pool, false);
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            _abilitySlots = new ElarionUiKit.ActionSlotHandle[4];
            _abilitySlotEquipped = new bool[4];
            bool combat = FeatureFlags.CombatHud611;
            if (combat) _abilityGlows = new ElarionUiKit.SoftGlowCooldown[4];

            // WO-611 arc611 REDESIGN (capture 2026-07-06 battle_hud.png, 1280x720): the previous
            // arc placed medallion centres as em-FRACTIONS of the actionRail ZONE rect
            // (Em611 = 0.093, mockup offsets Q(8.9,2.5)..R(2.5,7.1) em from the zone's
            // bottom-right). The zone is 0.780-0.995 x 0.040-0.420 of the screen (~275x274 px
            // at 720p), so the medallions scattered across the whole zone — captured at
            // Q~(985,570) W~(1015,505) E~(1077,460) R~(1150,450) — instead of hugging the
            // ~160x50 attack pill at ~(1100,648). Zone fractions scale with the ZONE, never
            // with the PILL. FIX: CombatArcLayout611 (below the controller) recomputes the
            // medallion rects at layout time FROM THE PILL RECT in pill-height units
            // (mockup em ~ pillHeight/3.5): diameter ~0.9x pill height, arcing from
            // just-left-of-pill-top sweeping up over the pill, adjacent medallions nearly
            // touching (gap ~15% of diameter). The pill rect derives from this row's own
            // rect via the shared Pill611* fractions — both widgets stretch the SAME
            // actionRail mount, so no cross-widget reference (and no drift) is possible.
            // Q sits nearest the pill's left, the arc sweeps up over the pill (owner design).
            // WO-750 mobile-input ruling (owner 2026-07-19): this is a touch game — the ability
            // ICON carries identity, so the medallions render with NO Q/W/E/R key-letter badge.
            // The keyboard/gamepad bindings stay live in code (PC/dev fallback); they are just
            // never surfaced on the touch HUD. Pass null keyBadge -> StyleAsRoundMedallion builds
            // no key chip.

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                Vector2 min, max;
                if (combat)
                {
                    // Placeholder rect — CombatArcLayout611 assigns the real pill-relative
                    // rect once the row's layout resolves (rect is 0x0 at build time).
                    min = Vector2.zero;
                    max = new Vector2(0.01f, 0.01f);
                }
                else
                {
                    min = new Vector2(i * 0.25f + 0.01f, 0.05f);
                    max = new Vector2((i + 1) * 0.25f - 0.01f, 0.95f);
                }
                _abilitySlots[i] = ElarionUiKit.BuildActionSlot(row.transform, min, max,
                    () => OnAbilitySlotTapped(slot));
                if (combat)
                {
                    // WO-750: null keyBadge — no Q/W/E/R letter on the touch medallion (icon = identity).
                    ElarionUiKit.StyleAsRoundMedallion(_abilitySlots[i], null);
                    _abilityGlows[i] = ElarionUiKit.AddSoftCooldownGlow(_abilitySlots[i]);
                }
            }
            if (combat)
            {
                var arc = row.AddComponent<CombatArcLayout611>();
                var meds = new RectTransform[4];
                for (int i = 0; i < 4; i++) meds[i] = (RectTransform)_abilitySlots[i].root.transform;
                arc.Medallions = meds;
            }
            Register("abilityRow", WrapAsWidget("abilityRow", row));
        }

        private void BuildAssignableSkillRow(Transform pool)
        {
            var row = new GameObject("AssignableSkillRow", typeof(RectTransform));
            row.transform.SetParent(pool, false);
            var rrt = (RectTransform)row.transform;
            bool combat = FeatureFlags.CombatHud611;
            // WO-611: the hot-swap bar (+ the potions region to its right) HOUSED full-width; the
            // potion slots (separate, later-registered widgets in the same ActionBar mount) render
            // over the housing. Non-combat keeps the WO-609 left-2/3 row.
            rrt.anchorMin = new Vector2(0f, 0.05f);
            rrt.anchorMax = new Vector2(combat ? 1f : 0.68f, 0.95f);
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            if (combat)
                ElarionUiKit.BuildActionBarHousing(row.transform, Vector2.zero, Vector2.one);

            _assignableSlots = new ElarionUiKit.ActionSlotHandle[4];
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                // Combat: pack the 4 hot-swap slots into the left ~62% so the potions sit to the right,
                // both housed. Non-combat: the original quarter-width row.
                float x0 = combat ? i * 0.15f + 0.02f : i * 0.25f + 0.01f;
                float x1 = combat ? (i + 1) * 0.15f - 0.005f : (i + 1) * 0.25f - 0.01f;
                float y0 = combat ? 0.12f : 0.05f, y1 = combat ? 0.88f : 0.95f;
                _assignableSlots[i] = ElarionUiKit.BuildActionSlot(row.transform,
                    new Vector2(x0, y0), new Vector2(x1, y1),
                    () => HudCommands.AssignableCast(slot));
                // WO-611 (capture 07-05): the tan/khaki Blink Action_Bar_Slot faces dominated the
                // housed bar — restyle each housed slot as the mockup's obsidian steel cell.
                if (combat) ElarionUiKit.StyleAsObsidianCell(_assignableSlots[i]);
            }
            Register("assignableSkillRow", WrapAsWidget("assignableSkillRow", row));
        }

        private void BuildStatusRow(Transform pool, string widgetId, out ElarionUiKit.ActionSlotHandle[] slots)
        {
            var row = new GameObject(widgetId, typeof(RectTransform));
            row.transform.SetParent(pool, false);
            var rrt = (RectTransform)row.transform;
            // Sit below the nameplate/target frame in the shared area mount (WO-609 layout).
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(1f, 0.38f);
            rrt.offsetMin = Vector2.zero;
            rrt.offsetMax = Vector2.zero;

            slots = new ElarionUiKit.ActionSlotHandle[StatusSlotCount];
            for (int i = 0; i < StatusSlotCount; i++)
            {
                float w = 1f / StatusSlotCount;
                float x0 = i * w + 0.005f, x1 = (i + 1) * w - 0.005f;
                slots[i] = ElarionUiKit.BuildActionSlot(row.transform,
                    new Vector2(x0, 0.05f), new Vector2(x1, 0.95f));
                if (slots[i].button != null) slots[i].button.interactable = false;
                slots[i].root.SetActive(false);
            }
            Register(widgetId, WrapAsWidget(widgetId, row));
        }

        private void BuildPotionSlots(Transform pool)
        {
            _itemSlot = ElarionUiKit.BuildActionSlot(pool,
                new Vector2(0.82f, 0.10f), new Vector2(0.99f, 0.95f), OpenItemPicker);
            var itemIcon = UiStyle.Icon("potion", "consumable", "bag");
            if (itemIcon != null) _itemSlot.SetIcon(itemIcon);
            _itemSlot.SetCaption("ITEM");
            _itemSlot.showZero = true;
            if (FeatureFlags.CombatHud611) ElarionUiKit.StyleAsRoundMedallion(_itemSlot);
            ElarionUiKit.StyleAsStackBadge(_itemSlot);
            SeatStackBadgeInMedallion(_itemSlot);   // WO-1468
            Register("itemSlot", WrapAsWidget("itemSlot", _itemSlot.root));
        }

        private void OpenItemPicker()
        {
            if (_itemPicker != null || _itemUseInFlight) return;

            if (_itemPickerPanelHandle == null)
                _itemPickerPanelHandle = PanelManager.RegisterBattleAllowed(
                    "Combat Item Picker", CloseItemPicker, () => _itemPicker != null);

            // ⛔ BUILD FIRST, ANNOUNCE LAST — THE PROBE MUST BE ANSWERABLE WHEN THE VERIFY RUNS.
            // WO-1301: NotifyOpened used to be called HERE, three lines before `_itemPicker` was
            // assigned. Its WO-465 visibility verify runs SYNCHRONOUSLY inside that call and invokes
            // the probe registered just above — `() => _itemPicker != null` — which was therefore
            // false BY CONSTRUCTION on every open (the guard on the first line of this method proves
            // `_itemPicker` is null on entry, so there was no path where it reported correctly).
            // Result: a FlowTrace.Fail -> LogError -> a new F8 error capture on every single picker
            // open, burying the owner's real flags. The arbiter was right to ask; the caller asked
            // it too early. The detector is NOT weakened — see the null-build branch below, which
            // still routes a genuinely blank picker through the same verify.
            // WO-1360: PLAYER-OWNED. The picker is open until the player picks or backs out.
            // WO-1369: the REQUIRED liveness probe is the SAME expression already registered with
            // PanelManager four lines up (`() => _itemPicker != null`) - one liveness concept for
            // this picker, not two that can disagree. It also covers the case OnDisable cannot: a
            // picker canvas destroyed by something other than CloseItemPicker.
            _itemPickerHold = WorldHold.AcquirePlayerOwned(WorldHold.ReasonCombatItemPicker,
                () => this != null && _itemPicker != null);
            _itemPicker = ElarionUiKit.BuildObsidianModal("CombatItemPicker", "CHOOSE AN ITEM",
                new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.82f), CloseItemPicker,
                sortingOrder: 31500);

            if (_itemPicker == null || _itemPicker.chrome == null)
            {
                // THE GENUINE GHOST MODAL. The build failed (null handle, or a handle with no
                // chrome — a shell with nothing in it), so there is nothing usable on screen.
                // Announce anyway so the arbiter's IsOpen verify runs and REPORTS the ghost: this
                // is exactly the WO-465 case the check exists for and it must still fire.
                // `_itemPicker` is cleared FIRST so the probe answers truthfully — a half-built
                // handle would otherwise report open and hide the very failure we are surfacing.
                var stillborn = _itemPicker;
                _itemPicker = null;
                PanelManager.NotifyOpened(_itemPickerPanelHandle);
                if (stillborn != null && stillborn.canvas != null) Destroy(stillborn.canvas);
                // Tear down so no world hold and no half-built canvas leaks on this path.
                CloseItemPicker();
                return;
            }

            MedievalUiSkin.ApplyShell(_itemPicker.chrome, compact: true);

            var body = _itemPicker.chrome.layout.body;
            // The legacy title zone is designed around a tall left medallion and floats above
            // this compact art. Replace only its presentation with a body-seated heading.
            if (_itemPicker.chrome.title != null) _itemPicker.chrome.title.gameObject.SetActive(false);
            if (_itemPicker.chrome.layout.header != null)
                _itemPicker.chrome.layout.header.gameObject.SetActive(false);
            // The procedural shell creates its title underline as a direct sibling rather
            // than inside the header zone. Once the legacy header is replaced, hide that
            // orphan too; otherwise it floats above the compact picker as an unexplained line.
            if (_itemPicker.chrome.content != null)
            {
                var contentTransform = _itemPicker.chrome.content.transform;
                for (int i = 0; i < contentTransform.childCount; i++)
                {
                    var child = contentTransform.GetChild(i);
                    if (child != null && child.name == "Rule") child.gameObject.SetActive(false);
                }
            }
            var pickerTitle = ElarionUiKit.Label(body, "CHOOSE AN ITEM",
                0.74f, 0.89f, ElarionUi.Gold, ElarionUi.FontTitle,
                TextAlignmentOptions.Center, 0.12f, 0.88f, bold: true);
            pickerTitle.characterSpacing = 3f;
            pickerTitle.enableAutoSizing = false;
            pickerTitle.fontSize = 48f;
            pickerTitle.enableWordWrapping = false;
            pickerTitle.raycastTarget = false;

            var hint = ElarionUiKit.Label(body, "Gameplay is paused while you choose.",
                0.59f, 0.71f, ElarionUi.ParchmentDim, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.12f, 0.88f);
            hint.enableAutoSizing = false;
            hint.fontSize = 28f;
            hint.enableWordWrapping = false;
            hint.raycastTarget = false;

            _itemHealButton = ElarionUiKit.BuildObsidianButton(body, "HEALING POTION",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.12f, 0.34f), new Vector2(0.88f, 0.51f), () => UseItem(false));
            _itemManaButton = ElarionUiKit.BuildObsidianButton(body, "MANA DRAUGHT",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.12f, 0.13f), new Vector2(0.88f, 0.30f), () => UseItem(true));
            MedievalUiSkin.ApplyButton(_itemHealButton, primary: true);
            MedievalUiSkin.ApplyButton(_itemManaButton, primary: false);
            _itemHealLabel = _itemHealButton != null ? _itemHealButton.GetComponentInChildren<TMP_Text>() : null;
            _itemManaLabel = _itemManaButton != null ? _itemManaButton.GetComponentInChildren<TMP_Text>() : null;
            if (_itemHealLabel != null) { _itemHealLabel.enableAutoSizing = false; _itemHealLabel.fontSize = 34f; }
            if (_itemManaLabel != null) { _itemManaLabel.enableAutoSizing = false; _itemManaLabel.fontSize = 34f; }
            RefreshItemPicker();

            // ANNOUNCE. The picker is fully built, so the probe `() => _itemPicker != null` can
            // answer truthfully and the arbiter's synchronous verify sees IsOpen=true.
            if (!PanelManager.NotifyOpened(_itemPickerPanelHandle))
            {
                // Refused (battle-lock). PanelManager already invoked handle.Close for us; call it
                // again — CloseItemPicker is idempotent (every field is null-checked then nulled) —
                // so the world hold is released and the canvas destroyed on this path too.
                CloseItemPicker();
                return;
            }

            FlowTrace.Step("HudKit", "combat Item picker opened; world hold acquired");
        }

        private void RefreshItemPicker()
        {
            if (_itemPicker == null) return;
            var c = _models != null ? _models.Consumables : null;
            int hp = c != null ? c.HpPotionCount : 0;
            int mana = c != null ? c.ManaPotionCount : 0;
            if (_itemHealLabel != null) _itemHealLabel.text = "HEALING POTION  x" + hp;
            if (_itemManaLabel != null) _itemManaLabel.text = "MANA DRAUGHT  x" + mana;
            if (_itemHealButton != null)
                _itemHealButton.interactable = HudCommands.HasPotion && c != null && c.HpCooldownRemaining <= 0f;
            if (_itemManaButton != null)
                _itemManaButton.interactable = HudCommands.HasManaPotion && c != null && c.ManaCooldownRemaining <= 0f;
        }

        private void UseItem(bool mana)
        {
            if (_itemUseInFlight) return;
            var c = _models != null ? _models.Consumables : null;
            bool eligible = c != null && (mana
                ? HudCommands.HasManaPotion && c.ManaCooldownRemaining <= 0f
                : HudCommands.HasPotion && c.HpCooldownRemaining <= 0f);
            if (!eligible) { RefreshItemPicker(); return; }

            _itemUseInFlight = true;
            try
            {
                // The authoritative Village command performs the final inventory check and
                // consumption. Closing only after it returns prevents rapid-tap duplication.
                if (mana) HudCommands.ManaPotion(); else HudCommands.Potion();
                CloseItemPicker();
            }
            finally { _itemUseInFlight = false; }
        }

        private void CloseItemPicker()
        {
            if (_itemPicker != null && _itemPicker.canvas != null) Destroy(_itemPicker.canvas);
            _itemPicker = null;
            _itemHealButton = _itemManaButton = null;
            _itemHealLabel = _itemManaLabel = null;
            _itemPickerHold?.Dispose();
            _itemPickerHold = null;
            if (_itemPickerPanelHandle != null) PanelManager.NotifyClosed(_itemPickerPanelHandle);
        }

        private void BuildTargetCycle(Transform pool)
        {
            var col = new GameObject("TargetCycle", typeof(RectTransform));
            col.transform.SetParent(pool, false);
            var crt = (RectTransform)col.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;

            _cycleRows = new ElarionUiKit.NameplateHandle[4];
            _cycleIds = new string[4];
            for (int i = 0; i < 4; i++)
            {
                float x0 = i * 0.25f + 0.005f, x1 = (i + 1) * 0.25f - 0.005f;
                _cycleRows[i] = ElarionUiKit.BuildNameplate(col.transform, ElarionUiKit.NameplateKind.Enemy,
                    new Vector2(x0, 0.05f), new Vector2(x1, 0.95f));
                int idx = i;
                // Tap-to-select: a Button over the compact plate, firing the Core command.
                // (§5 note, reported: the factory nameplate ships raycast-off with no tap
                // helper — a BuildNameplate onTap / TapTarget kit ask is filed to P1; this
                // AddComponent<Button> is the sanctioned interim, no visuals constructed.)
                var btn = _cycleRows[i].root.GetComponent<Button>();
                if (btn == null) btn = _cycleRows[i].root.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                if (_cycleRows[i].plate != null) _cycleRows[i].plate.raycastTarget = true;
                btn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(_cycleIds[idx])) HudCommands.CycleSelect(_cycleIds[idx]);
                });
                _cycleRows[i].root.SetActive(false);
            }
            Register("targetCycle", WrapAsWidget("targetCycle", col));
        }

        private void BuildResourceChips(Transform pool)
        {
            // WO-1221: Crystals joins the town rail. The severity line of the ticket is "the player
            // cannot see Wood / Iron / Stone / Crystals anywhere in town", and Crystals is the one
            // resource with no other town readout at all. Note Crystals is UNCAPPABLE by design
            // (TownBankCapacity.UncappableResources, owner ruling WO-901 §6), so its row is fed by
            // the chip's own SetAmount in OnEconomy — SetCappedResourceValue early-returns on it.
            // The word "Stone" stays paired with CurrencyKind.Food (canon §7 naming).
            var kinds = new[]
            {
                ElarionUiKit.CurrencyKind.Wood, ElarionUiKit.CurrencyKind.Iron,
                ElarionUiKit.CurrencyKind.Food, ElarionUiKit.CurrencyKind.Crystal,
            };
            var names = new[] { "Wood", "Iron", "Stone", "Crystals" };

            // Collapsed chip: gold + a "+N" hint. TAP TOGGLES the stack open/shut
            // (WO-1221 owner ruling 2026-08-26 — the old 6-second peek is retired).
            // WO-697 icon-first: the coin icon carries identity; "Gold" is the no-art
            // fallback tag only (builder-enforced — the chip is never a naked number).
            // ⭐ WO-1660 — THE CHIP'S HEIGHT IS AUTHORED IN PX, AT THE TAP FLOOR.
            // ---------------------------------------------------------------------
            // WHAT SHIPPED (owner device, Seeker 2670x1200, APK 2026.09.10.363786,
            // Builds/device-frames/2026-09-10_1019_363786_logcat.txt PID 8062 @10:17:26.858):
            //     [touch-oracle] CLAMP FIRED .../Widget_resourceChipsCollapsed/CurrencyChip_Gold:
            //     authored 398.1x103.5 -> grown 398.1x112 (1.0x on W, 1.08x on H).
            // The band was authored as a FRACTION of the ActionRail widget (y 0.45..1.0, i.e.
            // 0.55 of a 188.2 ref px band = 103.5 px) while every sibling rail element is
            // authored in px off RailChipHeightPx (see RailBand / BuildRailChip). A fraction
            // cannot know the floor, so the floor was missed by 8.5 px at this aspect and only
            // ClampMinTouch rescued it — symmetrically about the centre, spilling into both
            // neighbours, exactly what LayoutOracle's ASSERT A text forbids relying on.
            // ⛔ THE CLAMP IS UNCHANGED AND STAYS ARMED (see ElarionUiKit.cs:1082-1086, and the
            // ClampMinTouch(tapBtn) call below). It is the correct rescue for a build that
            // shipped wrong; this authoring simply gives it nothing to rescue.
            // THE FORM: point-anchor y to the ActionRail band's TOP and hang a fixed px band off
            // it — the same pivot-then-sizeDelta idiom RailBand uses for the Echoes/Builders
            // chips, so all three rail elements are denominated in the one constant. x is
            // deliberately left STRETCHED (0.05..1.0): the collapsed chip is 398 px wide on the
            // Seeker, already far above the floor, the four expanded rows inherit that width,
            // and HudRailGutter's stretched branch owns the right edge (it writes offsetMax.x
            // only — the two axes never cross).
            // ⚠ DELTA vs THE SHIPPED FRAME, stated rather than hidden: the clamp grew the chip
            // symmetrically, so its top edge sat 4.25 ref px ABOVE the ActionRail top. Authored
            // this way the top edge lands ON it, so the chip, the "+N" hint and the expanded
            // stack all seat ~4 ref px lower than APK 363786. Everything below derives
            // (HudRailClearance measures the laid-out bottom edge), so nothing needs a second
            // edit — but the owner's device re-frame will show that shift.
            _resGoldOnly = ElarionUiKit.CurrencyChip(pool, ElarionUiKit.CurrencyKind.Gold,
                new Vector2(0.05f, 1f), new Vector2(1f, 1f), primary: true, tag: "Gold");
            var goldRt = (RectTransform)_resGoldOnly.root.transform;
            goldRt.pivot = new Vector2(0.5f, 1f);
            goldRt.sizeDelta = new Vector2(0f, RailChipHeightPx);
            goldRt.anchoredPosition = Vector2.zero;
            var tapGo = _resGoldOnly.root;
            if (_resGoldOnly.plate != null)
            {
                var medievalChip = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                if (medievalChip != null)
                {
                    _resGoldOnly.plate.sprite = medievalChip;
                    _resGoldOnly.plate.type = Image.Type.Simple;
                    _resGoldOnly.plate.preserveAspect = false;
                    _resGoldOnly.plate.color = Color.white;
                }
            }
            var goldFrame = tapGo.transform.Find("PlateFrame")?.GetComponent<Image>();
            if (goldFrame != null)
            {
                var medievalChip = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
                if (medievalChip != null)
                {
                    goldFrame.sprite = medievalChip;
                    goldFrame.type = Image.Type.Simple;
                    goldFrame.preserveAspect = false;
                    goldFrame.fillCenter = true;
                    goldFrame.color = Color.white;
                }
            }
            var tapBtn = tapGo.AddComponent<Button>();
            tapBtn.transition = Selectable.Transition.None;
            tapBtn.onClick.AddListener(() =>
            {
                // Direct raise. The previous consumer only flipped `_resChipsExpanded` and
                // waited for LateTick to SetActive a SECOND occupancy widget that hud-areas.json
                // never occupies — tap logged EXPAND, tmp/resources-expanded-105803.png showed
                // only the gold chip (built-but-invisible).
                SetResourcePanelOpen(!_resChipsExpanded);
            });
            _resGoldOnly.plate.raycastTarget = true;   // the chip is the tap target here
            ElarionUiKit.ClampMinTouch(tapBtn);

            // ⭐ WO-1221 bounce 2026-08-27 — THE EXPANDED PIXELS LIVE ON THE GOLD CHIP.
            // Occupancy (hud-areas.json calm(town)/calm(explore) actionRail) lists ONLY
            // resourceChipsCollapsed. Register() deactivates every widget; occupancy is the
            // only thing that turns one on (docs/MASTER_CATALOG/hud.md). The Wood/Iron/Stone/
            // Crystals row used to live on a SECOND widget (`resourceChips` / `_resDock`) that
            // no posture occupies. LateTick raised that WRAPPER — a full-ActionRail empty
            // dock — and logged "expanded (opener live=True)" while the capture inside the
            // window showed only gold 1034. ApplyPosture then deactivates any unoccupied
            // widget; HudKitController is AddComponent'd BEFORE PostureEvaluator on the same
            // GameObject, so Update can probe-report painted and occupancy can still kill
            // the surface before render.
            //
            // Fix: four chips are CHILDREN of tapGo, hanging BELOW it (owner mockup). Same
            // width as gold, silhouette identity via CurrencyChip (icon; word tag only when
            // the icon is missing — never colour alone). Occupancy already shows the parent.
            // Toggle is SetActive on this child. Hiding the opener hides the stack.
            float panelH = kinds.Length * ResRowHeightPx + (kinds.Length - 1) * ResRowGapPx;
            _resExpandedRow = new GameObject("ResourceExpandedStack", typeof(RectTransform));
            _resExpandedRow.transform.SetParent(tapGo.transform, false);
            var ert = (RectTransform)_resExpandedRow.transform;
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(1f, 0f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.sizeDelta = new Vector2(0f, panelH);
            ert.anchoredPosition = new Vector2(0f, -RailGapPx);

            _resChips = new ElarionUiKit.CurrencyChipHandle[kinds.Length];
            _cappedResourceValues = new TMP_Text[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                var row = new GameObject("ResRow_" + names[i], typeof(RectTransform));
                row.transform.SetParent(_resExpandedRow.transform, false);
                var rowRt = (RectTransform)row.transform;
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, ResRowHeightPx);
                rowRt.anchoredPosition = new Vector2(0f, -(i * (ResRowHeightPx + ResRowGapPx)));

                // WO-1205 — OWNER RULING 2026-08-25: "just the count and not the wood name
                // just the chip". The row is [icon] <count>.
                //
                // ⛔ THE COLOURBLIND GUARD IS RE-POINTED, NOT DELETED. The name was the
                // identity carrier for the no-art case (owner is red/green colourblind; an
                // icon-only row whose icon fails to resolve is unidentifiable). That duty now
                // rides CurrencyChip's OWN icon-first fallback: `tag: names[i]` below renders
                // the word ONLY when the icon sprite comes up null (ElarionUiKitObsidian
                // CurrencyChip: `bool hasTag = iconSprite == null`). Icon resolves -> [icon] 80.
                // Icon missing -> "Wood 80". A naked number never ships either way.
                _resChips[i] = ElarionUiKit.CurrencyChip(row.transform, kinds[i],
                    new Vector2(0f, 0f), new Vector2(1f, 1f), primary: false,
                    tag: names[i]);
                SplitResourceRowChip(_resChips[i]);
                _cappedResourceValues[i] = _resChips[i].amount;
            }

            // COLLAPSED is the resting state (owner 2026-08-25). The stack is built inert;
            // SetResourcePanelOpen is the ONE owner of the SetActive.
            _resChipsExpanded = false;
            _resExpandedRow.SetActive(false);

            // ⭐ WO-1221 - THE "+N" HINT (owner ruling 2026-08-26): the collapsed chip is the ONLY
            // resource UI on screen by default, so Gold alone gives the player no reason to believe
            // anything is behind it. The hint says how many resource rows the tap will reveal.
            // It is a WORD-AND-NUMBER tell, never a colour or a glyph-only affordance (the owner is
            // red/green colourblind), and it is COUNTED from the built rows rather than authored -
            // add a fifth resource and the hint says +5 with no second edit.
            // It hangs BELOW the chip's right edge rather than inside it: the chip's amount is
            // right-aligned to 0.94 and would collide with anything seated in the same band.
            var hint = ElarionUiKit.Label(tapGo.transform, "+" + kinds.Length, 0f, 1f,
                                          ElarionUi.Parchment, ElarionUi.FontMicro,
                                          TextAlignmentOptions.MidlineRight, 0f, 1f);
            hint.raycastTarget = false;
            _resHintLabel = hint;
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(1f, 0f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, ResHintHeightPx);
            hrt.anchoredPosition = new Vector2(0f, -2f);
            ElarionUiKit.FitSingleLine(hint);
            // Same shared gutter as the town rail — one right edge in every posture.
            tapGo.AddComponent<HudRailGutter>();
            Register("resourceChipsCollapsed", WrapAsWidget("resourceChipsCollapsed", tapGo));
        }

        // =====================================================================
        // MODEL BINDING — VM Changed events only (§1.1 rule 4 / §5 rule 3).
        // =====================================================================

        private void BindModels()
        {
            BindActionBar();   // WO-835: model-independent of IHudModel — bind first, always
            var m = _models;
            if (m == null)
            {
                // HudModelHost registers after scene load; retry next frame(s).
                FlowTrace.Warn("HudKit", "CoreServices.HudModel not registered yet — binding deferred");
                InvokeRepeating(nameof(TryLateBind), 0.25f, 0.25f);
                return;
            }
            BindAll(m);
        }

        private void TryLateBind()
        {
            var m = CoreServices.HudModel;
            if (m == null) return;
            CancelInvoke(nameof(TryLateBind));
            _models = m;
            BindAll(m);
        }

        private void BindAll(IHudModel m)
        {
            Sub(m.HeroVitals, OnVitals);        OnVitals();
            Sub(m.Economy, OnEconomy);          OnEconomy();
            Sub(m.Wave, OnWave);                OnWave();
            Sub(m.World, OnWorld);              OnWorld();
            Sub(m.Abilities, OnAbilities);      OnAbilities();
            Sub(m.Assignable, OnAssignable);    OnAssignable();
            Sub(m.Consumables, OnConsumables);  OnConsumables();
            Sub(m.PlayerStatus, OnPlayerStatus); OnPlayerStatus();
            Sub(m.TargetStatus, OnTargetStatus); OnTargetStatus();
            Sub(m.TargetCycle, OnTargetCycle);  OnTargetCycle();
            _targetFrame.Bind(m.Target);
            _castBar.Bind(m.Cast);

            // WO-1225: the View owns the gold chip, so the View renders the acknowledgement.
            // Village raises through the Core seam (DeNelle.HUD never references DeNelle.Village
            // -- CLAUDE.md §5), and the unsubscribe rides the same teardown list as every model.
            RewardCelebration.Requested += OnRewardCelebrationRequested;
            _unsubscribe.Add(() => RewardCelebration.Requested -= OnRewardCelebrationRequested);

            FlowTrace.Step("HudKit", "models bound (vitals/economy/wave/world/abilities/cycle/target/cast) " +
                                     "+ RewardCelebration acknowledgement listener");
        }

        // =====================================================================
        // WO-835 ACTION BAR — the View consumes the Core model's array, only.
        // =====================================================================

        // Bind the shared Core applicability model. The View's ONLY action-bar inputs
        // from here on are ActiveButtonsChanged (render the new array) and
        // RaidsDimmedChanged (tint the Raids face) — zero predicate reads remain.
        private void BindActionBar()
        {
            // The approved adaptive HUD uses one stable five-medallion peaceful dock. Its
            // commands are the same authoritative routes as the retired repacking faces, but
            // its geometry is posture-owned through hud-areas.json and never changes with
            // transient Talk/Raid applicability. Keep the old faces constructed for reversal
            // compatibility, while leaving them unoccupied and out of the render pass.
            if (_peacefulDockRoot != null)
            {
                for (int i = 0; i < _barButtons.Length; i++)
                    if (_barButtons[i] != null) _barButtons[i].SetActive(false);
                FlowTrace.Step("HudKit", "adaptive peaceful dock owns the actionBar; legacy repacker retired");
                return;
            }
            _barModel = HudActionBarModel.Shared;
            _barModel.ActiveButtonsChanged += ApplyActionBar;
            _unsubscribe.Add(() => _barModel.ActiveButtonsChanged -= ApplyActionBar);
            _barModel.RaidsDimmedChanged += ApplyRaidsDim;
            _unsubscribe.Add(() => _barModel.RaidsDimmedChanged -= ApplyRaidsDim);
            _barModel.ManageFaceChanged += ApplyManageFaceTell;
            _unsubscribe.Add(() => _barModel.ManageFaceChanged -= ApplyManageFaceTell);
            // Sync to the model's CURRENT state (a scene-swap kit binds an already-live
            // shared model whose set may not change again for a while).
            ApplyActionBar();
            ApplyRaidsDim();
            ApplyManageFaceTell();
        }

        // WO-1027 — paint the session-shape numeral onto the Manage face.
        // ---------------------------------------------------------------------
        // The View decides NOTHING here: HudActionBarModel owns the words ("Manage" when every
        // line is cooking, "Manage - 2 of 3 idle" when they are not). CoC would put a red badge
        // here; the owner is red/green colourblind, so the ache is carried by a NUMERAL that has
        // no hue to get wrong. ⛔ Never add a tint or a badge to "help" — if it does not read,
        // the model's WORD gets clearer.
        private void ApplyManageFaceTell()
        {
            Guard.Try("HudKit", "apply manage face tell", () =>
            {
                if (_manageButtonLabel == null) return;
                // WO-1144: the model's ManageFaceLabel is the one-line SENTENCE ("Manage - 2 of 3
                // idle"). A bar face is ~144 ref px of label rect — about ten characters at the
                // legibility floor — so painting the sentence is what the fleet captured as
                // "Manag...". The face paints the WORD and the model's short BADGE on a second
                // line instead; the sentence keeps its home in the model (and in this trace).
                string badge = _barModel != null ? _barModel.ManageFaceBadge : "";
                string face = string.IsNullOrEmpty(badge)
                    ? HudActionBarModel.ManageBaseLabel
                    : HudActionBarModel.ManageBaseLabel + "\n" + badge;
                if (string.IsNullOrEmpty(face) ||
                    string.Equals(_manageButtonLabel.text, face, StringComparison.Ordinal)) return;
                _manageButtonLabel.text = face;
                FlowTrace.Step("HudKit", "Manage face text -> '" + face.Replace("\n", " / ") +
                               "' (model sentence: '" +
                               (_barModel != null ? _barModel.ManageFaceLabel : HudActionBarModel.ManageBaseLabel) +
                               "'; the idle-line ache is carried in WORDS + a NUMBER, never hue).");
            });
        }

        // Render pass (purely mechanical): SetActive + position EXACTLY the buttons in
        // the model's ordered array — constant slot width, group centered in the zone,
        // holes impossible by construction. Runs only on ActiveButtonsChanged (and the
        // bind-time sync), never per-frame.
        private void ApplyActionBar()
        {
            if (_barModel == null) return;
            var active = _barModel.Active;
            int n = active.Count;
            float groupW = n > 0 ? n * BarSlotW + (n - 1) * BarGap : 0f;
            float x = (1f - groupW) * 0.5f;

            for (int i = 0; i < _barButtons.Length; i++)
                if (_barButtons[i] != null && _barButtons[i].activeSelf)
                    _barButtons[i].SetActive(false);

            for (int i = 0; i < n; i++)
            {
                int idx = (int)active[i];
                var rt = _barButtonRects[idx];
                var go = _barButtons[idx];
                if (rt == null || go == null) continue;
                rt.anchorMin = new Vector2(x, BarY0);
                rt.anchorMax = new Vector2(x + BarSlotW, BarY1);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                go.SetActive(true);
                x += BarSlotW + BarGap;
            }
            FlowTrace.Step("HudKit", "action bar repacked: " + n + " face(s) centered");
        }

        // Raids dim visuals (WO-820 semantics via the model's decision): tint face +
        // label toward Disabled, restore the BUILT colours; interactable is never
        // touched, so a dimmed tap still reaches the drillmaster redirect.
        //
        // WO-1008 — COLOUR IS NEVER THE TELL. The owner is red/green colourblind, so a grey
        // tint communicates NOTHING on its own; the face must SAY why it is greyed. The model
        // owns the words (HudActionBarModel.RaidsFaceLabel: "Raids" live, "Raids 0/5" nothing
        // trained, "Raids 3/5" army not full) — this View only paints them. Still zero
        // predicates here: the reason is decided in Core.
        private void ApplyRaidsDim()
        {
            bool dim = _barModel != null && _barModel.RaidsDimmed;
            if (_raidsButtonImage != null)
                _raidsButtonImage.color = dim ? ElarionUi.Disabled : _raidsImageBuiltColor;
            if (_raidsButtonLabel != null)
            {
                _raidsButtonLabel.color = dim ? ElarionUi.Disabled : _raidsLabelBuiltColor;
                // WO-1219: the model's RaidsFaceLabel is the one-line string ("Raids 0/5"). A bar
                // face is ~144 ref px of label rect - about ten characters at the legibility floor,
                // side padding included - so painting the one-liner is what the owner captured as
                // "Raids ...". The face paints the WORD and the model's short BADGE on a second
                // line instead; the one-liner keeps its home in the model (and in this trace).
                string badge = _barModel != null ? _barModel.RaidsFaceBadge : "";
                string face = string.IsNullOrEmpty(badge)
                    ? HudActionBarModel.RaidsBaseLabel
                    : HudActionBarModel.RaidsBaseLabel + "\n" + badge;
                if (!string.IsNullOrEmpty(face) && !string.Equals(_raidsButtonLabel.text, face, StringComparison.Ordinal))
                {
                    _raidsButtonLabel.text = face;
                    FlowTrace.Step("HudKit", "Raids face text -> '" + face.Replace("\n", " / ") + "' (dim=" + dim +
                                   ", reason=" + (_barModel != null ? _barModel.RaidsDimReason.ToString() : "n/a") +
                                   "; model one-liner: '" +
                                   (_barModel != null ? _barModel.RaidsFaceLabel : HudActionBarModel.RaidsBaseLabel) +
                                   "') - the greyed state is carried in WORDS, never hue alone.");
                }
            }
        }

        private void Sub(HeroVitalsModel m, Action h)    { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(EconomyModel m, Action h)       { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(WaveModel m, Action h)          { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(WorldMetricsModel m, Action h)  { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(AbilityLoadoutModel m, Action h){ m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(AssignableLoadoutModel m, Action h){ m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(ConsumableHotbarModel m, Action h){ m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(StatusEffectsModel m, Action h) { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }
        private void Sub(TargetCycleModel m, Action h)   { m.Changed += h; _unsubscribe.Add(() => m.Changed -= h); }

        private void OnVitals()
        {
            var v = _models != null ? _models.HeroVitals : null;
            if (v == null) return;
            // WO-432: drive the shared PartyNameplate fills directly (fillAmount = hp/maxHp,
            // mp/maxMp). The fill sprites are non-null by contract so uGUI honours fillAmount.
            if (_vitals.HealthFill != null)
                _vitals.HealthFill.fillAmount = v.MaxHp > 0 ? Mathf.Clamp01((float)v.Hp / v.MaxHp) : 0f;
            if (_vitals.ManaFill != null)   // MP LIVE (§0 fix)
            {
                // WO-997 §3b: prefer the EXACT floats (sub-point regen reads); the ints stay
                // the fallback for any producer that never pushed them (sentinel -1).
                float curMana = v.ManaExact    >= 0f ? v.ManaExact    : v.Mana;
                float maxMana = v.MaxManaExact >  0f ? v.MaxManaExact : v.MaxMana;
                float target  = maxMana > 0f ? Mathf.Clamp01(curMana / maxMana) : 0f;
                if (!_manaFillBaseCaptured)
                {
                    _manaFillBaseColor = _vitals.ManaFill.color;
                    _manaFillBaseCaptured = true;
                }
                // A DOWNWARD jump is a spend — arm the brighten flash so burn-down reads.
                if (_manaFillTarget >= 0f && target < _manaFillTarget - ManaSpendThreshold)
                    _manaFlashUntil = Time.unscaledTime + ManaFlashSeconds;
                _manaFillTarget = target;
                if (_manaFillShown < 0f)   // first bind: snap, no ease-in from empty
                {
                    _manaFillShown = target;
                    _vitals.ManaFill.fillAmount = target;
                }
                else if (!Application.isPlaying)
                {
                    // Synchronous screenshot evidence has no runtime Update loop. Paint the
                    // authoritative target immediately so an empty capture cannot hide this row.
                    _manaFillShown = target;
                    _vitals.ManaFill.fillAmount = target;
                }
                // Steady-state easing runs in Update() (AnimateManaFill).
            }
            // FIX 2026-08-05: this rendered the CLASS WORD ("Ranger  Lv 1"), so nothing in the
            // game ever told a player who picked the Ranger that he is SYLAS. The nameplate now
            // shows the CANON NAME from Data/Canonical/en.json (hero.<job>.name), resolved through
            // HeroCanonNames in Core - the one reader HUD may legally reach (HUD -> Core only).
            // A missing file/key degrades to exactly the old capitalized class word, so the plate
            // can never go blank. NOTE: the nameplate has NO portrait Image socket today; adding
            // one is a layout change and is deliberately left as a follow-up, not smuggled in here.
            if (_vitals.NameLabel != null)
            {
                string heroName = string.IsNullOrEmpty(v.ClassId)
                    ? "Hero"
                    : DeNelle.Core.State.HeroCanonNames.ForJob(v.ClassId);
                // OWNER RULING 2026-09-02 (verbatim: "see how it says THrain Mana? Why is MAna
                // there"): the resource word is GONE from the nameplate. The plate is identity
                // only - name + level.
                //
                // WHY IT WAS HERE, so nobody re-adds it by reflex: WO-999 appended the class
                // resource identity (Mana / Vigor / Focus) "so the bar reads as a class economy,
                // not generic MP". That intent was sound; the ATTACHMENT POINT was wrong. It
                // labelled the MP BAR while living on the NAME line, so the plate read
                // "Thrain  Lv 2 - Mana", as if Mana were part of who he is.
                //
                // KNOWN AND ACCEPTED TRADE-OFF: with the word gone, the only thing separating the
                // two bars is that one is red and one is blue - meaning carried by COLOUR ALONE,
                // which this project otherwise forbids (the owner is red/green colourblind). She
                // was shown that trade-off explicitly and chose deletion anyway; her call stands.
                // If it ever needs to come back, put it ON THE BAR, never back on this line.
                // v.ResourceDisplayName is still produced by the model and is unused HERE only.
                _vitals.NameLabel.text = heroName + "  Lv " + Mathf.Max(1, v.Level);
            }
            // Owner 07-06: in-plate XP strip — fillAmount = xp/xpToNext, mirroring the HP/MP
            // fill-binding contract (§1.1). XpToNext<=0 = no HeroProgression data yet (the model
            // default; the producer never pushed) -> strip stays hidden, never blank/stuck-full.
            if (_vitals.XpRow != null)
            {
                bool hasXp = v.XpToNext > 0;
                if (_vitals.XpRow.activeSelf != hasXp) _vitals.XpRow.SetActive(hasXp);
                if (hasXp && _vitals.XpFill != null)
                {
                    _vitals.XpFill.fillAmount = Mathf.Clamp01((float)v.Xp / v.XpToNext);
                    if (!_xpStripBound)
                    {
                        _xpStripBound = true;
                        FlowTrace.Step("HudKit", "xp bar bound " + v.Xp + "/" + v.XpToNext);
                    }
                }
                // WO-1104: MEASURE the gain off this push versus the last one, and present it.
                if (hasXp) NoteXpGain(v.Xp, v.XpToNext, v.Level);
            }
            // Wisdom is intentionally not painted on the HUD; Hero -> Skills owns it.
        }

        private void OnEconomy()
        {
            var e = _models != null ? _models.Economy : null;
            if (e == null || _resChips == null) return;
            // Count-tween only — the no-flash law lives in CurrencyChip.SetAmount.
            SetCappedResourceValue(0, BankResource.Wood, e.Wood);
            SetCappedResourceValue(1, BankResource.Iron, e.Iron);
            SetCappedResourceValue(2, BankResource.Food, e.Food);
            // WO-1221: Crystals is uncapped by design, so SetCappedResourceValue early-returns on
            // it (TownBankCapacity.IsCapped == false). Feed the chip directly, or the row would
            // sit at its built value of 0 forever — a silently-wrong number, which is worse than
            // the missing row it replaced.
            if (_resChips.Length > 3 && _resChips[3] != null) _resChips[3].SetAmount(e.Crystals);

            // ⭐ WO-1225 -- THE MEASURED BALANCE. e.Gold is the wallet's post-grant value as the
            // economy model pushed it; it is the ONLY number the chip and the acknowledgement
            // ever count to. Nothing here reads the amount any grant path asked for.
            long measuredGold = e.Gold;
            bool celebrating = _goldCelebrateArmed && measuredGold > _goldPrev && _goldPrevValid;
            _resGoldOnly.SetAmount(measuredGold, animate: true,
                                   seconds: celebrating ? GoldCelebrateCountSeconds : 0.35f);
            NoteGoldGain(measuredGold);
        }

        /// <summary>
        /// WO-1225. A marquee grant asks for an acknowledgement. This renders from the last
        /// MEASURED wallet move when one has just landed (the usual case - see the look-back
        /// below), and otherwise ARMS a window and waits for one. Either way the number that
        /// reaches the screen is the wallet's, never the amount the caller asked for.
        /// </summary>
        private void OnRewardCelebrationRequested(RewardCelebration.Request r)
        {
            // Only Gold is anchored to a chip today (the rail's other chips live in the
            // collapsed panel and are not on screen at rest). Anything else is REFUSED OUT
            // LOUD rather than silently dropped.
            if (!string.Equals(r.Resource, "Gold", StringComparison.OrdinalIgnoreCase))
            {
                FlowTrace.Warn("HudKit",
                    $"reward celebration for '{r.Resource}' ({r.Reason}) IGNORED - only Gold has a " +
                    "persistent chip to anchor to; that resource's grant will go unacknowledged.");
                return;
            }

            _goldCelebrateRequested = r.RequestedAmount;
            _goldCelebrateReason = r.Reason ?? "unknown";
            _goldCelebrateOrigin = r.HasOrigin
                ? r.OriginScreen
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.52f);   // where a claim modal sits

            // ⚠ THE PUSH USUALLY ARRIVES *BEFORE* THE RAISE, AND THAT IS NOT A RACE TO PAPER OVER.
            // DailyChestController.Claim credits the wallet (EconomyService.AddCoins) and only THEN
            // calls AcknowledgeClaim. EconomyService.OnChanged is synchronous into EconomyProducer
            // (Village/HUD/HudModelProducers.cs:374) which pushes EconomyModel immediately, so
            // OnEconomy has already measured this grant by the time we are called. A pure
            // wait-for-the-next-push design would sit armed until it EXPIRED and show nothing —
            // exactly the silent grant this ticket exists to end.
            //
            // So the arm looks BACKWARD first, at the last measured positive move, and only waits
            // if there is nothing there. Both directions use the SAME measured numbers; nothing
            // here reconstructs an amount.
            bool lookback = _goldLastGainDelta > 0 && !_goldLastGainConsumed &&
                            (Time.unscaledTime - _goldLastGainTime) <= GoldCelebrateLookbackSeconds;
            if (lookback)
            {
                _goldCelebrateArmed = false;
                FlowTrace.Step("HudKit",
                    $"reward celebration ARMED reason={_goldCelebrateReason} requested={r.RequestedAmount} " +
                    "- the MEASURED wallet push had already landed; acknowledging it from the look-back " +
                    $"({(Time.unscaledTime - _goldLastGainTime):0.000}s ago, within {GoldCelebrateLookbackSeconds}s)");
                FireGoldAcknowledgement(_goldLastGainFrom, _goldLastGainTo, _goldLastGainDelta, replayChipCount: true);
                return;
            }

            _goldCelebrateArmed = true;
            _goldCelebrateUntil = Time.unscaledTime + GoldCelebrateWindowSeconds;
            FlowTrace.Step("HudKit",
                $"reward celebration ARMED reason={_goldCelebrateReason} requested={r.RequestedAmount} " +
                $"window={GoldCelebrateWindowSeconds}s - waiting for the MEASURED wallet push");
        }

        /// <summary>
        /// WO-1225 -- MEASURE the gold move off this push versus the last one, exactly as
        /// NoteXpGain does, and hand the acknowledgement the two measured balances.
        ///
        /// ⛔ The parameter is the post-grant balance READ FROM THE ECONOMY MODEL. The delta is
        /// derived from it and the previous push; the requested amount is compared against that
        /// delta for a shortfall WARN and is never displayed. An animation counting to a number
        /// that was never banked is a new hollow assertion -- the whole reason WO-1213's green
        /// log was worse than no log.
        /// </summary>
        private void NoteGoldGain(long measuredGold)
        {
            if (!_goldPrevValid)
            {
                // First bind is a BASELINE, never a gain (the whole banked total would fly).
                _goldPrevValid = true;
                _goldPrev = measuredGold;
                return;
            }

            long previous = _goldPrev;
            _goldPrev = measuredGold;
            long measuredDelta = measuredGold - previous;

            if (measuredDelta > 0)
            {
                // Record EVERY measured gain, armed or not, so a raise that arrives just after
                // its own wallet push can still acknowledge the real move (see the look-back in
                // OnRewardCelebrationRequested). This is a record of what happened, never a
                // trigger: an unclaimed gain shows nothing.
                _goldLastGainFrom = previous;
                _goldLastGainTo = measuredGold;
                _goldLastGainDelta = measuredDelta;
                _goldLastGainTime = Time.unscaledTime;
                _goldLastGainConsumed = false;
            }

            if (!_goldCelebrateArmed) return;

            if (measuredDelta <= 0)
            {
                // A spend or a no-op push inside the window. Keep waiting -- the grant's own
                // push may still be a frame away. TickGoldCelebration owns the timeout.
                return;
            }

            _goldCelebrateArmed = false;
            FireGoldAcknowledgement(previous, measuredGold, measuredDelta, replayChipCount: false);
        }

        /// <summary>
        /// WO-1225 -- render the acknowledgement from three MEASURED values and nothing else.
        /// <paramref name="replayChipCount"/> is set on the look-back path, where the chip has
        /// already snapped to the new balance before anyone asked for a celebration: it rewinds
        /// the chip to the measured PRE-grant balance and re-counts to the measured POST-grant
        /// one, so the climb the owner asked for is visible. Both ends are measured, so the
        /// rewind shows a number that was true a moment ago, never an invented one.
        /// </summary>
        private void FireGoldAcknowledgement(long previous, long measuredGold, long measuredDelta,
                                             bool replayChipCount)
        {
            _goldLastGainConsumed = true;

            if (measuredDelta < _goldCelebrateRequested)
            {
                // Same distinction Enemy.cs draws between rolled and credited: the shortfall is
                // the interesting fact, and the player is shown the SMALLER, TRUE number.
                FlowTrace.Warn("HudKit",
                    $"reward SHORTFALL reason={_goldCelebrateReason} requested={_goldCelebrateRequested} " +
                    $"creditedMeasured={measuredDelta} - the acknowledgement shows the credited amount, " +
                    "never the requested one.");
            }

            string headline = "+" + measuredDelta.ToString("N0", CultureInfo.InvariantCulture) + " Gold";
            var layer = RewardFlightLayer.Instance;
            var targetRect = (_resGoldOnly != null && _resGoldOnly.root != null)
                ? (RectTransform)_resGoldOnly.root.transform : null;

            if (replayChipCount && _resGoldOnly != null)
            {
                _resGoldOnly.SetAmount(previous, animate: false);
                _resGoldOnly.SetAmount(measuredGold, animate: true, seconds: GoldCelebrateCountSeconds);
            }

            if (layer != null)
                layer.Fly(headline, "Gold", _goldCelebrateOrigin, targetRect, previous, measuredGold);

            // §12 permanent trace: every value here is measured, and 'layerPresent'/'chipPresent'
            // separate "never asked" from "asked and nothing could render it" -- the exact
            // distinction WO-1213's green line could not make.
            FlowTrace.Step("HudKit",
                $"GOLD GAIN reason={_goldCelebrateReason} measuredDelta={measuredDelta} " +
                $"measuredBalance={previous}->{measuredGold} requested={_goldCelebrateRequested} " +
                $"headline='{headline}' replayChipCount={replayChipCount} " +
                $"layerPresent={(layer != null)} chipPresent={(targetRect != null)}");
        }

        /// <summary>
        /// WO-1225 timeout. An armed celebration whose wallet push never arrives means the grant
        /// did not reach the economy model at all -- a real defect, and one that must NOT be
        /// papered over with an animation. Fail loudly and show nothing.
        /// </summary>
        private void TickGoldCelebration()
        {
            if (!_goldCelebrateArmed) return;
            if (Time.unscaledTime < _goldCelebrateUntil) return;
            _goldCelebrateArmed = false;
            FlowTrace.Fail("HudKit",
                $"reward celebration EXPIRED reason={_goldCelebrateReason} requested={_goldCelebrateRequested} " +
                $"- no positive gold move reached the economy model within {GoldCelebrateWindowSeconds}s. " +
                "The grant did not land, or the model never pushed. NOTHING was shown, deliberately: " +
                "an acknowledgement for a grant we cannot measure would be a hollow assertion.");
        }

        private void SetCappedResourceValue(int index, BankResource resource, int current)
        {
            if (_cappedResourceValues == null || index < 0 || index >= _cappedResourceValues.Length)
                return;
            var label = _cappedResourceValues[index];
            if (label == null || !TownBankCapacity.IsCapped(resource)) return;
            // WO-1205 (owner: "recourse we should remove the /2000"): the row prints the COUNT
            // ONLY. The IsCapped read above stays — it still decides whether this row is a
            // capped resource at all — and CompactNumber still owns the formatting. The cap
            // itself is untouched; WO-1191's collect toasts remain its surviving voice.
            label.text = ElarionUi.CompactNumber(Mathf.Max(0, current));
        }

        private void OnWave()
        {
            var w = _models != null ? _models.Wave : null;
            if (w == null || _waveBlockRoot == null) return;

            // WAVE-CHROME LAW (§0): the block lives only in the calm(town) row (occupancy)
            // AND self-gates to BETWEEN-waves phases. Countdown shows ONLY when real.
            bool betweenWaves = w.Phase == WavePhase.Idle || w.Phase == WavePhase.Countdown ||
                                w.Phase == WavePhase.Cleared;
            bool activeWave = w.Phase == WavePhase.Active || w.Phase == WavePhase.Breached;
            // WO-1436: AND the hub scene gate. This is the SECOND writer of waveBlock's
            // visibility (ApplyHeartSceneGate is the other), and without this term the two
            // disagree in a raid: the gate hides the block every frame while a single wave-model
            // event re-shows it, giving a one-frame flash of a TOWN WAVE TIMER over a raid
            // battlefield. Waves attack the town; a raid has no wave and never can.
            RefreshHubGateCache();
            bool show = (betweenWaves || activeWave) && _heartGateIsHub;
            _waveBlockRoot.SetActive(show);
            if (!show) return;

            // WO-432: the wave label shows ONLY during an actual wave (Number > 0); the
            // village-at-rest state hides the label entirely instead of a resting caption.
            bool hasWave = w.Number > 0;
            _waveLabel.gameObject.SetActive(hasWave);
            if (hasWave) _waveLabel.text = "Wave " + w.Number;
            bool realCountdown = w.Phase == WavePhase.Countdown && w.CountdownRemaining > 0f;
            var labelRt = (RectTransform)_waveLabel.transform;
            var countdownRt = (RectTransform)_waveCountdown.transform;
            var progressRt = _waveProgress != null ? _waveProgress.track : null;
            float contentX1 = activeWave ? 0.97f : 0.60f;
            labelRt.anchorMin = new Vector2(0.03f, 0.50f);
            labelRt.anchorMax = new Vector2(contentX1, 0.96f);
            countdownRt.anchorMin = new Vector2(0.03f, 0.18f);
            countdownRt.anchorMax = new Vector2(contentX1, 0.49f);
            if (progressRt != null)
            {
                progressRt.anchorMin = new Vector2(0.05f, 0.07f);
                progressRt.anchorMax = new Vector2(activeWave ? 0.95f : 0.58f, 0.15f);
                progressRt.offsetMin = Vector2.zero;
                progressRt.offsetMax = Vector2.zero;
            }
            _waveCountdown.text = activeWave
                ? Mathf.Max(0, w.EnemiesLive) + " enemies remain"
                // WO-1407: "Next wave in 14m 15s", never "855s" - ElarionUi.Duration is the one
                // formatter (shared with WaveCountdownUI and the queue rail).
                : (realCountdown ? "Next wave in " + ElarionUi.Duration(Mathf.CeilToInt(w.CountdownRemaining)) : "");
            _waveProgress.SetValue(activeWave ? w.EnemiesLive : w.EnemiesTotal - w.EnemiesLive,
                Mathf.Max(1, w.EnemiesTotal));
            _waveProgress.track.gameObject.SetActive(w.EnemiesTotal > 0);
            // Owner 07-06 ("missing option to start wave now... they might be fully ready"):
            // the button used to HIDE during Countdown; with countdown now = active battle it
            // must stay available as the skip. Relabel contextually so one control = one action.
            if (_startWaveButton != null)
            {
                _startWaveButton.gameObject.SetActive(
                    betweenWaves && _startWaveAvailable);
                var swLabel = _startWaveButton.GetComponentInChildren<TMP_Text>(true);
                if (swLabel != null)
                {
                    string want = realCountdown ? "Start Now" : "Start Wave";
                    if (swLabel.text != want) swLabel.text = want;
                }
            }
        }

        private void OnWorld()
        {
            var wm = _models != null ? _models.World : null;
            if (wm == null) return;
            // WO-432: the Heart of Elarion drives the shared plate's HealthFill (mana row hidden).
            if (_heartPlate.HealthFill != null)
                _heartPlate.HealthFill.fillAmount = wm.HeartMaxHp > 0
                    ? Mathf.Clamp01((float)wm.HeartHp / wm.HeartMaxHp) : 0f;
        }

        private void OnAbilities()
        {
            var a = _models != null ? _models.Abilities : null;
            if (a == null || _abilitySlots == null) return;
            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                var h = _abilitySlots[i];
                // WO-611: a combat-HUD MEDALLION (glow driver present <=> flag was ON at build) always
                // RENDERS — the 07-05 capture showed NO arc in battle because every slot with no
                // resolved def (AbilityLoadoutProducer: equipped = def != null) was SetActive(false)
                // here, hiding the whole arc on an unassigned loadout. Empty = dimmed medallion + key
                // badge, non-interactable (mockup). Flag OFF (glows null) keeps the shipping behavior
                // byte-identical.
                bool medallion = _abilityGlows != null && i < _abilityGlows.Length && _abilityGlows[i] != null;
                if (i >= a.Slots.Count)
                {
                    _abilitySlotEquipped[i] = false;
                    h.root.SetActive(true);
                    SetEmptyMedallion(h);
                    continue;
                }
                var s = a.Slots[i];
                _abilitySlotEquipped[i] = s.Equipped;
                h.root.SetActive(true);
                if (!s.Equipped)
                {
                    SetEmptyMedallion(h);
                    continue;
                }
                if (medallion)
                {
                    if (h.frame != null) h.frame.color = Color.white;   // un-dim (colours baked in the face)
                    if (h.icon != null) h.icon.enabled = true;
                }
                // OWNER PLACEHOLDER (2026-07-11): an IconKey with the in-band "text:" prefix
                // (AbilityLoadoutProducer sets it for Q/knight.q — "use word Dodge/Attack") renders
                // as a centered TEXT face instead of a sprite. SetLabel hides the icon; SetLabel(null)
                // restores icon mode when the loadout changes back. Cooldown glow/press untouched.
                if (!string.IsNullOrEmpty(s.IconKey) && s.IconKey.StartsWith("text:", System.StringComparison.Ordinal))
                {
                    h.SetLabel(s.IconKey.Substring(5));
                }
                else
                {
                    h.SetLabel(null);
                    h.SetIcon(string.IsNullOrEmpty(s.IconKey) ? null : UiStyle.Icon(s.IconKey));
                }
                // ⭐ WO-1105 REVISION (owner 2026-08-16, verbatim: "change the bow and arrow attack
                // to the action bar and leave the attack as the dagger attack"). The bow is an
                // action-bar ability now, so the word she asked for rides ITS slot: "with [Sylas]
                // ... it should be a picture of a bow and arrow. It should be the word shoot" —
                // BOTH, which is exactly what SetCaption gives (a bottom strip that sits WITH the
                // icon, unlike SetLabel, which replaces the face). Empty verb => no strip, so only
                // abilities that AUTHOR a verb in abilities.json show one; the view holds no class
                // knowledge and could not, since DeNelle.HUD may not reference DeNelle.Village.
                // The word is also what keeps the control readable without colour (owner is
                // red/green colourblind — meaning never rides on hue).
                h.SetCaption(s.Verb);
                // WO-611: combat HUD medallions use the SOFT under-glow; else the hard radial sweep.
                bool cooling = s.CooldownRemaining > 0f && s.CooldownTotal > 0f;
                if (medallion)
                {
                    _abilityGlows[i].Set(s.CooldownRemaining, s.CooldownTotal);
                    // Tap gate: cooling OR unaffordable resource (WO-999 mobile economy).
                    if (h.button != null) h.button.interactable = !cooling && s.Affordable;
                }
                else
                    h.SetCooldown(s.CooldownRemaining, s.CooldownTotal);

                // WO-999: cost digit on the face (count badge). Free skills blank.
                // Direct text so cost "1" is not swallowed by SetCount's charge-badge rule.
                if (h.count != null)
                {
                    int costShown = s.ManaCost > 0.05f ? Mathf.RoundToInt(s.ManaCost) : 0;
                    h.count.text = costShown > 0 ? costShown.ToString() : "";
                }
                // Dim face when unaffordable (not on cooldown-only) — luminance, not hue.
                if (h.frame != null && s.Equipped)
                {
                    float frameAlpha = s.Affordable ? 1f : 0.42f;
                    var c = h.frame.color;
                    h.frame.color = new Color(c.r, c.g, c.b, frameAlpha);
                }
                if (h.icon != null && s.Equipped && h.icon.enabled)
                {
                    float iconAlpha = s.Affordable ? 1f : 0.45f;
                    var c = h.icon.color;
                    h.icon.color = new Color(c.r, c.g, c.b, iconAlpha);
                }
                if (!medallion && h.button != null && s.Equipped)
                    h.button.interactable = !cooling && s.Affordable;
            }

            // The adaptive combat dock's primary face mirrors the authored class Q. Mage shows
            // Cast/Fireball and Ranger shows Shoot/bow; the command bridge dispatches that same Q.
            if (_adaptiveCombatSlots != null && _adaptiveCombatSlots.Length > 0 &&
                _adaptiveCombatSlots[0] != null && a.Slots.Count > 0)
            {
                var primary = _adaptiveCombatSlots[0];
                var q = a.Slots[0];
                primary.SetLabel(null);
                primary.SetIcon(string.IsNullOrEmpty(q.IconKey) ? null : UiStyle.Icon(q.IconKey));
                primary.SetCaption(string.IsNullOrEmpty(q.Verb) ? "ATTACK" : q.Verb.ToUpperInvariant());
                primary.SetCooldown(q.CooldownRemaining, q.CooldownTotal);
                // ⭐ WO-1429: the ATTACK face is ALWAYS pressable — the HUD half of the dead-button
                // defect. It used to be gated on `q.Equipped && q.Affordable && q.CooldownRemaining
                // <= 0f`, i.e. exactly while the class Q was cooling or unaffordable.
                //
                // ⚠ CAREFUL WITH THE MECHANISM — the capture disproves the obvious story. The
                // owner's tap DID reach the bridge at cd=0.47s (freeze-20260904-095249.log:544639),
                // so the face was still live at press time. This producer refreshes on a 0.20s
                // cadence (AbilityLoadoutProducer's `base(m, 0.20f)`, HudModelProducers.cs:580), so
                // the face went dark on the NEXT refresh after the cast, not instantly: the old
                // behaviour was a button that blinked out for most of each cooldown and was
                // pressable only inside the refresh lag. Either way the tap that DID land produced
                // no verb, which is the bridge's half of the fix.
                //
                // Why it must be unconditional: in the hostile(activebattle) posture the combat
                // dock is the ONLY combat control (Assets/StreamingAssets/Data/Canonical/
                // hud-areas.json:242-249 — the actionRail area is EMPTY, so there is no attack pill
                // and no Q/W/E/R arc), and HudKitCommandBridge now falls a refused Q through to the
                // FREE melee sweep. A press is therefore always a real attack, and the button must
                // be able to receive it.
                //
                // ORDER IS LOAD-BEARING: ActionSlotHandle.SetCooldown itself ends with
                // `if (button != null) button.interactable = !cooling;`
                // (ElarionUiKitObsidian.cs:1138). This assignment MUST stay AFTER the SetCooldown
                // call above or the kit re-disables the face every refresh. The cooldown SWEEP is
                // deliberately kept: the player still watches the spell cool on the face, they just
                // get the free sweep instead of silence meanwhile.
                if (primary.button != null)
                    primary.button.interactable = true;
            }

            // The second face is held physical Block for martial heroes. Thrain
            // instead gets his authored W protection spell and its real cooldown.
            if (_adaptiveCombatSlots != null && _adaptiveCombatSlots.Length > 1 &&
                _adaptiveCombatSlots[1] != null)
            {
                var defensive = _adaptiveCombatSlots[1];
                bool mage = _models.HeroVitals != null &&
                            string.Equals(_models.HeroVitals.ClassId, "mage",
                                System.StringComparison.OrdinalIgnoreCase);
                if (mage && a.Slots.Count > 1)
                {
                    var shell = a.Slots[1];
                    defensive.SetLabel(null);
                    defensive.SetIcon(string.IsNullOrEmpty(shell.IconKey) ? null : UiStyle.Icon(shell.IconKey));
                    defensive.SetCaption("SHELL");
                    defensive.SetCooldown(shell.CooldownRemaining, shell.CooldownTotal);
                    bool cooling = shell.CooldownRemaining > 0f && shell.CooldownTotal > 0f;
                    if (defensive.button != null)
                        defensive.button.interactable = shell.Equipped && shell.Affordable && !cooling;
                }
                else
                {
                    defensive.SetLabel(null);
                    defensive.SetIcon(UiStyle.Icon("block", "shield", "defense"));
                    defensive.SetCaption("BLOCK");
                    defensive.SetCooldown(0f, 0f);
                    if (defensive.button != null) defensive.button.interactable = true;
                }
            }
        }

        // WO-611 + WO-917 Phase B: an unassigned slot is a dimmed "+" plate, not a blank.
        // Its tap explains how to activate it; no cast is dispatched until the slot is equipped.
        private void OnAbilitySlotTapped(int slot)
        {
            bool equipped = _abilitySlotEquipped != null && slot >= 0 &&
                            slot < _abilitySlotEquipped.Length && _abilitySlotEquipped[slot];
            if (!equipped)
            {
                ShowToast(ElarionUiKit.ToastTone.Info, "Add a skill to activate");
                return;
            }
            if (_owner != null) _owner.AbilityRequested?.Invoke(slot);
        }

        private static void SetEmptyMedallion(ElarionUiKit.ActionSlotHandle h)
        {
            if (h == null) return;
            h.SetLabel("+");
            h.SetCaption(null); // WO-1105 REVISION: and the verb strip ("Shoot"), or it outlives its ability
            if (h.icon != null) h.icon.enabled = false;
            if (h.frame != null) h.frame.color = new Color(1f, 1f, 1f, 0.45f);
            if (h.button != null) h.button.interactable = true;
            if (h.cdText != null) h.cdText.text = "";
            if (h.count != null) h.count.text = "";
        }

        private void OnAssignable()
        {
            var a = _models != null ? _models.Assignable : null;
            if (a == null) return;
            if (_assignableSlots != null)
            {
                for (int i = 0; i < _assignableSlots.Length; i++)
                {
                    var h = _assignableSlots[i];
                    if (i >= a.Slots.Count) { h.root.SetActive(true); continue; }
                    var s = a.Slots[i];
                    h.root.SetActive(true);
                    h.SetIcon(string.IsNullOrEmpty(s.IconKey) ? null : UiStyle.Icon(s.IconKey));
                    h.SetCooldown(s.CooldownRemaining, s.CooldownTotal);
                    if (h.button != null) h.button.interactable = s.Equipped;
                }
            }

            if (_adaptiveCombatSlots != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    var h = _adaptiveCombatSlots[i + 2];
                    if (h == null) continue;
                    if (i >= a.Slots.Count)
                    {
                        h.SetIcon(null);
                        h.SetCaption("EMPTY");
                        h.SetCooldown(0f, 0f);
                        if (h.button != null) h.button.interactable = false;
                        continue;
                    }
                    var s = a.Slots[i];
                    bool equipped = s.Equipped;
                    h.SetIcon(equipped && !string.IsNullOrEmpty(s.IconKey) ? UiStyle.Icon(s.IconKey) : null);
                    h.SetCaption(equipped && !string.IsNullOrWhiteSpace(s.Name) ? s.Name : "EMPTY");
                    h.SetCooldown(s.CooldownRemaining, s.CooldownTotal);
                    if (h.button != null) h.button.interactable = equipped;
                }
            }
        }

        private void OnConsumables()
        {
            var c = _models != null ? _models.Consumables : null;
            if (c == null) return;
            if (_itemSlot != null)
            {
                _itemSlot.SetCount(c.HpPotionCount + c.ManaPotionCount);
                if (_itemSlot.button != null)
                    _itemSlot.button.interactable = HudCommands.HasPotion || HudCommands.HasManaPotion;
            }
            if (_adaptiveCombatSlots != null && _adaptiveCombatSlots.Length > 5)
            {
                var item = _adaptiveCombatSlots[5];
                item.SetCount(c.HpPotionCount + c.ManaPotionCount);
                if (item.button != null)
                    item.button.interactable = HudCommands.HasPotion || HudCommands.HasManaPotion;
            }
            // WO-1672 — the outside dock's ITEM face is driven by the SAME model event as the
            // combat one. Without this line its badge would freeze at whatever it read on the
            // frame it was built, and a stale "3" over an empty belt is worse than no digit.
            if (_outsideItemSlot != null)
            {
                _outsideItemSlot.SetCount(c.HpPotionCount + c.ManaPotionCount);
                if (_outsideItemSlot.button != null)
                    _outsideItemSlot.button.interactable = HudCommands.HasPotion || HudCommands.HasManaPotion;
            }
            RefreshItemPicker();
        }

        private void OnPlayerStatus() => RefreshStatusRow(_playerStatusSlots, _models?.PlayerStatus);
        private void OnTargetStatus() => RefreshStatusRow(_enemyStatusSlots, _models?.TargetStatus);

        private static void RefreshStatusRow(ElarionUiKit.ActionSlotHandle[] slots, StatusEffectsModel model)
        {
            if (slots == null) return;
            var icons = model != null ? model.Icons : null;
            for (int i = 0; i < slots.Length; i++)
            {
                var h = slots[i];
                if (h == null) continue;
                bool has = icons != null && i < icons.Count;
                h.root.SetActive(has);
                if (!has) continue;
                var ic = icons[i];
                h.SetIcon(StatusIcon(ic.IconKey, ic.IsBuff));
                h.SetCount(0);
                h.SetCooldown(ic.RemainingSeconds, Mathf.Max(0.01f, ic.TotalSeconds));
                if (h.button != null) h.button.interactable = false;
            }
        }

        private static Sprite StatusIcon(string id, bool isBuff)
        {
            var s = UiStyle.Icon(id, "status", id);
            if (s != null) return s;
            switch (id)
            {
                case "slow":   s = UiStyle.Icon("ice", "frost", "cold"); break;
                case "freeze": s = UiStyle.Icon("ice", "frost", "cold"); break;
                case "burn":   s = UiStyle.Icon("fire", "flame", "ember"); break;
                case "mana-draught": s = UiStyle.Icon("mana", "potion", "consumable"); break;
            }
            if (s != null) return s;
            return UiStyle.Icon(isBuff ? "buff" : "debuff", "status");
        }

        private void OnTargetCycle()
        {
            var tc = _models != null ? _models.TargetCycle : null;
            if (tc == null || _cycleRows == null) return;
            for (int i = 0; i < _cycleRows.Length; i++)
            {
                bool has = i < tc.Targets.Count;
                _cycleRows[i].root.SetActive(has);
                _cycleIds[i] = has ? tc.Targets[i].Id : null;
                if (!has) continue;
                var t = tc.Targets[i];
                _cycleRows[i].SetName(t.Name);
                _cycleRows[i].hp.SetValue(t.HpFraction, 1f);
            }
        }

        // (WO-835: the old OnTalkChanged dim handler is retired — Talk availability now
        // packs the face in/out through HudActionBarModel; see ApplyActionBar.)

        // Two-tap flee (see BuildWidgets) — arm, confirm-inside-window, or disarm.
        private float _fleeArmedUntil;
        private void OnFleeTapped()
        {
            if (Time.unscaledTime < _fleeArmedUntil)
            {
                _fleeArmedUntil = 0f;
                if (_fleeLabel != null) _fleeLabel.text = CombatHudText.ResolveFlee(false);
                HudCommands.Flee();
                return;
            }
            _fleeArmedUntil = Time.unscaledTime + 2f;
            if (_fleeLabel != null) _fleeLabel.text = CombatHudText.ResolveFlee(true);
            FlowTrace.Step("HudKit", "flee armed (tap again within 2s to confirm)");
        }

        private void SetResourcePanelOpen(bool open)
        {
            // ⭐ WO-1221 — ONE OWNER of the expanded stack's SetActive.
            // The stack is a CHILD of the gold chip (occupancy-live resourceChipsCollapsed).
            // Raising a second unoccupied widget (`resourceChips` / `_resDock`) is the
            // defect: occupancy never turns it on, ApplyPosture turns it off, and the
            // capture tmp/resources-expanded-105803.png showed only gold 1034 after a tap
            // that logged "expanded (opener live=True)".
            bool stateChanged = _resChipsExpanded != open;
            _resChipsExpanded = open;
            if (_resExpandedRow != null && _resExpandedRow.activeSelf != open)
            {
                _resExpandedRow.SetActive(open);
                stateChanged = true;
            }
            if (_resHintLabel != null && _resHintLabel.gameObject.activeSelf == open)
                _resHintLabel.gameObject.SetActive(!open);

            if (!stateChanged) return;

            // ⭐ WO-1435 — the panel just changed height, so the chips below it must re-derive.
            // SetResourcePanelOpen is already the ONE owner of the stack's SetActive, so it is
            // also the one honest place to ring the clearance: a SetActive on a DESCENDANT raises
            // no OnRectTransformDimensionsChange on the chip's own band, and leaving that to a
            // poll is what turns a layout rule back into a race.
            if (_collectorsClearance != null) _collectorsClearance.MarkDirty();
            if (_buildersClearance != null) _buildersClearance.MarkDirty();

            // ⭐ WO-1670b (owner ruling 2026-09-10 12:55) — RING the ATTACK REPORT chip's own
            // repaint on this edge. ⛔ THIS IS NOT A SECOND VISIBILITY WRITER, AND THAT IS THE
            // whole design: TickDefenseReportChip holds the ONLY SetActive on _defenseChipBand and
            // now reads _resChipsExpanded as an input. Setting the band active/inactive from here
            // would race that method's next throttled tick and the chip would flicker back on half
            // a second later. Clearing the throttle and calling the one writer makes the hide land
            // on the SAME frame as the expand, with no new owner — the identical idiom as the
            // MarkDirty ring two lines above, and for the identical reason.
            _defenseChipPollTimer = DefenseChipPollSeconds;
            TickDefenseReportChip();

            if (open)
            {
                if (_resExpandedRow != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_resExpandedRow.transform);
                _resExpandVerifyFrames = ResExpandVerifyMaxFrames;
                FlowTrace.Step("HudKit",
                    "resource panel expand REQUESTED (toggle=ON, child of gold chip, no timer - " +
                    "WO-1221 owner ruling) — NOT yet a claim that anything painted; measuring for " +
                    "up to " + ResExpandVerifyMaxFrames + " frames.");
            }
            else
            {
                _resExpandVerifyFrames = 0;
                FlowTrace.Step("HudKit",
                    "resource panel collapsed (toggle=OFF, cause=player or opener left this posture).");
            }
        }

        /// <summary>
        /// WO-1221 — the MEASURED half of the expand trace. Runs for up to
        /// <see cref="ResExpandVerifyMaxFrames"/> frames after an expand is requested and reports
        /// what the player can actually see: the rail's resolved rect, its resolved opacity, its
        /// occlusion, and how many ROWS measured non-zero.
        ///
        /// Three rules from WO-976, all load-bearing:
        ///  * MEASURE AFTER LAYOUT SETTLES — a read taken on the activation frame is pre-settle and
        ///    would report 0x0 forever (registry shape H4). So this POLLS and only concludes when a
        ///    measurement clears, or when the poll budget runs out.
        ///  * UNMEASURABLE => NAMED SKIP, NEVER A PASS. Batchmode runs no layout pass;
        ///    UiSurfaceProbe.Report turns that into an explicit MEASURE_SKIPPED Warn. "Not measured"
        ///    and "measured and fine" must never be the same value.
        ///  * DO NOT RE-DERIVE THE ARITHMETIC. Rect/opacity/coverage and the four-way
        ///    ZERO_SIZE / TRANSPARENT / OFFSCREEN / BEHIND split all come from UiSurfaceProbe.
        ///
        /// The row count is the half that catches THIS ticket's exact failure: the rail's own rect
        /// can be perfectly healthy while its contents are inactive, which is what shipped.
        /// </summary>
        private void TickResourceExpandVerify()
        {
            if (_resExpandVerifyFrames <= 0) return;
            _resExpandVerifyFrames--;
            bool lastFrame = _resExpandVerifyFrames <= 0;
            int settleFrames = ResExpandVerifyMaxFrames - _resExpandVerifyFrames;

            if (_resExpandedRow == null)
            {
                _resExpandVerifyFrames = 0;
                FlowTrace.Fail("HudKit",
                    "resource panel expand UNVERIFIABLE — _resExpandedRow is null, so the expanded " +
                    "rail was never built. The tap window can raise nothing.");
                return;
            }

            // Inactive is THE original defect, not an unmeasurable environment. MeasureRect
            // reports it as a named skip (same bucket as batchmode); promoting that skip to a
            // pass is how "opener live=True" came back. Fail it by name, do not poll it away.
            if (!_resExpandedRow.activeInHierarchy)
            {
                _resExpandVerifyFrames = 0;
                FlowTrace.Fail("HudKit",
                    "resource panel expand INACTIVE — _resExpandedRow.activeInHierarchy=false " +
                    "(activeSelf=" + _resExpandedRow.activeSelf + "). The tap requested expand " +
                    "and the stack is off; the player sees only the gold chip. This is the " +
                    "tmp/resources-expanded-105803.png failure class.");
                return;
            }

            var m = UiSurfaceProbe.MeasureRect((RectTransform)_resExpandedRow.transform);

            // Keep polling while the answer could still change: not measurable yet, or measurable
            // but still pre-settle at 0x0. Only the LAST frame is allowed to conclude.
            // INACTIVE was already failed above. Batchmode / no-viewport stays a named skip.
            if (!lastFrame && (!m.Measurable || m.ZeroSize)) return;
            _resExpandVerifyFrames = 0;   // one verdict per expand, never a per-frame repeat

            int rowsLive = 0, rowsMeasured = 0;
            var t = _resExpandedRow.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null || !c.name.StartsWith("ResRow_") || !c.gameObject.activeInHierarchy) continue;
                rowsLive++;
                var rm = UiSurfaceProbe.MeasureRect(c as RectTransform);
                if (rm.Measurable && !rm.ZeroSize && !rm.Offscreen) rowsMeasured++;
            }
            int rowsExpected = _resChips != null ? _resChips.Length : 0;

            // The four-way surface split, named separately, on the shared helper.
            bool surfaceOk = UiSurfaceProbe.Report("HudKit", "resource panel expand", m);

            if (!m.Measurable)
                return;   // Report already emitted the NAMED SKIP. Never upgrade a skip to a pass.

            if (rowsMeasured < rowsExpected)
            {
                FlowTrace.Fail("HudKit",
                    "resource panel expand ROWS_MISSING — " + rowsMeasured + "/" + rowsExpected +
                    " resource rows measured non-zero on screen (" + rowsLive + " active in hierarchy). " +
                    "The rail surface is " + (surfaceOk ? "fine" : "ALSO failing") + ", so the player " +
                    "sees a frame with nothing in it. Panel: " + m.Describe());
                return;
            }

            if (surfaceOk)
                FlowTrace.Step("HudKit",
                    "resource panel expand VERIFIED PAINTED — " + rowsMeasured + "/" + rowsExpected +
                    " rows measured on screen, childCount=" + t.childCount +
                    ", panel " + m.Describe() +
                    " (settled after " + settleFrames + " frame(s)).");
        }

        /// <summary>
        /// WO-1205 — pin the resource-row chip's icon and digits into DISJOINT sub-rects.
        /// The kit chip right-aligns its amount and lets it grow LEFTWARD, which is how the
        /// device capture (tmp/wo970/crop-resources.png) ended up with Stone's icon buried
        /// under its own "80". This strip pins them apart; no other CurrencyChip consumer
        /// is touched, so nobody else's layout moves.
        /// Icon resolved  -> [icon] on the left, digits to its right, no word.
        /// Icon UNresolved -> the chip's own no-art tag ("Wood"/"Iron"/"Stone") takes the
        /// left sub-rect instead. The colourblind guard rides that branch (see the build
        /// block); a row is never a naked number.
        /// </summary>
        private static void SplitResourceRowChip(ElarionUiKit.CurrencyChipHandle chip)
        {
            if (chip == null) return;
            bool iconResolved = chip.icon != null && chip.icon.gameObject.activeSelf;

            if (iconResolved)
            {
                var irt = (RectTransform)chip.icon.transform;
                irt.anchorMin = new Vector2(0.04f, 0.12f);
                irt.anchorMax = new Vector2(0.20f, 0.88f);
                irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            }
            else if (chip.tag != null)
            {
                var trt = (RectTransform)chip.tag.transform;
                trt.anchorMin = new Vector2(0.05f, 0f);
                trt.anchorMax = new Vector2(0.52f, 1f);
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            }

            if (chip.amount != null)
            {
                var art = (RectTransform)chip.amount.transform;
                // Digits start clear of whichever identity carrier is present.
                art.anchorMin = new Vector2(iconResolved ? 0.24f : 0.56f, 0f);
                art.anchorMax = new Vector2(0.95f, 1f);
                art.offsetMin = Vector2.zero; art.offsetMax = Vector2.zero;
            }
        }

        // WO-835 §3c: the old OpenQuestOrUpgrade context relabel is SPLIT into two
        // dedicated handlers — Quests always opens the board; Upgrade routes the focused
        // building. (Reading HudBuildingFocus here is COMMAND ROUTING — the tap's target
        // argument — not an applicability predicate; visibility lives in the model.)
        private void OnQuestsAction()
        {
            if (SwallowedByCloseGrace("Journey face")) return;   // WO-1393
            if (!PanelRouter.Open(PanelId.JourneyDeck))
                FlowTrace.Warn("HudKit", "Journey workspace opener not registered - journey destinations unreachable");
        }

        /// <summary>
        /// WO-911 — the RE-POINTED bar face: the SINGLE door onto the unified Manage/Queues screen.
        /// -------------------------------------------------------------------------------------
        /// It raises <see cref="ObsidianQueueGate.RequestToggle"/>, the existing queue verb, which
        /// ManageScreenPanel now subscribes to. Going through the gate (rather than straight to the
        /// router) keeps ONE opening verb for the queues no matter who raises it, and keeps this
        /// controller's call to it — the thing the [obsidian-queue] oracle requires.
        ///
        /// PanelRouter.Open(PanelId.Manage) is the fallback for the case where the gate has no
        /// subscriber yet (a boot race), so the face is never a dead tap.
        ///
        /// The old context behaviour (focused-building -> BuildingUpgrade panel) is NOT lost: the
        /// Manage screen's tabs list every upgradable building and drill in to that very panel, and
        /// walking up to a building still opens it directly via BuildingInteractable.
        /// </summary>
        private void OnManageAction()
        {
            if (SwallowedByCloseGrace("Manage face")) return;   // WO-1393
            FlowTrace.Step("HudKit", "Manage face tapped -> ObsidianQueueGate.RequestToggle (WO-911 single door)");
            if (ObsidianQueueGate.HasSubscriber)
            {
                ObsidianQueueGate.RequestToggle();
                return;
            }
            if (!PanelRouter.Open(PanelId.Manage))
                FlowTrace.Warn("HudKit",
                    "Manage tapped but neither the queue gate nor PanelId.Manage has a listener — screen unreachable.");
        }

        // WO-439: the LEFT slide-out dock — a gear tab pinned to the left screen edge (collapsed by
        // default) that slides open a panel with FIVE rows: Chat / Leaderboard / Music / Settings /
        // Pause (Pause folded in 2026-07-24, cosmetic flag A).
        // Built from the shared ElarionUiKit.BuildSlideTab helper; registered under the same "chatDock"
        // widget id so the hud-areas.json occupancy rows are unchanged. The GEAR on the handle is the
        // dock's ONE icon (kit-resolved, gilt plate, ASCII "=" fallback so it never blanks); the rows
        // themselves are label-only — see AddDockTab for why (WO-908).
        private void BuildSlideDock(Transform pool)
        {
            _slideDock = ElarionUiKit.BuildSlideTab(pool, ElarionUiKit.SlideEdge.Left,
                tabYCenter: 0.5f, panelWidthFrac: 0.22f, panelHeightFrac: 0.52f,
                tabIconConcept: "settings");   // GEAR tab (owner: replaces the down "v/>" trigger)

            // WO-1393: the gear HANDLE is a HUD tap surface under every modal, and the kit wires
            // its onClick to SetExpanded directly (ElarionUiKit.BuildSlideTab). Re-wire it through
            // the close-frame grace so a tap in flight when a modal closes cannot pop the dock.
            if (_slideDock != null && _slideDock.tab != null)
            {
                var dock = _slideDock;
                dock.tab.onClick.RemoveAllListeners();
                dock.tab.onClick.AddListener(() =>
                {
                    if (SwallowedByCloseGrace("gear dock handle")) return;
                    dock.SetExpanded(!dock.Expanded);
                });
            }

            // F8-12 (owner 2026-07-07 "very small font and cells"): this widget re-parents into
            // the Dock AREA mount — only 23% x 10% of the screen (HudAreasHost Dock rect) — and
            // BuildSlideTab sizes by fraction-of-parent, so the tab + slide-out panel rendered at
            // ~5% of screen. Pin both to FIXED reference pixels (1080x1920 canvas units, the same
            // canonical-CTA discipline) so the tiny mount can't scale them: thumb-size tab on the
            // left edge, real-size panel overlaying when open. Cells/fonts inside are fractions of
            // the PANEL, so they inherit the fix.
            //
            // WO-908 (owner felt-test, Seeker 2670x1200 — capture
            // docs/qa/screens/2026-08-05/gear-menu-double-icon.png): the gear HANDLE and the
            // slide-out PANEL were BOTH pinned to the mount's left edge at anchoredPosition zero,
            // so opening the drawer parked the handle plate ON TOP of the panel — dead centre of
            // its height, which is exactly the MUSIC row (row 2 of 5 is centred on 0.5) — and over
            // the panel's left rim, reading as a second, mis-seated gear. Fix: the handle owns its
            // own FIXED-PIXEL column at the edge and the panel STARTS where that column ends. The
            // handle therefore never moves on toggle (the owner taps the same spot to close), never
            // covers a row, and nothing overhangs the frame. Fixed px, never a parent fraction.
            //
            // WO-1219 (owner Seeker felt-test 2026-08-26, tmp/screen-103219.png +
            // tmp/shield-seat-101829.png, both 2670x1200) - THE BAND ARITHMETIC, written out so
            // nobody has to re-derive it:
            //   * At 2670x1200 the kit canvas (1080x1920 reference, match 0.5) resolves to a
            //     scale factor of sqrt((2670/1080) * (1200/1920)) = 1.243, i.e. a canvas of
            //     ~2148 x 965 REFERENCE units.
            //   * HudArea.Dock was 0.330..0.430 of screen height = 0.100 * 965 = ~96.5 ref units.
            //   * The column mounted in it was STACKED: 112 + 12 + 112 = 236 ref units tall.
            //   A 236-unit column centred in a 96.5-unit band overflows ~70 units in EACH
            //   direction - up into the Minimap band (over the plate's lower edge AND over the
            //   region chip, which is why "Elarion - Safe - N threats" read from under the gear)
            //   and down into the MoveCluster thumb arc. Nothing was ever wrong with the two
            //   buttons: they are authored at EXACTLY MinTouchPx, so ClampMinTouch is a no-op on
            //   both and is NOT the cause. The band could not hold what was put in it.
            //
            // THE FIX: the two 112 px controls become a HORIZONTAL PAIR. The Dock band is ~494
            // ref units WIDE and only ~96.5 tall, so the free axis is x, not y. Stacked the pair
            // demanded 236 units of a 96.5-unit band; side by side it demands 112 - still 15.5
            // over the band, but the neighbours now clear it by 38 units above (minimap plate
            // bottom) and 16 below (analog-stick ring top) instead of being sat on. The Dock band
            // also grows 0.430 -> 0.440 in HudAreasHost so it abuts the Minimap band exactly.
            // ⛔ Do NOT re-stack these two. The left column has no vertical room left.
            // WO-1219: all three numbers come from the shared left-column table now, so the
            // regression that asserts this row clears the status line above it and the thumb
            // stick below it is resolving the SAME values the row is built from.
            const float dockTabPx = HudLayoutBands.DockControlPx;   // == ElarionUiKit.MinTouchPx (112)
            const float dockGapPx = HudLayoutBands.DockGapPx;
            float safeLeftPx = HudLayoutBands.DockEdgePx;
            // One compact menu handle owns secondary navigation. The former persistent
            // "Realm" face was actually the Store/Night Market and both mislabeled the route
            // and created an unrelated two-control island over the world.
            const float dockColumnPx = dockTabPx;
            var dockPanelRt = _slideDock.panel;
            // ── WO-1465 — THE OPEN DRAWER SEATS CLEAR OF THE MOVEMENT STICK ────────
            //
            // EVIDENCE (Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png): the PAUSE face
            // lands ON the analog-stick ring, so "pause" and "move" share a rect. That is not a
            // tuning slip - it is arithmetic. The panel was seated at x = edge + 112 + 12 ref px
            // and centred on the Dock mount, which at the owner's aspect (canvas 2147.9 x 965.4)
            // puts it at screen x 0.058..0.393, y 0.182..0.648. HudAreasHost seats MoveCluster at
            // x 0.010..0.270, y 0.030..0.330 - so the drawer's bottom-left cell, which is exactly
            // where AddDockTab's 2x3 grid puts row index 4 (PAUSE), lies inside the stick's band.
            //
            // ⛔ WHY THE PANEL MOVES SIDEWAYS AND NOT UPWARD, AND WHY THE GRID IS NOT RESHAPED:
            //  * UPWARD is impossible. The clear vertical between the stick's top (0.330) and the
            //    Heart objective's bottom (HudLayoutBands.HeartMount.yMin, 0.655) is 0.325 of
            //    screen = 313.8 ref units at this aspect, and a 2x3 drawer whose rows must each
            //    clear ElarionUiKit.MinTouchPx needs 112 / ((0.82/3) - 0.02) = 442 units. Raising
            //    the existing panel instead parks it over the Heart card.
            //  * RESHAPING to 3x2 would fit (112 / ((0.82/2) - 0.02) = 287 units), but
            //    DockSettingsRouteRegression [six-cells] PINS `const int columns = 2;` and
            //    `const int rows = 3;` inside AddDockTab. Changing the grid fails a suite this
            //    lane may not edit. The grid is untouched.
            //  * SIDEWAYS is free and it is DERIVED, not tuned: the panel's left edge is seated on
            //    HudAreasHost.ActionBarMinX - the constant that IS the MoveCluster's right edge,
            //    named once for exactly this reason ("the dock may never grow past it, or the bar
            //    sits under the movement stick"). Expressed as an anchor fraction of the Dock
            //    mount so it holds at EVERY aspect without reading Screen. No new magic offset.
            // It also clears the Night Market card (xMax ~0.160) by construction, so the two
            // fixes in WO-1465 are belt AND braces rather than one standing in for the other.
            dockPanelRt.anchorMin = new Vector2(DockPanelSeatAnchorX, 0.5f);
            dockPanelRt.anchorMax = new Vector2(DockPanelSeatAnchorX, 0.5f);
            dockPanelRt.pivot = new Vector2(0f, 0.5f);
            dockPanelRt.anchoredPosition = Vector2.zero;
            // Height carries FIVE tabs now (Pause folded in — cosmetic flag A) at ~112px
            // touch targets each: 700 / 5 = 140px slot, well above MinTouchPx. Do NOT shrink 700:
            // AddDockTab's rows resolve to EXACTLY 112px (0.16 * 700), so any smaller panel puts
            // them under the floor and ClampMinTouch would grow them about their centres into each
            // other — the documented WO-852/865/868 overlap trap.
            // Six full-height rows obscured the objective and analog stick when expanded.
            // A 2 x 3 drawer preserves six mobile-safe targets in a compact footprint.
            dockPanelRt.sizeDelta = new Vector2(DockPanelWidthPx, DockPanelHeightPx);
            // BuildSlideTab's legacy "Rim" is a full-centre rounded Image, not a hollow
            // border. With gold trim tint it paints over the obsidian panel and produces the
            // flat mustard slab seen on device. Retire that fill and draw four structural gold
            // rules around the actual black-iron surface instead.
            var legacyPanelRim = _slideDock.panel.Find("Rim");
            if (legacyPanelRim != null) legacyPanelRim.gameObject.SetActive(false);
            var drawerImage = _slideDock.panel.GetComponent<Image>();
            var drawerArt = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (drawerImage != null && drawerArt != null)
            {
                drawerImage.sprite = drawerArt;
                drawerImage.type = Image.Type.Sliced;
                drawerImage.fillCenter = true;
                drawerImage.color = Color.white;
            }
            var dockTabRt = (RectTransform)_slideDock.tab.transform;
            dockTabRt.anchorMin = new Vector2(0f, 0.5f);
            dockTabRt.anchorMax = new Vector2(0f, 0.5f);
            dockTabRt.pivot = new Vector2(0f, 0.5f);
            // One touch-safe menu handle sits inside the shared safe-area breathing margin.
            dockTabRt.anchoredPosition = new Vector2(safeLeftPx, 0f);
            dockTabRt.sizeDelta = new Vector2(dockTabPx, dockTabPx);   // was 84 - under the 112 floor

            // Complete-reskin contract: retain the gear glyph and command, but replace the
            // legacy flat mustard face with the approved black-iron / antique-gold icon frame.
            var dockTabImage = _slideDock.tab.targetGraphic as Image ??
                               _slideDock.tab.GetComponent<Image>();
            var dockTabFrame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/square-icon-frame");
            if (dockTabImage != null && dockTabFrame != null)
            {
                dockTabImage.sprite = dockTabFrame;
                dockTabImage.type = Image.Type.Simple;
                dockTabImage.preserveAspect = true;
                dockTabImage.color = Color.white;
                _slideDock.tab.targetGraphic = dockTabImage;
            }
            // BuildSlideTab's procedural Rim is a filled child, not border-only artwork. It
            // would paint over this authored frame, which already owns its complete rim.
            var legacyDockRim = _slideDock.tab.transform.Find("Rim");
            if (legacyDockRim != null) legacyDockRim.gameObject.SetActive(false);

            int dockRow = 0;
            if (DeNelle.Core.Services.ClanFeatureGate.PlayerFacingEnabled)
                AddDockTab(_slideDock.panel, dockRow++, "Chat", OpenClanChat);
            AddDockTab(_slideDock.panel, dockRow++, "Leaderboard", OpenLeaderboard);
            AddDockTab(_slideDock.panel, dockRow++, "Music", OpenJukebox);
            AddDockTab(_slideDock.panel, dockRow++, "Settings", OpenSettings);
            // WO-1398: this row opens the REALM DECK (PanelId.RealmDeck - the four-card
            // launcher: store / Defense Report / Monthly Ledger / Game Guide), so it is labelled
            // with what it opens. It used to read "Night Market" while the HUD card beside it,
            // reading the same words, opened the store itself - one name for two screens
            // (docs/qa/UI_SCREEN_GRAPH_2026-09-04.md dead end 7). "Realm" is the workspace's own
            // name (PlayerDeckKind.Realm) and is the WO's proposed default pending owner word.
            AddDockTab(_slideDock.panel, dockRow++, "Realm", OpenRealmDeck);
            // Pause folded into the LEFT gear (cosmetic flag A, 2026-07-24): the standalone
            // top-right pause chip (PauseHudBootstrap.PauseHudButton) was culled to leave ONE
            // door. PauseController/SettingsController stay installed by PauseHudBootstrap; this
            // tab is the caller that opens Pause/Quit-to-Title via PauseGate.RequestBack().
            AddDockTab(_slideDock.panel, dockRow, "Pause", () => PauseGate.RequestBack());
            // History of the Realm row (owner, 2026-08-22: "the only entrance to the Realm shop
            // is from an interaction with a person in town" - so the store was unreachable
            // without walking to the vendor, and unreachable at all outside town). The row was
            // added as a second CALLER of the store door; WO-1335 then gave the store its own
            // PERMANENT HUD card (BuildNightMarketCard -> PanelId.RealmStore) and this row was
            // re-pointed at the Realm deck, which is where its label now comes from (WO-1398).
            // It lives here rather than on the bottom action bar deliberately: the bottom bar
            // remains reserved for immediate play verbs.
            RaiseDockAboveNeighbourMounts();
            Register("chatDock", WrapAsWidget("chatDock", _slideDock.root));
        }

        // ── WO-1465 — THE OPENED MENU IS NEVER OCCLUDED BY A NEIGHBOURING MOUNT ────
        //
        // PROVEN CAUSE, from source and the capture, not inferred:
        //  * the capture reads "...ERBOARD" - LEADERBOARD's first four glyphs are under the
        //    Night Market card (Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png);
        //  * hud-areas.json puts the two widgets in DIFFERENT area mounts - "chatDock" in
        //    `dock`, "nightMarketCard" in `minimap` (Assets/Resources/Data/Canonical/
        //    hud-areas.json lines 20 + 68/138);
        //  * HudAreasHost.Build adds Dock (line 155) BEFORE Minimap (line 170), so the Minimap
        //    mount is the later sibling and uGUI paints it last - on top.
        // So no sibling shuffle INSIDE the dock can fix this: the occluder is in another mount.
        // A sibling shuffle of the MOUNTS would fix it for one pair and re-break the next, and
        // it would fight the occupancy table that owns mount order.
        //
        // THE FIX: the dock root carries its own sorting Canvas, one step above the host canvas.
        // Derived from HudAreasHost's own order (never a second literal), so it stays under the
        // battle overlays (5000) and far under the modal band (30000+) that the close-frame grace
        // at the top of this file describes. A nested Canvas registers its graphics to ITSELF, so
        // the host's GraphicRaycaster would no longer see the dock's rows - it gets its own, or
        // every menu row goes dead. That is why the raycaster is not optional here.
        private void RaiseDockAboveNeighbourMounts()
        {
            if (_slideDock == null || _slideDock.root == null) return;
            var go = _slideDock.root;
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (_host != null && _host.Canvas != null ? _host.Canvas.sortingOrder : 4000)
                                  + DockSortingStep;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            FlowTrace.Step("HudKit", "WO-1465: gear dock raised to sortingOrder " + canvas.sortingOrder +
                                     " (host " + (_host != null && _host.Canvas != null
                                                  ? _host.Canvas.sortingOrder.ToString() : "n/a") +
                                     ") - the Night Market card's mount can no longer occlude an opened menu");
        }

        /// <summary>WO-1465: how far above the HUD host canvas the gear dock sorts. ONE step -
        /// enough to clear every sibling AREA MOUNT, small enough that the battle overlay band
        /// (5000) and the modal band (30000+) still win, which is the order the close-frame grace
        /// assumes.</summary>
        private const int DockSortingStep = 1;

        /// <summary>
        /// WO-1465: the open drawer's LEFT EDGE, as an anchor fraction of the Dock area mount.
        /// <para>DERIVED, so the oracle and the layout cannot disagree: the panel's left edge is
        /// <see cref="HudAreasHost.ActionBarMinX"/> - the constant HudAreasHost already names as
        /// "also the MoveCluster's RIGHT edge" - re-expressed in the parent mount's fraction
        /// space (<see cref="HudLayoutBands.DockMount"/>). At the authored bands this is
        /// (0.270 - 0.000) / 0.230 = 1.174, i.e. the panel hangs to the RIGHT of its own mount.
        /// An anchor rather than a pixel offset, so it holds at every aspect without reading
        /// Screen at build time.</para>
        /// </summary>
        public static float DockPanelSeatAnchorX
        {
            get
            {
                float w = HudLayoutBands.DockMount.width;
                if (w <= 0f) return 0f;
                return (HudAreasHost.ActionBarMinX - HudLayoutBands.DockMount.xMin) / w;
            }
        }

        /// <summary>
        /// WO-1465: the OPEN gear drawer's band in screen fractions, from the same numbers
        /// <see cref="BuildSlideDock"/> builds it with - the seat anchor above, the fixed
        /// <see cref="DockPanelWidthPx"/> x <see cref="DockPanelHeightPx"/> size, and the Dock
        /// mount's vertical centre. Public so the regression measures the SHIPPING geometry
        /// instead of numbers it made up.
        /// </summary>
        public static Rect ResolveDockPanel(float screenW, float screenH)
        {
            var refSize = HudLayoutBands.CanvasReferenceSize(screenW, screenH);
            float ux = refSize.x > 0f ? 1f / refSize.x : 0f;
            float uy = refSize.y > 0f ? 1f / refSize.y : 0f;
            var mount = HudLayoutBands.DockMount;
            float x0 = mount.xMin + DockPanelSeatAnchorX * mount.width;
            float midY = (mount.yMin + mount.yMax) * 0.5f;
            float half = DockPanelHeightPx * uy * 0.5f;
            return Rect.MinMaxRect(x0, midY - half, x0 + DockPanelWidthPx * ux, midY + half);
        }

        /// <summary>
        /// WO-1465: one drawer CELL's band in screen fractions, using the SAME grid arithmetic
        /// <see cref="AddDockTab"/> lays the rows out with. The 2x3 shape is passed in rather
        /// than re-typed here so the two cannot drift; AddDockTab keeps its own
        /// <c>const int columns/rows</c> lines because DockSettingsRouteRegression [six-cells]
        /// pins them by source.
        /// </summary>
        public static Rect ResolveDockCell(Rect panel, int index, int columns, int rows)
        {
            if (columns <= 0 || rows <= 0) return default;
            int column = index % columns;
            int row = index / columns;
            float cellW = (DockInnerX1 - DockInnerX0) / columns;
            float cellH = (DockInnerY1 - DockInnerY0) / rows;
            float fx0 = DockInnerX0 + column * cellW + DockRowGapFrac;
            float fx1 = DockInnerX0 + (column + 1) * cellW - DockRowGapFrac;
            float fy1 = DockInnerY1 - row * cellH - DockRowGapFrac;
            float fy0 = DockInnerY1 - (row + 1) * cellH + DockRowGapFrac;
            return Rect.MinMaxRect(panel.xMin + fx0 * panel.width, panel.yMin + fy0 * panel.height,
                                   panel.xMin + fx1 * panel.width, panel.yMin + fy1 * panel.height);
        }

        /// <summary>WO-1465: the drawer row PAUSE occupies - Chat is gated off in the shipping
        /// build (ClanFeatureGate), so the rows are Leaderboard/Music/Settings/Realm/Pause and
        /// PAUSE is index 4: the bottom-LEFT cell of the 2x3 grid, the one that sat on the
        /// stick.</summary>
        public const int DockPauseCellIndex = 4;

        // One labelled tab inside the slide-out (stacked vertically, top-to-bottom).
        //
        // WO-908: this row used to stamp a leading concept icon over the button. It was removed,
        // for THREE reasons proven from source, not taste:
        //  1. Of the five row concepts only "settings" is mapped in concept-icons.json (line 165) —
        //     chat / leaderboard / music / pause are absent from the table AND there is no art for
        //     them in Assets/Resources/RpgUi/icons/ at all. So the path could only ever badge ONE
        //     row of five: a per-row icon treatment is not achievable, it is a lone odd row out.
        //  2. BuildObsidianButton's label is a FULL-STRETCH centred TMP (ElarionUiKitObsidian.cs:679),
        //     and the icon was added AFTER it, so the icon drew ON TOP of the row's own label — the
        //     gear sat on the "S" of "Settings" in the felt-test capture.
        //  3. Assets/Resources/RpgUi/icons/icon_settings.png is DARK art; bare (no plate) on the
        //     Gray obsidian face it is a near-contrastless smudge, which is the "formatting is wrong
        //     on colour" half of the report.
        // The menu's one gear is now the drawer HANDLE (BuildSlideTab), which carries the same
        // sprite on the kit's gilt plate. Rows are uniformly label-only — ONE treatment.
        // ── dock sizing is DERIVED, never authored twice (2026-08-22) ──────────
        // The row count used to be a `const int n = 5` in here while the panel height
        // was a separate literal 700f up in BuildSlideDock, tuned so that
        // 700 * (1/5 - 2*0.02) = EXACTLY MinTouchPx. Two numbers, one invariant,
        // in two methods - so adding a sixth tab silently drove rows to 106px, under
        // the floor, where ClampMinTouch grows them about their centres INTO each
        // other. That is the documented WO-852/865/868 overlap trap, and it would
        // have been re-entered by the edit that adds a tab, which is the worst
        // possible time to discover it.
        //
        // Both numbers now come from DockTabCount. Add a tab -> the panel grows to
        // keep every row at the touch floor, automatically. Do NOT re-introduce a
        // literal height.
        private const int DockTabCount = 6;   // Chat/Leaderboard/Music/Settings/Realm/Pause
        private const float DockRowGapFrac = 0.01f;

        /// <summary>WO-1465: the drawer's inner cell field, named ONCE. AddDockTab lays the rows
        /// out with these and <see cref="ResolveDockCell"/> measures them with the same four, so
        /// the oracle cannot measure a grid the HUD does not draw.</summary>
        public const float DockInnerX0 = 0.08f;
        /// <summary>See <see cref="DockInnerX0"/>.</summary>
        public const float DockInnerX1 = 0.92f;
        /// <summary>See <see cref="DockInnerX0"/>.</summary>
        public const float DockInnerY0 = 0.09f;
        /// <summary>See <see cref="DockInnerX0"/>.</summary>
        public const float DockInnerY1 = 0.91f;

        /// <summary>WO-1465: the drawer's fixed reference width (was a bare 720f literal at the
        /// one call site). Named so <see cref="ResolveDockPanel"/> measures the shipping box.</summary>
        public const float DockPanelWidthPx = 720f;

        // Three physical rows with breathing room around the 112px touch floor.
        public static float DockPanelHeightPx => 450f;

        private void AddDockTab(RectTransform panel, int i, string label, Action onTap)
        {
            const int columns = 2;
            const int rows = 3;
            int column = i % columns;
            int row = i / columns;
            const float innerX0 = DockInnerX0;
            const float innerX1 = DockInnerX1;
            const float innerY0 = DockInnerY0;
            const float innerY1 = DockInnerY1;
            float cellW = (innerX1 - innerX0) / columns;
            float cellH = (innerY1 - innerY0) / rows;
            float x0 = innerX0 + column * cellW + DockRowGapFrac;
            float x1 = innerX0 + (column + 1) * cellW - DockRowGapFrac;
            float y1 = innerY1 - row * cellH - DockRowGapFrac;
            float y0 = innerY1 - (row + 1) * cellH + DockRowGapFrac;
            // WO-1393: every dock row consults the close-frame grace before its own command.
            string face = "gear dock '" + label + "'";
            ElarionUiKit.BuildObsidianButton(panel, label,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(x0, y0), new Vector2(x1, y1), () =>
                {
                    if (SwallowedByCloseGrace(face)) return;
                    onTap?.Invoke();
                });
        }

        // Settings tab -> the REAL options screen (SettingsController: quality / difficulty /
        // wallet / privacy / offline), through Core SettingsGate - PauseGate's twin.
        // WO-1399: this row used to toggle the HELP menu's overlay directly, so a
        // row labelled "Settings" opened the bug-report/Controls/Credits menu, and the real
        // Settings was reachable only through Pause. The HUD cannot call SettingsController
        // directly (DeNelle.HUD.asmdef references Core + Data only; DeNelle.Settings references
        // Core only), so the request crosses through the Core gate and SettingsController
        // subscribes. Help now lives as a row INSIDE Settings (PanelId.Help) - one door, and
        // the 2x3 dock grid keeps its six cells (no seventh row).
        // A request with no subscriber is a FlowTrace.Fail inside the gate, never a silent no-op.
        private void OpenSettings()
        {
            Guard.Try("Settings", "open Settings from the gear dock", () =>
            {
                FlowTrace.Step("Settings", "gear dock 'Settings' tapped -> SettingsGate.RequestOpen(dock)");
                SettingsGate.RequestOpen("dock");
            });
        }

        // The gear dock's "Realm" row -> PanelId.RealmDeck, the PlayerDeckWorkspace launcher
        // (its first card opens the store; the store's own HUD door is OpenNightMarket above).
        // WO-1398: renamed from OpenRealmStore - the old name said RealmStore while the body
        // opened RealmDeck, and the comment above it still described a store door.
        //
        // Shaped after RealmStoreVendor.Open deliberately: PanelRouter.Open returns
        // FALSE when no opener is registered, and an unchecked call would look to the
        // player like the deck is broken and to us like nothing happened. A refusal is
        // reported, never swallowed.
        private void OpenRealmDeck()
        {
            Guard.Try("Realm", "open the Realm workspace from the HUD", () =>
            {
                if (PanelRouter.Open(PanelId.RealmDeck))
                    FlowTrace.Step("Realm", "HUD Realm face opened PanelId.RealmDeck.");
                else
                    FlowTrace.Fail("Realm",
                        "PanelRouter.Open(PanelId.RealmDeck) returned FALSE from the HUD - the " +
                        "PlayerDeckWorkspace opener is not registered in this scene.");
            });
        }

        // ── dock intents (parity with the retired SocialAccessCluster) ──────
        private void OpenClanChat()
        {
            var p = FindAnyObjectByType<ClanChatPanel>(FindObjectsInactive.Include);
            if (p != null) p.Toggle();
            else FlowTrace.Warn("HudKit", "dock: ClanChatPanel not present");
        }

        private void OpenLeaderboard()
        {
            var p = FindAnyObjectByType<LeaderboardPanel>(FindObjectsInactive.Include);
            if (p != null) p.Toggle();
            else FlowTrace.Warn("HudKit", "dock: LeaderboardPanel not present");
        }

        private void OpenJukebox()
        {
            // MusicSelectionPanel lives in DeNelle.Audio — the same loose-reflection toggle
            // the retired SocialAccessCluster used (HUD -> Core only, no Audio asmdef edge).
            Guard.Try("HudKit", "dock jukebox toggle", () =>
            {
                var t = Type.GetType("DeNelle.Audio.MusicSelectionPanel, DeNelle.Audio");
                if (t == null) return false;
                var panel = FindAnyObjectByType(t) as MonoBehaviour;
                if (panel == null) return false;
                var toggle = t.GetMethod("Toggle");
                if (toggle == null) return false;
                toggle.Invoke(panel, null);
                return true;
            }, false);
        }

        // =====================================================================
        // POSTURE OCCUPANCY — data rows drive everything (A4).
        // =====================================================================

        private void ApplyPosture(HudPosture posture)
        {
            // WO-611 behavior rules (combat HUD only): on the flip to hostile(prebattle|activebattle)
            // every other screen CLOSES + HIDES so ONLY the combat HUD renders (owner rule 1+2).
            if (FeatureFlags.CombatHud611 &&
                (posture == HudPosture.HostilePrebattle || posture == HudPosture.HostileActiveBattle))
            {
                PanelManager.CloseAll();
                FlowTrace.Step("HudKit", "combat HUD is the active screen: CloseAll (posture " +
                               HudPostureKeys.Key(posture) + ")");
            }

            // COLLAPSED IS THE DEFAULT STATE (owner 2026-08-05). A posture flip re-presents
            // the rail, so it re-presents it minimized — the player never returns to town and
            // finds a panel she left open in another posture already occupying the column.
            SetRailSection(RailSection.None);
            if (_resChipsExpanded) SetResourcePanelOpen(false);

            var occupancy = _config.Occupancy(posture);
            int shown = 0;
            foreach (var kv in _widgets)
            {
                HudArea area;
                bool present = occupancy.TryGetValue(kv.Key, out area);
                if (present)
                {
                    var mount = _host.Mount(area);
                    if (mount != null && kv.Value.transform.parent != mount)
                        kv.Value.transform.SetParent(mount, false);
                    if (!kv.Value.activeSelf) kv.Value.SetActive(true);
                    shown++;
                }
                else if (kv.Value.activeSelf) kv.Value.SetActive(false);
            }

            // Dynamic gates on top of the rows (availability, never layout):
            if (_widgets.TryGetValue("fleeButton", out var flee) && flee.activeSelf)
                flee.SetActive(HudCommands.HasFlee);
            ApplyHeartSceneGate(posture);
            // WO-835: relay the posture key to the applicability model (a relay of the
            // notification this View already receives — the key->set mapping lives in
            // the model). A set change comes back as ActiveButtonsChanged -> render.
            if (_barModel != null) _barModel.SetPosture(HudPostureKeys.Key(posture));
            OnConsumables();
            OnPlayerStatus();
            OnTargetStatus();
            OnWave();   // wave block phase gate re-evaluates with the posture
            // WO-1407: the posture is ONE input to the objective line; the words come from Core.
            _heartObjectiveHostile = posture == HudPosture.HostilePrebattle ||
                                     posture == HudPosture.HostileActiveBattle;
            RepaintHeartObjective(force: false);

            FlowTrace.Step("HudKit", "occupancy applied: posture " + HudPostureKeys.Key(posture) +
                           " -> " + shown + " widgets live");
        }

        // ---------------------------------------------------------------------
        // heartStatus SCENE GATE (owner felt-test, Seeker: the "Heart of Elarion"
        // bar rendered INSIDE Dungeon_HealersCottage).
        //
        // The Heart is the VILLAGE world-tree; its status bar has no meaning outside a
        // hub/town scene. hud-areas.json RIGHTLY lists heartStatus in calm(town) AND in
        // hostile(prebattle|activebattle) — a wave defence is exactly the situation the
        // bar exists for — but posture alone cannot tell a village wave from a dungeon
        // fight: in the dungeon the evaluator resolved hostile(activebattle) (correctly)
        // and the row fired, so the bar appeared. The row is NOT the bug and must NOT be
        // edited; the missing SCENE test is. So this is a dynamic availability gate ON TOP
        // of the rows — the exact fleeButton/HudCommands.HasFlee precedent above.
        //
        // In the hub every posture that lists heartStatus still shows it: IsHub() is true
        // there, so `want` collapses to pure row membership (today's behaviour, unchanged).
        //
        // ── WO-1436 GENERALISATION ───────────────────────────────────────────
        // The gate above was RIGHT and was scoped to one widget, so the next town-only
        // widget in a hostile row repeated the defect. `waveBlock` is that widget: it sits
        // in the hostile(prebattle) AND hostile(activebattle) rows for the same good reason
        // heartStatus does — a village wave defence is a fight — and the owner's raid
        // screenshot shows a WAVE TIMER counting down over a raid battlefield, where there
        // is no wave and never can be. Before WO-1436 that only surfaced during the brief
        // pursuit windows that flipped the raid hostile; now that a raid declares combat for
        // its whole duration, an ungated waveBlock would be on screen the ENTIRE raid.
        //
        // So the mechanism is reused verbatim, driven by a LIST instead of a name — one gate,
        // N widgets. Adding a hub-only widget is one string in HubOnlyWidgets + its reason.
        // As before: the hud-areas.json rows are NOT edited. The rows are not wrong; posture
        // simply cannot tell a village wave from a raid, and only a SCENE test can.
        /// <summary>
        /// Refresh the cached "is the active scene a hub?" answer. Scene.name allocates and this
        /// is read per frame, so it is cached by scene HANDLE and recomputed only on a change.
        ///
        /// <para>Its own method since WO-1436 because there are TWO readers, and that is the
        /// whole point: <see cref="ApplyHeartSceneGate"/> is not the only writer of
        /// waveBlock.activeSelf — <see cref="OnWave"/> also sets it from the wave phase. Two
        /// writers with different opinions is a flicker, so both consult this one answer.</para>
        /// </summary>
        private void RefreshHubGateCache()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle == _heartGateSceneHandle) return;
            _heartGateSceneHandle = scene.handle;
            _heartGateSceneName = scene.name;
            _heartGateIsHub = HubScenes.IsHub(_heartGateSceneName);
            // re-announce every hub-only widget's decision once per scene
            for (int i = 0; i < _hubOnlyLogged.Length; i++) _hubOnlyLogged[i] = -1;
        }

        private void ApplyHeartSceneGate(HudPosture posture)
        {
            if (_config == null) return;

            RefreshHubGateCache();

            var occupancy = _config.Occupancy(posture);

            for (int i = 0; i < HubOnlyWidgets.Length; i++)
            {
                string id = HubOnlyWidgets[i];
                GameObject go;
                if (!_widgets.TryGetValue(id, out go) || go == null) continue;

                bool inRow = occupancy.ContainsKey(id);
                bool want = inRow && _heartGateIsHub;
                if (go.activeSelf != want) go.SetActive(want);

                // Project law: a decision leaves a logged line. This runs per occupancy apply
                // AND per frame, so log ONLY on a flip (and once per scene) — never per frame.
                int decision = want ? 1 : 0;
                if (decision == _hubOnlyLogged[i]) continue;
                _hubOnlyLogged[i] = decision;

                if (inRow && !_heartGateIsHub)
                    FlowTrace.Warn("HudKit", id + ": posture " + HudPostureKeys.Key(posture) +
                                   " lists it, but scene '" + _heartGateSceneName +
                                   "' is not a hub -> scene gate HIDES it (" + HubOnlyWhy[i] + ")");
                else
                    FlowTrace.Step("HudKit", id + " scene gate: " + (want ? "show" : "hide") +
                                   " (scene " + _heartGateSceneName + ", hub " + _heartGateIsHub +
                                   ", inRow " + inRow + ")");
            }
        }

        // WO-997 §3b: per-frame mana-bar animation. Eases the shown fill toward the model's
        // target (so a 1/s regen is visible MOTION, not a 10% step every second) and runs the
        // spend flash: a brief brighten of the fill toward white that decays back to the BUILT
        // colour. Brightness carries the meaning — never a hue swap (red/green colourblind law).
        private void AnimateManaFill()
        {
            var img = _vitals.ManaFill;
            if (img == null || _manaFillTarget < 0f || _manaFillShown < 0f) return;

            if (!Mathf.Approximately(_manaFillShown, _manaFillTarget))
            {
                // Exponential ease — frame-rate independent, snaps when within a hair.
                float k = 1f - Mathf.Exp(-ManaFillLerpSpeed * Time.unscaledDeltaTime);
                _manaFillShown = Mathf.Lerp(_manaFillShown, _manaFillTarget, k);
                if (Mathf.Abs(_manaFillShown - _manaFillTarget) < 0.0015f)
                    _manaFillShown = _manaFillTarget;
                img.fillAmount = _manaFillShown;
            }

            if (_manaFlashUntil > 0f && _manaFillBaseCaptured)
            {
                float remain = _manaFlashUntil - Time.unscaledTime;
                if (remain <= 0f)
                {
                    img.color = _manaFillBaseColor;
                    _manaFlashUntil = 0f;
                }
                else
                {
                    // 0..1 flash strength, strongest at the spend instant, decaying to 0.
                    float t = Mathf.Clamp01(remain / ManaFlashSeconds);
                    img.color = Color.Lerp(_manaFillBaseColor, Color.white, 0.75f * t);
                }
            }
        }

        // =====================================================================
        // WO-1104 — XP GAIN FEEDBACK (owner felt-test 2026-08-16)
        // =====================================================================

        /// <summary>
        /// MEASURE one XP push against the previous one and, when it is a real gain, arm the
        /// two feedback channels: the strip flash + the "+N XP" readout.
        ///
        /// THE AMOUNT IS A MEASURED STATE DELTA, NOT A REQUESTED GRANT. The HUD never sees
        /// "the grant code asked for 14 XP" — it sees the hero's banked XP move, which is the
        /// only number that proves something actually landed. (Project law: never log/present
        /// the amount requested in place of the amount credited.)
        ///
        /// A level-up is folded in: the carry across the boundary is
        /// (prevToNext - prevXp) + newXp, a FLOOR for a multi-level jump (rare; a single kill
        /// never crosses two levels). A level going DOWN, or an implausibly large jump, is a
        /// save restore / dev grant, not a kill — suppressed and traced, never popped.
        /// </summary>
        private void NoteXpGain(int xp, int xpToNext, int level)
        {
            if (xpToNext <= 0) return;

            if (!_xpPrevValid)
            {
                // First bind is a BASELINE, never a gain (the whole banked total would pop).
                _xpPrevValid = true;
                _xpPrevXp = xp; _xpPrevToNext = xpToNext; _xpPrevLevel = level;
                return;
            }

            int gained;
            if (level == _xpPrevLevel) gained = xp - _xpPrevXp;
            else if (level > _xpPrevLevel) gained = Mathf.Max(0, _xpPrevToNext - _xpPrevXp) + xp;
            else gained = 0;   // level DOWN = a restore/reset, never an award

            int levelsGained = Mathf.Max(0, level - _xpPrevLevel);
            _xpPrevXp = xp; _xpPrevToNext = xpToNext; _xpPrevLevel = level;

            if (gained <= 0) return;

            // Restore/dev-grant guard: a single award never exceeds two levels' worth.
            if (gained > xpToNext * 2)
            {
                FlowTrace.Warn("HudKit",
                    $"XP GAIN SUPPRESSED measuredDelta={gained} (> 2x xpToNext={xpToNext}) - " +
                    "reads as a save restore or dev grant, not a kill award; no pop shown.");
                return;
            }

            float now = Time.unscaledTime;
            bool merged = _xpGainRunning > 0 && (now - _xpGainLastTime) <= XpGainMergeSeconds;
            _xpGainRunning = merged ? _xpGainRunning + gained : gained;
            _xpGainLastTime = now;
            _xpGainHoldUntil = now + XpGainHoldSeconds;
            _xpFlashUntil = now + XpFlashSeconds;

            if (_vitals.XpGainLabel != null)
            {
                // Word + number carry the meaning; the flash is the redundant channel.
                string text = "+" + _xpGainRunning + " XP";
                if (levelsGained > 0) text += "  LEVEL UP";
                SetXpGainText(text);
            }

            // §12 permanent trace: the MEASURED delta, the merged running total, and whether
            // the readout surface actually existed - so a capture can tell "credited but not
            // shown" from "never credited". No hollow assertion: 'measuredDelta' is the state
            // move, not an amount anybody asked for.
            FlowTrace.Step("HudKit",
                $"XP GAIN measuredDelta={gained} runningShown={_xpGainRunning} merged={merged} " +
                $"levels={levelsGained} xp={xp}/{xpToNext} lv={level} " +
                $"labelPresent={(_vitals.XpGainLabel != null)} stripPresent={(_vitals.XpFill != null)}");
        }

        /// <summary>Retext + reveal the gain readout at full alpha (single writer for the label).</summary>
        private void SetXpGainText(string text)
        {
            var lbl = _vitals.XpGainLabel;
            if (lbl == null) return;
            lbl.text = text;
            var c = lbl.color; c.a = 1f; lbl.color = c;
            if (!lbl.gameObject.activeSelf) lbl.gameObject.SetActive(true);
        }

        /// <summary>
        /// WO-1104 per-frame XP feedback: brighten-decay on the strip fill (never a hue swap)
        /// and hold-then-fade on the "+N XP" readout. Cheap; early-outs when nothing is armed.
        /// </summary>
        private void AnimateXpGain()
        {
            // 1) Strip flash — brightness pulse toward white, decaying to the BUILT colour.
            var fill = _vitals.XpFill;
            if (fill != null)
            {
                if (!_xpFillBaseCaptured) { _xpFillBaseColor = fill.color; _xpFillBaseCaptured = true; }
                if (_xpFlashUntil > 0f)
                {
                    float remain = _xpFlashUntil - Time.unscaledTime;
                    if (remain <= 0f) { fill.color = _xpFillBaseColor; _xpFlashUntil = 0f; }
                    else
                    {
                        float t = Mathf.Clamp01(remain / XpFlashSeconds);
                        fill.color = Color.Lerp(_xpFillBaseColor, Color.white, 0.85f * t);
                    }
                }
            }

            // 2) Readout — hold at full alpha, then fade out and retire the running total so
            //    the NEXT fight starts its own count (a fresh climb, not a lifetime tally).
            var lbl = _vitals.XpGainLabel;
            if (lbl == null || !lbl.gameObject.activeSelf) return;
            float over = Time.unscaledTime - _xpGainHoldUntil;
            if (over <= 0f) return;
            var col = lbl.color;
            if (over >= XpGainFadeSeconds)
            {
                col.a = 1f; lbl.color = col;          // reset for the next pop
                lbl.gameObject.SetActive(false);
                _xpGainRunning = 0;
            }
            else
            {
                col.a = 1f - (over / XpGainFadeSeconds);
                lbl.color = col;
            }
        }

        private void Update()
        {
            // WO-997 §3b: ease the hero plate's mana fill toward its target + run the
            // spend flash (brightness pulse, colourblind-safe). Cheap; early-outs when idle.
            AnimateManaFill();

            // WO-1104: run the XP strip flash + the "+N XP" readout hold/fade.
            AnimateXpGain();

            // WO-1384b: the Night Market card's chasing rim light + palette drift. Early-outs
            // when the card is not built or hidden; the first 60 frames are cost-sampled once.
            AnimateNightMarketGlow();

            // WO-1225: time out an armed reward acknowledgement whose wallet push never came.
            TickGoldCelebration();

            // WO-611: drive the animated lock crosshair badge from the target model (combat HUD only).
            // 0 = no target (unlocked/faint), 1 = target held but not locked (acquiring pulse),
            // 2 = manual lock (locked/gold). Bound to TargetModel.HasTarget/Locked.
            if (_lockBadge != null && _models != null && _models.Target != null)
            {
                var t = _models.Target;
                _lockBadge.SetState(!t.HasTarget ? 0 : (t.Locked ? 2 : 1));
            }

            // WO-835: tick the Core applicability model — it polls the no-event Core
            // statics (focus/onboarded/army version/capability) and edge-triggers
            // ActiveButtonsChanged/RaidsDimmedChanged; this View holds zero predicates.
            // (Replaces the retired Quests<->Upgrade relabel, Raids dim and Map hide
            // polls that used to live right here.)
            if (_barModel != null) _barModel.Tick();

            // Cheap availability polls (no model event exists for these Core statics).
            if (_widgets.TryGetValue("fleeButton", out var flee))
            {
                bool want = HudCommands.HasFlee &&
                            _config.Occupancy(_evaluator.Posture).ContainsKey("fleeButton");
                if (flee.activeSelf != want) flee.SetActive(want);
            }
            // Same cheap poll for the Heart's scene gate: a scene change (hub -> dungeon)
            // need not move the posture, so the ApplyPosture call alone can be missed.
            // Self-throttled (cached hub test, log only on a flip) — see ApplyHeartSceneGate.
            if (_evaluator != null) ApplyHeartSceneGate(_evaluator.Posture);
            // WO-1205 — THE PANEL CAN NEVER OUTLIVE ITS OPENER. The expanded stack is a
            // child of resourceChipsCollapsed, so occupancy hiding the opener hides the
            // stack visually. Reset the toggle so returning to town does not silently
            // re-open a rail the player never asked for again (build / modal / battle).
            // ⛔ Do NOT SetActive a second `resourceChips` occupancy widget here — that
            // widget is not in hud-areas.json, ApplyPosture kills it, and it is the
            // empty-dock path that painted zero pixels on device.
            bool openerLive = _widgets.TryGetValue("resourceChipsCollapsed", out var col) &&
                              col != null && col.activeSelf;
            if (!openerLive && _resChipsExpanded) SetResourcePanelOpen(false);
            TickResourceExpandVerify();

            // WO-778: Builders chip repaint — poll the Core static (the HudBuildingFocus
            // precedent, no model event); repaint only when the published Version moves
            // (BuildTimerService publishes on QueueChanged + its own 1s tick).
            if (_queueChipLabel != null)
            {
                var qs = ObsidianQueueGate.Status;
                if (qs.Version != _queueStatusVersion)
                {
                    _queueStatusVersion = qs.Version;
                    _queueChipLabel.text = FormatQueueChip(qs);
                    // The card rail (WO-864) is self-driving off the same published Version
                    // and repaints only when the queue SHAPE moves — nothing to do here.
                }
            }

            // WO-1379: the Heartfire flames under the Heart plate. Same cheap poll shape as
            // the collector chip below - repaint only when the published values move.
            RepaintHeartfire(force: false);

            // WO-1407: the Heart plate's objective line - same cheap poll; the inputs are the
            // Core posture rail (RaidCapable/RaidLock) + the model's army snapshot Version, all
            // change-detected inside, so this is a few compares per frame and a repaint only on
            // a transition.
            RepaintHeartObjective(force: false);

            // WO-900 §4: the ambient collector chip — the same cheap poll, on the same terms.
            // The Village publisher bumps Version at most twice a second, so this repaints only
            // when the collectors actually moved; nothing here derives any collector state.
            if (_collectorsChipLabel != null)
            {
                var cs = CollectorStatusGate.Status;
                if (cs.Version != _collectorStatusVersion)
                {
                    _collectorStatusVersion = cs.Version;
                    _collectorsChipLabel.text = FormatCollectorChip(cs);
                }
            }

            // WO-1515 sec.2B: the ATTACK REPORT chip appears the moment a report lands unread and
            // is gone the moment it is read. Throttled + change-detected inside.
            TickDefenseReportChip();

            // One post-expand re-sync, once the newly-shown fixed-px band has been laid out.
            if (_queueRailSyncFrames > 0 && --_queueRailSyncFrames == 0 && _queueRail != null)
                _queueRail.Sync();

            // (WO-835: the Raids army-dim poll and the Map Onboarded poll that lived here
            // moved into HudActionBarModel — the View consumes its events above.)
        }

        /// <summary>
        /// Paint the Heartfire flame row + rekindle line from the Core posture rail.
        /// PURE PRESENTATION: every number and every word comes from
        /// DeNelle.Core.State.HeartfireCharges via PostureSignals - this method decides
        /// nothing, which is what keeps the HUD unable to disagree with the service about
        /// whether a march is possible.
        /// </summary>
        private void RepaintHeartfire(bool force)
        {
            if (_heartfireLabel == null) return;

            int lit = PostureSignals.HeartfireLit;
            int max = PostureSignals.HeartfireMax;
            long secs = (long)PostureSignals.HeartfireSecondsToNext;

            if (!force && lit == _heartfireLitPainted && max == _heartfireMaxPainted &&
                secs == _heartfireSecondsPainted) return;

            bool countMoved = lit != _heartfireLitPainted || max != _heartfireMaxPainted;
            _heartfireLitPainted = lit;
            _heartfireMaxPainted = max;
            _heartfireSecondsPainted = secs;

            // WO-1415 (owner ruling 2026-09-05): the plate says what a charge BUYS, not only
            // that it is full - "Heartfire 3/3 (raids)" / "Heartfire 0/3 (raids) - next in
            // 3h 12m". The PARENTHETICAL form and not the sentence, because this row is one
            // FITTED line (FitSingleLine at its own floor) and a sentence ellipsises there;
            // both strings are composed in DeNelle.Core.State.HeartfireCharges, which is the
            // ONE owner of every Heartfire word, so the guide entry and the introduction beat
            // cannot drift from what the plate says.
            string label = DeNelle.Core.State.HeartfireCharges.PlateLabel(lit, max);
            string line = DeNelle.Core.State.HeartfireCharges.PlateRekindle(lit, max, secs);
            if (force || countMoved)
                RepaintHeartfireFlameSlots(DeNelle.Core.State.HeartfireCharges.FlameStates(lit, max));
            // WO-1384: two rows, two labels. The marks row keeps the words; the rekindle line
            // has its own band so neither is shrunk to seat the other. If the second label is
            // absent (it is built in the same method, so only a factory failure) the combined
            // text is painted rather than dropping the line - and the combined form is the
            // owner's ruled spent string exactly, "<label> - <line>".
            if (_heartfireRekindleLabel != null)
            {
                _heartfireLabel.text = label;
                _heartfireRekindleLabel.text = line;
            }
            else
            {
                _heartfireLabel.text = DeNelle.Core.State.HeartfireCharges.PlateCombined(label, line);
            }

            // Only the COUNT is worth a line; the countdown moves every second and would
            // otherwise be a per-second firehose in every capture (the lesson of the
            // [Flow:Offset] ring-buffer eviction, memory logcat-ring-buffer-destroys-evidence).
            if (force || countMoved)
            {
                string flames = DeNelle.Core.State.HeartfireCharges.FlameRow(lit, max);
                FlowTrace.Step("HudKit", "heartfire painted -> " + flames + " '" + label +
                                "' (" + lit + "/" + max + "), rekindle row '" + line + "'");
            }
        }

        /// <summary>Build/update flame Images only when the charge count or ceiling moves.</summary>
        private void RepaintHeartfireFlameSlots(bool[] states)
        {
            if (_heartfireFlameHost == null || states == null) return;
            while (_heartfireFlameSlots.Count > states.Length)
            {
                int last = _heartfireFlameSlots.Count - 1;
                var stale = _heartfireFlameSlots[last];
                _heartfireFlameSlots.RemoveAt(last);
                if (stale != null) Destroy(stale.gameObject);
            }

            Sprite flame = ResolveHeartfireFlameSprite();
            while (_heartfireFlameSlots.Count < states.Length)
            {
                int index = _heartfireFlameSlots.Count;
                var slot = ElarionUiKit.AddImage(_heartfireFlameHost, "HeartfireFlameSlot_" + index,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Color.white, rounded: false);
                var image = slot.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                _heartfireFlameSlots.Add(image);
            }

            for (int i = 0; i < _heartfireFlameSlots.Count; i++)
            {
                var image = _heartfireFlameSlots[i];
                if (image == null) continue;
                var rt = image.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(HeartfireFlameIconPx, HeartfireFlameIconPx);
                rt.anchoredPosition = new Vector2(i * (HeartfireFlameIconPx + HeartfireFlameGapPx), 0f);
                image.sprite = flame;
                image.enabled = flame != null;
                image.color = states[i]
                    ? new Color(1f, 1f, 1f, HeartfireFlameLitAlpha)
                    : new Color(HeartfireFlameSpentGray, HeartfireFlameSpentGray,
                        HeartfireFlameSpentGray, HeartfireFlameSpentAlpha);
            }
        }

        /// <summary>
        /// Resolve the Heartfire flame sprite ONCE per domain. The repaint above runs on every
        /// charge-count change, so a Resources.Load in that path would be a per-change disk/lookup
        /// hit for a sprite that never changes.
        /// A MISS is latched too, and it is TRACED once: without the line, a runtime miss reads as
        /// the CLAUDE.md s16 pattern - the plate paints, the word paints, the slots are simply
        /// invisible and nothing anywhere says why (the same silent shape as an unpushed bundle).
        /// The count word keeps painting either way; only the icons drop.
        /// </summary>
        private static Sprite ResolveHeartfireFlameSprite()
        {
            if (_heartfireFlameSprite != null) return _heartfireFlameSprite;
            if (_heartfireFlameSpriteMissing) return null;

            var loaded = Resources.Load<Sprite>(HeartfireFlameSpritePath);
            if (loaded == null)
            {
                _heartfireFlameSpriteMissing = true;
                FlowTrace.Once("HudKit", "heartfire-flame-sprite-missing",
                    "heartfire flame sprite MISSING at Resources '" +
                    HeartfireFlameSpritePath + "' - slots hidden, the count word still paints");
                return null;
            }

            _heartfireFlameSprite = loaded;
            return loaded;
        }

        /// <summary>
        /// WO-1407: paint the Heart plate's line 2 from Core state. PURE PRESENTATION - the
        /// sentence is HeartObjectiveCopy.Resolve's, the inputs are PostureSignals.RaidCapable /
        /// RaidLock (Village-published capability: flag + a standing Barracks) and the model's
        /// HudActionBarModel.ArmySnapshot (Village-published readiness incl. the WO-823
        /// RequiredSlots). ⛔ The army snapshot comes through the MODEL, never off the gate: the
        /// army predicate belongs to HudActionBarModel (WO-835 View-purity law, pinned by
        /// HudActionBarRegression.CheckViewPurity).
        /// Change-detected on (hostile, capable, lock, army.Version): a repaint and ONE trace line
        /// per transition, never per frame (the [Flow:Offset] ring-buffer lesson).
        /// </summary>
        private void RepaintHeartObjective(bool force)
        {
            if (_heartObjectiveLabel == null) return;

            bool capable = PostureSignals.RaidCapable;
            var lock_ = PostureSignals.RaidLock;
            // The model owns the army read (WO-835): the View may not touch the gate.
            // HudActionBarModel.Shared is the SAME instance BindActionBar assigns to _barModel,
            // and _barModel is deliberately left null when the adaptive peaceful dock owns the
            // bar (BindActionBar returns early there), so the fallback is the normal path on that
            // branch - one source either way, never a second one.
            var army = (_barModel ?? HudActionBarModel.Shared).ArmySnapshot;
            bool hostile = _heartObjectiveHostile;

            if (!force && _heartObjectiveEverPainted &&
                capable == _heartObjectiveCapablePainted && lock_ == _heartObjectiveLockPainted &&
                army.Version == _heartObjectiveArmyVersionPainted && hostile == _heartObjectiveHostilePainted)
                return;

            _heartObjectiveEverPainted = true;
            _heartObjectiveCapablePainted = capable;
            _heartObjectiveLockPainted = lock_;
            _heartObjectiveArmyVersionPainted = army.Version;
            _heartObjectiveHostilePainted = hostile;

            int troopsNeeded;
            string text = HeartObjectiveCopy.Resolve(hostile, capable, lock_, army, out troopsNeeded);
            if (_heartObjectiveLabel.text != text) _heartObjectiveLabel.text = text;

            // "barracks" is read off the lock reason - this View may not ask StructureSingleton
            // (DeNelle.HUD never references DeNelle.Village); the bridge already folded the
            // building test into RaidCapable/RaidLock, and NoBarracks/BarracksLost are the only
            // two reasons that mean "no Barracks stands".
            bool barracks = capable ||
                            (lock_ != PostureSignals.RaidLockReason.NoBarracks &&
                             lock_ != PostureSignals.RaidLockReason.BarracksLost);
            FlowTrace.Step("HudKit", "objective -> '" + text + "' (raidCapable=" + capable +
                           ", barracks=" + barracks + ", deployable=" + army.DeployableSlots +
                           ", queued=" + army.QueuedSlots + ", required=" + army.RequiredSlots +
                           ", ready=" + army.Ready + ", pastFirstRaid=" + army.PastFirstRaid +
                           ", lock=" + lock_ + ", hostile=" + hostile +
                           (troopsNeeded > 0 ? ", trainNeeded=" + troopsNeeded : "") + ")");
        }

        private static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // WO-611: locate the Blink target frame's portrait circle (prefab child "TargetIcon";
        // future re-skins may say "Portrait"). Null when the constructed MODE-2 frame is live.
        private static Image FindTargetPortrait611(Transform root)
        {
            if (root == null) return null;
            var images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                string n = images[i].name.Replace(" ", "").Replace("_", "");
                if (n.IndexOf("targeticon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0)
                    return images[i];
            }
            return null;
        }

        /// <summary>
        /// ⛔ WO-1369 (the seven-hold audit, PARTIAL #1): this controller had NO OnDisable, so the
        /// PLAYER-OWNED 'combat-item-picker' hold survived a HUD that was deactivated rather than
        /// destroyed - and a merely-disabled component never receives OnDestroy. Since WO-1360 took
        /// the ceiling off, that stranded the world clock at 0 with no picker anyone could answer.
        ///
        /// <para>Closing the picker here is the CORRECT behaviour, not just the safe one: a
        /// disabled HUD cannot drive the picker's buttons, so leaving its canvas up would be a
        /// modal nothing owns. <see cref="CloseItemPicker"/> is idempotent, so this costs nothing
        /// when no picker is open.</para>
        /// </summary>
        private void OnDisable()
        {
            CloseItemPicker();
        }

        private void OnDestroy()
        {
            CloseItemPicker();
            foreach (var u in _unsubscribe) { try { u(); } catch { /* teardown */ } }
            _unsubscribe.Clear();
            if (_targetFrame != null) _targetFrame.Unbind();
            if (_castBar != null) _castBar.Unbind();
            if (_evaluator != null) _evaluator.PostureChanged -= ApplyPosture;
            HudMoveInput.Set(Vector2.zero);
        }
    }

    /// <summary>
    /// THE SHARED RIGHT-RAIL GUTTER (owner ruling 2026-08-05; device review P2 "the right rail
    /// has three different right edges, the '!' chip runs off-screen").
    ///
    /// WHY A COMPONENT AND NOT A CONSTANT. The rail's elements are parented to HudAreasHost AREA
    /// mounts, which are anchored as FRACTIONS of the screen (QueueStatus and ActionRail both end
    /// at x = 0.995 — HudAreasHost.cs:99/117). 0.005 of the canvas is ~5 ref px at the 1080 author
    /// width and ~11 at the 2670x1200 Seeker, so any inset authored against the AREA lands on a
    /// different SCREEN margin at every aspect — which is exactly how the column ended up with
    /// three right edges. This component measures the live gap between its parent's right edge
    /// and the ROOT CANVAS' right edge and writes the difference into anchoredPosition.x, so the
    /// element's right edge sits <see cref="HudKitController.RailGutterPx"/> reference px from the
    /// SCREEN edge at every resolution — the same 54 ref px EchoUnlockFeedback.cs:381 authors for
    /// the Echoes chip, which lives on a different canvas entirely and cannot be edited from here.
    ///
    /// Presentation-only, allocation-free, and it writes ONLY on an actual change (a dirty flag
    /// plus a screen-size compare), so it is not a per-frame layout cost.
    /// Requires: anchorMin.x == anchorMax.x == 1 and pivot.x == 1 (what RailBand authors).
    /// </summary>
    internal sealed class HudRailGutter : MonoBehaviour
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private RectTransform _rt, _canvasRt;
        private bool _dirty = true;
        private int _lastW = -1, _lastH = -1;
        private bool _warned;

        private void OnEnable() { _dirty = true; }
        private void OnRectTransformDimensionsChange() { _dirty = true; }

        private void LateUpdate()
        {
            if (Screen.width != _lastW || Screen.height != _lastH)
            {
                _lastW = Screen.width; _lastH = Screen.height; _dirty = true;
            }
            if (!_dirty) return;
            Apply();
        }

        private void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) { _dirty = false; return; }
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;                      // re-parented by occupancy; retry
            if (_canvasRt == null)
            {
                var c = GetComponentInParent<Canvas>();
                if (c == null) return;                        // not mounted yet; retry
                var root = c.rootCanvas != null ? c.rootCanvas : c;
                _canvasRt = root.transform as RectTransform;
            }
            if (_canvasRt == null) return;

            if (parent.rect.width < 1f || _canvasRt.rect.width < 1f) return;   // unresolved; retry

            // Gap the parent already provides, measured in CANVAS-LOCAL units (== reference px,
            // the unit RailGutterPx and MinTouchPx are authored in). Going through the canvas'
            // own local space rather than lossyScale keeps this correct whatever scale the HUD
            // host happens to be parented under.
            parent.GetWorldCorners(Corners);
            float parentRight = _canvasRt.InverseTransformPoint(Corners[2]).x;
            float gap = _canvasRt.rect.xMax - parentRight;
            float inset = HudKitController.RailGutterPx - gap;
            if (inset < 0f)
            {
                // The parent already sits further inside than the shared gutter. Snapping the
                // chip back OUT would put it on a fourth edge, so hold at the parent's edge and
                // say so once — a real layout finding, never a silent swallow (CLAUDE.md 12.2).
                if (!_warned)
                {
                    _warned = true;
                    FlowTrace.Warn("HudKit", "rail gutter: parent '" + parent.name + "' already insets " +
                                   gap.ToString("F1") + " ref px > the shared " +
                                   HudKitController.RailGutterPx + " - pinning to the parent edge");
                }
                inset = 0f;
            }

            _dirty = false;
            if (Mathf.Approximately(_rt.anchorMin.x, _rt.anchorMax.x))
            {
                // Point-anchored at the right edge (RailBand) — move it.
                if (Mathf.Abs(_rt.anchoredPosition.x + inset) < 0.01f) return;
                _rt.anchoredPosition = new Vector2(-inset, _rt.anchoredPosition.y);
            }
            else
            {
                // Right-STRETCHED (the calm(explore) collapsed gold chip) — inset its right edge
                // so it lands on the same gutter instead of on a fourth edge.
                if (Mathf.Abs(_rt.offsetMax.x + inset) < 0.01f) return;
                _rt.offsetMax = new Vector2(-inset, _rt.offsetMax.y);
            }
        }
    }

    /// <summary>
    /// ⭐ WO-1435 — THE RAIL CHIP'S VERTICAL OFFSET IS DERIVED, NOT AUTHORED.
    /// ---------------------------------------------------------------------------
    /// THE DEFECT THIS EXISTS TO END (owner felt-test 2026-09-06, build 2026.09.06.358161:
    /// *"can we move harvest down when someone opens the resource window ... so it doesnt
    /// overlap"*): the resource panel's height is a FUNCTION of its row count
    /// (`BuildResourceChips`: `kinds.Length * ResRowHeightPx + (kinds.Length-1) * ResRowGapPx`)
    /// and grows DOWNWARD out of the ActionRail mount, while the Harvest/Collectors chip sat at
    /// a CONSTANT `yFromTopPx = 0f` in the QueueStatus mount directly beneath it. Two things
    /// sharing one gutter, one of them variable, and nothing reconciling them.
    ///
    /// ⛔ THE FIX IS NOT A BIGGER CONSTANT. A hand-picked offset that clears today's FOUR rows
    /// is the identical bug the day a fifth resource lands — the row count is `kinds.Length`,
    /// not a literal. A second hand-maintained number tracking a live one is the duplicated-state
    /// failure CLAUDE.md documents four separate times (§2 the stale WO block, §5 the retired
    /// dependency table, §7 `MaxVisibleFaces` — stale TWICE — §16 the copy-pasted R2 verify).
    /// The cure is never a better copy; it is deleting the copy. So this component MEASURES the
    /// source's laid-out bottom edge every time it can change, and places the chip beneath it.
    /// Add a fifth resource row and the chip moves with no second edit.
    ///
    /// THE RULE, in one line:
    ///   chipTopFromMountTop = max(authored base, (mountTop - sourceBottom) + RailGapPx)
    /// measured in CANVAS-LOCAL units — i.e. reference px, the same unit `RailGutterPx` and
    /// `MinTouchPx` are authored in, reached the same way <see cref="HudRailGutter"/> reaches it
    /// (world corners -> the ROOT canvas' local space), so it is correct whatever scale the HUD
    /// host is parented under and at every resolution. The two components are deliberately
    /// orthogonal: the gutter owns x, this owns y, and neither writes the other's axis — writing
    /// the whole vector from both would make them fight every LateUpdate.
    ///
    /// FIXED PIXELS ONLY (WO-841). Not a fraction of the band: a fraction can resolve under
    /// `MinTouchPx`, and `ClampMinTouch` then grows the chip about its CENTRE into its
    /// neighbour — which would recreate this exact overlap by a second route.
    /// The chip's 220x112 box is untouched (canon: `RailChipWidthPx` == the Echoes chip's width;
    /// three rail chips share one right edge). Nothing here narrows it, shortens its label or
    /// lowers its font — `ElarionUiKit.FontFloor` is a FLOOR, and the fleet capture recorded at
    /// `FormatCollectorChip` proves the fix for a tight label is FEWER CHARACTERS, never a
    /// smaller box.
    ///
    /// Presentation-only. It moves a band; it reads no state, owns no data, decides nothing.
    /// </summary>
    internal sealed class HudRailClearance : MonoBehaviour
    {
        private static readonly Vector3[] Corners = new Vector3[4];
        private static readonly List<RectTransform> Buf = new List<RectTransform>(32);

        /// <summary>The authored resting offset, used whenever no source is live.</summary>
        internal float BaseYFromTopPx;
        /// <summary>Gap held between the lowest live source edge and this band's top.</summary>
        internal float GapPx = HudKitController.RailGapPx;

        private readonly List<RectTransform> _sources = new List<RectTransform>(2);

        private RectTransform _rt, _canvasRt;
        private bool _dirty = true;
        private int _lastW = -1, _lastH = -1;
        private bool _lastSourcesLive;
        private float _appliedY = float.NaN;
        private bool _clampWarned;

        /// <summary>Add something this band must stay clear of. Order-independent and
        /// null-safe, so a caller built BEFORE its source can register itself later.</summary>
        internal void AddSource(RectTransform source)
        {
            if (source == null || _sources.Contains(source)) return;
            _sources.Add(source);
            _dirty = true;
        }

        /// <summary>The one owner of the expanded panel (SetResourcePanelOpen) rings this the
        /// frame it toggles. A SetActive on a DESCENDANT of the source raises no
        /// OnRectTransformDimensionsChange here, so without an explicit ring the only thing
        /// left would be polling — and the live-state compare below is the belt to this
        /// brace, never the primary signal.</summary>
        internal void MarkDirty() { _dirty = true; }

        private void OnEnable() { _dirty = true; _appliedY = float.NaN; }
        private void OnRectTransformDimensionsChange() { _dirty = true; }

        private void LateUpdate()
        {
            if (Screen.width != _lastW || Screen.height != _lastH)
            {
                _lastW = Screen.width; _lastH = Screen.height; _dirty = true;
            }
            // Cheap belt: an allocation-free bool over at most two roots. Catches any path that
            // shows/hides a source without ringing MarkDirty (occupancy re-parenting, a posture
            // flip, a future second opener) so the chip can never be left parked over a panel.
            bool live = SourcesLive();
            if (live != _lastSourcesLive) { _lastSourcesLive = live; _dirty = true; }
            if (!_dirty) return;
            Apply();
        }

        private bool SourcesLive()
        {
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i] != null && _sources[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        private void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) { _dirty = false; return; }
            if (_canvasRt == null)
            {
                var c = GetComponentInParent<Canvas>();
                if (c == null) return;                        // not mounted yet; retry (KEEP dirty)
                var root = c.rootCanvas != null ? c.rootCanvas : c;
                _canvasRt = root.transform as RectTransform;
            }
            if (_canvasRt == null) return;
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;                       // re-parented by occupancy; retry
            if (parent.rect.height < 1f || _canvasRt.rect.height < 1f) return;   // unresolved; retry

            // ⛔ PRE-SETTLE IS A RETRY, NEVER A VERDICT — and it must NOT clear the dirty flag.
            // A rect that has been built and activated in the same frame has not been through a
            // layout pass and reads 0x0 (registry shape H4; the same rule TickResourceExpandVerify
            // polls for at "MEASURE AFTER LAYOUT SETTLES"). Concluding from that read would park
            // the chip at its base offset forever, and the live-state compare could not rescue it
            // because the state never changes again. So: return with _dirty still set.
            float mountTop = CanvasTopOf(parent);
            if (float.IsNaN(mountTop)) return;

            float lowest = float.NaN;
            for (int i = 0; i < _sources.Count; i++)
            {
                var s = _sources[i];
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                float b = UnionBottom(s);
                if (float.IsNaN(b)) return;                   // measurable-but-unsettled: retry
                if (float.IsNaN(lowest) || b < lowest) lowest = b;
            }

            // No live source => the authored resting offset. "Nothing to clear" is a real,
            // named state, never a stale hold of the last computed y.
            float want = BaseYFromTopPx;
            if (!float.IsNaN(lowest))
                want = Mathf.Max(BaseYFromTopPx, (mountTop - lowest) + GapPx);

            // The chip may legitimately hang below its own mount (the QueueStatus band is a mount
            // point, not a clip rect — HudAreasHost builds bare RectTransforms, no Mask), but it
            // may never leave the screen. If the clamp ever bites, the rail genuinely cannot fit
            // and that is a FINDING, not something to swallow (CLAUDE.md §12.2 — no silent
            // failures). Say it once, with the numbers.
            float maxY = (mountTop - _canvasRt.rect.yMin) - _rt.rect.height;
            if (maxY > BaseYFromTopPx && want > maxY)
            {
                if (!_clampWarned)
                {
                    _clampWarned = true;
                    FlowTrace.Warn("HudKit", "rail clearance: '" + name + "' wants y=" +
                                   want.ToString("F1") + " ref px to clear the panel above it but the " +
                                   "canvas bottom caps it at " + maxY.ToString("F1") + " - the right " +
                                   "column can no longer seat the expanded panel AND this chip. " +
                                   "The chip will overlap; shorten the panel or move the chip's band.");
                }
                want = maxY;
            }

            _dirty = false;
            if (!float.IsNaN(_appliedY) && Mathf.Abs(_appliedY - want) < 0.01f) return;
            _appliedY = want;
            // x belongs to HudRailGutter. Write ONLY y.
            _rt.anchoredPosition = new Vector2(_rt.anchoredPosition.x, -want);
            FlowTrace.Step("HudKit", "rail clearance: '" + name + "' y=" + want.ToString("F1") +
                           " ref px from mount top (base " + BaseYFromTopPx.ToString("F1") +
                           ", gap " + GapPx.ToString("F1") + ", mountTop " + mountTop.ToString("F1") +
                           ", lowest live source edge " +
                           (float.IsNaN(lowest) ? "none" : lowest.ToString("F1")) +
                           ") - DERIVED from the laid-out panel, never a literal (WO-1435).");
        }

        /// <summary>Canvas-local y of a rect's TOP edge, or NaN if it has not laid out yet.</summary>
        private float CanvasTopOf(RectTransform rt)
        {
            if (rt.rect.height < 1f) return float.NaN;
            rt.GetWorldCorners(Corners);
            return _canvasRt.InverseTransformPoint(Corners[1]).y;   // [1] = top-left
        }

        /// <summary>Canvas-local y of the LOWEST edge of <paramref name="root"/> and every ACTIVE
        /// RectTransform beneath it — which is precisely what makes this derived: the expanded
        /// resource stack, its rows and the "+N" hint are all descendants of the gold chip, so a
        /// fifth resource row deepens this union and the chip follows with no second edit.
        /// NaN means "not laid out yet" (see the pre-settle note in Apply).</summary>
        private float UnionBottom(RectTransform root)
        {
            float lowest = float.NaN;
            Buf.Clear();
            root.GetComponentsInChildren(false, Buf);   // false = skip inactive; no allocation
            for (int i = 0; i < Buf.Count; i++)
            {
                var rt = Buf[i];
                if (rt == null || rt.rect.height < 1f) continue;
                rt.GetWorldCorners(Corners);
                float b = _canvasRt.InverseTransformPoint(Corners[0]).y;   // [0] = bottom-left
                if (float.IsNaN(lowest) || b < lowest) lowest = b;
            }
            Buf.Clear();
            return lowest;
        }
    }

    /// <summary>
    /// WO-611: positions the Q/W/E/R medallions AROUND THE ATTACK PILL, in pill-height units,
    /// at layout time (capture 2026-07-06 battle_hud.png — the previous zone-fraction arc
    /// scattered the medallions across the whole actionRail; see BuildAbilityRow).
    ///
    /// Geometry (mockup em = pillHeight / 3.5):
    ///   pill rect  = the row's own rect x the shared HudKitController.Pill611* fractions
    ///                (the AbilityRow widget wrap and the attackButton wrap both stretch the
    ///                same actionRail mount, so the rects agree by construction — works even
    ///                in hostile(prebattle) where the pill widget itself is unoccupied);
    ///   pivot      = pill top-right corner, inset 1.8em left (keeps the last medallion's
    ///                right edge inside the pill/screen);
    ///   centres    = pivot + 8em * (cos, sin) at 171deg -> 92.7deg (Q lowest-left just above
    ///                the pill's top, sweeping up over the pill to R above its right end);
    ///   diameter   = 0.9x pill height; adjacent centre spacing = 2*8em*sin(13.05deg)
    ///                ~ 3.62em = 1.15x diameter => gap ~15% of a diameter (nearly touching).
    ///
    /// Presentation-only re-layout: taps, icons, key badges, dimmed-empty faces and the soft
    /// cooldown glow all live on the slots themselves and are untouched. Recomputes only on
    /// enable/resize (dirty flag), never per-frame.
    /// </summary>
    internal sealed class CombatArcLayout611 : MonoBehaviour
    {
        internal RectTransform[] Medallions;

        private static readonly float[] ArcAngleDeg = { 171.0f, 144.9f, 118.8f, 92.7f };
        private const float ArcRadiusEm = 8.0f;     // arc radius around the pivot, in em
        private const float PivotInsetEm = 1.8f;    // pivot inset left of the pill's top-right
        private const float MedallionPerPillH = 0.9f;
        private bool _dirty = true;

        private void OnEnable() { _dirty = true; }
        private void OnRectTransformDimensionsChange() { _dirty = true; }

        private void LateUpdate()
        {
            if (!_dirty || Medallions == null) return;
            var row = (RectTransform)transform;
            var r = row.rect;
            if (r.width < 1f || r.height < 1f) return;   // layout not resolved yet — retry
            _dirty = false;

            float pillH = (HudKitController.Pill611Y1 - HudKitController.Pill611Y0) * r.height;
            float em = pillH / 3.5f;
            float d = MedallionPerPillH * pillH;
            var pivotPt = new Vector2(
                r.xMin + HudKitController.Pill611X1 * r.width - PivotInsetEm * em,
                r.yMin + HudKitController.Pill611Y1 * r.height);

            for (int i = 0; i < Medallions.Length && i < ArcAngleDeg.Length; i++)
            {
                var m = Medallions[i];
                if (m == null) continue;
                float a = ArcAngleDeg[i] * Mathf.Deg2Rad;
                Vector2 centre = pivotPt + ArcRadiusEm * em * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                m.anchorMin = m.anchorMax = new Vector2(0.5f, 0.5f);
                m.pivot = new Vector2(0.5f, 0.5f);
                m.sizeDelta = new Vector2(d, d);
                m.anchoredPosition = centre - r.center;
            }
        }
    }
}

// =============================================================================
// HudActionBarModel — the COMMON applicability model for the bottom action bar
// (WO-835; owner architecture law 2026-08-02, HP B2B: "the applicability logic is
// managed in COMMON, not in the presentation class").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.HudModel
//
// THE PROBLEM THIS SOLVES: HudKitController.Update() held per-button gate reads
// (Talk dim via PostureSignals, Raids dim via RaidEntryGate.ArmyStatus, Map hide
// via GameStateService.Onboarded, Quests<->Upgrade relabel via HudBuildingFocus)
// and the bar was a FIXED /6 row — hiding a face left a visible HOLE. This model
// owns every predicate and publishes ONE ordered array of ACTIVE buttons; the
// View (HudKitController) subscribes ActiveButtonsChanged and just renders +
// centers the array it is passed. Zero predicates remain in the View.
//
// INPUTS (all Core-visible; Village mirrors in via the *HudBridge push seam):
//   TalkAvailable    - PostureSignals.TalkAvailable   (TalkHudBridge pushes)
//   RaidCapable      - PostureSignals.RaidCapable     (RaidCapabilityHudBridge:
//                      FeatureFlags.Raid AND barracks built. ⚠ WO-1008: the old
//                      "AND >=1 deployable" clause was REMOVED — an empty army is
//                      a DIM reason, not a hide reason.)
//   RaidArmyReady    - RaidEntryGate.ArmyStatus.Ready (BuildTimerService pushes;
//                      WO-820/823 dim gate — SEMANTICS PRESERVED: a capable-but-
//                      not-full army DIMS the visible Raids face, never disables,
//                      so the tap still reaches the drillmaster redirect)
//   Raid slot counts - RaidEntryGate.ArmyStatus deployable/queued/cap. WO-1008
//                      dim-REASON inputs only: they pick NoTroops vs ArmyNotFull
//                      and build the face's WORD/NUMBER tell (the owner is
//                      red/green colourblind — grey alone says nothing).
//   MapUnlocked      - GameStateService.State.Onboarded (WO-825 R4 / WO-826)
//   BuildingFocused  - HudBuildingFocus.CurrentBuildingId non-empty
//   posture key      - forwarded by the View from PostureEvaluator (the View
//                      relays the notification it already receives; the town/
//                      explore set mapping lives HERE, not in the View)
//
// EDGE-TRIGGERED: Tick() polls the cheap sources every frame (the established
// HudBuildingFocus/ObsidianQueueGate poll precedent — no model event exists for
// most of these statics); ActiveButtonsChanged fires ONLY when the computed set
// actually changes, so the View re-lays out only on a real transition, never
// per-frame. The eventful signals (TalkChanged/RaidCapableChanged) are also
// subscribed by the Shared instance for same-frame response.
//
// TESTABILITY (the payoff of moving logic to Core): the ISource seam lets
// EditMode tests + HudActionBarRegression drive every signal combination through
// the REAL compute and assert the exact output array (impossible when the logic
// lived in the View's Update()). ServiceSource is the SOLE live resolution site.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.HudModel
{
    /// <summary>
    /// Identity of a bottom-action-bar button (WO-835). Enum ORDER IS THE BAR
    /// ORDER — the model emits actives sorted by this ordinal, left to right.
    /// Upgrade is the WO-835 split-out: its own context button (a focused
    /// building packs it IN), so Quests is never relabeled away again.
    /// </summary>
    public enum ActionBarButtonId
    {
        Build = 0,
        Talk = 1,
        Bag = 2,
        Raids = 3,

        /// <summary>
        /// ⚠ RETIRED FROM THE BAR (WO-911, owner ruling Q10+Q13, 2026-08-06) — Map moved INTO Bag
        /// as a tab. The enum VALUE is kept DORMANT on purpose, not deleted: the ordinal is the bar
        /// order AND the index into the View's face arrays, so renumbering would silently re-point
        /// every other face. Nothing ever sets this bit any more (see <c>ComputeMask</c>), so it
        /// never renders. Do not reuse the value for a new face.
        /// </summary>
        Map = 4,

        Quests = 5,

        /// <summary>
        /// RE-POINTED, NOT ADDED (WO-911, ruling Q10+Q13). This face was the context-sensitive
        /// "Upgrade" button; it is now the single door to the unified MANAGE / QUEUES screen
        /// (<c>PanelId.Manage</c>) and is always applicable in town rather than gated on a focused
        /// building. Keeping the VALUE at 6 is what dissolves the 8th-face problem entirely — no
        /// enum extension, no <see cref="HudActionBarModel.ButtonCount"/> increase, no new
        /// hud-areas.json widget id (the row stays "upgradeButton").
        /// </summary>
        Upgrade = 6,
    }

    /// <summary>
    /// Computes the ordered set of APPLICABLE action-bar buttons from the Core
    /// context signals and raises <see cref="ActiveButtonsChanged"/> on a real
    /// set change (WO-835). The View renders exactly the array it is passed.
    /// </summary>
    public sealed class HudActionBarModel
    {
        /// <summary>
        /// Number of button IDENTITIES — the array-sizing / mask-iteration bound for the View.
        /// -------------------------------------------------------------------------------------
        /// ⚠ THIS IS NOT THE NUMBER OF FACES THAT RENDER. The rendered list is
        /// <see cref="Active"/>, whose length varies 0..<see cref="MaxVisibleFaces"/> per posture;
        /// the View lays out <c>Active.Count</c> faces and centres them, so a shorter set can never
        /// leave a dead trailing slot.
        ///
        /// WO-911 (ruling Q10+Q13) settled this at source, because the ruling's "no ButtonCount
        /// change is needed" is exact for the Upgrade->Manage RE-POINT but Map's REMOVAL is a
        /// separate count question: this stays 7 because <see cref="ActionBarButtonId.Map"/> is kept
        /// DORMANT rather than renumbered, and every face array is indexed by the enum ORDINAL
        /// (Upgrade = 6). Dropping this to 6 would put Upgrade out of bounds. The count that
        /// actually changed 7 -> 6 is <see cref="MaxVisibleFaces"/>, which is what the View's slot
        /// geometry must derive from.
        /// </summary>
        public const int ButtonCount = 7;

        /// <summary>
        /// The maximum length <see cref="Active"/> can reach — i.e. the widest set
        /// <see cref="ComputeMask"/> can produce. Read that method for the authority: both calm
        /// postures currently return an UNCONDITIONAL four bits (Build, Bag, Quests, Upgrade);
        /// Talk, Raids and Map are dormant identities that no branch sets any more. Every other
        /// posture returns 0.
        ///
        /// ⛔ WO-1467 — THIS IS NOT THE NUMBER OF FACES THE PLAYER SEES, AND IT NEVER WAS.
        /// The shipped bottom bar is the adaptive peaceful dock, built by
        /// <c>HudKitController.BuildAdaptivePeacefulDock</c>; <c>HudKitController.BindActionBar</c>
        /// returns early whenever that dock exists, so this model is never subscribed on the
        /// shipping path and its geometry does not size the dock's medallions
        /// (<c>HudDockSlotLayout</c> / <c>DeNelle.Core.UI.HudDockLayout</c> solve those in
        /// reference pixels from the live mount). Two suites used to assert a literal against this
        /// constant AS IF it described the dock; those pins are retired and the dock is now covered
        /// by a measured oracle — <c>HudActionBarRegression.CheckMeasuredPeacefulDock</c>, which
        /// builds the real dock and reads the faces out of the built tree.
        ///
        /// What legitimately consumes it (all of them model-side or legacy-geometry, none of them
        /// the shipped dock): <c>HudKitController.BarSlotW</c> (the retired repacker's slot width),
        /// <c>UICaptureLaunch</c>'s bar mock, <c>HudActionBarRegression</c>'s overflow check and
        /// <c>HudActionBarModelTests</c>. <see cref="ButtonCount"/> is a DIFFERENT axis — the
        /// enum-identity bound, deliberately larger, and the one that must never be renumbered.
        /// </summary>
        public const int MaxVisibleFaces = 4;

        // Posture keys the model maps to button sets (HudPostureKeys spellings —
        // the owner's hud-areas.json vocabulary; any other key => empty bar,
        // matching the occupancy rows that drop the bar in build/hostile/modal).
        public const string PostureTown = "calm(town)";
        public const string PostureExplore = "calm(explore)";

        /// <summary>Context-signal seam — tests inject a fake; live code uses
        /// <see cref="ServiceSource"/> (the sole live singleton-resolution site).</summary>
        public interface ISource
        {
            /// <summary>A talkable NPC is in range (PostureSignals.TalkAvailable).</summary>
            bool TalkAvailable { get; }
            /// <summary>Player CAN raid: FeatureFlags.Raid + a built Barracks
            /// (PostureSignals.RaidCapable, Village-published).
            /// ⚠ WO-1008 (owner ask 2026-08-16 "can we add a greyed out option once we have a
            /// barracks"): the ">=1 deployable troop" clause was REMOVED from this VISIBILITY
            /// predicate. An empty army is now a DIMMED-WITH-A-REASON face, never an absent one —
            /// the owner hit a built Barracks with zero troops and reported "I do not see a way to
            /// start a raid", because a feature that hides itself is indistinguishable from a
            /// broken one.</summary>
            bool RaidCapable { get; }
            /// <summary>Army full (WO-820/823 dim gate, RaidEntryGate.ArmyStatus.Ready).</summary>
            bool RaidArmyReady { get; }
            /// <summary>Healthy roster slots (RaidEntryGate.ArmyStatus.DeployableSlots) — WO-1008
            /// dim-REASON input only; never a visibility input.</summary>
            int RaidDeployableSlots { get; }
            /// <summary>Slots committed to in-flight Train jobs (ArmyStatus.QueuedSlots).</summary>
            int RaidQueuedSlots { get; }
            /// <summary>Army cap in slots (ArmyStatus.CapSlots).</summary>
            int RaidCapSlots { get; }
            /// <summary>Realm Map unlocked (GameState.Onboarded, WO-825 R4).</summary>
            bool MapUnlocked { get; }
            /// <summary>An upgradable building holds focus (HudBuildingFocus).</summary>
            bool BuildingFocused { get; }
        }

        /// <summary>The live source — reads the Core statics the retired View
        /// polls read, in one place.</summary>
        private sealed class ServiceSource : ISource
        {
            public bool TalkAvailable => PostureSignals.TalkAvailable;
            public bool RaidCapable => PostureSignals.RaidCapable;
            public bool RaidArmyReady => DeNelle.Core.UI.RaidEntryGate.ArmyStatus.Ready;
            public int RaidDeployableSlots => DeNelle.Core.UI.RaidEntryGate.ArmyStatus.DeployableSlots;
            public int RaidQueuedSlots => DeNelle.Core.UI.RaidEntryGate.ArmyStatus.QueuedSlots;
            public int RaidCapSlots => DeNelle.Core.UI.RaidEntryGate.ArmyStatus.CapSlots;
            public bool MapUnlocked
            {
                get
                {
                    // Explicit null checks — UnityEngine.Object never gets ?. (lint law).
                    var gs = DeNelle.Core.State.GameStateService.Instance;
                    return gs != null && gs.State != null && gs.State.Onboarded;
                }
            }
            public bool BuildingFocused =>
                !string.IsNullOrEmpty(DeNelle.Core.UI.HudBuildingFocus.CurrentBuildingId);
        }

        /// <summary>The one live model instance the HUD kit binds. Static lifetime
        /// (like the signal statics it composes); each kit instance subscribes on
        /// bind and unsubscribes on destroy, so scene swaps never go stale.</summary>
        public static HudActionBarModel Shared { get; } =
            new HudActionBarModel(new ServiceSource(), subscribeSignals: true);

        private readonly ISource _source;
        private readonly List<ActionBarButtonId> _active = new List<ActionBarButtonId>(ButtonCount);
        private int _activeMask = -1;        // -1 = never computed (first compute always publishes)
        private string _postureKey = "";
        private bool _raidsDimmed;
        private bool _raidsDimComputed;
        private RaidDimReason _raidsDimReason = RaidDimReason.None;
        private string _raidsFaceLabel = RaidsBaseLabel;
        private string _raidsFaceBadge = "";
        private string _manageFaceLabel = ManageBaseLabel;
        private string _manageFaceBadge = "";
        private int _manageStatusVersion = int.MinValue;   // change-detect on the published snapshot

        /// <summary>The base (undimmed) Raids face word. The View builds with this exact string.</summary>
        public const string RaidsBaseLabel = "Raids";

        /// <summary>The base (unadorned) Manage face word. The View builds with this exact string.</summary>
        public const string ManageBaseLabel = "Manage";

        /// <summary>
        /// WO-1027 — the SESSION-SHAPE tell on the Manage face. CoC's retention engine was never
        /// the queue; it was the ACHE of an idle builder. CoC carries that ache on a RED BADGE,
        /// which is banned here outright: the owner is red/green colourblind, so the message is
        /// carried by a NUMERAL — it has no hue to get wrong and needs no mitigation. Same
        /// WORD/NUMBER-tell contract the Raids face already ships (WO-1008, RaidsFaceLabel above).
        ///   3 idle  -> "Manage - 3 idle"        (nothing cooking at all; the denominator is noise)
        ///   1-2     -> "Manage - 2 of 3 idle"   (the denominator is what makes it legible)
        ///   0       -> "Manage"                 (the calm state IS the bare word; silence is the reward)
        /// The ache lives ON the button that fills it, so WO-1027 §3.2 ("one tap from the thing that
        /// fills it") costs zero new code — OnManageAction is already the single Queues door.
        /// ⛔ NEVER append a colour, a glyph badge or a tint. If it does not read, make the WORD
        /// clearer. ⛔ Never a toast — ruling (c) of WO-1027 §4 is REJECTED, and a permanent
        /// adornment on a calm bar is that nag sneaking back in by another door.
        /// </summary>
        public string ManageFaceLabel => _manageFaceLabel;

        /// <summary>
        /// WO-1144 — the SAME session-shape tell, split into the half that can actually be
        /// PAINTED ON A BAR FACE.
        ///
        /// PROVEN CAUSE (2026-08-22 headed fleet, break_24_error.png, identical in all 8 runs):
        /// the Manage face rendered "Manag..." while Build/Bag/Raids/Quests all fit. It is not a
        /// font fault and not a missing fit call — <see cref="ManageFaceLabel"/> is a SENTENCE
        /// ("Manage - 2 of 3 idle", up to 20 characters) and a bar face is one
        /// <see cref="MaxVisibleFaces"/>-th of a 46 %-wide ActionBar zone, about 144 reference px
        /// of label rect. At ElarionUiKit's 30 px legibility FLOOR that seats roughly ten
        /// characters, so FitSingleLine did the only thing left to it and ellipsised. No layout
        /// number was wrong; the string was four times its box.
        ///
        /// So the numeral moves to a SECOND LINE on the face (the face is ~110 ref px tall — two
        /// lines cost nothing) and the badge carries ONLY the count:
        ///   3 idle  -> "3 idle"     (the denominator is noise when it equals the numerator)
        ///   1-2     -> "2/3 idle"   (the denominator is what makes it legible; "of" does not fit)
        ///   0       -> ""           (the calm state IS the bare word; silence is the reward)
        ///
        /// <see cref="ManageFaceLabel"/> is DELIBERATELY UNCHANGED — it stays the one-line
        /// sentence WO-1027 authored, for traces, tooltips and anything with room for it. The
        /// View paints <see cref="ManageBaseLabel"/> + newline + this badge.
        /// ⛔ Still never a colour, a glyph badge or a tint: if it does not read, the WORDS get
        /// shorter — and now they can, because they live in canon-strings.json
        /// (<see cref="DeNelle.Core.UI.HudStrings"/>) rather than inline here.
        /// </summary>
        public string ManageFaceBadge => _manageFaceBadge;

        /// <summary>
        /// WHY the Raids face is greyed (WO-1008). The owner is red/green colourblind — a grey tint
        /// carries NO meaning for her, so every dim state must ship a WORD/NUMBER tell as well
        /// (<see cref="RaidsFaceLabel"/> on the face, <see cref="RaidsDimMessage"/> on the tap).
        /// The two reasons are deliberately distinct: "you have no army at all" is a different
        /// player action from "your army is not full yet".
        /// </summary>
        public enum RaidDimReason
        {
            /// <summary>Not dimmed (face is live).</summary>
            None = 0,
            /// <summary>Barracks built, but ZERO troops ready AND zero training — go train.</summary>
            NoTroops = 1,
            /// <summary>Some troops, but ready+queued does not cover the cap (WO-820 gate).</summary>
            ArmyNotFull = 2,
        }

        /// <summary>Raised when the ACTIVE set changed (edge-triggered — never per-frame).
        /// The View re-renders + re-centers exactly <see cref="Active"/>.</summary>
        public event Action ActiveButtonsChanged;

        /// <summary>Raised when the Raids dim state changed (WO-820 full-army gate —
        /// visual dim only; the View never reads the army status itself).</summary>
        public event Action RaidsDimmedChanged;

        /// <summary>WO-1027 — raised ONLY when <see cref="ManageFaceLabel"/> actually changed
        /// (a transition, never a per-frame string build). The View repaints the face word.</summary>
        public event Action ManageFaceChanged;

        /// <summary>The ordered active buttons (enum order, left to right).</summary>
        public IReadOnlyList<ActionBarButtonId> Active => _active;

        /// <summary>
        /// WO-1407 — the Village-published army SNAPSHOT, republished here so the View never
        /// reads the gate itself. The army predicate is the model's (WO-835 View-purity law,
        /// pinned by HudActionBarRegression.CheckViewPurity: HudKitController.cs may not contain
        /// the literal RaidEntryGate.ArmyStatus), and the Heart plate's objective line needs the
        /// whole struct — Version (change detection) and RequiredSlots (the WO-823 bar) are
        /// SNAPSHOT FIELDS, not predicates.
        ///
        /// It deliberately bypasses <see cref="_source"/>: <see cref="ISource"/> carries only the
        /// derived bools/ints the bar's own predicates need, and widening that interface would
        /// force every fixture that implements it (the headless FakeSource + the EditMode fixture
        /// it mirrors) to change for a value no bar face consumes.
        /// </summary>
        public DeNelle.Core.UI.RaidEntryGate.RaidArmyStatus ArmySnapshot =>
            DeNelle.Core.UI.RaidEntryGate.ArmyStatus;

        /// <summary>True while Raids is applicable but the army is not full — the View
        /// tints the face toward Disabled and keeps it INTERACTABLE (owner ruling:
        /// a dimmed tap still opens the drillmaster redirect).</summary>
        public bool RaidsDimmed => _raidsDimmed;

        /// <summary>WO-1008 — WHY the face is greyed. <see cref="RaidDimReason.None"/> when live.</summary>
        public RaidDimReason RaidsDimReason => _raidsDimReason;

        /// <summary>
        /// WO-1008 — the Raids face TEXT for the current state. This is the colourblind-safe tell:
        /// the greyed face reads "Raids 0/5" (nothing trained) or "Raids 3/5" (not full) instead of
        /// a plain "Raids" that differs only by hue. ASCII only; kept short because the kit
        /// single-line-fits (auto-shrink + ellipsis) every bar label.
        /// </summary>
        public string RaidsFaceLabel => _raidsFaceLabel;

        /// <summary>
        /// WO-1219 — the SAME dim tell, split into the half that can actually be PAINTED ON A
        /// BAR FACE. Exactly the <see cref="ManageFaceBadge"/> split WO-1144 made, for exactly
        /// the same proven reason and now with a second capture behind it.
        ///
        /// PROVEN CAUSE (owner Seeker felt-test 2026-08-26, tmp/screen-103219.png and
        /// tmp/shield-seat-101829.png, both 2670x1200): the Raids face rendered "Raids ..." while
        /// Build/Bag/Quests all fit. It is not a font fault and not a missing fit call —
        /// <see cref="RaidsFaceLabel"/> is "Raids 0/5" (9 characters) and a bar face is one
        /// <see cref="MaxVisibleFaces"/>-th of a 46 %-wide ActionBar zone, about 144 reference px
        /// of label rect, which at ElarionUiKit's 30 px legibility FLOOR seats roughly ten
        /// characters INCLUDING the button's own side padding. FitSingleLine did the only thing
        /// left to it and ellipsised — and it ellipsised away the NUMBERS, which are the entire
        /// colourblind-safe tell (WO-1008). A greyed face reading "Raids ..." says strictly less
        /// than a greyed face reading "Raids".
        ///
        /// So the numerals move to a SECOND LINE on the face (the face is ~110 ref px tall — two
        /// lines cost nothing) and the badge carries ONLY the count:
        ///   live        -> ""      (the calm state IS the bare word)
        ///   dimmed      -> "0/5"   (nothing trained) or "3/5" (army not full)
        ///
        /// <see cref="RaidsFaceLabel"/> is DELIBERATELY UNCHANGED — it stays the one-line string
        /// WO-1008 authored, for traces, tooltips, the RaidsFaceStates capture caption and
        /// anything with room for it. The View paints <see cref="RaidsBaseLabel"/> + newline +
        /// this badge. ASCII only; never a colour, never a glyph.
        /// </summary>
        public string RaidsFaceBadge => _raidsFaceBadge;

        /// <summary>
        /// WO-1008 — the full sentence for the greyed state, for any surface that can afford one
        /// (toast / tooltip). The Village-side RaidSelectionScreen owns the AUTHORITATIVE refusal
        /// copy on tap; this is the same distinction stated Core-side so a View never invents one.
        /// </summary>
        public string RaidsDimMessage
        {
            get
            {
                switch (_raidsDimReason)
                {
                    case RaidDimReason.NoTroops:
                        return "No troops yet - train troops at the Barracks to start a raid.";
                    case RaidDimReason.ArmyNotFull:
                        return "Army " + (_source.RaidDeployableSlots + _source.RaidQueuedSlots) + "/" +
                               Math.Max(1, _source.RaidCapSlots) +
                               " - fill every slot at the Barracks, then open Raids.";
                    default:
                        return "";
                }
            }
        }

        /// <summary>The posture key last forwarded by the View (test/probe seam).</summary>
        public string PostureKey => _postureKey;

        /// <summary>Construct around a signal source. Tests pass a fake and leave
        /// <paramref name="subscribeSignals"/> false; the Shared live instance
        /// subscribes the eventful Core signals for same-frame edge response.</summary>
        public HudActionBarModel(ISource source, bool subscribeSignals = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _source = source;
            if (subscribeSignals)
            {
                // Static events + static-lifetime Shared instance — no teardown needed.
                PostureSignals.TalkChanged += Recompute;
                PostureSignals.RaidCapableChanged += Recompute;
            }
        }

        /// <summary>The View forwards each posture transition it receives from the
        /// evaluator (a relay, not a predicate — the key->set mapping lives here).</summary>
        public void SetPosture(string postureKey)
        {
            _postureKey = postureKey ?? "";
            Recompute();
        }

        /// <summary>Poll the poll-only sources (no model event exists for these Core
        /// statics — the HudBuildingFocus/ObsidianQueueGate precedent) and recompute.
        /// Cheap: five bool reads; the OUTPUT is edge-triggered.</summary>
        public void Tick() => Recompute();

        private void Recompute()
        {
            int mask = ComputeMask();
            bool raidsVisible = (mask & (1 << (int)ActionBarButtonId.Raids)) != 0;
            bool dim = raidsVisible && !_source.RaidArmyReady;

            // WO-1008: the SAME dim mechanism now carries TWO reasons. Zero troops AND zero
            // training is "go train"; anything else short of the cap is the WO-820 full-army
            // gate. Never a single generic grey — that tells the player nothing.
            RaidDimReason reason = RaidDimReason.None;
            if (dim)
                reason = (_source.RaidDeployableSlots + _source.RaidQueuedSlots) <= 0
                    ? RaidDimReason.NoTroops
                    : RaidDimReason.ArmyNotFull;
            // WO-1219: the numerals are built ONCE and used twice — the one-line label (traces,
            // tooltips, captures) and the face BADGE (the second line the bar actually paints).
            // One source, so the sentence and the face can never disagree.
            string faceBadge = reason == RaidDimReason.None
                ? ""
                : Math.Max(0, _source.RaidDeployableSlots + _source.RaidQueuedSlots) +
                  "/" + Math.Max(1, _source.RaidCapSlots);
            string faceLabel = reason == RaidDimReason.None
                ? RaidsBaseLabel
                : RaidsBaseLabel + " " + faceBadge;

            if (mask != _activeMask)
            {
                _activeMask = mask;
                _active.Clear();
                for (int i = 0; i < ButtonCount; i++)
                    if ((mask & (1 << i)) != 0) _active.Add((ActionBarButtonId)i);
                FlowTrace.Step("HudKit", "action bar set -> [" + DescribeActive() +
                               "] mask=0x" + mask.ToString("X2") + " (posture '" + _postureKey + "')");
                ActiveButtonsChanged?.Invoke();
            }

            // Edge on the REASON too, not just the bool: 0 troops -> 2 troops keeps dim==true but
            // changes the face text, and the View repaints only on this event.
            if (!_raidsDimComputed || dim != _raidsDimmed || reason != _raidsDimReason ||
                !string.Equals(faceLabel, _raidsFaceLabel, StringComparison.Ordinal) ||
                !string.Equals(faceBadge, _raidsFaceBadge, StringComparison.Ordinal))
            {
                _raidsDimComputed = true;
                _raidsDimmed = dim;
                _raidsDimReason = reason;
                _raidsFaceLabel = faceLabel;
                _raidsFaceBadge = faceBadge;
                if (dim)
                    FlowTrace.Step("HudKit", "Raids face DIMMED reason=" + reason + " label='" + faceLabel +
                                   "' (visible + interactable - tap still reaches the drillmaster redirect)");
                else if (raidsVisible)
                    FlowTrace.Step("HudKit", "Raids face restored (army full)");
                RaidsDimmedChanged?.Invoke();
            }

            RecomputeManageFace();
        }

        // WO-1027 — the session-shape numeral on the Manage face.
        // ---------------------------------------------------------------------
        // GUARDED ON THE PUBLISHED VERSION, not the frame: ObsidianQueueGate.Status bumps
        // Version once per publish (BuildTimerService's QueueChanged + its 1s tick), so this
        // builds a string only on a real transition. Everything it needs is derived ONCE, on
        // the snapshot itself (WorkQueueStatus.IdleLineCount) — this model computes no
        // idleness of its own, so it can never disagree with the rail or the Manage screen.
        private void RecomputeManageFace()
        {
            var st = DeNelle.Core.UI.ObsidianQueueGate.Status;
            if (st.Version == _manageStatusVersion) return;
            _manageStatusVersion = st.Version;

            if (!st.Available)
            {
                // NORMAL AT BOOT, not a defect — Once, never Warn. Without this line "the numeral
                // never appears" is indistinguishable from "the numeral is broken", which is the
                // exact ambiguity §12.3 says to split BEFORE touching code.
                FlowTrace.Once("HudKit", "manage-tell-unavailable",
                    "ObsidianQueueGate.Status.Available is false - the Manage face shows the bare word; " +
                    "BuildTimerService has not published a queue snapshot yet.");
            }

            int idle = st.IdleLineCount();
            string label =
                idle <= 0 ? ManageBaseLabel :
                idle >= DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus.LineCount
                    ? ManageBaseLabel + " - " + idle + " idle"
                    : ManageBaseLabel + " - " + idle + " of " +
                      DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus.LineCount + " idle";

            // WO-1144: the FACE half of the same tell — the count with no verb and no "of", short
            // enough to seat on a ~144 ref px face at the legibility floor. Words from canon.
            string badge =
                idle <= 0 ? "" :
                idle >= DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus.LineCount
                    ? DeNelle.Core.UI.HudStrings.Format(DeNelle.Core.UI.HudStrings.KeyManageIdleAll, idle)
                    : DeNelle.Core.UI.HudStrings.Format(DeNelle.Core.UI.HudStrings.KeyManageIdleSome,
                        idle, DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus.LineCount);

            if (string.Equals(label, _manageFaceLabel, StringComparison.Ordinal) &&
                string.Equals(badge, _manageFaceBadge, StringComparison.Ordinal)) return;

            // Step on the EDGE only. A permanent/recurring condition logged per frame buries the
            // owner's real F8 signals — and an idle line is a NORMAL player state, so it is never
            // Warn/Fail either.
            FlowTrace.Step("HudKit", "manage face tell: '" + _manageFaceLabel + "' -> '" + label +
                           "' (idle " + idle + "/" + DeNelle.Core.UI.ObsidianQueueGate.WorkQueueStatus.LineCount +
                           ", statusVer=" + st.Version + ")");
            _manageFaceLabel = label;
            _manageFaceBadge = badge;
            ManageFaceChanged?.Invoke();
        }

        // The WO-835 §3b predicate table, verbatim. Posture-first: only the two calm
        // postures show a bar at all (build/hostile/modal drop it via occupancy — the
        // model agrees by construction so the View never sees a stale set).
        private int ComputeMask()
        {
            if (_postureKey == PostureTown)
            {
                // WO-911 (ruling Q10+Q13, 2026-08-06): the bar goes 7 -> 6 faces.
                //  • Map is GONE from the bar — it is a tab inside Bag now. The
                //    ActionBarButtonId.Map bit is never set here again; _source.MapUnlocked stays on
                //    the ISource seam because it is still the Onboarded gate other code reads, but
                //    it no longer packs a face.
                //  • Upgrade is RE-POINTED to the unified Manage/Queues screen and is therefore
                //    ALWAYS applicable in town — it is the single door to the three production
                //    lines, so gating it on _source.BuildingFocused (the old WO-835 §3c split-out)
                //    would make the queues reachable only while standing next to a building, which
                //    is the exact undiscoverability this WO exists to remove.
                int mask = Bit(ActionBarButtonId.Build)      // BUILD
                         | Bit(ActionBarButtonId.Bag)        // HERO (ordinal retained)
                         | Bit(ActionBarButtonId.Quests)     // JOURNEY (ordinal retained)
                         | Bit(ActionBarButtonId.Upgrade);   // MANAGE (ordinal retained)
                // WO-1286: Raids moved under the always-visible Journey card. Keeping a second
                // conditional Raids face made the same destination appear/disappear in two places.
                // The enum identity stays dormant for array/save compatibility; Journey calls the
                // unchanged RaidEntryGate authority.
                return mask;
            }
            if (_postureKey == PostureExplore)
            {
                // The calm(explore) occupancy row carries only Talk + Bag — same set here.
                return Bit(ActionBarButtonId.Build)
                     | Bit(ActionBarButtonId.Bag)
                     | Bit(ActionBarButtonId.Quests)
                     | Bit(ActionBarButtonId.Upgrade);
            }
            return 0;   // build / hostile / modal / unknown: the bar is down
        }

        /// <summary>WO-1884 — captured town exists. Homes chip reads this, not GameStateService.</summary>
        public static bool HasOwnedTownHome()
        {
            return DeNelle.Core.State.OwnedBaseProgression.Validate(
                DeNelle.Core.State.GameStateService.Instance?.State?.OwnedBase, out _);
        }

        private static int Bit(ActionBarButtonId id) => 1 << (int)id;

        private string DescribeActive()
        {
            var sb = new System.Text.StringBuilder(48);
            for (int i = 0; i < _active.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(_active[i]);
            }
            return sb.ToString();
        }
    }
}

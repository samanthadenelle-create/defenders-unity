// =============================================================================
// ManageStateModel - WO-2011, the UNIFIED item / upgrade-track / action state
// contract for every Manage surface (BUILD, ARMY, RESEARCH).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Manage
//
// THE RULE THIS FILE EXISTS TO ENFORCE (canon 00_MANAGE_REDESIGN_CANON.md 7 + 9,
// owner rulings 13/14/15/16): ownership, the upgrade TRACK and an ACTION's
// availability are THREE SEPARATE AXES. Collapsing them into one enum is what
// makes the UI lie:
//   * a built Lumber Mill whose NEXT upgrade is Heart-gated is OWNED and
//     operating - it is the upgrade ACTION that is blocked, never the item;
//   * a MAX-level Footman is still TRAINABLE - MAX is a property of the upgrade
//     track, not of the item and not of every action on it;
//   * "the Builder line is full" is a first-class action state, not an excuse
//     for a greyed-out button with no sentence.
//
// The View is DUMB (canon 9). It binds what is on these records and nothing
// else. It must NEVER reverse-engineer state from:
//     a null callback, a disabled button, a label string, a colour, a level
//     number, or a service it reaches into itself.
// Every field a View needs - the badge, the CTA words, the blocker sentence and
// WHERE the player must go to clear the blocker - is supplied here by the model.
//
// PURE CONTRACT, ZERO GAME RULES. This file references no catalog, no service and
// no GameState; it lives in DeNelle.Core precisely so BUILD/ARMY/RESEARCH VMs in
// DeNelle.Village and any future surface can all bind the SAME shape. The VMs
// populate it; nothing here reads the world.
//
// ASCII-ONLY player copy (device tofu risk - no em-dash, no curly quote).
//
// Contradictions are not caught by review, they are caught by
// ManageStateInvariants.Validate (same folder), which the ManageStateModelRegression
// oracle drives with the four WO-2011 canon examples as fixtures.
// =============================================================================

using System;
using System.Collections.Generic;

namespace DeNelle.Core.Manage
{
    // ── AXIS 1: does the player HAVE this thing? ──────────────────────────────

    /// <summary>
    /// Whether the player owns / has access to the item itself. This axis says NOTHING
    /// about whether any action on it can run right now (see <see cref="ManageActionAvailability"/>)
    /// and NOTHING about how far up its ladder it sits (see <see cref="ManageUpgradeTrack"/>).
    /// </summary>
    public enum ManageOwnership
    {
        /// <summary>Built / recruited / researched. The item is real and operating.</summary>
        Owned = 0,
        /// <summary>Authored and visible, but a prerequisite has not been met yet.</summary>
        NotUnlocked = 1,
        /// <summary>Not offered at all in this build (feature flag, platform, unreleased content).</summary>
        Unavailable = 2
    }

    // ── AXIS 2: where on its ladder does it sit? ──────────────────────────────

    /// <summary>
    /// The state of the item's UPGRADE LADDER only. Owner ruling 13: MAX is a property of
    /// the upgrade track, never of the item - a maxed troop is still trainable, a maxed
    /// building still produces.
    /// </summary>
    public enum ManageUpgradeTrack
    {
        /// <summary>This item has no upgrade ladder at all (or none is authored).</summary>
        NotApplicable = 0,
        /// <summary>A higher rung exists above the current level.</summary>
        Upgradable = 1,
        /// <summary>The top authored rung has been reached. Non-upgrade actions are UNAFFECTED.</summary>
        Max = 2
    }

    // ── AXIS 3: can this ONE action run, right now? ───────────────────────────

    /// <summary>What the action would do. One item carries several (train + upgrade + cancel...).</summary>
    public enum ManageActionKind
    {
        None = 0,
        Build = 1,
        Upgrade = 2,
        Train = 3,
        Research = 4,
        Cancel = 5,
        InstantFinish = 6,
        /// <summary>Pure navigation - the CTA on a blocked action that opens the blocker's home.</summary>
        Navigate = 7
    }

    /// <summary>
    /// Whether ONE action can start now, and if not, WHY. Owner ruling 14: queue-blocked is
    /// first class - it is a valid action that has nowhere to go, which is a different sentence
    /// and a different destination from "you cannot afford it" or "you have not unlocked it".
    /// </summary>
    public enum ManageActionAvailability
    {
        /// <summary>This action does not apply to this item (e.g. Upgrade on a maxed track).</summary>
        NotApplicable = 0,
        /// <summary>Ready. The player taps and it happens.</summary>
        Available = 1,
        /// <summary>Valid, permitted, but the wallet is short. <see cref="ManageAction.BlockerReason"/> names what is missing.</summary>
        Unaffordable = 2,
        /// <summary>A gate is unmet (Heart level, barracks tier, another building). <see cref="ManageAction.Route"/> MUST point at it.</summary>
        PrerequisiteBlocked = 3,
        /// <summary>Affordable and permitted, but the relevant queue line has no capacity.</summary>
        QueueBlocked = 4,
        /// <summary>Already running. <see cref="ManageAction.Progress01"/> / <see cref="ManageAction.RemainingSeconds"/> are live.</summary>
        InProgress = 5
    }

    // ── Where a blocked action sends the player (owner ruling 18) ─────────────

    /// <summary>
    /// The destination class for a prerequisite / queue CTA. Ruling 18 makes direct
    /// prerequisite navigation P0: a lock without a route is the defect this whole program
    /// exists to kill, and a CTA pointing at a screen that cannot be opened is worse than
    /// no CTA at all (the barracks panel proved it - it had zero callers for months).
    /// The VIEW does not choose this. The model does.
    /// </summary>
    public enum ManageRouteKind
    {
        None = 0,
        /// <summary>The BUILD tab, optionally filtered.</summary>
        BuildTab = 1,
        /// <summary>One building's card inside BUILD (<see cref="ManageRoute.TargetId"/> = building id).</summary>
        BuildCard = 2,
        /// <summary>The ARMY tab.</summary>
        ArmyTab = 3,
        /// <summary>The RESEARCH tab, optionally a school (<see cref="ManageRoute.TargetId"/>).</summary>
        ResearchTab = 4,
        /// <summary>The Heart of Elarion progression surface (ruling 10 - the realm spine).</summary>
        HeartCard = 5,
        /// <summary>The global Queue (ruling 17). The only honest destination for QueueBlocked.</summary>
        Queue = 6
    }

    /// <summary>
    /// An addressable destination plus the words that go on the button. Immutable value.
    /// </summary>
    public readonly struct ManageRoute
    {
        public readonly ManageRouteKind Kind;
        /// <summary>Building id / school id / troop id the destination should focus. May be null.</summary>
        public readonly string TargetId;
        /// <summary>ASCII CTA words, e.g. "VIEW HEART". Never derived from the enum name by the View.</summary>
        public readonly string Cta;

        public ManageRoute(ManageRouteKind kind, string targetId, string cta)
        {
            Kind = kind;
            TargetId = targetId;
            Cta = cta;
        }

        /// <summary>True when this route actually goes somewhere.</summary>
        public bool IsRoutable => Kind != ManageRouteKind.None;

        /// <summary>The explicit "no destination" route. Only legal on an action that is not blocked.</summary>
        public static ManageRoute None => new ManageRoute(ManageRouteKind.None, null, null);

        public static ManageRoute ToHeart(string cta = "VIEW HEART") => new ManageRoute(ManageRouteKind.HeartCard, null, cta);
        public static ManageRoute ToQueue(string cta = "VIEW QUEUE") => new ManageRoute(ManageRouteKind.Queue, null, cta);
        public static ManageRoute ToBuildCard(string buildingId, string cta) => new ManageRoute(ManageRouteKind.BuildCard, buildingId, cta);
    }

    // ── One action on one item ────────────────────────────────────────────────

    /// <summary>
    /// ONE button's worth of truth: what it does, whether it can run, the sentence that says
    /// why not, the door that clears the blocker, and the command to run.
    ///
    /// <para><b>The View never infers.</b> <see cref="Invoke"/> being null is an implementation
    /// detail, NOT a state - a blocked action may still carry a command that the model refuses,
    /// and an available one may be pure navigation. Read <see cref="Availability"/>.</para>
    /// </summary>
    public sealed class ManageAction
    {
        public ManageActionKind Kind = ManageActionKind.None;
        public ManageActionAvailability Availability = ManageActionAvailability.NotApplicable;

        /// <summary>ASCII words on the button, supplied by the model ("TRAIN", "UPGRADE", "VIEW HEART").</summary>
        public string Cta;

        /// <summary>
        /// ASCII sentence naming the blocker, in the player's words ("Need 320 more Wood.",
        /// "The Builder line is full."). Null when <see cref="Availability"/> is Available or InProgress.
        /// </summary>
        public string BlockerReason;

        /// <summary>Where to send the player to clear <see cref="BlockerReason"/>. Ruling 18.</summary>
        public ManageRoute Route = ManageRoute.None;

        /// <summary>0..1 while InProgress; 0 otherwise. The View renders it, never computes it.</summary>
        public float Progress01;

        /// <summary>Seconds left while InProgress; 0 otherwise.</summary>
        public float RemainingSeconds;

        /// <summary>Pre-formatted ASCII cost line ("320 Wood, 80 Iron"), or null when the action is free.</summary>
        public string CostLine;

        /// <summary>The command. The View invokes it; it never decides whether it should.</summary>
        public Action Invoke;

        /// <summary>True for the one action a tile surfaces first.</summary>
        public bool IsPrimary;

        /// <summary>
        /// The blocker is a LOCK the player cannot walk out of from this face, so the button reads
        /// this action's own <see cref="Cta"/> and renders DISABLED even though <see cref="Route"/>
        /// is routable (owner ruling 2026-09-10, WO-1668, closing WO-1566 audit row 6.3:
        /// "the locked ARMY tile's CTA must be a DISABLED 'LOCKED' face, not 'VIEW BARRACKS'").
        ///
        /// <para>⛔ THE ROUTE STAYS, AND THAT IS THE POINT. ManageStateInvariants'
        /// [lock-without-a-door] (ruling 18) FAILS a PrerequisiteBlocked action carrying
        /// Route.None, so clearing the route to get a dead face would trade one defect for a
        /// validator failure. The MODEL still names a destination; only the PRESENTATION declines
        /// to offer it as a button - which is the HP B2B split (CLAUDE.md architecture law:
        /// presentation is a separate layer that never touches the objects).</para>
        ///
        /// <para>The unlock guidance is NOT lost: a NotUnlocked item's <see cref="ManageItemState.LockReason"/>
        /// is promoted onto the card's hint band by ManageVmProjection.ProjectSelection, which is
        /// where the captured frame already painted "Requires Barracks Tier 4".</para>
        /// </summary>
        public bool LockedFace;

        public static ManageAction NotApplicable(ManageActionKind kind) =>
            new ManageAction { Kind = kind, Availability = ManageActionAvailability.NotApplicable };
    }

    // ── The tile badge (canon 8 - tile state is MANDATORY) ────────────────────

    /// <summary>
    /// The single actionable indicator every BUILD and ARMY tile must show. Canon 8: do not
    /// ship a grid where the player must tap every item to discover what can be acted on.
    /// The badge is chosen by the MODEL from the three axes; the View paints the word it is given.
    /// </summary>
    public enum ManageTileBadge
    {
        None = 0,
        /// <summary>Item is not unlocked. NEVER used on an Owned item, however blocked its actions are (ruling 15).</summary>
        Locked = 1,
        UpgradeAffordable = 2,
        UpgradeUnaffordable = 3,
        /// <summary>The relevant line has no capacity (ruling 14).</summary>
        QueueBlocked = 4,
        /// <summary>An upgrade job is running on this item.</summary>
        Upgrading = 5,
        /// <summary>Top of the upgrade ladder. Says nothing about train/produce actions (ruling 13).</summary>
        Max = 6,
        Trainable = 7,
        Training = 8,
        /// <summary>Owned and operating with nothing to do right now.</summary>
        Idle = 9
    }

    // ── One item, all three axes ──────────────────────────────────────────────

    /// <summary>
    /// The common Manage item contract (canon 10 - one reusable presentation path for
    /// BUILD / ARMY / RESEARCH, not three systems with duplicated lock/cost/queue logic).
    ///
    /// <para>Read the three axes independently. The canon examples, which are also this model's
    /// acceptance criteria:</para>
    /// <list type="bullet">
    /// <item>Built Lumber Mill, next upgrade Heart-gated -> Owned + Upgradable + Upgrade action
    /// PrerequisiteBlocked routed to the Heart, CTA "VIEW HEART". Badge is NOT Locked.</item>
    /// <item>Max-level Footman, train queue open -> Owned + Max + Train action Available,
    /// CTA "TRAIN". MAX does not suppress training.</item>
    /// <item>Max-level Footman, train queue full -> Owned + Max + Train action QueueBlocked
    /// routed to the Queue, CTA "VIEW QUEUE".</item>
    /// <item>Locked Outrider -> NotUnlocked + Train action PrerequisiteBlocked routed to the
    /// barracks BUILD card (owner ruling 21: the barracks BUILDING tier gates troop unlocks, so
    /// the model always names a destination that genuinely opens) and flagged
    /// <see cref="ManageAction.LockedFace"/>, so the FACE reads "LOCKED" and is disabled.
    /// ⛔ This example said CTA "VIEW BARRACKS" until 2026-09-10; the owner ruled that face out
    /// (WO-1668 / WO-1566 row 6.3). The ROUTE is unchanged - only what the button says and
    /// whether it is pressable.</item>
    /// </list>
    /// </summary>
    public sealed class ManageItemState
    {
        /// <summary>Stable id (building id / troop id / perk id). The View never parses it (canon 9).</summary>
        public string ItemId;

        /// <summary>ASCII display name supplied by the model - never derived from the id by the View.</summary>
        public string DisplayName;

        /// <summary>Asset key for the tile art, resolved by the model.</summary>
        public string IconId;

        // ── the three axes ──
        public ManageOwnership Ownership = ManageOwnership.NotUnlocked;
        public ManageUpgradeTrack UpgradeTrack = ManageUpgradeTrack.NotApplicable;
        public readonly List<ManageAction> Actions = new List<ManageAction>();

        /// <summary>Current ladder level (1-based). 0 when the item has no ladder or is not owned.</summary>
        public int Level;
        /// <summary>Top authored ladder level. 0 when there is no ladder.</summary>
        public int MaxLevel;

        // ── presentation the model owns (canon 8 + 9) ──
        public ManageTileBadge Badge = ManageTileBadge.None;
        /// <summary>The ASCII badge WORD. Supplied so the View never derives a label from an enum name.</summary>
        public string BadgeText;
        /// <summary>
        /// The CLOSED one-word form of <see cref="BadgeText"/>, for a grid CELL - READY /
        /// NOT BUILT / SHORT / LOCKED / UPGRADING / MAX / QUEUE FULL (mockup panel 2).
        /// <para>Null means "the same word": <see cref="BadgeText"/> is already short enough, and
        /// the projection falls back to it. Only a composer that lengthens BadgeText with numbers
        /// (WO-1518's "SHORT 280 STONE, 720 GOLD") needs to set this, and only that composer knows
        /// which half is the word - which is why the SHORTENING lives here and never in the View.</para>
        /// </summary>
        public string BadgeWord;
        /// <summary>ASCII "why is this locked" sentence when <see cref="Ownership"/> is NotUnlocked. Null otherwise.</summary>
        public string LockReason;
        /// <summary>ASCII one-line summary of what changes at the next rung. Null when none.</summary>
        public string NextRungLine;

        /// <summary>The action a tile surfaces first. Null when the item offers none.</summary>
        public ManageAction PrimaryAction
        {
            get
            {
                for (int i = 0; i < Actions.Count; i++)
                    if (Actions[i] != null && Actions[i].IsPrimary) return Actions[i];
                return null;
            }
        }

        /// <summary>The first action of <paramref name="kind"/>, or null. Model-side helper; the View binds fields.</summary>
        public ManageAction ActionOf(ManageActionKind kind)
        {
            for (int i = 0; i < Actions.Count; i++)
                if (Actions[i] != null && Actions[i].Kind == kind) return Actions[i];
            return null;
        }

        /// <summary>Convenience for composers: adds and returns the action.</summary>
        public ManageAction Add(ManageAction action)
        {
            if (action != null) Actions.Add(action);
            return action;
        }
    }
}

// =============================================================================
// ManageViewContract - WO-2002, the ONE presentation contract BUILD / ARMY /
// RESEARCH all render through.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Manage
//
// WAVE 1 of the Manage redesign. Wave 0 shipped the STATE axes next door
// (ManageStateModel.cs: ManageOwnership / ManageUpgradeTrack /
// ManageActionAvailability / ManageAction / ManageRoute / ManageTileBadge) and the
// ManageStateInvariants validator. THIS file is the PRESENTATION shape the View
// binds. It CARRIES Wave 0's model, it does not duplicate it:
//
//     composer (tab VM, in DeNelle.Village)
//         -> ManageItemState        (Wave 0: ownership / track / actions / badge)
//         -> ManageVmProjection     (this folder: model-side, collapses 9 badges
//                                    to the 5 canon visual states and attaches the
//                                    delivered art keys)
//         -> ManageTileVM / ManageSelectionVM / ManageActionVM   (this file)
//         -> ManageWorkspacePanel   (this folder: the ONE dumb renderer)
//
// ⛔ ZERO UnityEngine. This file has NO `using UnityEngine`, on purpose and it is
// oracle-enforced (ManageDumbViewRegression case [contract-is-pure]). Every visual
// is addressed as a STRING KEY, never a Sprite, so the contract can be composed,
// validated and unit-tested with no Unity object graph at all - and so a second
// renderer (a capture harness, a device overlay) can bind the same VMs. A Sprite
// field here would silently make the contract un-testable outside play mode.
//
// ⛔ THE VIEW NEVER READS ManageRoute. Canon 9 forbids the View deciding "which
// destination a prerequisite CTA should open". So a ManageActionVM carries a
// LABEL and an ACTIVATE CALLBACK and nothing else - the projection has already
// bound the route into that callback. That is what lets the oracle ban the tokens
// `.Route` and `PanelId.` outright inside the renderer, rather than trusting a
// reviewer to notice.
//
// ⛔ NO STATE IS INFERRED FROM A NULL. A null Activate is an implementation detail,
// exactly as ManageStateModel.cs says of ManageAction.Invoke. Enabled and Visible
// are EXPLICIT fields and they are the only things the View may read. The renderer
// calls `Activate?.Invoke()` and never `if (Activate != null)` as a state test;
// the oracle bans the comparison form.
//
// ASCII-ONLY player copy. Every string on these records is already sanitised by
// the composer (the old path's ManageScreenVM.Ascii stays where it is - the View
// does not sanitise, because sanitising is deriving). Device tofu risk: no em-dash,
// no curly quote.
// =============================================================================

using System;
using System.Collections.Generic;

namespace DeNelle.Core.Manage
{
    // ── Which tab (canon 2: BUILD / ARMY / RESEARCH, QUEUE is global) ─────────

    /// <summary>
    /// The three player-facing Manage tabs. This is an IDENTITY, never a label source -
    /// <see cref="ManageTabVM.Label"/> carries the words. Canon 9 forbids the View
    /// deriving a label from an enum name.
    /// </summary>
    public enum ManageTabId
    {
        Build = 0,
        Army = 1,
        Research = 2
    }

    // ── The five canon visual states (canon 7) ────────────────────────────────

    /// <summary>
    /// The FIVE states canon 7 makes mandatory - and exactly the five status medallions
    /// delivered in Assets/Resources/RpgUi/manage/ (status-available / status-locked /
    /// status-inprogress / status-queue / status-max).
    ///
    /// <para>This is a NARROWER axis than <see cref="ManageTileBadge"/> (nine values), and
    /// the collapse happens ONCE, model-side, in <c>ManageVmProjection</c>. It is not a
    /// second truth: the badge stays the authored state, this is what the tile PAINTS.</para>
    ///
    /// <para>⚠ There is deliberately NO "Unaffordable" visual state. Owner ruling 15 and the
    /// precedent already shipped at <c>HeartPanel.cs:420-440</c>: an owned thing the player
    /// cannot currently afford is NOT locked, and painting a padlock on it teaches the same
    /// false "you can never get there" this whole program exists to kill. Unaffordable wears
    /// <see cref="Available"/> and carries its refusal in the CTA's DisabledReasonText.</para>
    /// </summary>
    public enum ManageTileVisualState
    {
        Available = 0,
        Locked = 1,
        InProgress = 2,
        QueueBlocked = 3,
        Max = 4
    }

    /// <summary>
    /// How a button should LOOK. The model chooses the role; the View maps the role to its
    /// kit face. A role, not a colour - the owner is red/green colourblind (memory
    /// `owner-colorblind-delegate-visual-creative`), so the renderer must pair every role
    /// with a SHAPE or a WORD, never a hue alone.
    /// </summary>
    public enum ManageActionStyleRole
    {
        /// <summary>The one thing the player most likely wants. Gold face.</summary>
        Primary = 0,
        /// <summary>A supporting action (cancel a peek, view details). Quiet face.</summary>
        Secondary = 1,
        /// <summary>Destructive - sell, cancel a paid job. Danger face, never the primary slot.</summary>
        Destructive = 2,
        /// <summary>Pure navigation to a blocker's home (ruling 18). Confirm face.</summary>
        Navigate = 3
    }

    // ── One button ────────────────────────────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageActionVM</c>. One button's worth of PRESENTATION, projected from a
    /// Wave-0 <see cref="ManageAction"/>.
    ///
    /// <para><b>Enabled and Visible are separate on purpose.</b> A hidden button says "this
    /// verb does not exist here"; a visible-but-disabled button with a
    /// <see cref="DisabledReasonText"/> says "this verb exists and here is why you cannot use
    /// it yet" - canon 11 question 6. Collapsing them is how a screen ends up telling the
    /// player nothing.</para>
    /// </summary>
    public sealed class ManageActionVM
    {
        /// <summary>ASCII words on the face, supplied by the model ("UPGRADE", "VIEW HEART").</summary>
        public string Label;

        /// <summary>Explicit. The View binds it; it never derives it from a null callback.</summary>
        public bool Enabled = true;

        /// <summary>Explicit. False hides the control entirely.</summary>
        public bool Visible = true;

        public ManageActionStyleRole StyleRole = ManageActionStyleRole.Primary;

        /// <summary>
        /// The command. Already carries the route for a navigation CTA, so the View never
        /// decides a destination (canon 9). Invoked as <c>Activate?.Invoke()</c>.
        /// </summary>
        public Action Activate;

        /// <summary>
        /// ASCII sentence shown when <see cref="Enabled"/> is false ("Need 320 more Wood.",
        /// "The Builder line is full."). Null when the action is enabled.
        /// </summary>
        public string DisabledReasonText;

        /// <summary>Pre-formatted ASCII cost line for the face ("320 Wood, 80 Iron"), or null.</summary>
        public string CostText;

        /// <summary>An action that renders nothing. Use instead of a null field so the View never null-tests.</summary>
        public static ManageActionVM Hidden => new ManageActionVM { Visible = false, Enabled = false };
    }

    // ── Selected-item detail rows ─────────────────────────────────────────────

    /// <summary>One "name : value" line in the selection card. Both halves are model-supplied.</summary>
    public sealed class ManageStatVM
    {
        public string Label;
        public string Value;
        /// <summary>ASCII "what changes next" fragment ("+120/hr"), already computed. Null when none.</summary>
        public string DeltaText;
        /// <summary>
        /// WO-1654 row 5.3 - the glyph drawn against this stat, e.g.
        /// <see cref="ManageArt.StatAttack"/>. Null / empty means NO glyph, which is the correct
        /// answer for every row that has none authored (Production / hr, Storage, Placed, the
        /// prose rows) - the renderer paints nothing and the row keeps its full width.
        ///
        /// <para>THE ICON IS AN ADDITION TO <see cref="Label"/>, NEVER A REPLACEMENT FOR IT.
        /// The owner is red/green colourblind and WO-1566 C8 makes greyscale the gate: every stat
        /// must stay identifiable with hue stripped, and the WORD is the channel that survives.
        /// A renderer that drops the label once a key resolves has broken the accessibility
        /// contract, not tightened the layout.</para>
        ///
        /// <para>Resolved by the View through <c>ManageArt.LoadSprite</c>, the same door the cost
        /// glyphs and the clock already use, so an unresolvable key degrades to a transparent
        /// image rather than a pink square. The MAPPING from stat to key lives in the MODEL
        /// (<c>ManageScreenVM.TroopStatRows</c>) because a View that switched on a stat name
        /// would be canon-9 derivation - the same reason <c>ManageCostVM.IconKey</c> is filled by
        /// <c>ManageScreenVM.CostIconFor</c> and not by the panel.</para>
        /// </summary>
        public string IconKey;
    }

    /// <summary>
    /// One resource line in a cost basket. <see cref="Affordable"/> is DECIDED BY THE MODEL -
    /// canon 9 forbids the View inspecting player resources.
    /// </summary>
    public sealed class ManageCostVM
    {
        public string Label;
        public string AmountText;
        /// <summary>Resources key for the currency glyph, or null.</summary>
        public string IconKey;
        public bool Affordable = true;
    }

    // ── One grid tile ─────────────────────────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageTileVM</c>. The common tile contract for BUILD and ARMY (and the
    /// RESEARCH school row, which is the same shape).
    ///
    /// <para>Canon 8 is MANDATORY here: every tile carries an actionable state indicator
    /// (<see cref="VisualState"/> + <see cref="StateText"/> + <see cref="StateIconKey"/>).
    /// A grid where the player must tap each item to find out what can be acted on is the
    /// defect this program exists to remove.</para>
    /// </summary>
    public sealed class ManageTileVM
    {
        /// <summary>Stable id. Carried for the composer's own bookkeeping - the View NEVER parses it.</summary>
        public string Id;

        public string Title;
        /// <summary>Second line ("LEVEL 3", "12 READY"). Model-supplied, never assembled by the View.</summary>
        public string Subtitle;

        /// <summary>Resources key for the item art.</summary>
        public string PortraitKey;

        /// <summary>
        /// Keep the portrait inside the model-supplied frame seat. Army art uses this because its
        /// grid cards are wide while the authored troop frames are square. The View binds this
        /// explicit presentation fact; it never infers it from an id, title, or active tab.
        /// </summary>
        public bool ContainPortrait;

        public bool IsSelected;

        public ManageTileVisualState VisualState = ManageTileVisualState.Available;

        /// <summary>ASCII state word ("UPGRADING", "QUEUE FULL", "MAX"). Never derived from the enum.</summary>
        public string StateText;

        /// <summary>
        /// ⭐ THE TILE'S CLOSED STATE WORD - the SHORT form, for a grid CELL.
        /// Mockup panel 2 puts exactly one closed word on a tile: READY / NOT BUILT / SHORT /
        /// LOCKED / UPGRADING / MAX / QUEUE FULL.
        ///
        /// <para>⛔ THIS IS A SECOND FACE OF ONE FACT, NOT A SECOND FACT. WO-1518 (owner ruling
        /// 2026-09-06 20:12, <i>"short doesnt help, i need to know waht im short"</i>) made
        /// <see cref="StateText"/> carry the amounts - "SHORT 280 STONE, 720 GOLD". That is right
        /// where there is room for it: the research LIST ROW's state column and the DETAIL card.
        /// It is wrong on a grid tile, where the measured result was "SHORT 28..." and "SHORT 72..."
        /// (Logs/device/screens/owner-screen-20260907-004825.png) - an ellipsised state word is the
        /// same defect class as no state word at all.</para>
        ///
        /// <para>⛔ THE MODEL COMPOSES BOTH. The View must never truncate, split or re-word
        /// StateText to get here - that would be the View deriving state (canon 9), and it would
        /// go stale the first time a composer authored a new word. The grid renderer paints THIS;
        /// the row renderer and the detail card paint StateText. Null falls back to StateText, so
        /// a composer that has nothing shorter to say simply says the same thing twice.</para>
        /// </summary>
        public string StateWord;

        /// <summary>Resources key for the status medallion. Supplied by the projection.</summary>
        public string StateIconKey;

        /// <summary>Resources key for the tile frame. Supplied by the projection.</summary>
        public string FrameKey;

        /// <summary>0..1 while a job runs on this item; null when nothing runs. Never computed here.</summary>
        public float? Progress01;

        /// <summary>Pre-formatted ASCII countdown ("4m 12s"). Null when nothing runs.</summary>
        public string TimerText;

        /// <summary>Selects this tile. Invoked as <c>Activate?.Invoke()</c>.</summary>
        public Action Activate;

        /// <summary>
        /// ⭐ The INLINE action a LIST ROW offers, with its cost already on the VM
        /// (<see cref="ManageActionVM.CostText"/>). Mockup panel 7 puts a gold <c>RESEARCH</c>
        /// button and its price inside each available row, so the player can act without opening
        /// anything.
        /// <para><see cref="ManageActionVM.Hidden"/> on a grid CARD (panel 2/4/6 tiles carry no
        /// inline button - the tile is the tap target) and on any row whose action is not
        /// AVAILABLE. A locked row shows the padlock and the requirement in the state column
        /// instead, which is what panel 7 draws - not a greyed button.</para>
        /// <para>⚠ Projected WITHOUT a route handler on purpose: a blocked action's door belongs on
        /// the DETAIL card, where there is room for the sentence that explains it. Routing from a
        /// list row would put a navigate button where the mockup draws a padlock.</para>
        /// </summary>
        public ManageActionVM RowAction = ManageActionVM.Hidden;

        /// <summary>
        /// ⭐ THE PADLOCK ROW'S SENTENCE - "Requires Lumber Mill Tier 3" (mockup panel 7).
        /// Null on every row that is not locked, which is what makes the row COLLAPSE to two
        /// lines instead of reserving an empty band.
        ///
        /// <para>⛔ IT EXISTS BECAUSE THE TWO FACTS WERE BEING GLUED INTO ONE LINE, AND THE GLUE
        /// WAS THE DEFECT. <c>ManageScreenVM.ComposeResearchItem</c> wrote
        /// <c>NextRungLine + " . " + LockReason</c>, so the owner's capture
        /// (Logs/device/screens/owner-screen-20260907-010151.png) reads
        /// <i>"Wood +8%, offline bucket +8% . Upgrade the building to Tier 3 f..."</i> - a benefit
        /// and a requirement separated by a floating period, with the requirement truncated away.
        /// The mockup draws them as TWO CHANNELS: the effect under the name, the requirement on
        /// its own line beside a padlock. Two facts, two rows.</para>
        ///
        /// <para>⚠ THE WO-1518 DOOR AFFORDANCE IS NOT HERE AND MUST NOT BE COPIED HERE. The word
        /// that says the row is tappable ("LOCKED - TAP") stays on <see cref="StateText"/>, where
        /// the composer derives it from whether a route actually exists. A second copy of it on
        /// this line would be the duplicated state this contract's siblings keep paying for, and
        /// it would go stale the first time a locked row has no door.</para>
        /// </summary>
        public string RequirementText;
    }

    // ── The selected-item region ──────────────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageSelectionVM</c>. Answers canon 11's seven questions for the selected
    /// thing and nothing beyond them.
    ///
    /// <para>Canon 3: "no nested scroll region inside a selected-item detail card". So this
    /// contract is deliberately FLAT and SHORT - stats and costs are small arrays the renderer
    /// lays out in fixed pixel bands, not a list it can scroll. If a tab needs more, it belongs
    /// behind secondary detail or the Queue (canon 11).</para>
    /// </summary>
    public sealed class ManageSelectionVM
    {
        /// <summary>False when nothing is selected; the renderer then paints <see cref="EmptyText"/>.</summary>
        public bool Visible;

        /// <summary>Shown in place of the card when <see cref="Visible"/> is false. ASCII, model-supplied.</summary>
        public string EmptyText;

        public string Title;
        /// <summary>"LEVEL 3 of 6" - already formatted (question 1).</summary>
        public string LevelText;
        /// <summary>What it does (question 2).</summary>
        public string Description;

        public ManageTileVisualState State = ManageTileVisualState.Available;
        /// <summary>ASCII state word (question 6's first half).</summary>
        public string StateText;
        public string StateIconKey;
        public string PortraitKey;

        /// <summary>What changes next (question 3). Never empty-null-checked into an inferred state.</summary>
        public IReadOnlyList<ManageStatVM> Stats = Array.Empty<ManageStatVM>();
        /// <summary>What it costs (question 4). Affordability is decided model-side.</summary>
        public IReadOnlyList<ManageCostVM> Costs = Array.Empty<ManageCostVM>();

        /// <summary>
        /// The caption over the cost band - "Upgrade Cost" (mockup panel 3) or "Train Cost"
        /// (panel 5). Model-supplied ASCII, because WHICH verb is being paid for is a model fact;
        /// a View that chose the word from the tab would be deriving it (canon 9). Null draws no
        /// caption, which is what a card with no costs wants.
        /// </summary>
        public string CostCaption;

        /// <summary>
        /// ⭐ HOW LONG IT TAKES, ON ITS OWN LINE - the clock in mockup panels 3 and 5.
        /// <para>⛔ IT IS DELIBERATELY NOT A <see cref="ManageCostVM"/>. A cost row carries an
        /// <c>Affordable</c> verdict measured against a bank; a duration has no bank and cannot be
        /// afforded or not, so putting it in the basket would either invent a verdict or force
        /// every reader to special-case one row. Two channels, one each.</para>
        /// <para>⚠ IT IS ALSO NOT A SECOND COPY OF THE STAT ROW. Where the stats table already
        /// carries "Upgrade time", the composer supplies this INSTEAD, not as well - the owner's
        /// Lumber Mill capture is the reason the rule is written down.</para>
        /// </summary>
        public string TimeText;

        /// <summary>Resources key for the clock glyph beside <see cref="TimeText"/>.</summary>
        public string TimeIconKey;

        /// <summary>What can I do now (question 5).</summary>
        public ManageActionVM PrimaryAction;
        public ManageActionVM SecondaryAction;
        /// <summary>Where do I go to resolve the blocker (question 7). Ruling 18 - P0.</summary>
        public ManageActionVM RequirementAction;

        /// <summary>0..1 while a job runs on the selected item; null otherwise.</summary>
        public float? Progress;
        /// <summary>Pre-formatted ASCII progress/countdown line.</summary>
        public string ProgressText;

        /// <summary>One extra ASCII sentence the model wants under the card (a caution, a hint).</summary>
        public string AuxiliaryText;
    }

    // ── The contextual current-job strip ──────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageActivityVM</c>. The "what is running right now" strip that sits
    /// between the grid and the Queue door. Tab-contextual: BUILD shows the Builder line,
    /// ARMY the Train line.
    /// </summary>
    public sealed class ManageActivityVM
    {
        public bool Visible;
        public string IconKey;
        public string Title;
        public string TimerText;
        /// <summary>"2 QUEUED" - already counted and worded by the model (canon 9 bans queue reads).</summary>
        public string QueuedCountText;
        // ⛔ NO OpenQueue COMMAND. The strip is a STATUS GLANCE, not a second door.
        // Removed 2026-09-06 (WO-1443): the capture showed its button on screen beside the tab-row
        // QUEUE door - two entries for one verb, which is the exact ambiguity CLAUDE.md 7's "one
        // Queues entry" rule exists to remove, and the same shape the HUD Builders chip was
        // stripped of in WO-911. The field went with the button rather than being left unbound.
    }

    // ── The global queue door ─────────────────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageQueueVM</c>. The always-available global QUEUE affordance (canon 2).
    /// <see cref="AtCapacity"/> is a MODEL verdict - canon 9 forbids the View computing queue
    /// capacity, which is precisely the <c>if (queue.Count &lt; max)</c> shape the work order bans.
    ///
    /// <para>⛔ IT IS A **DOOR**, NOT A FOURTH TAB, AND THE DISTINCTION IS LOAD-BEARING.
    /// WO-1443 section 1B (owner ruling 2026-09-06) seats it as the last entry of the TAB ROW -
    /// WO-2001's own header spec, <c>BACK | MANAGE | BUILD | ARMY | RESEARCH | QUEUE[count]</c> -
    /// so it shares that row's geometry. It does NOT share its semantics: it is never the ACTIVE
    /// tab, never carries an underline, never selects a workspace, and lives OUTSIDE
    /// <see cref="ManageWorkspaceVM.Tabs"/> so <c>ActiveTabIndex</c> can never address it. It
    /// opens an overlay and returns. "Looks like a tab, behaves like a door" is exactly the
    /// ambiguity that would produce the next round of this conversation, so the separation is
    /// structural rather than a comment: the renderer reads it from a different field.</para>
    /// </summary>
    public sealed class ManageQueueVM
    {
        public bool Visible = true;
        /// <summary>The door's words ("QUEUE").</summary>
        public string Label;
        /// <summary>
        /// The count that rides ON the door's face, e.g. "0 OF 5" or "FULL 5 OF 5" - model-counted.
        /// <para>⚠ REPLACED <c>CountText</c> ("IDLE" / "3 RUNNING") AND <c>CapacityText</c> ("0 OF 5")
        /// on 2026-09-06. Those two fed a SECOND LINE printed under the old header chip, and the
        /// owner's ruling deleted that line: <i>"remove heart level queue"</i>, with the count
        /// riding on the face instead. Two fields feeding a band that no longer exists would have
        /// been the composed-but-unpainted duplicated state this file has already been burned by
        /// once today (see ManageWorkspaceVM.HeaderTitle), so they were replaced, not left.</para>
        /// </summary>
        public string FaceCountText;
        // ⛔ NO AtCapacity FLAG. "Full" reaches the player as the WORD "FULL" inside
        // FaceCountText, which the model composes. The flag existed so the renderer could add a
        // gold pip beside the face; the 2026-09-06 14:59 capture showed that pip rendering as a
        // stray vertical bar outside the button art, so the pip went - and the flag went with it
        // rather than sitting here unread. One channel, and it is words.
        public Action Open;
    }

    // ── One tab of the QUEUE OVERLAY (mockup panel 8) ─────────────────────────

    /// <summary>
    /// One tab of the queue overlay: <c>BUILDERS (2/2)</c> / <c>TRAINING (2/2)</c> /
    /// <c>RESEARCH (2/2)</c>.
    ///
    /// <para>⛔ <see cref="CountText"/> IS MODEL-COMPOSED FROM THE LIVE CHANNEL SUMMARY
    /// (<c>Busy</c>/<c>Slots</c>) - the same source the three-line status strip reads, so a tab can
    /// never drift from the strip beside it. The mockup's "2/2" is TODAY'S STATE, not the spec:
    /// a literal would start lying the moment the player buys a builder.</para>
    ///
    /// <para>⚠ These are DESTINATION tabs inside an overlay, not the retired workspace tab row -
    /// they switch which channel's queue is listed and nothing else.</para>
    /// </summary>
    public sealed class ManageQueueTabVM
    {
        /// <summary>The line this tab lists. Carried so the View never parses the label.</summary>
        public DeNelle.Core.Jobs.ChannelId Channel;
        /// <summary>ASCII word ("BUILDERS"), model-supplied - never derived from the enum name.</summary>
        public string Label;
        /// <summary>"2/2" - busy over slots, model-counted.</summary>
        public string CountText;
        public bool IsActive;
        /// <summary>Selects this tab. Invoked as <c>Activate?.Invoke()</c>.</summary>
        public Action Activate;
    }

    // ── One filter chip ───────────────────────────────────────────────────────

    /// <summary>
    /// One BUILD filter chip (canon 3: ALL / ECONOMY / DEFENSE / CRAFT / STORAGE / CIVIC).
    /// Membership is decided by the model (canon 5 says the same for RESEARCH schools); the
    /// View never infers a category from an id.
    /// </summary>
    public sealed class ManageFilterVM
    {
        public string Id;
        public string Label;
        public bool IsActive;
        public Action Activate;
    }

    // ── One tab ───────────────────────────────────────────────────────────────

    /// <summary>
    /// WO-2002's <c>ManageTabVM</c>. Everything one tab paints. A tab supplies STATE; it does
    /// not rewrite action logic (WO-2002 "Tab-specific code should provide state, not rewrite
    /// action logic").
    /// </summary>
    public sealed class ManageTabVM
    {
        public ManageTabId Id;
        /// <summary>ASCII tab words. Never derived from <see cref="Id"/> by the View.</summary>
        public string Label;
        public bool IsActive;
        /// <summary>Switches to this tab.</summary>
        public Action Activate;

        /// <summary>Empty when the tab has no filter row (ARMY, canon 4).</summary>
        public IReadOnlyList<ManageFilterVM> Filters = Array.Empty<ManageFilterVM>();

        /// <summary>The tiles, ALREADY ordered and ALREADY filtered by the model (WO-2002 "item order").</summary>
        public IReadOnlyList<ManageTileVM> Tiles = Array.Empty<ManageTileVM>();

        /// <summary>
        /// Columns the model wants. BUILD asks 5, ARMY asks 3, RESEARCH asks 4 - read off the
        /// owner's mockup, panel by panel, not derived. The renderer treats this as a REQUEST and
        /// reports in px when the measured well cannot honour it - it never silently re-columns.
        /// </summary>
        public int GridColumns = 3;

        /// <summary>
        /// Rows the model wants VISIBLE AT REST. ⛔ THIS IS A REQUIREMENT, NOT A DERIVED NUMBER.
        /// <para>docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png states capacity in words on the
        /// panels themselves - screen 4: <i>"All 9 troops visible, no scrolling"</i> (3x3), screen 2
        /// draws ten buildings as 5 columns x 2 rows. So the geometry is derived FROM the capacity,
        /// which is the reverse of what this renderer did before: it sized a cell from the leftover
        /// band and let the row count fall out, which is how a screen ended up offering FOUR tiles
        /// under a chip that says ALL.</para>
        /// <para>The renderer sizes SQUARE cells to fit <c>GridRows</c> x <see cref="GridColumns"/>
        /// and, when the band cannot seat them at the touch/legibility floor, says so in px through
        /// FlowTrace rather than quietly showing fewer.</para>
        /// </summary>
        public int GridRows = 3;

        /// <summary>ASCII sentence when <see cref="Tiles"/> is empty. Model-supplied - the View invents no copy.</summary>
        public string EmptyText;

        /// <summary>
        /// ⭐ THE SCREEN'S OWN PAINTING - mockup panel 7 draws the SCHOOL (a cathedral, a lumber
        /// mill) filling the left ~40% of the well with the perk rows stacked beside it.
        /// Null on every screen the mockup draws without one, and a null costs the rows nothing:
        /// the renderer only carves the left column when a key is supplied.
        ///
        /// <para>⛔ THE KEY IS THE MODEL'S, FROM <c>ManageArt.BuildingPortraitKey(id, level)</c> -
        /// the SAME producer the BUILD grid and the research PICKER already use. The retired
        /// display-name slug (<c>Portraits/&lt;slug&gt;</c>) is what painted the 1963x789 landscape
        /// strips through an oval mask on the owner's device, and WO-1567 section 5 item 3 records
        /// that it was a SECOND key producer, not missing art. There is one producer; this field
        /// carries its output.</para>
        /// </summary>
        public string HeaderArtKey;

        public ManageSelectionVM Selection;
        public ManageActivityVM Activity;
    }

    // ── The whole workspace ───────────────────────────────────────────────────

    /// <summary>
    /// The root the renderer binds. Named <c>ManageWorkspaceVM</c> rather than canon 10's
    /// <c>ManageScreenVM</c> BECAUSE THAT NAME IS TAKEN by the old rail path
    /// (Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs, 2830 lines). Canon 10 allows
    /// repository naming; two live types called ManageScreenVM would make WO-2001's re-point
    /// ambiguous the moment both namespaces are in scope.
    /// </summary>
    public sealed class ManageWorkspaceVM
    {
        /// <summary>
        /// The one heading for this screen, e.g. "MANAGE / ARMY". The HOST chrome binds it into
        /// the panel title; <see cref="ManageWorkspacePanel"/> deliberately paints no copy of it.
        /// <para>⛔ THERE IS NO HeaderSubtitle, AND IT IS NOT COMING BACK. WO-1443 section 1, owner
        /// felt-test 2026-09-06: the screen stacked THREE headings - the panel title, this
        /// breadcrumb and a sub line ("Every troop, unlocked or not.") - and her ruling was to keep
        /// one. The field was deleted rather than left composed-but-unpainted, because a value
        /// nothing reads is the duplicated state this repo has been burned by (CLAUDE.md 2 / 5 / 16):
        /// the next seat would have re-rendered it believing it was load-bearing.</para>
        /// </summary>
        public string HeaderTitle;

        public IReadOnlyList<ManageTabVM> Tabs = Array.Empty<ManageTabVM>();

        /// <summary>Index into <see cref="Tabs"/>. The model owns "last-used tab" (canon 2).</summary>
        public int ActiveTabIndex;

        /// <summary>The global QUEUE door (ruling 17). Always available, hence not per-tab.</summary>
        public ManageQueueVM Queue;

        /// <summary>The tab to paint. Returns null rather than guessing when the index is out of range.</summary>
        public ManageTabVM ActiveTab
        {
            get
            {
                if (Tabs == null) return null;
                if (ActiveTabIndex < 0 || ActiveTabIndex >= Tabs.Count) return null;
                return Tabs[ActiveTabIndex];
            }
        }
    }
}

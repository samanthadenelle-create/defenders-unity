// =============================================================================
// ManageScreenVM — the pure ViewModel behind the unified MANAGE / QUEUES screen.
// -----------------------------------------------------------------------------
// WO-911 (absorbs WO-905).   Assembly: DeNelle.Village   Namespace: DeNelle.Village.UI
//
// WHAT THIS SCREEN IS (owner, 2026-08-05/06):
//   "move [the builders queue] to its own dedicated button at the bottom where we
//    can open up the queue and see the different types of queues ... Anything
//    that's applicable should be in a single screen"
//   "ability to see all the items in the queue and cancel the second thing and
//    refund the amount and bump up the next item ... max of five things"
//   Framing: "Think Warcraft-style parallel production lines."
//
// ⚠ THE STRUCTURAL FACT THIS MODEL IS BUILT AROUND (do not re-derive it):
//   The owner's CONTENT tabs CROSS the queue CHANNELS. There are only THREE
//   channels (Builder / Train / Research, JobKind.cs) but FOUR content tabs, and
//   two of them ride the SAME rail:
//
//     TAB          ->  CHANNEL           note
//     Defense      ->  Builder           towers / walls / gates
//     Buildings    ->  Builder           SHARES the Builders line with Defense
//     Troops       ->  Train             training + the WO-897 muster
//     Research     ->  Research          troop / tech upgrades
//
//   Defense and Buildings are one shared capacity. A player queuing a tower is
//   spending the same builder as a player queuing a farm. The tabs filter the
//   BROWSE list by content; they never imply two Builder pools.
//
//   Weapons / armour are deliberately ABSENT: GearProgression.Improve is instant
//   ("instant V1 — no job/channel") so gear has NO wall-clock cost and nothing to
//   put on a rail. WO-905 §7.3 resolved them as FUTURE, and Q3's ruled tab set
//   does not include them. Adding them would mean two of six tabs behaving unlike
//   the rest. The tab model takes them later without a rewrite.
//
// MVVM: no UnityEngine.UI here. The View reads these rows and calls these
// commands; every mutation goes through BuildTimerService / BarracksService, and
// this class never charges, grants or enqueues anything itself.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Wallet;     // WO-1282 - PackCatalog now ships in DeNelle.Commerce but KEEPS this
                          // namespace (PromoCodeService resolves it as a reflection string literal).
using DeNelle.Commerce;   // WO-1282 - StoreFocusRequest, the rail-neutral store focus latch.
using DeNelle.Core.Manage;// WO-2001 - the Wave 0/1 Manage state + presentation contract this VM composes.
using UnityEngine;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village.UI
{
    /// <summary>The four CONTENT tabs. Ordinal is the tab-row order.</summary>
    public enum ManageTab
    {
        /// <summary>Towers, walls and gates. Rides the BUILDER line (shared with Buildings).</summary>
        Defense = 0,
        /// <summary>Economy / production buildings. Rides the BUILDER line (shared with Defense).</summary>
        Buildings = 1,
        /// <summary>Troop training + the army muster. Rides the TRAIN line.</summary>
        Troops = 2,
        /// <summary>Troop / tech upgrades and building research. Rides the RESEARCH line.</summary>
        Research = 3,
    }

    /// <summary>One line's at-a-glance state, for the always-visible three-channel strip.</summary>
    public struct ChannelSummary
    {
        /// <summary>Which production line.</summary>
        public ChannelId Channel;
        /// <summary>ASCII display word ("Builders" / "Training" / "Research").</summary>
        public string Name;
        /// <summary>Jobs running right now.</summary>
        public int Busy;
        /// <summary>Worker slots (concurrency).</summary>
        public int Slots;
        /// <summary>Items lined up (active + pending).</summary>
        public int Depth;
        /// <summary>Line-length cap (0 = uncapped).</summary>
        public int DepthCap;

        /// <summary>
        /// ASCII, colour-independent one-liner: "Builders 2/3 . 4 queued".
        /// State is TEXT because the owner is red/green colourblind.
        /// </summary>
        public string Describe()
        {
            if (Busy == 0) return $"{Name} idle - {Slots} free";
            return $"{Name} {Busy}/{Slots} . {Depth} queued";
        }
    }

    /// <summary>
    /// One row in the "IN QUEUE" section. Either ONE addressable job, or a COLLAPSED stack of
    /// identical pending jobs (ruling Q12), which carries no destructive affordance at all.
    /// </summary>
    public sealed class QueueRowVM
    {
        /// <summary>Player-facing ASCII label ("Barracks - Level 2", "Footman x1").
        /// <para>WO-1564: WORDS, never id grammar and never the developer arrow. A catalog miss is
        /// a traced failure with an honest placeholder - see <c>MakeJobRow</c>.</para></summary>
        public string Label;
        /// <summary>State as TEXT, never colour ("Building 2m 10s", "Queued - 3rd in line").</summary>
        public string StateText;
        /// <summary>The line this job runs on.</summary>
        public ChannelId Channel;
        /// <summary>The engine key. Null ONLY on a collapsed stack header.</summary>
        public string JobId;
        /// <summary>Normalized BuildingTierCatalog id for Builder building jobs, otherwise empty.
        /// The View resolves structure art from this typed identity without parsing <see cref="Label"/>.</summary>
        public string BuildingId;

        /// <summary>
        /// ⭐ WO-1488 SECTION 2 - THE ROW'S THUMBNAIL KEY. Mockup panel 8 draws a small picture of
        /// the thing being built between the row number and its name; the owner's capture
        /// (Logs/device/screens/owner-screen-20260907-010356.png) has none on any row.
        /// <para>⛔ COMPOSED HERE, BY <c>ManageArt.BuildingPortraitKey</c>, WHICH IS THE ONE KEY
        /// PRODUCER. The View must not build it from <see cref="BuildingId"/> itself - that is the
        /// canon-9 derivation the WO-2002 oracle looks for, and it is literally the defect WO-1567
        /// section 5 item 3 records (a second, slug-based producer that asked for keys no folder
        /// held and painted a tan placeholder disc instead).</para>
        /// <para>Empty on a row with no structure identity - a troop stack, a research perk - and
        /// the View then draws no thumbnail rather than a placeholder.</para>
        /// </summary>
        public string PortraitKey = string.Empty;

        /// <summary>True when the job has not started yet.</summary>
        public bool Queued;
        /// <summary>Position among pending jobs (0-based); -1 for an active job.</summary>
        public int PendingIndex;

        /// <summary>
        /// Short timeline marker painted at the leading edge of the row ("NOW" for active work,
        /// otherwise the model-owned queue number). Kept separate from <see cref="StateText"/> so
        /// the View never has to turn queue state into player-facing copy.
        /// </summary>
        public string PositionText;

        /// <summary>
        /// Q12 — a COLLAPSED stack header standing for <see cref="StackCount"/> identical pending
        /// jobs. It has NO JobId, so it can never be the target of a cancel or a paid finish.
        /// </summary>
        public bool IsStackHeader;
        /// <summary>How many identical jobs this header stands for (1 when not a stack).</summary>
        public int StackCount = 1;
        /// <summary>Grouping key for the stack (used to expand/collapse).</summary>
        public string StackKey;
        /// <summary>True when this header's stack is currently expanded below it.</summary>
        public bool Expanded;
        /// <summary>True when this row is one of an expanded stack's children (indented).</summary>
        public bool IsStackChild;

        /// <summary>Price to Complete Now, or 0 when unavailable. Crystals on every channel except a
        /// gold-priced TrainTroop job (WO-1372; see <see cref="FinishVerbText"/>).</summary>
        public int FinishPrice;
        /// <summary>True when the player can afford <see cref="FinishPrice"/> right now.</summary>
        public bool CanAffordFinish;

        /// <summary>
        /// The verb for the Finish CTA's primary button ("Finish Now", "Hire Reinforcements", etc.).
        /// Empty string means the View will default to "Finish Now".
        /// </summary>
        public string FinishVerbText = string.Empty;

        /// <summary>
        /// The Finish CTA's SECOND LINE, in ASCII words: the price with its currency SPELLED OUT,
        /// plus the shortfall when the player is short ("5 crystals" / "5 crystals - need 3 more").
        ///
        /// Owner felt-test 2026-08-08: "finish five c is a little vague ... it's really hard to tell
        /// if five c doesn't really say anything". "5c" assumed the player already knew that c meant
        /// crystals AND that the price scales with time remaining (cheap because the job is nearly
        /// done, not because finishing is cheap) - neither is knowable on day one. The old
        /// unaffordable face said "(short)", which silently meant "you cannot afford this" and read
        /// like part of the price.
        ///
        /// Composed HERE, not in the View: this is the same MVVM law the rest of the row follows
        /// (StateText / RefundText / CostText are all VM-composed ASCII), and the crystal balance
        /// needed for the shortfall is already in hand at the one place rows are built. The PRICE
        /// ITSELF IS UNTOUCHED - this is presentation only.
        /// </summary>
        public string FinishCostText;
        /// <summary>
        /// ⭐ The row's POSITION NUMBER as the player reads it - "1", "2", "3" - for mockup panel
        /// 8's numbered rows.
        /// <para>⛔ MODEL-SUPPLIED, and the View must never count its own children to get it. A
        /// stack header stands for several jobs (<see cref="StackCount"/>) and expanding one changes
        /// how many ROWS exist without changing the queue, so a view-side count would disagree with
        /// the engine the moment a stack opens. This is the queue's own ordering, published once.</para>
        /// </summary>
        public string OrdinalText;
        /// <summary>True when a rewarded-ad skip is offered (running jobs only).</summary>
        public bool AdAvailable;
        /// <summary>True when this row may be cancelled (never on a collapsed stack header).</summary>
        public bool CanCancel;
        /// <summary>True when this row may be moved one place up the pending FIFO.</summary>
        public bool CanBumpUp;
        /// <summary>
        /// WO-1479 - the FINISHED line the row prints beside Cancel: "Refund: 400 wood, 200 stone",
        /// or the honest zero wording when the job carries no paid basket. Composed by
        /// <see cref="ObsidianQueueVM.QuoteRefund(DeNelle.Core.State.JobCost)"/>, never by the View.
        /// Empty/null ONLY on a row that cannot be cancelled (a collapsed stack header).
        /// </summary>
        public string RefundText;

        /// <summary>
        /// WO-898 item 1 — how far along this job is, 0..1 (filled = elapsed). A RUNNING job
        /// reports real progress from StartMs/DurationMs; a QUEUED job is 0 by definition (it has
        /// not started, StartMs &lt;= 0) and a collapsed stack header is 0 because it stands for
        /// several jobs at different points.
        ///
        /// This is the half of WO-898 that drives the spend: "Complete now" already worked, but a
        /// bare countdown does not communicate "the wall is nearly up and the raid is inbound" the
        /// way a filling bar does. -1 means "do not draw a bar".
        /// </summary>
        public float Progress01 = -1f;

        // ── Row identity icon (owner: "should be a select icon") ──────────────
        // KEYS, not a Sprite: the VM must not touch UnityEngine.UI/art loading (the MVVM
        // conformance oracle fails a View/VM that reads game state or resolves assets itself).
        // The View hands these to QueueIconResolver - the SAME resolver the card rail uses, so
        // a job can never look like one thing in the rail and another here.
        /// <summary>RpgUiCatalog role, or empty to resolve art from <see cref="JobId"/>.</summary>
        public string IconRole;
        /// <summary>Sprite key within <see cref="IconRole"/>. Ignored when IconRole is empty.</summary>
        public string IconKey;
        /// <summary>ASCII uppercase verb (BUILD / UPGRADE / TRAIN / RESEARCH) - the icon's fallback.</summary>
        public string Verb;
        /// <summary>Target tier, part of the icon cache key.</summary>
        public int TargetTier;
    }

    /// <summary>One row in the "UPGRADES" browse section — the WO-905 affordability answer.</summary>
    public sealed class BrowseRowVM
    {
        /// <summary>Stable subject id for destination-specific grouping (for example a troop id).</summary>
        public string SubjectId;
        /// <summary>What it is, ASCII ("Arrow Tower -&gt; L3").</summary>
        public string Label;
        /// <summary>Its cost, ASCII ("400 wood, 200 food"), or "" when the cost lives in the panel.</summary>
        public string CostText;
        /// <summary>Affordability as TEXT, never colour ("Ready" / "Short 150 wood").</summary>
        public string StateText;
        /// <summary>True when the player can pay for it right now (drives the affordable-first sort).</summary>
        public bool Affordable;
        /// <summary>Sort weight within the affordable/unaffordable groups (cheapest first).</summary>
        public float CostWeight;
        /// <summary>Invoked on drill-in. Never null.</summary>
        public Action Activate;
        /// <summary>ASCII verb for the drill-in control ("Open" / "Upgrade").</summary>
        public string ActionText;
        /// <summary>
        /// WO-1390 - true when the row is a LOCKED prerequisite (a research perk whose building or
        /// Village Tier is too low). The View dims it and seats the lock badge; StateText carries the
        /// gate sentence verbatim and Activate is the DOOR to the prerequisite (the upgrade page),
        /// never a dead "Locked" button. Sorts after every unlocked row.
        /// </summary>
        public bool Locked;
    }

    /// <summary>One authoritative troop selector entry for Manage → Troops.</summary>
    public sealed class TroopChoiceVM
    {
        public string Id;
        public string Name;
        public string Description;
        public string IconId;
        public int Level;
        public bool Unlocked;
        public string Requirement;

        // ── WO-1382 (2026-09-04) — the selected-troop CARD's facts, composed HERE (MVVM strict:
        //    the View is a skin). Every state is a SENTENCE, never a tint (owner colourblind).
        /// <summary>Barracks tier that unlocks this troop (shown as "Locked . T2" / "LOCKED . TIER 2").</summary>
        public int LockTier = 1;
        /// <summary>WO-1387 (2026-09-04): ALWAYS "" - training charges nothing (owner: "just time").
        /// Kept as a field so the View's contract is unchanged; it used to read "550 gold".</summary>
        public string TrainCostText = "";
        /// <summary>"45s" - the authored BuildSeconds, formatted. The ONLY price of a train.</summary>
        public string TrainTimeText = "";
        /// <summary>"Ready" / "Training line full . 5/5 queued" (ruling #4; no gold term since WO-1387).</summary>
        public string TrainStateText = "";
        /// <summary>True when a TRAIN tap would be accepted right now (line depth only, WO-1387).</summary>
        public bool TrainReady;
        /// <summary>The whole fact sentence: "Train one: 45s . Ready" (WO-1387 shape).</summary>
        public string TrainFactText = "";

        // ── WO-1517 (owner 2026-09-06 20:10) - the two CAPS, as first-class facts ──────
        // "on train army screens should show if queue is full and army is full". Both were
        // knowable and neither reached a face; a player at the cap tapped TRAIN and got silence.
        // Composed in FillTrainFacts from ArmyReadiness.Compute - the SAME formula
        // BarracksService.EnqueueTraining seeds its own refusal from - so the sentence on the
        // button and the refusal in the service can never disagree.
        /// <summary>True when one more of THIS troop would not fit under the army cap (its own
        /// slot cost included, exactly as EnqueueTraining tests it).</summary>
        public bool ArmyFull;
        /// <summary>"Army is full . 20/20 slots used", or "" when it is not. The band the TRAIN
        /// face wears, replacing the free-floating footnote the owner captured.</summary>
        public string ArmyFullText = "";
        /// <summary>"Training line full . 5/5 queued", or "" when the Train line has room.</summary>
        public string QueueFullText = "";
        /// <summary>Army slots committed right now (roster + in-flight). 0 when unknown.</summary>
        public int ArmyUsedSlots;
        /// <summary>The army's slot ceiling. 0 when unknown (headless, no state).</summary>
        public int ArmyCapSlots;
        /// <summary>WO-1517 - the per-troop UPGRADE word, exactly one of
        /// <c>"UPGRADE AVAILABLE" | "MAX" | "UPGRADING" | "NEEDS &lt;blocker&gt;"</c>. ASCII, and
        /// the WORD is the carrier (the owner is red/green colourblind). Composed in
        /// <see cref="ManageScreenVM.FillUpgradeFacts"/> from BarracksService.CanUpgradeTroop's own
        /// reason, never from a second gate.</summary>
        public string UpgradeWord = "";
        /// <summary>False at max level - the View then shows a non-interactable MAX LEVEL face.</summary>
        public bool HasNextLevel;
        /// <summary>True while a TroopUpgrade job for this troop is on the Research line.</summary>
        public bool UpgradeInProgress;
        /// <summary>WO-1387 (2026-09-04): the upgrade's price is its TIME ("1m 30s"), or "" at max level.
        /// The View composes its sub-line as UpgradeCostText + " . " + UpgradeStateText, so the time
        /// rides this field (the Panel is another lane's file). It used to read "300 wood, 120 iron".</summary>
        public string UpgradeCostText = "";
        /// <summary>"Ready" / "Upgrading now" / "At max level" (no shortfall term since WO-1387).</summary>
        public string UpgradeStateText = "";
        /// <summary>True when an UPGRADE tap would be accepted right now.</summary>
        public bool UpgradeReady;
        /// <summary>The whole fact sentence: "Upgrade: 1m 30s . Ready" (WO-1387 shape).</summary>
        public string UpgradeFactText = "";
        /// <summary>WO-1389 - what the NEXT levels buy: "L3 unlocks Sweeping Cut"
        /// (BarracksProgression.NextAbilityLine), or "" when no ability remains above this level.
        /// The View paints it under the UPGRADE face so the button has a destination, not just a
        /// number. Pinned by ManageTroopsTrainDoorRegression case 7.</summary>
        public string NextUnlockText = "";

        // ── WO-1422 ruling 3.10 / 3.5 — Troops parity with the WO-1418 Buildings card ──
        /// <summary>WO-1422 ruling 3.10 item 1 — the card's state BADGE word, exactly one of
        /// <c>"Training" | "Locked" | "Max" | "Upgradable"</c> (ASCII; the WORD is the only carrier
        /// of state, the owner is red/green colourblind).
        /// <para>
        /// "Training" is defined HERE because no suite pins it: it means WORK IN FLIGHT ON THIS
        /// TROOP - either a TroopUpgrade job (<see cref="UpgradeInProgress"/>) or a Train-channel
        /// job whose id carries this troop (BarracksService.TrainPrefix + id). That is the Troops
        /// analogue of the Buildings card's "Building", which means a Builder job on the card's own
        /// subject.
        /// </para></summary>
        public string StateWord = "";
        /// <summary>WO-1422 ruling 3.5 - the SECOND door's label, or NULL when this troop has no
        /// door behind it. Currently ALWAYS null: there is no troop skill/perk panel to open
        /// (PanelRouter.PanelId carries HeroSkillTree/HeroTalents but no troop equivalent), and the
        /// ruling forbids inventing one. Kept as a field so the View's contract is the same on all
        /// four tabs.</summary>
        public string DoorLabel;
    }

    /// <summary>One authoritative selector/card entry for Manage -&gt; Buildings.</summary>
    public sealed class BuildingChoiceVM
    {
        public string Id;
        /// <summary>The actual placed structures-catalog palette id, never the shared ladder id.</summary>
        public string CatalogEntryId;
        public string Name;
        public int Level;
        public int MaxLevel;
        public string IconKey;
        public bool Locked;
        public string LockText;
        /// <summary>The authored Village level gate for the next tier; 0 when no gate applies.</summary>
        public int RequiresVillageTier;
        /// <summary>WO-1423 - the one SENTENCE a locked card paints as BODY TEXT, never on a button
        /// face (a sentence never fits a face - the WO-1422 3.7 lesson the Research card already
        /// learned). "" when not locked. The View pins this, not a face string.</summary>
        public string LockReason;
        /// <summary>WO-1423 - the SHORT face worn by the locked card's ONE full-width live door.
        /// "UPGRADE THE HEART", the same word the locked RESEARCH card uses for the same gate, so the
        /// two cards read alike. "" when not locked. The door itself is <see cref="ViewDetails"/>.</summary>
        public string LockCtaLabel;
        public string StateWord;
        public string Description;
        public IReadOnlyList<CostPart> UpgradeCostParts;
        public string UpgradeTimeText;
        public bool UpgradeReady;
        public string AfterUpgradeText;
        public int NextTier;
        public Action Activate;
        public Action ViewDetails;
        /// <summary>WO-1422 ruling 3.5 - the SECOND door's LABEL, or NULL when this ladder has
        /// nothing behind it. "PERKS" when building-tiers.json authors at least one perk for this
        /// ladder (<see cref="ManageScreenVM.HasAuthoredPerk"/>), else null - measured, the Farm
        /// authors 0 perks, so the Farm card shows ONE full-width CTA and no second door. The door
        /// itself is still <see cref="ViewDetails"/>; only the WORD changed (the owner's ruling
        /// "keep one door, but name what's behind it" retires the developer label VIEW DETAILS).</summary>
        public string DoorLabel;
    }

    /// <summary>
    /// WO-1422 - one authoritative selector/card entry for Manage -&gt; DEFENSE.
    ///
    /// ⚠ ONE ROW PER TYPE, NEVER PER PLACED INSTANCE (ruling 3.1). <c>wall_wood</c> is upgradable and
    /// a town has many segments; keying per instance would emit an unbounded rail. The card names the
    /// TYPE, states how many are placed and at what level, and its CTA targets the FIRST placed
    /// instance at the LOWEST level - which is exactly what the legacy browse row already targeted
    /// (see the comment above <see cref="ManageScreenVM.BuildDefenseBrowse"/>'s job key), so this is
    /// a PRESENTATION change and not a behaviour change.
    /// </summary>
    public sealed class DefenseChoiceVM
    {
        /// <summary>BaseLayout itemId, e.g. "tower_ground_archer".</summary>
        public string Id;
        /// <summary>The placed catalog row's id, for BuildPaletteUI.ResolveEntryArtPublic.</summary>
        public string CatalogEntryId;
        /// <summary>Display name only - never "X - grid 3, 7 - L1 -&gt; L2" (the retired browse label).</summary>
        public string Name;
        /// <summary><see cref="ManageArt.BuildingPortraitKey"/> output, e.g.
        /// "Portraits/Buildings/tower_ground_archer-2". ⛔ ID-keyed, never a display-name slug -
        /// see the note where the slug composer was deleted.</summary>
        public string PortraitKey;
        /// <summary>The LOWEST placed level of this type - the one the CTA acts on.</summary>
        public int Level;
        /// <summary>PlacedStructureUpgradeService.MaxLevelFor(entry) - the shared clamped ceiling.</summary>
        public int MaxLevel;
        /// <summary>How many of this type stand in this town.</summary>
        public int PlacedCount;
        /// <summary>"3 placed . lowest L1" / "1 placed . L1" (ruling 3.1).</summary>
        public string PlacedText;
        /// <summary>"Building" | "Max" | "Upgradable" (ASCII, ruling 3.7 discipline).</summary>
        public string StateWord;
        /// <summary>One sentence: StructureCardVM.DescriptionFor, first clause.</summary>
        public string Description;
        /// <summary>BuildModeController.UpgradeCostFor(entry, Level); EMPTY at max level.</summary>
        public IReadOnlyList<CostPart> UpgradeCostParts;
        /// <summary>QueueRailView.FormatTime of the derived duration; NULL when it is not reachable
        /// (no BuildTimerService / no config, or already at max) - never a hardcoded number.</summary>
        public string UpgradeTimeText;
        /// <summary>affordable AND not already building AND not maxed.</summary>
        public bool UpgradeReady;
        /// <summary>What the next level buys; "" when Max.</summary>
        public string AfterUpgradeText;
        /// <summary>Level + 1, or 0 when Max.</summary>
        public int NextLevel;
        /// <summary>PlacedUpgradeKey.Compose(itemId, cellX, cellZ) of the FIRST instance standing at
        /// <see cref="Level"/> - the instance the CTA upgrades.</summary>
        public string JobKey;
        /// <summary>NULL for Defense (ruling 3.5): there is no per-defense detail page and the
        /// ruling forbids inventing one.</summary>
        public string DoorLabel;
        /// <summary>() =&gt; UpgradePlaced(JobKey); NULL when Max.</summary>
        public Action Activate;
    }

    /// <summary>
    /// WO-1422 - one authoritative selector/card entry for Manage -&gt; RESEARCH.
    ///
    /// ⚠ ONE ROW PER PERK, NOT PER BUILDING (ruling 3.6), and the WHOLE TREE including the two states
    /// the legacy browse list HID (ruling 3.7): an OWNED perk and an IN-PROGRESS perk both emitted no
    /// row at all. This is the same deliberate delta WO-1418 made when it stopped hiding maxed
    /// buildings - a ladder the player cannot see is a ladder they cannot plan against.
    /// </summary>
    public sealed class ResearchChoiceVM
    {
        /// <summary>The owning building's ladder id, e.g. "arcane-tower".</summary>
        public string BuildingId;
        /// <summary>The perk id, e.g. "warding".</summary>
        public string PerkId;
        /// <summary>The perk's display name, e.g. "Improved Logging" - NEVER "Lumber Mill - Improved
        /// Logging"; the developer " - " label shape died with the paged list (ruling 3.6).</summary>
        public string Name;
        /// <summary>Rail sub-line + card sub-line, e.g. "Lumber Mill".</summary>
        public string BuildingName;
        /// <summary>BuildingPerkDef.IconId (defaulting to the perk id) - the View loads
        /// <c>HudIcons/BuildingUpgrades/&lt;IconName&gt;</c>, the path BuildingUpgradePanelMvvm
        /// already loads. ⚠ BuildingPerkDef.IconId's own doc comment names Resources/HudItems/
        /// BuildingUpgrades/ and THAT FOLDER DOES NOT EXIST - do not follow the comment.</summary>
        public string IconName;
        /// <summary>BuildingTierCatalog.PerkUnlockTier(bId, pId).</summary>
        public int UnlockTier;
        /// <summary>"TIER 2" - what the card's LEVEL slot carries, because research has NO level
        /// (ruling 3.7). Never paint "LEVEL 0".</summary>
        public string TierText;
        /// <summary>"Researched" | "Researching" | "Available" | "Locked" (ASCII, ruling 3.7).</summary>
        public string StateWord;
        /// <summary>StateWord == "Locked". The migrated lock-badge pin reads this.</summary>
        public bool Locked;
        /// <summary>BuildingPerkService.CanResearch's out reason, VERBATIM (no Ascii(), no
        /// "Locked." substitution - a suite asserts exact equality); "" when not locked.</summary>
        public string LockReason;
        /// <summary>The perk's authored effect sentence.</summary>
        public string Description;
        /// <summary>Gold-only. ⚠ ResourceCost has NO gold field, which is why this is a
        /// CostFormat.Parts list with an explicit ("gold","Gold",price) part - the same shape
        /// BuildingUpgradeCostParts already uses. Do not add a field to ResourceCost.</summary>
        public IReadOnlyList<CostPart> CostParts;
        /// <summary>FormatTime(BuildingPerkService.ResearchSeconds(bId,pId)).</summary>
        public string TimeText;
        /// <summary>Available AND affordable.</summary>
        public bool Ready;
        /// <summary>"RESEARCH" | "RESEARCHING" | "UPGRADE THE HEART" | "UPGRADE &lt;NAME&gt;" | null.</summary>
        public string CtaLabel;
        /// <summary>NULL (ruling 3.5).</summary>
        public string DoorLabel;
        /// <summary>() =&gt; Research(bId,pId) when Available, OpenUpgradePanel(bId) when Locked;
        /// NULL when Researched or Researching (both are non-interactable faces, ruling 3.7).</summary>
        public Action Activate;
    }

    /// <summary>
    /// ViewModel for the unified Manage / Queues screen. Rebuilt on demand; holds no Unity objects.
    /// </summary>
    public sealed class ManageScreenVM
    {
        /// <summary>Raised whenever the rows change and the View must repaint.</summary>
        public event Action Changed;

        /// <summary>The selected content tab.</summary>
        public ManageTab Tab { get; private set; } = ManageTab.Buildings;

        /// <summary>All three lines' at-a-glance state — every channel stays visible on every tab.</summary>
        public readonly List<ChannelSummary> Channels = new List<ChannelSummary>(3);

        /// <summary>The selected tab's channel queue, in line order.</summary>
        public readonly List<QueueRowVM> QueueRows = new List<QueueRowVM>(16);

        /// <summary>The selected tab's upgrade browse list, affordable-first.</summary>
        public readonly List<BrowseRowVM> BrowseRows = new List<BrowseRowVM>(32);

        /// <summary>All authored troops, including locked entries, for explicit selector disclosure.</summary>
        public readonly List<TroopChoiceVM> TroopChoices = new List<TroopChoiceVM>(12);

        /// <summary>Every placed building with an authored tier ladder, including maxed entries.</summary>
        public readonly List<BuildingChoiceVM> BuildingChoices = new List<BuildingChoiceVM>(16);

        /// <summary>WO-1422 — every upgradable placed defense TYPE standing in this town, including
        /// maxed ones. ONE entry per type, never per instance (ruling 3.1).</summary>
        public readonly List<DefenseChoiceVM> DefenseChoices = new List<DefenseChoiceVM>(16);

        /// <summary>WO-1422 — every authored perk of every owned ladder building, in all four
        /// states including the two the legacy browse list hid (ruling 3.7).</summary>
        public readonly List<ResearchChoiceVM> ResearchChoices = new List<ResearchChoiceVM>(24);

        /// <summary>WO-1406 Troops header copy, projected here so the View never reads game state.
        /// <para>WO-1541: its camp clause is READ from <c>PostureSignals.RaidNextCampName</c> /
        /// <c>RaidNextCampGarrison</c>, never re-derived here. See
        /// <see cref="BuildTroopArmySummary"/> for the one-producer reasoning.</para></summary>
        public string TroopArmySummaryText { get; private set; }

        /// <summary>WO-1541 ruling 2 - the door from the army line to the raid grid. Null when no
        /// camp is published, so the View can never paint an affordance that goes nowhere.
        /// <para>⚠ NOT PAINTED YET, and that is a BLOCKED RULING, not an oversight. The Troops
        /// card has no seat that clears <c>ElarionUiKit.MinTouchPx</c> (112): the army band is
        /// 26px, the card is at its 256px selection floor, and the CTA band already holds TRAIN +
        /// UPGRADE at the floor with WO-1422 ruling 3.10 recorded IN THE VIEW
        /// (<c>ManageScreenPanel</c>, the DoorLabel escape hatch) saying a third face is REPORTED
        /// rather than squeezed. The model publishes the door so it is one BuildObsidianButton
        /// away the moment the owner names a seat.</para></summary>
        public Action TroopArmyDoor { get; private set; }

        /// <summary>ASCII face for <see cref="TroopArmyDoor"/> ("RAID THE FORSAKEN CAMP"). Null
        /// whenever the door is null - the two are set and cleared together.</summary>
        public string TroopArmyDoorLabel { get; private set; }

        /// <summary>Categories earned by structures standing in the current town. Defense is the
        /// one intentional empty-state exception: it remains visible before the first placement
        /// so a fresh-town player can discover the defensive build route.</summary>
        public readonly List<ManageTab> VisibleTabs = new List<ManageTab>(4);

        // ── THE HUB'S HEART CHIP - one predicate, one producer (WO-1597) ─────────────────
        /// <summary>
        /// ⭐ TRUE ONLY WHILE A HEART UPGRADE IS ACTUALLY DUE. This is the whole reason the hub
        /// draws a HEART chip at all.
        ///
        /// <para>Owner, 2026-09-07 on the device frame, verbatim: <i>"there is no reason to have
        /// heart on this set of manage screens unless for an upgrade"</i>. Mockup panel 1 draws no
        /// chip; the chip earns its place only when it is a DOOR TO A PENDING UPGRADE, and then it
        /// says so in the upgrade verb rather than badging a level.</para>
        ///
        /// <para>⛔ READ FROM THE ONE PRODUCER, NOT RECOMPOSED. <c>HeartProgression.State</c> is the
        /// same expression the Heart's own detail surface binds (HeartPanel), so the chip and the
        /// screen it opens can never disagree. Rebuilding it here as
        /// <c>!IsMax &amp;&amp; Crystals &gt;= NextCost()</c> would be a second copy of a live
        /// predicate - the duplicated state CLAUDE.md sections 2/5/8/16 keep paying for.</para>
        ///
        /// <para>⚠ WHY <c>!= Max</c> AND NOT <c>== Ready</c>, STATED SO IT IS NOT "CORRECTED" BLIND.
        /// The three states are Max / MissingCrystals / Ready. An upgrade EXISTS in two of them; in
        /// <c>Ready</c> it is also affordable right now. Gating the chip on affordability would
        /// hide the goal from the player who is saving for it AND would strand the Heart's only hub
        /// door behind a wallet balance. MEASURED 2026-09-07: <c>heart-progression.json</c> authors
        /// <c>maxLevel: 3</c> and the owner's frame reads HEART L3, so she is at MAX - which is why
        /// her frame must show NO chip either way, and this predicate is the one that also keeps
        /// the door while there is something to spend on. If the owner wants the stricter rule, the
        /// change is <c>== HeartActionState.Ready</c> here and nothing else moves.</para>
        /// </summary>
        public static bool HeartUpgradeAvailable =>
            DeNelle.Village.Buildings.Progression.HeartProgression.State
                != DeNelle.Village.Buildings.Progression.HeartActionState.Max;

        /// <summary>
        /// The chip's FACE while <see cref="HeartUpgradeAvailable"/> - the upgrade VERB, never a
        /// level badge. Composed here because the model owns the words (canon section 9); the View
        /// binds a string it did not write.
        /// <para>⚠ The cost is the producer's own <c>NextCost()</c>, compacted through the shared
        /// formatter, so no number is typed and none can go stale.</para>
        /// </summary>
        public static string HeartUpgradeFace =>
            HeartUpgradeAvailable
                ? "UPGRADE HEART"
                : null;

        /// <summary>The cost line under the chip's verb ("750 Crystals"), or null at max. Second
        /// line rather than a longer face: "HEART ..." truncating at ~177px is this control's
        /// recorded failure mode (three rounds, WO-1443), and a wider face is not available in the
        /// hub's header band.</summary>
        public static string HeartUpgradeCost =>
            HeartUpgradeAvailable
                ? DeNelle.Core.UI.ElarionUi.CompactNumber(
                      DeNelle.Village.Buildings.Progression.HeartProgression.NextCost()) + " Crystals"
                : null;

        /// <summary>Last command's player-facing message (ASCII), or null. The View toasts it.</summary>
        public string Notice { get; private set; }

        /// <summary>True when <see cref="Notice"/> is the broke case and the View should offer the store.</summary>
        public bool NoticeIsBrokeCase { get; private set; }

        /// <summary>WO-1253 Manage button copy. Measured: 11 chars, shorter than the old
        /// "Buy slot 250c" (14) that already fit the 0.33-width slot button.</summary>
        public const string BuyBuilderButtonCopy = "Buy builder";

        /// <summary>WO-1253 Manage label. 20 chars. Words carry the product (concurrency), not hue.</summary>
        public const string BuyBuilderLabelCopy = "Permanent builder +1";

        /// <summary>WO-1253 Manage label when the SKU is already owned. 20 chars.</summary>
        public const string BuyBuilderOwnedLabelCopy = "You own this builder";

        /// <summary>Retired crystal-price field. Always 0 after WO-1253: Manage no longer sells a crystal slot.</summary>
        public int SlotPrice { get; private set; }

        /// <summary>ASCII sentence describing the permanent-builder store offer.</summary>
        public string SlotOfferText { get; private set; } = "";

        /// <summary>True only when every Builder slot is occupied and the permanent crew is not owned.</summary>
        public bool BuilderUpsellVisible { get; private set; }

        /// <summary>Commerce-priced permanent-builder CTA; empty while the upsell is hidden.</summary>
        public string BuilderUpsellButtonText { get; private set; } = "";

        /// <summary>WO-911 (Q2) — crystal-free instant repair cost, or null when nothing is damaged.</summary>
        public string RepairOfferText { get; private set; }

        private readonly HashSet<string> _expandedStacks = new HashSet<string>();

        /// <summary>
        /// Placed ids already reported by <see cref="WarnNoLadder"/>. STATIC on purpose: the warning
        /// is about the DATA (an unauthored ladder row), not about one screen instance, so opening
        /// Manage a second time must not re-print the same to-do list.
        /// </summary>
        private static readonly HashSet<string> _noLadderWarned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // =====================================================================
        //  Tab -> channel. The ONE place the crossing is expressed.
        // =====================================================================

        /// <summary>
        /// The queue CHANNEL a content tab's work runs on. Defense and Buildings deliberately map to
        /// the SAME channel — they share one Builders line and one set of slots (WO-905 §2a).
        /// </summary>
        public static ChannelId ChannelOf(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Troops: return ChannelId.Train;
                case ManageTab.Research: return ChannelId.Research;
                default: return ChannelId.Builder;   // Defense AND Buildings
            }
        }

        /// <summary>ASCII tab labels, in tab order.</summary>
        public static readonly string[] TabLabels = { "Defense", "Buildings", "Troops", "Research" };

        /// <summary>Select a tab and rebuild.</summary>
        public void SelectTab(ManageTab tab)
        {
            if (Tab == tab) return;
            Tab = tab;
            FlowTrace.Step("Manage", $"tab -> {tab} (line {ChannelOf(tab)})");
            Rebuild();
        }

        /// <summary>Expand or collapse a Q12 stack of identical pending jobs.</summary>
        public void ToggleStack(string stackKey)
        {
            if (string.IsNullOrEmpty(stackKey)) return;
            if (!_expandedStacks.Remove(stackKey)) _expandedStacks.Add(stackKey);
            FlowTrace.Step("Manage",
                $"stack '{stackKey}' {( _expandedStacks.Contains(stackKey) ? "EXPANDED" : "collapsed")} " +
                "(Q12: cancel is only reachable on an expanded child).");
            Rebuild();
        }

        /// <summary>Clear the transient notice (after the View has shown it).</summary>
        public void ClearNotice() { Notice = null; NoticeIsBrokeCase = false; }

        // =====================================================================
        //  BUILD
        // =====================================================================

        /// <summary>Recompute every row from live state and raise <see cref="Changed"/>.</summary>
        public void Rebuild()
        {
            Guard.Try("Manage", "rebuild manage rows", () =>
            {
                Channels.Clear();
                QueueRows.Clear();
                BrowseRows.Clear();
                TroopChoices.Clear();
                BuildingChoices.Clear();
                DefenseChoices.Clear();
                ResearchChoices.Clear();
                TroopArmySummaryText = null;
                TroopArmyDoor = null;             // WO-1541 - cleared with the copy it belongs to
                TroopArmyDoorLabel = null;
                _inventoryTiles = null;   // WO-2001 - the per-rebuild BUILD inventory cache
                _inventoryChip = null;

                BuildVisibleTabs();
                if (VisibleTabs.Count > 0 && !VisibleTabs.Contains(Tab))
                    Tab = VisibleTabs[0];

                BuildChannelSummaries();
                // ⭐ THE OVERLAY CHOOSES ITS OWN CHANNEL. Panel 8 has three TABS - BUILDERS /
                // TRAINING / RESEARCH - and until now the drawer rendered exactly ONE channel,
                // taken from whichever browse tab happened to be open (ManageScreenPanel.cs:2406,
                // ChannelOf(_vm.Tab)). A player could not reach another line's queue from inside
                // the overlay AT ALL. The selection is model state so the view never decides it.
                // ⚠ It DEFAULTS to the browse tab's channel, so opening the drawer is unchanged -
                // BUILD still lands on Builders. Only switching is new.
                if (!_queueOverlayChannelPinned) QueueOverlayChannel = ChannelOf(Tab);
                BuildQueueRows(QueueOverlayChannel);
                _queueTabs = ComposeQueueTabs();
                BuildQueueEmptyText(QueueOverlayChannel);   // WO-1488: the empty state names ITS verb
                BuildSlotOffer(QueueOverlayChannel);
                BuildRepairOffer();
                BuildBrowseRows();
                BuildBuildingChoices();
                // WO-1422 — UNCONDITIONAL, exactly like BuildBuildingChoices above. The card
                // projections are NOT tab-gated: the View selects a row before the tab is opened
                // (its selection default reads choice[0] on construction), so gating them on Tab
                // would hand the panel an empty list on the very first paint of that tab.
                BuildDefenseChoices();
                BuildResearchChoices();
                BuildTroopArmySummary();
            });
            Changed?.Invoke();
        }

        private void BuildVisibleTabs()
        {
            VisibleTabs.Clear();
            var placed = CountPlacedThisTown();
            bool defense = false, buildings = false, troops = false, research = false;
            foreach (var kv in placed)
            {
                var tier = BuildingTierCatalog.Find(kv.Key);
                if (tier != null)
                {
                    buildings = true;
                    if (kv.Key.IndexOf("barracks", StringComparison.OrdinalIgnoreCase) >= 0)
                        troops = true;
                    if (HasAuthoredPerk(tier)) research = true;
                }
                if (HasLevelLadder(kv.Value)) defense = true;
            }
            // WO-1285: hiding Defense until after the first defense is placed makes its route
            // circular. Keep one actionable empty-state tab; its View CTA opens the Defense builder.
            VisibleTabs.Add(ManageTab.Defense);
            if (buildings) VisibleTabs.Add(ManageTab.Buildings);
            if (troops) VisibleTabs.Add(ManageTab.Troops);
            if (research) VisibleTabs.Add(ManageTab.Research);

            // NO SILENT DISCLOSURE (§12). This is the single decision that answers the recurring
            // felt-test "there is no way to get to the upgrade/defensive screen", and until now it
            // left NO trace at all — so the only way to tell a correctly-gated fresh save from a
            // genuinely orphaned door was to read the source. It is a Step, not a Warn: zero tabs
            // on an empty BaseLayout is the DESIGNED progressive-disclosure state (the player is
            // sent to the "Build new" route), and the tabs appear as soon as something is placed.
            // A DEFENSE tab specifically needs a placed id whose repo.maxLevel > 1 — baked scene
            // walls and towers are NOT BaseLayout records and therefore never raise it.
            FlowTrace.Step("Manage",
                "visible tabs: " + string.Join(", ", VisibleTabs) + " (from " + placed.Count +
                " placed type(s); defenseOwned=" + defense + " buildings=" + buildings +
                " troops=" + troops + " research=" + research + ").");
        }

        private static bool HasAuthoredPerk(BuildingUpgradeDef def)
        {
            if (def?.Tiers == null) return false;
            for (int i = 0; i < def.Tiers.Count; i++)
                if (def.Tiers[i]?.Perks != null && def.Tiers[i].Perks.Count > 0) return true;
            return false;
        }

        private void BuildChannelSummaries()
        {
            var svc = BuildTimerService.Instance;
            if (svc == null)
            {
                FlowTrace.Warn("Manage", "no BuildTimerService — the queue strip renders empty.");
                return;
            }
            AddSummary(svc, ChannelId.Builder);
            AddSummary(svc, ChannelId.Train);
            AddSummary(svc, ChannelId.Research);
        }

        private void AddSummary(BuildTimerService svc, ChannelId id)
        {
            var summary = new ChannelSummary
            {
                Channel = id,
                Name = BuildTimerService.ChannelWord(id),
                Busy = svc.ActiveJobsOf(id).Count,
                Slots = svc.SlotCount(id),
                Depth = svc.QueueDepth(id),
                DepthCap = svc.QueueDepthLimit(id),
            };
            Channels.Add(summary);
            FlowTrace.Step("Manage", "launcher chip=" + summary.Name + " idle=" + (summary.Busy == 0) +
                " free=" + Mathf.Max(0, summary.Slots - summary.Busy));
        }

        // ── Queue rows ────────────────────────────────────────────────────────

        private void BuildQueueRows(ChannelId channel)
        {
            var svc = BuildTimerService.Instance;
            if (svc == null) return;

            int crystals = CrystalBalance();

            // ACTIVE jobs first, never collapsed — a running job is always individually addressable.
            var active = svc.ActiveJobsOf(channel);
            for (int i = 0; i < active.Count; i++)
                QueueRows.Add(MakeJobRow(svc, channel, active[i], queued: false, pendingIndex: -1,
                                         crystals: crystals, isChild: false));

            // PENDING jobs, with the Q12 collapse.
            //
            // Owner ruling Q12, verbatim: "can not cancel on a collapsed card, must expand then
            // select item to cancel and others automatically move up." So identical pending jobs
            // publish as ONE header with NO JobId and NO cancel/finish affordance; expanding it
            // reveals the REAL per-job ids (the collapse is a PRESENTATION concern — the engine
            // keys cancel by id, not index, so the underlying jobs were always addressable).
            // CONTENT TABS CROSS CHANNELS (canon: Defence and Buildings share the ONE Builder rail).
            // The Troops tab must therefore also show TROOP UPGRADES, which the engine runs on the
            // RESEARCH channel (BarracksService enqueues them there). Without this, tapping Upgrade
            // on a Troops row put the job on a tab the player was not looking at and Troops kept
            // saying "Nothing queued on this line" - it read as a dead button.
            if (Tab == ManageTab.Troops)
            {
                var xActive = svc.ActiveJobsOf(ChannelId.Research);
                for (int i = 0; i < xActive.Count; i++)
                    QueueRows.Add(MakeJobRow(svc, ChannelId.Research, xActive[i], queued: false,
                                             pendingIndex: -1, crystals: crystals, isChild: false));
            }

            var pending = svc.PendingJobsOf(channel);
            int idx = 0;
            while (idx < pending.Count)
            {
                string key = StackKeyOf(pending[idx]);
                int run = 1;
                while (idx + run < pending.Count && StackKeyOf(pending[idx + run]) == key) run++;

                if (run <= 1)
                {
                    QueueRows.Add(MakeJobRow(svc, channel, pending[idx], queued: true, pendingIndex: idx,
                                             crystals: crystals, isChild: false));
                    idx += 1;
                    continue;
                }

                bool expanded = _expandedStacks.Contains(key);
                QueueRows.Add(new QueueRowVM
                {
                    Label = ObsidianQueueHud.FormatJobTarget(pending[idx]) + " x" + run,
                    StateText = expanded ? "Queued - expanded, pick one to cancel"
                                         : "Queued x" + run + " - expand to cancel one",
                    Channel = channel,
                    JobId = null,                 // ⚠ Q12: an aggregate is never a cancel target
                    Queued = true,
                    PendingIndex = idx,
                    IsStackHeader = true,
                    StackCount = run,
                    StackKey = key,
                    Expanded = expanded,
                    FinishPrice = 0,              // ⚠ Q11/Q12: no paid verb on an aggregate either
                    CanCancel = false,
                    CanBumpUp = false,
                    RefundText = null,
                });

                if (expanded)
                    for (int k = 0; k < run; k++)
                        QueueRows.Add(MakeJobRow(svc, channel, pending[idx + k], queued: true,
                                                 pendingIndex: idx + k, crystals: crystals, isChild: true));
                idx += run;
            }

            // ⭐ NUMBER THE ROWS ONCE, HERE, IN QUEUE ORDER - mockup panel 8's "1. 2. 3.".
            // Numbered AFTER the whole list is assembled because the order is the list's, not any
            // one branch's: active jobs lead, then the pending FIFO, then a cross-channel troop
            // upgrade if this tab shows one. A per-branch counter would restart.
            // ⛔ A STACK CHILD TAKES NO NUMBER. It is one of several identical jobs revealed by
            // expanding a header (ruling Q12), and numbering them would renumber the queue in front
            // of the player every time they opened a stack. The header carries the position; the
            // children carry the detail.
            int ordinal = 0;
            for (int i = 0; i < QueueRows.Count; i++)
            {
                var r = QueueRows[i];
                if (r == null || r.IsStackChild) continue;
                ordinal++;
                r.OrdinalText = ordinal.ToString();
                r.PositionText = r.Queued ? r.OrdinalText : "NOW";
            }
        }

        private QueueRowVM MakeJobRow(BuildTimerService svc, ChannelId channel, BuildJobData job,
                                      bool queued, int pendingIndex, int crystals, bool isChild)
        {
            // Icon keys come from the SERVICE's card shape - the same one the queue rail uses.
            var card = BuildTimerService.EntryFor(job);
            int price = svc.InstantFinishPrice(channel, job.StructureId);
            double rem = svc.RemainingSeconds(channel, job.StructureId);
            // WO-1372 Lane D: the CURRENCY is the service's decision, asked of the ONE map
            // (BuildTimerService.FinishPaysGold) — never inferred here, so the word on the row
            // can never disagree with the wallet TryInstantFinish debits. A TrainTroop job is
            // priced and paid in GOLD and wears the canon HIRE REINFORCEMENTS verb (creative
            // canon §6); every other kind keeps crystals and the View's "Finish Now" default.
            bool paysGold = BuildTimerService.FinishPaysGold(job.JobKind);
            int balance = paysGold ? GoldBalance() : crystals;
            string buildingId = channel == ChannelId.Builder ? NormalizeBuildingJobId(job) : "";
            var building = !string.IsNullOrEmpty(buildingId) ? BuildingTierCatalog.Find(buildingId) : null;
            // ⭐ WO-1564 part 2 - THE QUEUE ROW NAMES THE STRUCTURE AND THE LEVEL IN WORDS.
            //
            // ⛔ THE DEFECT. The drawer read "Tower Ground Archer -> L2" and "Barracks -> L4".
            // The "-> Ln" was composed right here, and on a catalog MISS the row fell through to
            // BuildTimerService.PrettyJobLabel, which title-cases the id's own tokens
            // (tower_ground_archer -> "Tower Ground Archer") with its own comment conceding
            // "no catalog lookup". The player was reading an internal identifier dressed up as a
            // name, and a developer's arrow notation as a level.
            //
            // ⚠ THE RULE-SHAPED PART, and it is why this fix is HERE and not in the View: Manage
            // canon 9 forbids the UI parsing ids - and the UI was not parsing one. The VM was. The
            // dumb-View rule was technically honoured while the player still read an identifier.
            // The rule has to bind wherever the STRING IS MADE.
            //
            // ⛔ PrettyJobLabel IS LEFT ALONE - it has other callers (the Core-safe HUD queue chip,
            // which must never block on data readiness). The honest path is ADDED here rather than
            // its behaviour repurposed underneath them.
            string label = ObsidianQueueHud.FormatJobTarget(job);
            // ⭐ WO-1567 ROUND 26 - THE THUMBNAIL'S IDENTITY IS RESOLVED BESIDE THE LABEL'S,
            // BY THE SAME BRANCHES, BECAUSE THEY ARE THE SAME QUESTION.
            // ⛔ THIS IS THE MEASURED DEFECT: on every *_queue frame, row 1 ("Archer Tower -
            // Level 2") carries NO icon while rows 2 and 4 do. The asymmetry is exactly the
            // two-catalog split the block below already documents. A TOWER is not in
            // BuildingTierCatalog - it lives in CatalogRegistry - so `building` is null for it,
            // the LABEL is resolved correctly by the structures-catalog branch, and the
            // PortraitKey (composed further down, guarded on `building != null`) came out EMPTY.
            // Nothing logged, because nothing was missing: the art exists and the BUILD grid tile
            // paints it from this very id (ComposeBuildTiles -> ManageArt.BuildingPortraitKey).
            // The row simply never asked for it.
            // ⚠ ONE KEY PRODUCER, UNCHANGED. This carries the ID that won; ManageArt still composes
            // the key. Canon 9 holds - the View is handed a key, and no second spelling is minted.
            string portraitId = null;
            if (building != null)
            {
                string name = Ascii(!string.IsNullOrWhiteSpace(building.DisplayName)
                    ? building.DisplayName : buildingId);
                label = job.TargetTier > 0 ? name + " - Level " + job.TargetTier : name;
                portraitId = buildingId;
            }
            else if (channel == ChannelId.Builder && !string.IsNullOrEmpty(buildingId))
            {
                // ⛔ TWO CATALOGS, AND THE SECOND ONE IS NOT OPTIONAL. BuildingTierCatalog holds the
                // TIER LADDER buildings only; every TOWER and WALL lives in CatalogRegistry
                // (structures-catalog), which this VM already reads elsewhere (HasLevelLadder).
                // Treating a BuildingTierCatalog miss as the failure would rename EVERY tower
                // upgrade - by far the most common Builder job - to a placeholder and fire a Fail
                // on healthy data. The order is: tier catalog, then structures catalog, THEN the
                // honest miss.
                // ⚠ CatalogRegistry.Get is an EXACT id match, so it is asked with the RAW structure
                // id minus its placement suffix ("tower_ground_archer@15_7" -> "tower_ground_archer").
                // buildingId cannot be used: NormalizeBuildingJobId lower-cases it and rewrites
                // '_' to '-', which no structures-catalog id carries.
                string catalogId = job.StructureId ?? "";
                int suffix = catalogId.IndexOfAny(new[] { '@', ':' });
                if (suffix > 0) catalogId = catalogId.Substring(0, suffix);
                var structure = !string.IsNullOrEmpty(catalogId) ? CatalogRegistry.Get(catalogId) : null;
                if (structure != null && !string.IsNullOrEmpty(structure.displayName))
                {
                    string structureName = Ascii(structure.displayName);
                    label = job.TargetTier > 0
                        ? structureName + " - Level " + job.TargetTier
                        : structureName;
                    // The SAME id the BUILD grid's tile paints from - see the note above the
                    // `portraitId` declaration for the measured row-1-has-no-icon defect.
                    portraitId = catalogId;
                }
                else
                {
                    // Neither catalog knows this id. That is a DATA DEFECT, not a formatting
                    // inconvenience, and CLAUDE.md section 12 says it must be LOUD. The row still
                    // paints (a queue that hides a running job is worse), but it paints an honest
                    // placeholder instead of a title-cased id quietly presented as a name.
                    FlowTrace.Fail("Manage", "queue row catalog MISS: neither BuildingTierCatalog ('" +
                        buildingId + "') nor CatalogRegistry ('" + catalogId + "') has a display name " +
                        "for job '" + job.StructureId + "' (channel " + channel + "). The player would " +
                        "otherwise read the raw id as a structure name");
                    label = job.TargetTier > 0
                        ? "Unknown structure - Level " + job.TargetTier
                        : "Unknown structure";
                }
            }

            // The TRAIN and RESEARCH channels never reach the BuildingTierCatalog branch above,
            // and ObsidianQueueHud.FormatJobTarget still speaks the developer arrow for troop,
            // barracks and tower UPGRADE rows ("Archer -> L3"). ⛔ It is NOT changed underneath its
            // other callers - the Core-safe HUD queue chip renders the same string and
            // ObsidianQueueRegression pins "Barracks -> L2" verbatim. The Manage drawer normalises
            // the notation it RECEIVES, which is a presentation concern and belongs on this side.
            label = label.Replace(" -> L", " - Level ");

            // ⛔ A RAW INTERNAL ID REACHING THE PLAYER IS A DATA DEFECT AND IT IS LOUD
            // (CLAUDE.md section 12). Underscores and colons are id grammar, never display
            // grammar - "tower_ground_archer" and "barracks-train:footman:7" both trip this. The
            // row still paints; the failure is traced rather than silently prettified, which is
            // exactly what PrettyJobLabel's title-casing used to do.
            // ⛔ A BUILDER ROW WITH NO THUMBNAIL IDENTITY IS SAID OUT LOUD. The previous silence is
            // why row 1 lost its icon for a whole capture round with nothing in the log: an empty
            // key never reaches ManageArt, so ManageArt could not announce a miss, and the row
            // simply drew no picture. A gap nobody names is a gap nobody closes (CLAUDE.md 12).
            if (channel == ChannelId.Builder && string.IsNullOrEmpty(portraitId))
                FlowTrace.Once("Manage", "queue-thumb-miss:" + (job.StructureId ?? "<null>"),
                    "queue row for Builder job '" + job.StructureId + "' resolved no portrait " +
                    "identity from either catalog, so mockup panel 8's row thumbnail is absent. " +
                    "The LABEL may still be correct - these are two different lookups - so check " +
                    "the id against BuildingTierCatalog and CatalogRegistry, not the row's text.");

            if (label.IndexOf('_') >= 0 || label.IndexOf(':') >= 0)
                FlowTrace.Fail("Manage", "queue row label '" + label + "' carries id grammar for job '" +
                    job.StructureId + "' (channel " + channel + ") - the player is reading an " +
                    "internal identifier. Author the catalog display name for this id");

            return new QueueRowVM
            {
                Label = label,
                // Colourblind law: the state is a SENTENCE, never a tint.
                // The percentage is stated IN WORDS beside the bar (colourblind law: the fill is
                // never the only signal). WO-898's monetization driver is the player SEEING how
                // close the wall is when a raid is inbound; a bare countdown does not carry that.
                // The leading timeline marker already says NOW or supplies the queue position.
                // Keep this line to the decision-changing facts so it survives the narrow text
                // column without ellipsis.
                StateText = QueueTimingText(svc, channel, job.StructureId, queued, rem),
                Channel = channel,
                JobId = job.StructureId,
                BuildingId = building != null ? buildingId : "",
                // WO-1488 s2: the thumbnail key, from the ONE producer. Level 1 is the base sheet;
                // ManageArt.LoadSprite already falls back tier -> base, so a tier the art wave has
                // not drawn paints the building rather than a blank.
                // ⭐ WO-1567 ROUND 26 - OFF `portraitId`, WHICH BOTH CATALOG BRANCHES SET.
                // ⛔ IT READ `building != null ? BuildingPortraitKey(buildingId, 1) : ""`, and that
                // guard is the row-1-has-no-icon defect: a TOWER resolves its NAME through
                // CatalogRegistry and its `building` is null, so every tower and wall row - the
                // most common Builder job there is - asked for no art at all while its neighbours
                // did. See the `portraitId` note above the label branches.
                PortraitKey = !string.IsNullOrEmpty(portraitId)
                    ? ManageArt.BuildingPortraitKey(portraitId, 1) : string.Empty,
                Queued = queued,
                PendingIndex = pendingIndex,
                IsStackChild = isChild,
                StackCount = 1,
                // Ruling Q5: a QUEUED job is Finish-Now-able and priced, exactly like a running one.
                // The button is offered even when unaffordable (owner: "always show Finish while a
                // job runs, plus a get-crystals route when broke") — never hidden on price.
                FinishPrice = price,
                CanAffordFinish = price > 0 && balance >= price,
                FinishCostText = DescribeFinishCost(price, balance, paysGold),
                // ⭐ "SPEED UP" - the mockup's own word on panel 8's active row, and a ONE-FIELD
                // change: ManageScreenPanel.cs:4604 already reads FinishVerbText (falling back to
                // "Finish Now") and :4608 already calls _vm.FinishNow(channel, jobId). No new button,
                // no second rush path.
                // ⛔ "SPEED UP" ON EVERY TAB. DO NOT RESTORE "HIRE REINFORCEMENTS" HERE.
                // Owner ruling relayed 2026-09-06, on a MEASUREMENT this file produced: the CTA verb
                // warn reported HIRE REINFORCEMENTS needing 598px in a box that gives 236px at
                // ElarionUiKit.FontFloor - two and a half times over. No slot on a queue row can hold
                // it, at any font this project considers legible, so it ellipsised to "HIRE REIN...".
                // Panel 8 draws ONE gold SPEED UP and prices it on the line underneath, and the
                // owner has ruled the mockup absolute.
                // ⚠ NOTHING IS LOST, and this is deliberately NOT a currency change: FinishPaysGold
                // still decides what the player spends and FinishCostText still says it in words -
                // "349 gold" on a training job, "33 crystals" elsewhere. Only the VERB is now
                // uniform. The service's answer is untouched, which is what this suite's
                // [price-from-service] wall exists to protect.
                // BuildTimerService.HireReinforcementsVerb survives for its other callers; it is
                // this ROW that cannot seat it.
                FinishVerbText = "SPEED UP",
                // RELEASE BLOCKER GATE (2026-08-07): no ad SDK is wired, so the ad affordance is
                // ABSENT on every row of every channel until FeatureFlags.RewardedAdSkip's two
                // prerequisites land (real SDK + WO-912 server-side ad-window validation). The
                // service refuses too; this keeps the VM honest so the view builds no dead control.
                AdAvailable = DeNelle.Core.FeatureFlags.RewardedAdSkip &&
                              svc.CanWatchAdToSkip(channel, job.StructureId),
                CanCancel = true,
                CanBumpUp = queued && pendingIndex > 0,
                // WO-1479 - the WHOLE line, composed by ObsidianQueueVM.QuoteRefund, prefix and
                // zero-case wording included. It used to be the bare basket ("120 wood, 40 iron" /
                // "nothing") and the DRAWER decided what to do with it: it prefixed "Refund: " and
                // string-matched "nothing" to suppress the line entirely. So a job that would refund
                // NOTHING said nothing at all, and the player read a bare CANCEL with no idea the
                // press was free of charge or cost them everything. Deciding that is a model job
                // (WO-1512); the View now renders this string when it is non-empty and decides
                // nothing. The FIGURE is untouched - it is still the job's own v37 paid basket.
                RefundText = ObsidianQueueVM.QuoteRefund(job.Paid).Line,
                Progress01 = ProgressOf(job, queued, rem),
                IconRole = card.IconRole,
                IconKey = card.IconKey,
                Verb = card.Verb,
                TargetTier = card.TargetTier,
            };
        }

        /// <summary>
        /// WO-898 item 1. Elapsed fraction 0..1 for a RUNNING job; 0 for a queued one (it has not
        /// started - StartMs &lt;= 0 by the engine's contract, and RemainingSeconds deliberately
        /// reports the FULL duration for such a job, so deriving progress from `rem` alone would
        /// wrongly read as 0% forever on a job that is genuinely half done).
        /// </summary>
        private static float ProgressOf(BuildJobData job, bool queued, double remainingSec)
        {
            if (queued || job.StartMs <= 0d) return 0f;
            if (job.DurationMs <= 0d) return -1f;   // unknown duration: draw no bar rather than a lie

            double totalSec = job.DurationMs / 1000d;
            double elapsed = totalSec - remainingSec;
            return Mathf.Clamp01((float)(elapsed / totalSec));
        }

        /// <summary>
        /// Grouping key for the Q12 collapse. Mirrors the publish-time rule: only TRAINING jobs
        /// stack (their ids are "train:&lt;troop&gt;:&lt;guid&gt;"), so two different buildings never
        /// merge into one card.
        /// </summary>
        private static string StackKeyOf(BuildJobData job)
        {
            string id = job.StructureId ?? "";
            if (job.JobKind != JobKind.TrainTroop) return id;
            var parts = id.Split(':');
            return parts.Length >= 2 ? parts[0] + ":" + parts[1] : id;
        }

        // ── Extra-slot offer + repair fold ───────────────────────────────────

        private void BuildSlotOffer(ChannelId channel)
        {
            // WO-1253: Manage sells a PERMANENT BUILDER in the store, not a crystal extra slot.
            // Crystal extra-queue DEPTH is KEEP BOTH and still lives on the upgrade-queue-full
            // surface and ObsidianQueueHud. Channel is the visible tab's line; the SKU is always
            // the Builder crew.
            SlotPrice = 0;
            BuilderUpsellVisible = false;
            BuilderUpsellButtonText = "";
            var ownedIds = GameStateService.Instance != null
                ? GameStateService.Instance.State?.OwnedItemIds
                : null;
            bool owned = PackCatalog.OwnsPermanentBuilder(ownedIds);
            if (owned)
            {
                SlotOfferText = BuyBuilderOwnedLabelCopy;
                FlowTrace.Step("Manage", "builder upsell shown=false reason=owned");
                return;
            }

            var svc = BuildTimerService.Instance;
            int busy = svc != null ? svc.ActiveJobsOf(ChannelId.Builder).Count : 0;
            int slots = svc != null ? svc.SlotCount(ChannelId.Builder) : 0;
            BuilderUpsellVisible = svc != null && slots > 0 && busy >= slots;
            if (BuilderUpsellVisible)
            {
                var pack = PackCatalog.Find(PackCatalog.PermanentBuilderSku);
                // ⭐ OWNER RULING 2026-09-10 (WO-1412 item 2) - THIS LABEL IS USD ONLY, AND THAT IS
                // THE ANSWER, NOT A LIMITATION AWAITING A BETTER ONE. The ticket originally spec'd
                // "BUY BUILDER - 511 SKR (~$9.99)"; the owner ruled the busy-only label renders the
                // USD price the Village assembly can already read (PackDef.UsdReference, the
                // AUTHORED usd anchor, Assets/_Modules/Commerce/PackCatalog.cs:324), and the token
                // amount is shown ONLY where the Wallet assembly already renders it. No Core DTO
                // was sanctioned to carry it here.
                //
                // DO NOT "improve" this into a token price. The two ways to reach one are both
                // wrong from here:
                //   * pack.Pricing.Skr compiles today (PackPricing.Skr is public, Commerce
                //     assembly) and is the trap - SolanaPackPricing.cs:62-64 (WO-1158) calls that
                //     authored figure "a stale hand-typed figure ... nobody will honour".
                //   * the HONEST amount is server-quoted (PurchaseQuoteService.SkrAmountFor) and
                //     lives in DeNelle.Wallet, which DeNelle.Village.asmdef does NOT reference
                //     (:4-28) and MUST NOT - GooglePlayPackagingGate.cs:203-204 fails the build if
                //     that reference is ever added, because a Play artifact excludes Wallet whole.
                // Pinned by StoreReturnToManageRegression case F.
                string price = pack != null ? pack.UsdReference : "Price unavailable";
                BuilderUpsellButtonText = BuyBuilderButtonCopy + " - " + price;
                SlotOfferText = BuyBuilderLabelCopy;
            }
            else
            {
                int free = Mathf.Max(0, slots - busy);
                // ⭐ WO-1488 - THE VERB IS THIS CHANNEL'S, not TRAIN on every line. The literal
                // "tap TRAIN" was painted under the RESEARCH tab on the owner's device
                // (Logs/device/screens/owner-screen-20260907-010257.png), pointing her at a door
                // that cannot start a research job. ONE table: QueueChannelVerb, shared with
                // QueueEmptyText, so the two sentences in the same empty well cannot disagree.
                string verb = QueueChannelVerb(channel);
                SlotOfferText = free + (free == 1 ? " slot free - tap " + verb + " to fill it"
                                                  : " slots free - tap " + verb + " to fill them");
            }
            FlowTrace.Step("Manage", "builder upsell shown=" + BuilderUpsellVisible + " busy=" + busy + "/" + slots +
                " price='" + (BuilderUpsellVisible ? BuilderUpsellButtonText : "<hidden>") + "'");
        }

        private void BuildRepairOffer()
        {
            // Ruling Q2: repair stays the EXISTING instant crystal spend-and-heal. It is surfaced
            // here "if it fits" and is NEVER converted into a queued job. WallRepairController is
            // CALLED, never restructured.
            RepairOfferText = null;
            if (Tab != ManageTab.Defense) return;

            Guard.Try("Manage", "read repair offer", () =>
            {
                var repair = UnityEngine.Object.FindFirstObjectByType<WallRepairController>();
                if (repair == null)
                {
                    // NO SILENT FAILURE (CLAUDE.md section 12.2). Nothing in a non-wave scene
                    // installs a WallRepairController except HubRepairAffordance, so when that
                    // affordance does not install, THIS offer silently vanishes too and the
                    // player is left with no repair surface anywhere while fire still renders.
                    DeNelle.Core.Diagnostics.FlowTrace.Throttle("Manage", "repair-offer-no-controller", 5f,
                        "Manage repair offer SUPPRESSED - no WallRepairController in this scene. " +
                        "This is the second surface lost when HubRepairAffordance does not install.");
                    return;
                }
                var cost = repair.RepairAllCost();
                if (cost.wood <= 0 && cost.food <= 0 && cost.iron <= 0 && cost.crystals <= 0) return;
                RepairOfferText = "Repair all (instant): " + DescribeCost(cost);
            });
        }

        // ── Browse rows ──────────────────────────────────────────────────────

        private void BuildBrowseRows()
        {
            switch (Tab)
            {
                case ManageTab.Defense: BuildDefenseBrowse(); break;
                case ManageTab.Buildings: BuildBuildingsBrowse(); break;
                case ManageTab.Troops: BuildTroopsBrowse(); break;
                case ManageTab.Research: BuildResearchBrowse(); break;
            }

            // "Sorting is the feature" (WO-905 §3): affordable first, then cheapest first, so the
            // player sees what they can act on immediately without doing arithmetic.
            BrowseRows.Sort((a, b) =>
            {
                // WO-1390: a LOCKED prerequisite row (Research) always sorts after every row the
                // player can act on; within the locked group CostWeight is the unlock tier, so
                // the nearest door comes first. Non-Research tabs never set Locked.
                if (a.Locked != b.Locked) return a.Locked ? 1 : -1;
                if (a.Affordable != b.Affordable) return a.Affordable ? -1 : 1;
                // Troops has two actions on the same unit. Training is the primary reason this
                // destination exists and must not be paged behind zero-cost upgrade rows. The
                // approved hierarchy leads with TRAIN, then exposes upgrade options; keep that
                // verb order while preserving affordable-first within each action family.
                if (Tab == ManageTab.Troops)
                {
                    int ap = TroopActionPriority(a.ActionText);
                    int bp = TroopActionPriority(b.ActionText);
                    if (ap != bp) return ap.CompareTo(bp);
                }
                return a.CostWeight.CompareTo(b.CostWeight);
            });
        }

        private static int TroopActionPriority(string action)
        {
            if (string.Equals(action, "Train", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(action, "Upgrade", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        private void BuildDefenseBrowse()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                var placed = state.BaseLayout[i];
                if (string.IsNullOrEmpty(placed.itemId)) continue;

                var entry = CatalogRegistry.Get(placed.itemId);
                if (entry == null || entry.repo == null) continue;
                // The SHARED ceiling (clamped to RepoProps.MaxStructureLevel) — the same number
                // the upgrade page and BuildModeController use. Reading raw repo.maxLevel here
                // would offer a row for a rung the controller then refuses.
                int ceiling = Buildings.Progression.PlacedStructureUpgradeService.MaxLevelFor(entry);
                if (ceiling <= 1) continue;                              // nothing to upgrade to

                int level = Mathf.Max(1, placed.level);
                if (level >= ceiling) continue;                          // already maxed
                string dedupe = placed.itemId + "#" + level;
                if (!seen.Add(dedupe)) continue;                         // one row per id+level

                var cost = BuildModeController.UpgradeCostFor(entry, level);
                // THE JOB KEY, NOT THE BARE ID (defect fixed 2026-08-16). This CTA used to pass
                // placed.itemId, which UpgradeFamilyResolver classifies as None -> the panel's
                // BuildUnknown set MaxTier = 0 and rendered "has reached tier 0 of 0 - there is
                // nothing left to upgrade here" for a tower standing at level 1 of 3. Manage told
                // the player a tower was maxed. The '@' in the key is what makes the resolver
                // answer PlacedStructure and the page show the real ladder.
                //
                // ONE ROW PER id+level (the dedupe above) means this key names the FIRST placed
                // instance at that level — the row says "Stone Wall -> L2" and lands on a Stone
                // Wall that is at L1. Deliberate: keying rows per instance would emit one row per
                // wall segment. The trace names which instance was chosen.
                string jobKey = Buildings.Progression.PlacedUpgradeKey.Compose(
                    placed.itemId, placed.cellX, placed.cellZ);
                FlowTrace.Step("Manage", "defense row '" + placed.itemId + "' L" + level
                    + "/" + ceiling + " -> inline placed key '" + jobKey + "'");
                // ⛔ WO-1405 — THE DEVELOPER COORDINATE IS RETIRED. This line used to concatenate
                // the word for a cell with placed.cellX and placed.cellZ, so the row said
                // "Arcane Spire - grid 5, 16 - L1 -> L2". The retired literal is deliberately NOT
                // quoted here: ManageRowBenefitRegression bans it in this file, and a tombstone that
                // spells it out keeps a source-text pin green on a tree that no longer does the
                // thing the pin is about. A cell index is an
                // internal address; on a player screen it reads as "this screen was built for
                // someone else" (owner ruling WO-1405 section 2 #5, written to the default NO).
                // The cell is still the row's IDENTITY — it is composed into jobKey above, which is
                // the seam that makes the CTA land on THIS instance — it is simply never SPOKEN.
                string location = CompassSideOf(placed.cellX, placed.cellZ);
                AddBrowseRow(NameOf(entry, placed.itemId) + " - " + location + " - L" + level + " -> L" + (level + 1), cost, "Upgrade",
                             () => UpgradePlaced(jobKey));
            }
        }

        /// <summary>
        /// WO-1405 — WHERE a placement stands, said in WORDS: "north side" / "east side" /
        /// "town center". Never a coordinate (owner ruling section 2 #5, written to the default NO).
        /// (US spelling: there is NO player-facing precedent for this word anywhere in the corpus -
        /// measured - and the only in-repo spelling of it is US ("Command Center", RemoteTunables).
        /// Flagged for the owner rather than decided silently.)
        ///
        /// ⚠ THE AXES ARE READ OFF <c>PlacementGrid</c>, NOT ASSUMED. +Z is north and +X is east
        /// because <c>PlacementGrid.CellToWorld</c> maps <c>cell.y</c> to world Z and the grid grows
        /// NORTH ONLY as gridHeight increases (PlacementGrid.Awake, owner 2026-07-16). A cell index
        /// alone cannot answer this: the same cell number means a different side on a grid with a
        /// different origin, which is exactly why the coordinate was meaningless on screen.
        ///
        /// HEADLESS-PURE: there is no scene in a regression run, so a missing/uninitialised grid
        /// falls back to the SHIPPED defaults (cellSize 3 m, south edge fixed at -45, X centred) —
        /// the same numbers PlacementGrid seeds in Awake. It never returns "" and never throws, so a
        /// row can never silently lose its location clause.
        /// </summary>
        private static string CompassSideOf(int cellX, int cellZ)
        {
            // PlacementGrid's shipped configuration, mirrored (cellSize 3, gridWidth 30 -> X
            // centred at -45, and the SouthEdgeZ constant its Awake anchors the grid to).
            float cellSize = 3f;
            float originX = -45f;
            float originZ = -45f;

            var grid = PlacementGrid.Instance;
            if (grid != null && grid.cellSize > 0f)
            {
                cellSize = grid.cellSize;
                // Awake seeds origin from gridWidth + SouthEdgeZ. Vector3.zero is the
                // NOT-YET-INITIALISED sentinel Awake itself tests for, so treat it the same way
                // rather than reading it as a real origin (that would put the whole grid north-east
                // of the Heart and call every placement "north").
                if (grid.origin != Vector3.zero)
                {
                    originX = grid.origin.x;
                    originZ = grid.origin.z;
                }
                else
                {
                    originX = -grid.gridWidth * cellSize * 0.5f;
                }
            }

            // The Heart of Elarion stands at world (0,0,0) — the scene centre this whole town is
            // laid out around — so the placement's world XZ IS its offset from the Heart.
            float east = originX + (cellX + 0.5f) * cellSize;
            float north = originZ + (cellZ + 0.5f) * cellSize;
            float ax = Mathf.Abs(east);
            float az = Mathf.Abs(north);

            // Within one cell of the Heart on BOTH axes there is no honest side to name.
            if (ax < cellSize && az < cellSize) return "town center";
            if (az >= ax) return north >= 0f ? "north side" : "south side";
            return east >= 0f ? "east side" : "west side";
        }

        /// <summary>How many buildings of ONE ladder id stand in this town, and the placed catalog
        /// ids that folded into it (kept for the diagnostics — a warning must be able to name the
        /// id the player actually placed, not just the id its ladder is spelled with).</summary>
        private sealed class PlacedTally
        {
            /// <summary>Live BaseLayout instances resolving to this ladder id.</summary>
            public int Count;
            /// <summary>Distinct placed catalog ids that resolved here (e.g. "collector_farm").</summary>
            public readonly List<string> SourceIds = new List<string>();
        }

        /// <summary>
        /// The LIVE placements of THIS town, counted per UPGRADE-LADDER id.
        ///
        /// ⚠ KEYED THROUGH <see cref="CatalogRegistry.ResolveUpgradeId"/>, THE SHIPPED RESOLVER —
        /// not a mapping table written here (owner 2026-08-08 forbade INVENTING a translation layer
        /// that would drift; this is the opposite move, REUSE). A resource COLLECTOR is placed under
        /// its catalog id ("collector_farm") while its ladder is authored under the bare
        /// <c>repo.collectorBuildingId</c> ("farm") — the mapping is AUTHORED IN structures-catalog.json,
        /// not hardcoded, and this is the same resolver <c>BuildingUpgradeVM</c> (:139) and
        /// <c>BuildModeController.UpgradeSelected</c> (:2275) already call. Using anything else here
        /// would make Manage and the in-world upgrade panel disagree about the same building.
        ///
        /// It also settles the duplicate-row question: "lumbermill" (catalog "Sawmill") and
        /// "collector_lumbermill" BOTH resolve to ladder "lumbermill", and the tier is stored per
        /// LADDER id (GameState.BuildingTiers["lumbermill"]), so they are one upgradable kind and
        /// must fold into ONE row. Counting on the raw itemId would emit the same row twice.
        ///
        /// ⚠ READS <see cref="GameState.BaseLayout"/> ON PURPOSE — and must keep doing so (owner
        /// ruling 2026-08-08). BaseLayout is the only per-TOWN answer to "do I own one of these
        /// right now"; it drops the record when a building is sold or destroyed, which IS the
        /// owner's "if destroyed = 0" rule, already implemented. The two nearby sets are the wrong
        /// question and would BOTH be wrong in a second town, which is where this breaks visibly:
        ///
        ///   * <c>GameState.FreeBuildsUsed</c> (v32) — ACCOUNT-scoped and monotonic: it burns at
        ///     the committed placement and never resets. It answers "have you had your free one",
        ///     not "do you own one HERE". On a prefab/Default Town the buildings are already
        ///     placed, so it is largely spent and would hide things you own while offering things
        ///     you do not.
        ///   * <c>GameState.EverBuiltStructureIds</c> (v36) — MONOTONIC BY DESIGN: selling never
        ///     removes an id, because the WO-819 sell -> baked-twin-resurface contract depends on
        ///     that. A destroyed building would keep offering upgrades forever.
        ///
        /// Both are correct for their own jobs. Here, "their other town has everything, this town
        /// doesn't" is the deciding case: the player is looking at the town they are standing in.
        /// When multi-town lands and BaseLayout shards per base, this counting site is correct for
        /// free; anything reading the account-scoped sets would have to be unpicked.
        /// </summary>
        private static Dictionary<string, PlacedTally> CountPlacedThisTown()
        {
            var counts = new Dictionary<string, PlacedTally>(StringComparer.OrdinalIgnoreCase);
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null) return counts;

            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                string placedId = state.BaseLayout[i].itemId;
                if (string.IsNullOrEmpty(placedId)) continue;

                // Pass-through for every non-collector id (and for any id the registry has not
                // loaded), so this is a no-op everywhere except the three collectors.
                string ladderId = CatalogRegistry.ResolveUpgradeId(placedId);
                if (string.IsNullOrEmpty(ladderId)) ladderId = placedId;

                if (!counts.TryGetValue(ladderId, out var tally))
                {
                    tally = new PlacedTally();
                    counts[ladderId] = tally;
                }
                tally.Count++;
                if (!tally.SourceIds.Contains(placedId)) tally.SourceIds.Add(placedId);
            }
            return counts;
        }

        /// <summary>
        /// The BUILDINGS tab — every building STANDING IN THIS TOWN that has a next tier authored,
        /// offered at that tier's real price.
        ///
        /// ⚠ TWO DIFFERENT QUESTIONS, DELIBERATELY KEPT APART (owner ruling 2026-08-08, felt-test
        /// "no building upgrades are on the manage button anywhere"):
        ///     WHETHER a row appears -> do you OWN one right now?  -> COUNT the placements.
        ///     WHICH   row appears   -> what tier are you on?      -> ModifierService.TierOf.
        /// Conflating them is the defect this replaces. The old code asked TierOf for BOTH and
        /// skipped on <c>tier &lt; 1</c> under the comment "not built / locked" — but TierOf reads
        /// GameState.BuildingTiers, which only ever contains ids that have been UPGRADED, so the
        /// filter really asked "have you already upgraded this?" and the tab was EMPTY for exactly
        /// the player the browser exists for: the one who has never bought a tier.
        ///
        /// ⛔ DO NOT "fix" a future variant of this by writing tier=1 at placement. Tier 1 is a PAID
        /// upgrade (barracks T1 = 900 wood / 750 food / 150 crystals) and it grants
        /// <c>structureHpBonusPct 0.20</c> through ModifierService.StructureHpMultFor (which returns
        /// 1f below tier 1 and 1.2 at tier 1), so seeding it would gift every newly placed building
        /// a free upgrade. The ladder is 1-based for UPGRADES, not for existence: tier 0 = placed.
        ///
        /// The lookup is a STRAIGHT read of building-tiers.json under the id the SHIPPED resolver
        /// gives (<see cref="CountPlacedThisTown"/> keys on <c>CatalogRegistry.ResolveUpgradeId</c>).
        /// No mapping table is written here — the owner's 2026-08-08 objection was to INVENTING a
        /// translation layer that would drift, and inventing a second resolver beside the game's own
        /// is exactly that; reusing hers is not. A resolved id with nothing authored under it is a
        /// CONTENT GAP, and <see cref="WarnNoLadder"/> announces it as the to-do list.
        /// </summary>
        private void BuildBuildingsBrowse()
        {
            var placed = CountPlacedThisTown();
            if (placed.Count == 0)
            {
                // NO SILENT EMPTY LIST (§12): an empty tab must be diagnosable from a log line
                // rather than a felt-test. This is the "nothing placed / no town state" case.
                FlowTrace.Step("Manage", "buildings browse (this town): 0 placements in BaseLayout -> no rows.");
                return;
            }

            int rows = 0, maxed = 0, noLadder = 0, onDefenseTab = 0;
            foreach (var kv in placed)
            {
                string ladderId = kv.Key;                                // already resolved
                var tally = kv.Value;

                // WHICH row: the tier ABOVE the one you own. A placed, never-upgraded building is
                // tier 0, so it offers tier 1 at tier 1's real price — nothing is granted here.
                var next = BuildingTierCatalog.TierOf(ladderId, ModifierService.TierOf(ladderId) + 1);
                if (next == null)
                {
                    if (BuildingTierCatalog.IsUpgradable(ladderId)) { maxed++; continue; }   // topped out

                    // Not a gap: this id runs the OTHER ladder. Towers/walls/mines/stockpiles carry
                    // a per-instance repo.maxLevel and are already browsed by BuildDefenseBrowse, so
                    // naming them in the "author some rows" warning would make that to-do list lie.
                    if (HasLevelLadder(tally)) { onDefenseTab++; continue; }

                    noLadder++;
                    WarnNoLadder(ladderId, tally);
                    continue;
                }

                var def = BuildingTierCatalog.Find(ladderId);
                string name = (def != null && !string.IsNullOrEmpty(def.DisplayName)) ? def.DisplayName : ladderId;
                var cost = BuildingTierBasket(next);
                // Upgrade against the ladder id so the inline row uses the same authoritative
                // progression identity as the detailed building-management view.
                string rowId = ladderId;                                 // captured by the CTA closure
                int targetTier = next.Tier;
                AddGoldBrowseRow(Ascii(name), cost, next.CostGold, "Upgrade",
                    () => UpgradeBuilding(rowId, targetTier));
                rows++;
            }

            FlowTrace.Step("Manage",
                "buildings browse (this town): " + placed.Count + " placed type(s) -> " + rows +
                " upgrade row(s); " + maxed + " at max tier, " + onDefenseTab +
                " on the level ladder (Defense tab), " + noLadder + " with no authored ladder.");
        }

        /// <summary>
        /// WO-1418 selected-building rail/card projection. Unlike the legacy browse rows this keeps
        /// topped-out buildings visible and carries every sentence and decision the View paints.
        /// </summary>
        private void BuildBuildingChoices()
        {
            var placed = CountPlacedThisTown();
            var queue = BuildTimerService.Instance;
            int villageTier = Buildings.Progression.VillageTierService.Current;
            int maxed = 0, locked = 0, building = 0;

            foreach (var kv in placed)
            {
                string id = kv.Key;
                if (!BuildingTierCatalog.IsUpgradable(id)) continue;

                int level = ModifierService.TierOf(id);
                int maxLevel = BuildingTierCatalog.MaxTier(id);
                var current = BuildingTierCatalog.TierOf(id, level);
                var next = BuildingTierCatalog.TierOf(id, level + 1);
                bool isMax = next == null;
                bool isBuilding = !isMax && HasBuilderJob(queue, id);
                bool isLocked = !isMax && next.RequiresVillageTier > villageTier;
                var def = BuildingTierCatalog.Find(id);
                var entry = kv.Value.SourceIds.Count > 0 ? CatalogRegistry.Get(kv.Value.SourceIds[0]) : null;
                string description = FirstClause(current != null ? current.Effect : null);
                if (string.IsNullOrWhiteSpace(description) && entry != null)
                    description = FirstClause(StructureCardVM.DescriptionFor(entry));   // DeNelle.Village.StructureCardVM (BuildMode is a folder, not a namespace)
                if (string.IsNullOrWhiteSpace(description)) description = "A village structure.";

                var cost = BuildingTierBasket(next);
                bool ready = !isMax && !isLocked && !isBuilding && CanAfford(cost) && GoldBalance() >= next.CostGold;
                string stateWord = isBuilding ? "Building" : isLocked ? "Locked" : isMax ? "Max" : "Upgradable";
                int nextTier = isMax ? 0 : next.Tier;
                string rowId = id;
                int targetTier = nextTier;

                string time = null;
                if (!isMax && queue != null && queue.Config != null)
                {
                    int seconds = Mathf.CeilToInt(queue.Config.DurationSecondsForTier(
                        Mathf.Max(0, targetTier - 2), BuildJobKind.Upgrade));
                    time = QueueRailView.FormatTime(seconds);
                }

                var choice = new BuildingChoiceVM
                {
                    Id = id,
                    CatalogEntryId = entry != null ? entry.id : "",
                    Name = Ascii(def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : id),
                    Level = level,
                    MaxLevel = maxLevel,
                    // ⛔ RE-POINTED 2026-09-06: this used the mixed-root portrait resolver, which emits
                    // "Portraits/<ladder>[-N]" and misses all 20 tier keys. Why, and why the DEFENCE
                    // projection still uses that resolver: ManageArt.BuildingPortraitKey's doc comment.
                    // Pinned by ManagePortraitCoverageRegression [vm-uses-building-portrait-key], which
                    // greps this method body - so do not name the old resolver here, in any comment.
                    IconKey = ManageArt.BuildingPortraitKey(id, level),
                    Locked = isLocked,
                    // WO-2003: the rail sub-line said "Level 1 . T2", where "T" was the fourth way
                    // the same gate was spelled on screen. It now names the gate the player can
                    // actually go and raise. Kept terse - this is a 22-30px rail line.
                    LockText = isLocked ? "Level " + level + " . Heart " + next.RequiresVillageTier : "",
                    RequiresVillageTier = isLocked ? next.RequiresVillageTier : 0,
                    // WO-1423 - the sentence and the door WORD are authored here, in the VM, exactly
                    // as the Research card does it. The card's only gate is the HEART (isLocked IS
                    // next.RequiresVillageTier > villageTier), so the sentence can name it outright
                    // and say WHERE it is raised.
                    // ⚠ CORRECTED WO-2003 (2026-09-06): this said the place it is raised is "the
                    // upgrade page's VillageGated action band". That WAS true and was the whole
                    // problem - the door named the Heart and opened a different building's screen.
                    // ViewDetails below now opens PanelId.Heart for a locked card. The action band
                    // still exists and still works; it is no longer the ONLY control.
                    // Kept SHORT on purpose: it lands in a 28.6px band, and the Research card's own
                    // device note warns that TMP culls a short band it cannot fit. 45 chars, close to
                    // the 37 that were MEASURED to render there.
                    LockReason = isLocked
                        ? "Needs Heart Level " + next.RequiresVillageTier + " - raise it at the Heart."
                        : "",
                    LockCtaLabel = isLocked ? "UPGRADE THE HEART" : "",
                    StateWord = stateWord,
                    Description = Ascii(description),
                    UpgradeCostParts = isMax ? Array.Empty<CostPart>() : BuildingUpgradeCostParts(next),
                    UpgradeTimeText = time,
                    UpgradeReady = ready,
                    AfterUpgradeText = isMax ? "" : Ascii(FirstClause(next.Effect)),
                    NextTier = nextTier,
                    Activate = isMax ? null : (Action)(() => UpgradeBuilding(rowId, targetTier)),
                    // WO-2003: the ONE door, re-pointed by STATE, not by adding a second control.
                    // A locked card's only gate IS the Heart (isLocked is literally
                    // next.RequiresVillageTier > villageTier, above), its sentence says so and its
                    // door word is "UPGRADE THE HEART" - so the door now opens the HEART SURFACE
                    // instead of the building's own upgrade page, where the player then had to find
                    // the VillageGated action band. An unlocked card's PERKS door is unchanged.
                    ViewDetails = isLocked
                        ? (Action)(() => OpenHeartPanel(rowId))
                        : (Action)(() => OpenUpgradePanel(rowId)),
                    // WO-1422 ruling 3.5 - the owner's "keep one door, but name what's behind it".
                    // The door survives (it is still ViewDetails -> OpenUpgradePanel); only the WORD
                    // changes, and it is HIDDEN when the ladder authors no perks. Measured against
                    // building-tiers.json: the Farm authors ZERO perks, so the Farm card shows one
                    // full-width CTA and no second door - that is the feature, not a gap.
                    DoorLabel = HasAuthoredPerk(def) ? "PERKS" : null,
                };
                BuildingChoices.Add(choice);
                if (isMax) maxed++;
                else if (isBuilding) building++;
                else if (isLocked) locked++;
                // WO-1405 — the BENEFIT rides the same line as the state, so "the row prices the
                // tap and never says what it buys" is a log read, not a felt-test.
                FlowTrace.Step("Manage", "building choice id=" + id + " level=" + level + "/" + maxLevel +
                    " state=" + stateWord + " next=" + nextTier + " ready=" + ready +
                    " icon='" + (choice.IconKey ?? "<fallback>") + "'" +
                    " benefit='" + choice.AfterUpgradeText + "'");
                // NEVER A SILENT BLANK (§12): the card paints "After upgrade: " + this string, so an
                // unauthored tier Effect would render an empty band with no evidence anywhere.
                if (!isMax && string.IsNullOrWhiteSpace(choice.AfterUpgradeText))
                    FlowTrace.Warn("Manage", "no benefit string for " + id +
                        " - building-tiers.json authors no Effect for tier " + nextTier + ".");
            }

            FlowTrace.Step("Manage", "building choices projected=" + BuildingChoices.Count + " max=" + maxed +
                " locked=" + locked + " building=" + building);
        }

        private static bool HasBuilderJob(BuildTimerService svc, string id)
        {
            if (svc == null || string.IsNullOrEmpty(id)) return false;
            foreach (var job in svc.ActiveJobsOf(ChannelId.Builder))
                if (BuildingJobMatches(job.StructureId, id)) return true;
            foreach (var job in svc.PendingJobsOf(ChannelId.Builder))
                if (BuildingJobMatches(job.StructureId, id)) return true;
            return false;
        }

        private static bool BuildingJobMatches(string jobId, string buildingId) =>
            string.Equals(jobId, buildingId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(jobId) && jobId.StartsWith(buildingId + ":", StringComparison.OrdinalIgnoreCase));

        // =====================================================================
        //  WO-1422 — DEFENSE and RESEARCH card projections (the WO-1418 shape)
        // =====================================================================

        /// <summary>
        /// One type's placements, folded. <see cref="LowestLevel"/> and the cell beside it name the
        /// FIRST instance standing at that level, in BaseLayout order — which is the instance the
        /// card's CTA upgrades (ruling 3.1).
        /// </summary>
        private sealed class DefenseTally
        {
            public string ItemId;
            public int Count;
            public int LowestLevel = int.MaxValue;
            public int CellX;
            public int CellZ;
        }

        /// <summary>
        /// WO-1422 ruling 3.1 — the DEFENSE rail/card projection: ONE choice per placed upgradable
        /// TYPE, never per instance.
        ///
        /// ⚠ THIS IS A PRESENTATION CHANGE, NOT A BEHAVIOUR CHANGE. <see cref="BuildDefenseBrowse"/>
        /// keys its rows on <c>itemId + "#" + level</c> and its CTA already composes
        /// <c>PlacedUpgradeKey.Compose</c> against ONE grid cell — the FIRST placed instance at that
        /// level. This projection targets the same instance; it only stops emitting a second row when
        /// a second copy of the same tower stands at a different level, and it keeps MAXED types
        /// visible (the browse skipped them) so the card can say "Max" instead of the type vanishing.
        ///
        /// ⛔ Do NOT key this per instance. <c>wall_wood</c> is upgradable and a town has many
        /// segments, so a per-instance rail would be unbounded — the exact trap the browse comment
        /// has warned about since 2026-08-16.
        /// </summary>
        private void BuildDefenseChoices()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null)
            {
                // NO SILENT EMPTY LIST (CLAUDE.md §12): an empty Defense tab is read off a log line,
                // never guessed at from a felt-test.
                FlowTrace.Step("Manage", "defense choices: no game state / no BaseLayout -> 0 choices.");
                return;
            }

            var tallies = new Dictionary<string, DefenseTally>(StringComparer.OrdinalIgnoreCase);
            int skippedNoLadder = 0;
            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                var placed = state.BaseLayout[i];
                if (string.IsNullOrEmpty(placed.itemId)) continue;

                // Guard.Try, not a bare read: ONE malformed catalog row logs and is SKIPPED rather
                // than throwing out of Rebuild's outer Guard, which would also cost the Troops army
                // summary and every producer queued behind this one (§12 step 2).
                Guard.Try("Manage", "defense tally '" + placed.itemId + "'", () =>
                {
                    var entry = CatalogRegistry.Get(placed.itemId);
                    if (entry == null || entry.repo == null) return;
                    // The SHARED clamped ceiling — the same number BuildModeController and the
                    // upgrade page use. Reading raw repo.maxLevel here would offer a rung the
                    // controller then refuses.
                    if (Buildings.Progression.PlacedStructureUpgradeService.MaxLevelFor(entry) <= 1)
                    {
                        skippedNoLadder++;
                        return;                                  // no ladder: not a Defense card
                    }

                    int level = Mathf.Max(1, placed.level);
                    if (!tallies.TryGetValue(placed.itemId, out var tally))
                    {
                        tally = new DefenseTally { ItemId = placed.itemId };
                        tallies[placed.itemId] = tally;
                    }
                    tally.Count++;
                    // STRICTLY LESS-THAN keeps the FIRST instance at the lowest level: a later
                    // instance at the same level never displaces the one already recorded.
                    if (level < tally.LowestLevel)
                    {
                        tally.LowestLevel = level;
                        tally.CellX = placed.cellX;
                        tally.CellZ = placed.cellZ;
                    }
                });
            }

            var queue = BuildTimerService.Instance;
            int maxed = 0, building = 0, ready = 0;
            foreach (var kv in tallies)
            {
                var tally = kv.Value;
                Guard.Try("Manage", "defense choice '" + tally.ItemId + "'", () =>
                {
                    var entry = CatalogRegistry.Get(tally.ItemId);
                    if (entry == null) return;

                    int ceiling = Buildings.Progression.PlacedStructureUpgradeService.MaxLevelFor(entry);
                    int level = Mathf.Clamp(tally.LowestLevel, 1, ceiling);
                    bool isMax = level >= ceiling;
                    int nextLevel = isMax ? 0 : level + 1;

                    string jobKey = Buildings.Progression.PlacedUpgradeKey.Compose(
                        tally.ItemId, tally.CellX, tally.CellZ);

                    // BUSY IS PER KEY, NOT PER TYPE. PlacedStructureUpgradeService's own busy gate
                    // asks IsBuilding(jobKey), so asking "is any instance of this type upgrading"
                    // would grey out a CTA the service would have accepted — a behaviour change
                    // ruling 3.1 forbids.
                    bool isBuilding = HasPlacedBuilderJob(queue, jobKey);

                    var cost = BuildModeController.UpgradeCostFor(entry, level);
                    bool affordable = !isMax && CanAfford(cost);
                    string stateWord = isBuilding ? "Building" : isMax ? "Max" : "Upgradable";

                    string time = null;
                    if (!isMax && queue != null && queue.Config != null)
                    {
                        // The SAME derivation BuildTimerService.StartUpgrade applies
                        // (tier index = targetLevel - 2, floored at 0). Never a hardcoded number,
                        // and NULL rather than a guess when there is no config to ask.
                        int seconds = Mathf.CeilToInt(queue.Config.DurationSecondsForTier(
                            Mathf.Max(0, nextLevel - 2), BuildJobKind.Upgrade));
                        time = QueueRailView.FormatTime(seconds);
                    }

                    string name = NameOf(entry, tally.ItemId);
                    string description = FirstClause(StructureCardVM.DescriptionFor(entry));
                    if (string.IsNullOrWhiteSpace(description)) description = "A village structure.";

                    // Placed structures author NO per-level effect sentence (verified: RepoProps
                    // carries cost/maxLevel, not a benefit line). This mirrors the wording the
                    // upgrade page itself composes for a placed structure
                    // (BuildingUpgradeVM.ComposeNextPlaced) rather than inventing a second one, and
                    // deliberately claims no stat number the tower ladder has not been asked for.
                    string after = isMax
                        ? ""
                        : "Raises " + name + " to Level " + nextLevel + " of " + ceiling + ".";

                    string capturedKey = jobKey;
                    var choice = new DefenseChoiceVM
                    {
                        Id = tally.ItemId,
                        CatalogEntryId = !string.IsNullOrEmpty(entry.id) ? entry.id : tally.ItemId,
                        Name = name,
                        // ⛔ ONE PRODUCER OF THE PORTRAIT KEY, AND IT IS ManageArt.
                        //
                        // ⚠ THE COMMENT THAT STOOD HERE WAS THE SECOND PRODUCER, AND IT WAS THE
                        // STALE COPY. It read "the DISPLAY-NAME slug, not the itemId ... passing the
                        // itemId would emit Portraits/tower-ground-archer-2, which exists nowhere",
                        // and composed `ResolveBuildingPortraitKey(entry, PortraitSlug(displayName),
                        // level)` -> "Portraits/<display-name-slug>[-N]" in the MIXED ROOT folder.
                        // MEASURED in Builds/ui-capture/ManageFlow_BUILD_gridtop_2670x1200.png:
                        // Wooden Palisade and Crystal Mine painted as blank tan ovals - the warm-tan
                        // PLACEHOLDER DISC ManageArt.LoadSprite's own note describes (:177-186) -
                        // because cap-manage-wave3.log traced this line asking for
                        // 'Portraits/wooden-palisade' and 'Portraits/crystal-mine-2', which exist
                        // nowhere. The same run shows 'Portraits/lumberyard-3', 'Portraits/foundry-2',
                        // 'Portraits/stoneyard' and 'Portraits/healing-caravan' missing too.
                        //
                        // BuildBuildingChoices was re-pointed to ManageArt.BuildingPortraitKey
                        // already, and ManagePortraitCoverageRegression's header records why (its
                        // [building-tier-portrait] case failed on TWENTY keys against the root
                        // folder, and "(none)" were missing under Portraits/Buildings/). This line
                        // was the last surviving slug producer. ⛔ Two spellings of one filename is
                        // the duplicated-state failure CLAUDE.md 2/5/16 keeps paying for - the ID is
                        // load-bearing (WO-1567 section 7), so the id-keyed seam wins and the slug
                        // composition is deleted rather than "kept in sync".
                        //
                        // The tier ladder is UNCHANGED: BuildingPortraitKey appends "-<level>" for
                        // level >= 2 and leaves level 1 unsuffixed (ManageArt.cs:158-162), which is
                        // exactly what forge-4 and barracks-3 already resolve through.
                        PortraitKey = ManageArt.BuildingPortraitKey(
                            !string.IsNullOrEmpty(entry.id) ? entry.id : tally.ItemId, level),
                        Level = level,
                        MaxLevel = ceiling,
                        PlacedCount = tally.Count,
                        PlacedText = tally.Count == 1
                            ? "1 placed . L" + level
                            : tally.Count + " placed . lowest L" + level,
                        StateWord = stateWord,
                        Description = Ascii(description),
                        UpgradeCostParts = isMax ? Array.Empty<CostPart>() : PlacedUpgradeCostParts(cost),
                        UpgradeTimeText = time,
                        UpgradeReady = affordable && !isBuilding && !isMax,
                        AfterUpgradeText = Ascii(after),
                        NextLevel = nextLevel,
                        JobKey = jobKey,
                        DoorLabel = null,          // ruling 3.5: Defense has no second door
                        Activate = isMax ? null : (Action)(() => UpgradePlaced(capturedKey)),
                    };
                    DefenseChoices.Add(choice);

                    if (isMax) maxed++;
                    else if (isBuilding) building++;
                    else if (choice.UpgradeReady) ready++;

                    // WO-1405 — the row's BENEFIT and its LOCATION are traced, so "the row never
                    // says what the tap buys" is answered off a log line instead of a felt-test.
                    FlowTrace.Step("Manage", "defense choice id=" + choice.Id + " placed=" + choice.PlacedCount +
                        " lowest=L" + level + "/" + ceiling + " state=" + stateWord + " ready=" + choice.UpgradeReady +
                        " key='" + jobKey + "' portrait='" + (choice.PortraitKey ?? "<fallback>") + "'" +
                        " benefit='" + choice.AfterUpgradeText + "' location='" +
                        CompassSideOf(tally.CellX, tally.CellZ) + "'");
                    // NEVER A SILENT BLANK (CLAUDE.md §12): a row that can still be upgraded and
                    // carries no benefit sentence is the defect this ticket exists to close, so it
                    // announces itself rather than painting an empty band.
                    // ⚠ HONEST ABOUT ITSELF: today `after` is composed unconditionally above, so
                    // this branch is UNREACHABLE. It is a NET for the day the sentence is re-sourced
                    // from an authored field (the placed ladders author no per-level Effect yet), not
                    // a live detector - do not read its silence as evidence of anything.
                    if (!isMax && string.IsNullOrWhiteSpace(choice.AfterUpgradeText))
                        FlowTrace.Warn("Manage", "no benefit string for " + choice.Id +
                            " - the Defense card would price the tap without saying what it buys.");
                });
            }

            FlowTrace.Step("Manage", "defense choices projected=" + DefenseChoices.Count +
                " (from " + state.BaseLayout.Count + " placement(s), " + skippedNoLadder +
                " with no level ladder); max=" + maxed + " building=" + building + " ready=" + ready);
        }

        /// <summary>
        /// True when a Builder job — running or queued — is addressed to EXACTLY this placed-upgrade
        /// key. Deliberately an exact match, not a type prefix: the busy gate the service applies is
        /// <c>IsBuilding(jobKey)</c>, so a different instance of the same type being upgraded must
        /// NOT grey out this card's CTA.
        /// </summary>
        private static bool HasPlacedBuilderJob(BuildTimerService svc, string jobKey)
        {
            if (svc == null || string.IsNullOrEmpty(jobKey)) return false;
            foreach (var job in svc.ActiveJobsOf(ChannelId.Builder))
                if (string.Equals(job.StructureId, jobKey, StringComparison.Ordinal)) return true;
            foreach (var job in svc.PendingJobsOf(ChannelId.Builder))
                if (string.Equals(job.StructureId, jobKey, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The four-material cost parts for a placed-structure upgrade. Uses the SAME
        /// concept ids the rest of the screen uses — note "stone" carries <c>cost.food</c>, which is
        /// the shipped icon key (<see cref="DescribeCost"/> and
        /// <see cref="BuildingUpgradeCostParts"/> both do it); it looks like a typo and is not.</summary>
        private static IReadOnlyList<CostPart> PlacedUpgradeCostParts(CoreCost cost)
            => CostFormat.Parts(new[]
            {
                ("wood", "Wood", cost.wood), ("stone", "Stone", cost.food),
                ("iron", "Iron", cost.iron), ("crystal", "Crystals", cost.crystals)
            });

        /// <summary>
        /// WO-1422 rulings 3.6 + 3.7 — the RESEARCH rail/card projection: ONE choice per PERK of
        /// every ladder building standing in this town, in ALL FOUR states.
        ///
        /// ⚠ IT SHOWS THE TWO STATES THE BROWSE LIST HID. <see cref="BuildResearchBrowse"/> emits no
        /// row for an OWNED perk and no row for one already IN PROGRESS, so the tab could never
        /// answer "what have I already bought" or "what is running right now" — the player had to
        /// infer both from the queue. This is the same deliberate delta WO-1418 made when it stopped
        /// hiding maxed buildings.
        ///
        /// Research has NO LEVEL, so the card's level slot carries <see cref="ResearchChoiceVM.TierText"/>
        /// ("TIER 2") instead. Painting "LEVEL 0" would be a lie about a ladder that does not exist.
        /// </summary>
        private void BuildResearchChoices()
        {
            var all = BuildingTierCatalog.All;
            if (all == null)
            {
                FlowTrace.Warn("Manage", "research choices: BuildingTierCatalog.All is null - the tab can offer nothing.");
                return;
            }

            // Ownership is the LIVE per-town placement count, keyed on the RESOLVED ladder id, which
            // is the same id space building-tiers.json uses. (ModifierService.TierOf would answer a
            // DIFFERENT question — "have you already upgraded this" — and that conflation is exactly
            // the defect that made this tab empty for a player who owned a barracks at tier 0.)
            var placedThisTown = CountPlacedThisTown();
            int gold = GoldBalance();
            int owned = 0, researched = 0, researching = 0, available = 0, locked = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Id) || def.Tiers == null) continue;
                if (!placedThisTown.ContainsKey(def.Id)) continue;       // you do not own one HERE

                owned++;
                string buildingName = Ascii(string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName);
                string upperName = buildingName.ToUpperInvariant();

                for (int t = 0; t < def.Tiers.Count; t++)
                {
                    var tierDef = def.Tiers[t];
                    if (tierDef?.Perks == null) continue;

                    for (int p = 0; p < tierDef.Perks.Count; p++)
                    {
                        var perk = tierDef.Perks[p];
                        if (perk == null || string.IsNullOrEmpty(perk.Id)) continue;

                        // Captured by the CTA closures — never the loop variables.
                        string bId = def.Id;
                        string pId = perk.Id;
                        var perkDef = perk;

                        Guard.Try("Manage", "research choice '" + bId + ":" + pId + "'", () =>
                        {
                            bool isOwned = Buildings.Progression.BuildingPerkService.IsOwned(bId, pId);
                            bool inProgress = !isOwned &&
                                Buildings.Progression.BuildingPerkService.IsResearching(bId, pId);
                            string reason = "";
                            bool can = false;
                            if (!isOwned && !inProgress)
                            {
                                // ⚠ ASKED ONLY AFTER the owned/in-progress tests, because CanResearch
                                // reports BOTH of those as refusals ("Researched." / "Research
                                // already in progress.") — asking it first would label a finished
                                // perk Locked and print its own bookkeeping at the player.
                                can = Buildings.Progression.BuildingPerkService.CanResearch(
                                    bId, pId, out string why);
                                reason = why ?? "";
                            }

                            int unlock = BuildingTierCatalog.PerkUnlockTier(bId, pId);
                            int price = Mathf.Max(0, perkDef.GoldCost);
                            float seconds = Buildings.Progression.BuildingPerkService.ResearchSeconds(bId, pId);

                            string stateWord = isOwned ? "Researched"
                                             : inProgress ? "Researching"
                                             : can ? "Available" : "Locked";
                            bool isLocked = stateWord == "Locked";

                            string cta = null;
                            Action activate = null;
                            if (stateWord == "Available")
                            {
                                cta = "RESEARCH";
                                activate = () => Research(bId, pId);
                            }
                            else if (stateWord == "Researching")
                            {
                                // A non-interactable face (ruling 3.7): the work is already on the
                                // Research line and this screen must not offer to start it twice.
                                cta = "RESEARCHING";
                            }
                            else if (isLocked)
                            {
                                // THE DOOR, not a dead button. Both gates open the SAME page through
                                // the one existing start path: the building's upgrade page, whose
                                // action band renders "Raise Village Tier N" in the VillageGated
                                // state (BuildingUpgradePanelMvvm.cs:1322-1338 -> Select(
                                // BuildingUpgradeVM.VillageTierRowId) -> VillageTierService.TryUpgrade).
                                // ⚠ CORRECTED WO-1423: this said the page's "FIRST tile" was that
                                // control. It is not - PrependVillageTierRow's tile is filtered out of
                                // BOTH render paths (they take `perk:` ids only), so no such tile is
                                // ever drawn. The action band is that page's control.
                                // ⚠ CORRECTED AGAIN, WO-2003 (2026-09-06): this block used to end
                                // "There is NO separate Heart panel." THERE IS ONE NOW - HeartPanel on
                                // PanelId.Heart - and a VILLAGE-gated row opens it, because a door must
                                // open the thing its own face names. A BUILDING-gated row still opens
                                // the building's upgrade page, which is where a building tier is raised.
                                // The face names WHICH prerequisite the player is going to.
                                bool buildingLocked = ModifierService.TierOf(bId) < unlock;
                                // WO-1423 — the village gate is the perk's OWN row's requiresVillageTier
                                // (village scale), never `unlock` (building scale).
                                bool villageLocked = !buildingLocked &&
                                    Buildings.Progression.VillageTierService.Current <
                                        BuildingTierCatalog.PerkRequiredVillageTier(bId, pId);
                                cta = villageLocked ? "UPGRADE THE HEART" : "UPGRADE " + upperName;
                                // WO-2003 - THE DOOR NOW OPENS THE THING THE FACE NAMES. Both arms
                                // are still ONE door built from the same state; only the village arm
                                // moved. It used to send the player to the BUILDING's upgrade page and
                                // rely on them finding the VillageGated action band there, which is
                                // how "UPGRADE THE HEART" led to a screen with no Heart on it.
                                // ⚠ The building arm is untouched and still calls OpenUpgradePanel(bId):
                                // a BUILDING-tier gate is genuinely raised on that page.
                                string subject = bId + ":" + pId;
                                activate = villageLocked
                                    ? (Action)(() =>
                                    {
                                        FlowTrace.Step("Manage", "research locked door '" + subject +
                                            "' (tier " + unlock + ") -> HEART surface (village gate)");
                                        OpenHeartPanel(subject);
                                    })
                                    : (Action)(() =>
                                    {
                                        FlowTrace.Step("Manage", "research locked door '" + subject +
                                            "' (tier " + unlock + ") -> BuildingUpgrade page '" + bId + "'");
                                        OpenUpgradePanel(bId);
                                    });
                            }
                            // "Researched" keeps cta == null and activate == null: there is nothing
                            // left to do to it, so the card shows no CTA at all.

                            string description = FirstClause(perkDef.Effect);
                            if (string.IsNullOrWhiteSpace(description))
                            {
                                // WO-1405 — the fallback repeats the perk's NAME, which prices the
                                // tap and says nothing about what it buys. It is kept (a card with
                                // no body line is worse) but it is NEVER SILENT (§12): the authored
                                // gap is named so it can be closed in the catalog, not in the VM.
                                FlowTrace.Warn("Manage", "no benefit string for " + bId + ":" + pId +
                                    " - building-tiers.json authors no Effect, so the card falls back to the perk name.");
                                description = Ascii(string.IsNullOrEmpty(perkDef.Name) ? pId : perkDef.Name);
                            }

                            var choice = new ResearchChoiceVM
                            {
                                BuildingId = bId,
                                PerkId = pId,
                                Name = Ascii(string.IsNullOrEmpty(perkDef.Name) ? pId : perkDef.Name),
                                BuildingName = buildingName,
                                // BuildingPerkDef.IconId documents itself as "defaults to Id".
                                IconName = !string.IsNullOrEmpty(perkDef.IconId) ? perkDef.IconId : pId,
                                UnlockTier = unlock,
                                TierText = "TIER " + unlock,
                                StateWord = stateWord,
                                Locked = isLocked,
                                // VERBATIM. Not Ascii()'d and never replaced by a generic "Locked." —
                                // the sentence CanResearch composes is the one that teaches the loop
                                // ("Upgrade the building to Tier 2 first."), and a suite asserts it
                                // matches that out-string exactly.
                                LockReason = isLocked ? reason : "",
                                Description = Ascii(description),
                                // ⚠ ResourceCost has NO gold lane (RepoProps), which is why research
                                // cost is composed as an explicit gold CostPart here rather than
                                // formatted by hand the way the retired browse row did.
                                CostParts = CostFormat.Parts(new[] { ("gold", "Gold", price) }),
                                TimeText = FormatTime(seconds),
                                Ready = stateWord == "Available" && gold >= price,
                                CtaLabel = cta,
                                DoorLabel = null,        // ruling 3.5
                                Activate = activate,
                            };
                            ResearchChoices.Add(choice);

                            if (isOwned) researched++;
                            else if (inProgress) researching++;
                            else if (can) available++;
                            else locked++;

                            FlowTrace.Step("Manage", "research choice " + bId + ":" + pId +
                                " state=" + stateWord + " tier=" + unlock + " gold=" + price +
                                " ready=" + choice.Ready + " cta='" + (cta ?? "<none>") + "'" +
                                " benefit='" + choice.Description + "'" +
                                (isLocked ? " reason='" + choice.LockReason + "'" : ""));
                        });
                    }
                }
            }

            // NO SILENT EMPTY LIST (§12): say how many ladder buildings this town owns and what that
            // produced, so an empty Research tab is read off a log instead of a felt-test.
            FlowTrace.Step("Manage", "research choices projected=" + ResearchChoices.Count +
                " from " + owned + " owned ladder building(s) of " + placedThisTown.Count +
                " placed type(s); researched=" + researched + " researching=" + researching +
                " available=" + available + " locked=" + locked);
        }

        /// <summary>"Barracks:2:0" / "lumbermill@15_7" / the dedicated Barracks
        /// upgrade key to the stable tier-catalog id.</summary>
        private static string NormalizeBuildingJobId(BuildJobData job)
        {
            string jobId = job.StructureId;
            if (string.IsNullOrWhiteSpace(jobId)) return "";
            if (job.JobKind == JobKind.BarracksUpgrade ||
                string.Equals(jobId, BarracksService.BarracksJobId, StringComparison.OrdinalIgnoreCase))
                return "barracks";
            string id = jobId.Trim();
            int suffix = id.IndexOfAny(new[] { ':', '@' });
            if (suffix >= 0) id = id.Substring(0, suffix);
            return id.Trim().ToLowerInvariant().Replace('_', '-');
        }

        /// <summary>One card line only: retain the first authored sentence including its period.</summary>
        private static string FirstClause(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string value = text.Trim();
            int period = value.IndexOf('.');
            return period >= 0 ? value.Substring(0, period + 1).Trim() : value;
        }

        private static IReadOnlyList<CostPart> BuildingUpgradeCostParts(BuildingTierDef tier)
        {
            var cost = BuildingTierBasket(tier);
            return CostFormat.Parts(new[]
            {
                ("wood", "Wood", cost.wood), ("stone", "Stone", cost.food),
                ("iron", "Iron", cost.iron), ("crystal", "Crystals", cost.crystals),
                ("gold", "Gold", tier != null ? tier.CostGold : 0)
            });
        }

        // =====================================================================
        //  ⛔ ResolveBuildingPortraitKey AND PortraitSlug WERE HERE. THEY ARE DELETED.
        // ---------------------------------------------------------------------
        //  They composed "Portraits/<display-name-slug>[-N]" against the MIXED ROOT folder - a
        //  SECOND producer of a key ManageArt.BuildingPortraitKey already owns, built from the
        //  catalog ID against Portraits/Buildings/. Two spellings of one filename is the
        //  duplicated-state failure this repo keeps paying for, and this pair was the stale copy:
        //  ManagePortraitCoverageRegression's header records that its [building-tier-portrait]
        //  case failed on TWENTY root keys while "(none)" were missing under Portraits/Buildings/,
        //  and BuildBuildingChoices was re-pointed at ManageArt then. BuildDefenseChoices was the
        //  last caller and is re-pointed now, so the pair has ZERO callers.
        //
        //  ⛔ DO NOT REINTRODUCE EITHER. The id is load-bearing (WO-1567 section 7). A missing tier
        //  must go BLANK AND LOG so the oracle catches it and the owner gets an art request -
        //  quietly serving a level-1 sheet for a level-4 building is a wrong icon, and a wrong icon
        //  is a lie the capture loop cannot see (ManageArt.cs:152-156). The oracle case
        //  [vm-uses-building-portrait-key] fires if this composition returns.
        // =====================================================================

        /// <summary>WO-1406 army/camp line, composed once in the VM for the Troops header.</summary>
        private void BuildTroopArmySummary()
        {
            int used = DeNelle.Core.HudModel.PostureSignals.ArmyFillUsed;
            int cap = DeNelle.Core.HudModel.PostureSignals.ArmyFillCap;
            if (cap <= 0)
            {
                TroopArmySummaryText = null;
                TroopArmyDoor = null;             // WO-1541 - no sentence, no destination, no door
                TroopArmyDoorLabel = null;
                FlowTrace.Step("Manage", "troops army summary omitted: army fill has not been published");
                return;
            }

            // ⛔ WO-1541 - THIS METHOD NO LONGER DERIVES WHICH CAMP IS NEXT. IT READS THE FACT.
            // It used to build its own Hero.RaidSelectionVM over SceneConfigCatalog.All-where-
            // IsEnemy and walk it for the lowest unlockVictories - a SECOND, independent
            // derivation of a fact BuildTimerService.PublishJourneyOpenCamps was already
            // computing for the Journey deck. Different walk, different separator, two answers to
            // "which camp is next" and nothing keeping them equal. That is the duplicated-state
            // class PlayerDeckWorkspace.cs:719-723 names in words: "a second check would drift
            // from the first, and the drift is the actual defect."
            //
            // ⚠ IT WAS NEVER AN ASSEMBLY VIOLATION - this VM is DeNelle.Village and may construct
            // a RaidSelectionVM legally. It broke the ONE-PRODUCER rule. Do not "fix" a future
            // reading of this comment by moving code across assemblies, and do not restore the
            // walk because it compiles.
            string campName = DeNelle.Core.HudModel.PostureSignals.RaidNextCampName;
            int fields = DeNelle.Core.HudModel.PostureSignals.RaidNextCampGarrison;

            string army = "Army " + used + " / " + cap;
            if (!string.IsNullOrEmpty(campName) && fields > 0)
                army += " - " + Ascii(campName) + " fields " + fields;
            TroopArmySummaryText = army;

            // WO-1541 ruling 2 - THE DOOR. The model decides it exists and what it says; the View
            // only paints it. A door is offered exactly when there is a named camp to walk to -
            // otherwise the sentence has no destination and a live button would be a lie.
            if (!string.IsNullOrEmpty(campName))
            {
                string doorCamp = campName;
                TroopArmyDoorLabel = "RAID " + Ascii(doorCamp).ToUpperInvariant();
                TroopArmyDoor = () =>
                {
                    FlowTrace.Step("Manage", "army line door -> raid grid (camp='" + doorCamp + "')");
                    // ⛔ THE SAME CALL THE JOURNEY DECK'S RAIDS CARD MAKES, reused verbatim:
                    // PlayerDeckWorkspace.cs:746 is `Open = RaidEntryGate.RequestOpen`.
                    // ⚠ NOT RaidSelectionScreen.Open() directly, even though this VM is
                    // DeNelle.Village and could call it. RaidEntryGate is the Core seam whose
                    // Village-side subscriber (RaidEntryBridge) opens the screen, and routing both
                    // doors through it is what stops the raid entry point from forking - the same
                    // one-producer discipline this whole ticket is about. And NO new PanelId: the
                    // raid grid has never had one, and an unregistered id ships a dead door.
                    Guard.Try("Manage", "open the raid grid from the army line",
                        () => DeNelle.Core.UI.RaidEntryGate.RequestOpen());
                };
            }
            else
            {
                TroopArmyDoorLabel = null;
                TroopArmyDoor = null;
            }

            FlowTrace.Step("Manage", "troops army summary='" + army + "' door=" +
                (TroopArmyDoor != null ? TroopArmyDoorLabel : "(none)"));
        }

        /// <summary>
        /// True when ANY of the placed ids behind this tally carries a per-instance level ladder
        /// (<c>repo.maxLevel &gt; 1</c>) — i.e. it upgrades through <see cref="BuildDefenseBrowse"/>
        /// on the Defense tab. Such an id is NOT missing an upgrade path, so it must never land on
        /// the "author some rows" to-do list.
        /// </summary>
        private static bool HasLevelLadder(PlacedTally tally)
        {
            if (tally == null) return false;
            for (int i = 0; i < tally.SourceIds.Count; i++)
            {
                var entry = CatalogRegistry.Get(tally.SourceIds[i]);
                if (entry != null && entry.repo != null && entry.repo.maxLevel > 1) return true;
            }
            return false;
        }

        /// <summary>
        /// LOUD, ONCE PER LADDER ID PER SESSION: a building is standing in this town, its id has
        /// already been through the shipped resolver, and building-tiers.json STILL has nothing
        /// authored under the result — so Manage can offer it nothing.
        ///
        /// This is the TO-DO LIST of buildings that still need upgrade rows authored. Do not downgrade
        /// it to a Step: a silently skipped id is precisely how the empty-tab defect hid for so long.
        ///
        /// Because the id is resolved BEFORE this point, the collector case ("collector_farm" -> "farm")
        /// no longer reaches here at all. If one ever does, the message says so explicitly and that is
        /// a REAL SECOND DEFECT to chase, not noise: it means <c>repo.collectorBuildingId</c> points at
        /// a ladder that does not exist, so the in-world upgrade panel is equally dead for that
        /// building. Never fix that by authoring rows under the PLACED id — tiers persist under the
        /// RESOLVED id (GameState.BuildingTiers), so the copy would be a ghost that never advances.
        /// </summary>
        private static void WarnNoLadder(string ladderId, PlacedTally tally)
        {
            // Deduped HERE rather than through FlowTrace.Once because Once logs at INFO and this
            // must stay at WARNING level to survive an F8 harvest. Rebuild() runs on every tab
            // change / economy tick, so an undeduped Warn would bury the rest of the capture.
            if (!_noLadderWarned.Add(ladderId)) return;

            int count = tally != null ? tally.Count : 0;
            string sources = (tally != null) ? string.Join(", ", tally.SourceIds.ToArray()) : "";
            bool resolvedAway = !string.IsNullOrEmpty(sources) &&
                                !string.Equals(sources, ladderId, StringComparison.OrdinalIgnoreCase);

            FlowTrace.Warn("Manage",
                "no upgrade ladder authored for '" + ladderId + "' (x" + count + " in this town, placed as: " +
                sources + ") - the Buildings tab can offer it nothing. " +
                (resolvedAway
                    ? "SECOND DEFECT: that id came from repo.collectorBuildingId, so the resolver points at " +
                      "a ladder that does not exist and the in-world upgrade panel is dead for it too. " +
                      "Fix the pointer or author '" + ladderId + "' - never author rows under the placed id, " +
                      "because tiers persist under the resolved one."
                    : "CONTENT GAP: author tier rows for '" + ladderId + "' in building-tiers.json."));
        }

        /// <summary>
        /// The Troops tab's browse list — TRAIN rows, UPGRADE rows and the ARMIES/muster entry.
        ///
        /// ⚠ WHY THE TRAIN ROWS EXIST (PROD-013, 2026-08-20 — do not remove them). This method used
        /// to emit UPGRADE rows ONLY. PROD-002 (commit 233613615, 2026-08-18) closed the barracks
        /// talk-door on the stated premise that "Manage owns training" — but that premise was FALSE
        /// when it was written: nothing on this tab could ever start a training job, so closing the
        /// door left the player with an Upgrade-only Troops tab and NO way to train at all. The
        /// owner reported exactly that ("under manage i see option to upgrade the troops, but i
        /// dont se a way to train troops"). This method is what makes PROD-002's premise true.
        ///
        /// TRAIN and UPGRADE are DIFFERENT ACTIONS ON THE SAME TROOP and both belong here — the
        /// labels are prefixed with the verb ("Train Footman" / "Upgrade Footman -&gt; L2") so the
        /// two can never be mistaken for each other. Everything still routes through
        /// BarracksService; this screen charges and enqueues nothing itself.
        /// </summary>
        private void BuildTroopsBrowse()
        {
            var all = TroopCatalog.All;
            if (all == null)
            {
                // NO SILENT EMPTY LIST (§12): an empty Troops tab is read off a log, never guessed.
                FlowTrace.Warn("Manage", "troops browse: TroopCatalog.All is null - the tab can offer nothing.");
                return;
            }

            int trainRows = 0, upgradeRows = 0, locked = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                // Guard.TryEach semantics by hand: ONE malformed TroopDef logs and is SKIPPED
                // rather than throwing out of the loop and blanking the whole tab (§12 step 2).
                Guard.Try("Manage", "troops browse row " + i, () =>
                {
                    if (def == null || string.IsNullOrEmpty(def.Id)) return;

                    // Two authorities, both real: BarracksService.IsTroopUnlocked is the gate
                    // EnqueueTraining itself enforces (barracks.json unlocksTroopIds); TroopUnlock
                    // .IsTrainable is the WO-733 tier authority every other train path asks. A row
                    // is offered only when BOTH say yes, so this tab can never show a CTA the
                    // service will refuse. They are filters, not a defect.
                    bool unlocked = BarracksService.IsTroopUnlocked(def.Id);
                    bool trainable = TroopUnlock.IsTrainable(def);
                    string id = def.Id;
                    string name = NameOfTroop(def);
                    int level = BarracksService.TroopLevel(id);
                    var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
                    int owned = state != null && state.Army != null ? state.Army.CountOfDef(id) : 0;
                    var choice = new TroopChoiceVM
                    {
                        Id = id,
                        Name = name,
                        Description = Ascii(def.ShortDescription ?? ""),
                        IconId = def.IconId,
                        Level = Mathf.Max(1, level),
                        Unlocked = unlocked && trainable,
                        Requirement = unlocked && trainable
                            ? owned + " in your army"
                            : "Requires Barracks Tier " + Mathf.Max(1, def.UnlockBarracksTier),
                        LockTier = Mathf.Max(1, def.UnlockBarracksTier),
                    };
                    TroopChoices.Add(choice);
                    // WO-1422 ruling 3.10 item 1 - the state BADGE word, set on EVERY path so a
                    // locked troop's card is never badge-less (Buildings paints one on all four of
                    // its states). DoorLabel stays null: ruling 3.5 forbids inventing a door, and
                    // there is no troop skill/perk panel to open.
                    choice.StateWord = "Locked";
                    FlowTrace.Step("Manage", "troop choice id=" + id + " unlocked=" + choice.Unlocked +
                        " armyOwned=" + owned);
                    if (!unlocked || !trainable)
                    {
                        locked++;
                        return;
                    }

                    // ── WO-1382: the card's facts, in plain English (owner ruling #4). The depth
                    //    test is the SAME gate EnqueueTraining applies (queue.Enqueue -> line full),
                    //    read here so the sentence is on screen BEFORE the tap. WO-1387: there is
                    //    no gold test any more - training charges nothing.
                    FillTrainFacts(choice, def);
                    FillUpgradeFacts(choice, id, level);
                    FillTroopStateWord(choice, id);

                    // ── TRAIN ──────────────────────────────────────────────────
                    // WO-1387 (owner 2026-09-04 23:16, "training free ... just time"): a FREE row.
                    // This was AddGoldBrowseRow(..., def.CostGold, ...) - the gold row builder is
                    // NOT used here any more; the label "Train <name>" is byte-identical (pinned by
                    // ManageTroopsTrainDoorRegression). TroopDef.CostGold is deliberately not read.
                    AddBrowseRow("Train " + name, default, "Train", () => TrainTroop(id));
                    BrowseRows[BrowseRows.Count - 1].SubjectId = id;
                    trainRows++;

                    // -- UPGRADE (unchanged path; the cost is EMPTY since WO-1387) --
                    if (!BarracksProgression.HasNextTroopLevel(id, level)) return;

                    var econCost = BarracksProgression.TroopUpgradeCost(id, level + 1);
                    var cost = new CoreCost
                    {
                        wood = econCost.Wood,
                        food = econCost.Food,
                        iron = econCost.Iron,
                        crystals = econCost.Crystals,
                    };
                    AddBrowseRow("Upgrade " + name + " -> L" + (level + 1), cost, "Upgrade",
                                 () => UpgradeTroop(id));
                    BrowseRows[BrowseRows.Count - 1].SubjectId = id;
                    upgradeRows++;
                });
            }

            AddMusterRow();

            FlowTrace.Step("Manage",
                "troops browse: " + all.Count + " troop def(s) -> " + trainRows + " Train row(s), " +
                upgradeRows + " Upgrade row(s), " + locked + " still locked, + 1 Armies/muster entry.");

            if (trainRows == 0)
                FlowTrace.Warn("Manage",
                    "troops browse produced NO Train row - every troop is locked or the catalog is empty. " +
                    "This is the PROD-013 defect shape: the Troops tab is the ONLY door to training.");
        }

        /// <summary>
        /// WO-1382 - the TRAIN half of the selected-troop card. WO-1387 (owner 2026-09-04 23:16,
        /// "training free ... just time"): "Train one: 45s . Ready" - NO gold term. The state is
        /// ruling #4's shape minus the gold arm: `Ready` / `Training line full . q/depth queued`.
        /// The depth test is the ONE gate EnqueueTraining still applies for a unit that fits.
        /// </summary>
        private static void FillTrainFacts(TroopChoiceVM choice, TroopDef def)
        {
            var svc = BuildTimerService.Instance;
            bool lineFull = svc != null && svc.IsLineFull(ChannelId.Train);

            choice.TrainCostText = "";
            choice.TrainTimeText = FormatTime(def.BuildSeconds);

            // ⭐ WO-1517 - THE ARMY CAP IS NOW A SENTENCE ON THIS SCREEN, NOT A SILENT REFUSAL.
            // Owner ruling 2026-09-06 20:10, verbatim: "on train army screens should show if queue
            // is full and army is full also should show if a troop type can be upgraded".
            // Until now this method tested queue DEPTH only ("WO-1387: there is no gold test any
            // more"), so a player at the cap read "Train one: 1m 0s . Ready", tapped TRAIN, and got
            // nothing but a notice - which is exactly the frame she captured
            // (owner-screen-20260906-201037.png: TRAIN . 1M 0S inviting the tap, with "Army is
            // full." as a footnote under it, contradicting the button).
            //
            // ⛔ ONE AUTHORITY, AND IT IS THE SERVICE'S OWN. ArmyReadiness.Compute is the formula
            // BarracksService.EnqueueTraining itself seeds its refusal from (BarracksService.cs:
            // "the SEED numbers ... come from ArmyReadiness.Compute - the ONE readiness formula"),
            // and the slot cost is TroopDialogueCommands.SlotOf, the same reader. So the sentence
            // on the face and the refusal in the service can never disagree.
            //
            // ⛔ AND IN THE SERVICE'S ORDER. EnqueueTraining tests the army cap INSIDE its per-unit
            // loop, before it ever reaches queue.Enqueue - so the cap refuses first and the line
            // depth second. Reporting them the other way round would name the wrong blocker to a
            // player who is at both.
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            bool armyFull = false;
            if (state != null && state.Army != null)
            {
                var readiness = ArmyReadiness.Compute(state);
                int unitSlots = TroopDialogueCommands.SlotOf(choice.Id);
                int used = readiness.RosterSlots + readiness.QueuedSlots;
                armyFull = readiness.CapSlots > 0 && used + unitSlots > readiness.CapSlots;
                choice.ArmyUsedSlots = used;
                choice.ArmyCapSlots = readiness.CapSlots;
                if (armyFull)
                    choice.ArmyFullText = "Army is full . " + used + "/" + readiness.CapSlots + " slots used";
            }
            choice.ArmyFull = armyFull;

            if (armyFull)
            {
                choice.TrainStateText = choice.ArmyFullText;
                choice.TrainReady = false;
            }
            else if (lineFull)
            {
                int depth = svc.QueueDepth(ChannelId.Train);
                int cap = svc.QueueDepthLimit(ChannelId.Train);
                choice.QueueFullText = "Training line full . " + depth + "/" + cap + " queued";
                choice.TrainStateText = choice.QueueFullText;
                choice.TrainReady = false;
            }
            else
            {
                choice.TrainStateText = "Ready";
                choice.TrainReady = true;
            }
            choice.TrainFactText = "Train one: " + choice.TrainTimeText + " . " + choice.TrainStateText;
            FlowTrace.Step("Manage", "train facts id=" + choice.Id + " ready=" + choice.TrainReady +
                " armyFull=" + armyFull + " lineFull=" + lineFull + " state='" + choice.TrainStateText + "'");
        }

        /// <summary>
        /// WO-1382 - the UPGRADE half of the card. Reads the SAME gates as
        /// <see cref="BarracksService.CanUpgradeTroop"/> (next level exists, not already upgrading)
        /// so the sentence can never disagree with the refusal. WO-1387: the upgrade's only price is
        /// <see cref="BarracksProgression.TroopUpgradeSeconds"/>, so the line reads
        /// "Upgrade: 1m 30s . Ready" and there is no affordability arm.
        /// </summary>
        private static void FillUpgradeFacts(TroopChoiceVM choice, string id, int level)
        {
            choice.HasNextLevel = BarracksProgression.HasNextTroopLevel(id, level);
            if (!choice.HasNextLevel)
            {
                choice.UpgradeCostText = "";
                choice.UpgradeStateText = "At max level";
                choice.UpgradeReady = false;
                choice.UpgradeFactText = "This troop is at its current maximum level.";
                choice.UpgradeWord = "MAX";
                return;
            }

            // The time IS the price (WO-1387); it rides UpgradeCostText because the View composes
            // its sub-line from that field (see the TroopChoiceVM field comment).
            choice.UpgradeCostText = FormatTime(BarracksProgression.TroopUpgradeSeconds(id, level + 1));
            // WO-1389: the reason to press it - "L3 unlocks Sweeping Cut" from troop-upgrades.json.
            choice.NextUnlockText = BarracksProgression.NextAbilityLine(id, level) ?? "";
            if (choice.NextUnlockText.Length == 0)
                FlowTrace.Step("Manage", "troop '" + id + "' L" + level + ": no ability above this level - " +
                    "the UPGRADE face carries no next-unlock line.");
            choice.UpgradeInProgress = BarracksService.IsUpgradingTroop(id);
            if (choice.UpgradeInProgress)
            {
                choice.UpgradeStateText = "Upgrading now";
                choice.UpgradeReady = false;
            }
            else
            {
                choice.UpgradeStateText = "Ready";
                choice.UpgradeReady = true;
            }
            choice.UpgradeFactText = "Upgrade: " + choice.UpgradeCostText + " . " + choice.UpgradeStateText;

            // ⭐ WO-1517 - "also should show if a troop type can be upgraded" (owner, 20:10).
            // The word is ASKED OF THE SERVICE, never re-derived: BarracksService.CanUpgradeTroop
            // is the one gate UpgradeTroop itself calls before it enqueues, and it hands back the
            // reason. A refusal that is not "already running" or "at max" is a PREREQUISITE
            // (research/tier) and is reported as NEEDS <blocker>, so the tile names what to go and
            // get instead of leaving the player to guess which of the two ladders is short.
            // ⚠ The Research LINE being full is deliberately NOT folded in here: that is the
            // QUEUE's state, it is reported by the queue face, and mixing it into the upgrade word
            // would say "this troop cannot be upgraded" about a troop that can.
            if (!BarracksService.CanUpgradeTroop(id, out string upgradeBlocker))
            {
                choice.UpgradeWord = choice.UpgradeInProgress
                    ? "UPGRADING"
                    : "NEEDS " + Ascii(string.IsNullOrEmpty(upgradeBlocker)
                        ? "a prerequisite" : upgradeBlocker.TrimEnd('.')).ToUpperInvariant();
                FlowTrace.Step("Manage", "troop '" + id + "' upgrade word='" + choice.UpgradeWord +
                    "' from CanUpgradeTroop reason '" + (upgradeBlocker ?? "") + "'");
            }
            else
            {
                choice.UpgradeWord = "UPGRADE AVAILABLE";
            }
        }

        /// <summary>
        /// WO-1422 ruling 3.10 item 1 — the UNLOCKED troop's state badge word, exactly one of
        /// "Training" / "Max" / "Upgradable" (the "Locked" arm is set before the locked early-return
        /// in <see cref="BuildTroopsBrowse"/>).
        ///
        /// ⚠ "Training" IS DEFINED HERE, because nothing pinned it. It reads the Buildings meaning
        /// of "Building" - WORK IN FLIGHT ON THIS CARD'S OWN SUBJECT - which for a troop is either a
        /// TroopUpgrade job (already resolved into <see cref="TroopChoiceVM.UpgradeInProgress"/> by
        /// <see cref="FillUpgradeFacts"/>, and note the engine runs those on the RESEARCH channel)
        /// or a training job on the Train line whose id carries this troop. Precedence puts the
        /// in-flight word first, because a card that says "Upgradable" while its own upgrade is
        /// running is the state the badge exists to stop.
        /// </summary>
        private static void FillTroopStateWord(TroopChoiceVM choice, string troopId)
        {
            var svc = BuildTimerService.Instance;
            bool training = choice.UpgradeInProgress;
            if (!training && svc != null && !string.IsNullOrEmpty(troopId))
            {
                // The job-id grammar is BarracksService's own: "barracks-train:<troopId>:<guid8>"
                // (BarracksService.cs:366). The TRAILING COLON is load-bearing: without it a troop
                // id that is a PREFIX of another id would read the other troop's training job as
                // its own. StackKeyOf keys on the same second colon-segment.
                string prefix = BarracksService.TrainPrefix + troopId + ":";
                foreach (var job in svc.ActiveJobsOf(ChannelId.Train))
                {
                    if (job.StructureId != null &&
                        job.StructureId.StartsWith(prefix, StringComparison.Ordinal)) { training = true; break; }
                }
                if (!training)
                    foreach (var job in svc.PendingJobsOf(ChannelId.Train))
                    {
                        if (job.StructureId != null &&
                            job.StructureId.StartsWith(prefix, StringComparison.Ordinal)) { training = true; break; }
                    }
            }

            choice.StateWord = training ? "Training" : !choice.HasNextLevel ? "Max" : "Upgradable";
            FlowTrace.Step("Manage", "troop state id=" + troopId + " word=" + choice.StateWord +
                " upgrading=" + choice.UpgradeInProgress + " hasNext=" + choice.HasNextLevel);
        }

        /// <summary>
        /// WO-1382 ruling #1 — the Training chip uses the channel's colour-independent
        /// description, including queue depth while busy and the explicit idle/free wording
        /// while idle.
        /// Null when the Train line is not summarised (no BuildTimerService) - the View then
        /// keeps its occupancy-only fallback.
        /// </summary>
        public string TrainingChipText
        {
            get
            {
                for (int i = 0; i < Channels.Count; i++)
                {
                    var c = Channels[i];
                    if (c.Channel != ChannelId.Train) continue;
                    return c.Describe();
                }
                return null;
            }
        }

        /// <summary>
        /// WO-897 army muster / loadout bank entry (save schema v38, 3 named composition slots).
        /// It ships and, until PROD-013, had no player-reachable door either — the barracks Yarn
        /// verb &lt;&lt;ShowMusterUI&gt;&gt; was its only caller and that door is closed. Free, so the
        /// affordable-first sort floats it to the top of the tab where an entry point belongs.
        /// </summary>
        private void AddMusterRow()
        {
            BrowseRows.Add(new BrowseRowVM
            {
                Label = "Armies - saved compositions",
                CostText = "",
                StateText = "Muster a saved army onto the Training line",
                Affordable = true,
                CostWeight = 0f,
                ActionText = "Open",
                Activate = OpenMuster,
            });
        }

        /// <summary>Opens the WO-897 Armies/muster panel. The panel owns its own locked refusal.</summary>
        private static void OpenMuster()
        {
            FlowTrace.Step("Manage", "Armies CTA - opening the muster panel.");
            TroopDialogueCommands.ShowMusterUI();
        }

        private void BuildResearchBrowse()
        {
            // ⚠ REWRITTEN 2026-08-07 (owner ruling: building-perk research is now TIME-BASED, like
            // Warcraft 3). The old version emitted ONE row per BUILDING with CostText="",
            // Affordable=false and StateText="Open to see costs", pinned to CostWeight=MaxValue so
            // it always sorted last. That was correct while research was an instant purchase this
            // screen had no business pricing — but it made the Research tab the one tab that could
            // never answer the question the whole screen exists to answer ("can I act on this
            // now?"), and it could never produce a Research QUEUE row because "Open" only drilled
            // into another panel.
            //
            // Now a perk is a real priced+timed action, so this browses PER PERK and states the
            // real numbers: the authored goldCost (perks are the ONLY gold-priced work in the
            // game — the other three tabs are wood/food/iron/crystals, which is why this method
            // cannot reuse AddBrowseRow / CoreCost, whose struct has no coins field), the derived
            // duration, and a CTA that calls the same BuildingPerkService.TryResearch the panel
            // calls. This screen still charges NOTHING itself.
            var all = BuildingTierCatalog.All;
            if (all == null) return;

            int gold = GoldBalance();

            // SAME DEFECT AS THE BUILDINGS TAB, SAME FIX (2026-08-08). This gate used to be
            // `ModifierService.TierOf(def.Id) < 1  // not built`, which is NOT what TierOf answers:
            // BuildingTiers only holds ids that have been UPGRADED, so a player who owned a barracks
            // but had never bought a tier saw an empty Research tab too. Ownership is the LIVE
            // per-town placement count (CountPlacedThisTown); the tier gate is BuildingPerkService's
            // job and it already states the requirement in words ("Upgrade the building to Tier N
            // first"), which is the sentence that teaches the loop instead of hiding it.
            //
            // CountPlacedThisTown is keyed on the RESOLVED ladder id, which is the same id space
            // building-tiers.json uses — so this ContainsKey compares like with like, and a placed
            // collector ("collector_lumbermill" -> "lumbermill") correctly unlocks its perks.
            var placedThisTown = CountPlacedThisTown();
            int owned = 0;
            int before = BrowseRows.Count;
            int locked = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Id) || def.Tiers == null) continue;
                if (!placedThisTown.ContainsKey(def.Id)) continue;       // you do not own one HERE

                owned++;
                string buildingName = Ascii(string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName);

                for (int t = 0; t < def.Tiers.Count; t++)
                {
                    var tierDef = def.Tiers[t];
                    if (tierDef?.Perks == null) continue;

                    for (int p = 0; p < tierDef.Perks.Count; p++)
                    {
                        var perk = tierDef.Perks[p];
                        if (perk == null || string.IsNullOrEmpty(perk.Id)) continue;

                        // Captured by the CTA closure — never the loop variables.
                        string bId = def.Id;
                        string pId = perk.Id;
                        if (Buildings.Progression.BuildingPerkService.IsOwned(bId, pId)) continue;

                        bool can = Buildings.Progression.BuildingPerkService.CanResearch(bId, pId, out string reason);
                        if (!can)
                        {
                            // WO-1390 (owner, Seeker 2026-09-04: "under manage research it shows
                            // nothing, should it show Tier one and show locked with a link to
                            // upgrade the prerequisite"). This used to be a bare `continue` on
                            // !can under a "progressive disclosure" comment - so with six laddered
                            // buildings at tier 1 the tab rendered ZERO rows, and the one sentence
                            // that teaches the loop (the CanResearch reason) was discarded as `_`.
                            // The Troops tab already shows its locked choice with the badge and
                            // "Build a Barracks to unlock"; Research now follows the same rule.
                            //
                            // Only a TIER gate becomes a row. "Research already in progress." is
                            // not a prerequisite - that perk is a queue row on this very screen -
                            // so it stays skipped, as does anything unexpected.
                            int unlock = BuildingTierCatalog.PerkUnlockTier(bId, pId);
                            bool buildingLocked = ModifierService.TierOf(bId) < unlock;
                            // WO-1423 — village gate on the VILLAGE scale (the perk's own tier row's
                            // requiresVillageTier), never the building tier number `unlock`.
                            bool villageLocked = !buildingLocked &&
                                                 Buildings.Progression.VillageTierService.Current <
                                                     BuildingTierCatalog.PerkRequiredVillageTier(bId, pId);
                            if (!buildingLocked && !villageLocked) continue;

                            // THE DOOR, not a dead button - and since WO-2003 each gate opens the
                            // thing its own face names. A BUILDING-tier gate opens the building's
                            // upgrade page (PanelId.BuildingUpgrade + the ladder id, the id
                            // BuildModeController hands it too), which is where a building tier is
                            // raised. A HEART gate opens the HEART SURFACE (PanelId.Heart).
                            // ⚠ CORRECTED WO-1423: this comment said the page's "FIRST tile" was that
                            // control (BuildingUpgradeVM.PrependVillageTierRow, WO-481). That tile is
                            // never drawn - both render paths filter on `perk:` ids - so the page's
                            // ACTION BAND, not a tile, is its live control.
                            // ⚠ CORRECTED AGAIN, WO-2003 (2026-09-06): it then said "there is no
                            // separate Heart panel". THERE IS ONE NOW (HeartPanel / PanelId.Heart), and
                            // the village arm below opens it - a face reading "UPGRADE THE HEART" that
                            // opened another building's screen is the defect the owner reported.
                            // This screen still charges NOTHING: the destination does.
                            string upperName = buildingName.ToUpperInvariant();
                            string face = villageLocked ? "UPGRADE THE HEART" : "UPGRADE " + upperName;
                            string gate = Ascii(string.IsNullOrEmpty(reason) ? "Locked." : reason);

                            locked++;
                            BrowseRows.Add(new BrowseRowVM
                            {
                                Label = Ascii(string.IsNullOrEmpty(perk.Name) ? pId : perk.Name),
                                CostText = buildingName + " - Tier " + unlock,
                                StateText = gate,                 // the CanResearch reason, verbatim
                                Affordable = false,
                                Locked = true,
                                // Within the locked group the sort key is the unlock tier, so the
                                // nearest prerequisite (Tier 2 before Tier 3) lists first.
                                CostWeight = unlock,
                                ActionText = face,
                                Activate = villageLocked
                                    ? (Action)(() =>
                                    {
                                        FlowTrace.Step("Manage", "research locked door '" + bId + ":" + pId +
                                            "' (heart gate, tier " + unlock + ") -> HEART surface");
                                        OpenHeartPanel(bId + ":" + pId);
                                    })
                                    : (Action)(() =>
                                    {
                                        FlowTrace.Step("Manage", "research locked door '" + bId + ":" + pId +
                                            "' (building tier " + unlock + ") -> BuildingUpgrade page '" + bId + "'");
                                        OpenUpgradePanel(bId);
                                    }),
                            });
                            continue;
                        }
                        int price = Mathf.Max(0, perk.GoldCost);
                        bool affordable = can && gold >= price;
                        float seconds = Buildings.Progression.BuildingPerkService.ResearchSeconds(bId, pId);

                        // Colourblind law: the state is a SENTENCE. "Ready" now also carries the
                        // WAIT, because with a timed research the price is no longer the only cost.
                        string state;
                        if (!affordable) state = "Short " + (price - gold) + " gold";
                        else state = "Ready - takes " + FormatTime(seconds);

                        BrowseRows.Add(new BrowseRowVM
                        {
                            Label = buildingName + " - " +
                                    Ascii(string.IsNullOrEmpty(perk.Name) ? pId : perk.Name),
                            CostText = price > 0 ? price + " gold" : "free",
                            StateText = state,
                            Affordable = affordable,
                            // Every row on THIS tab is priced in gold, so a raw gold weight sorts
                            // cheapest-first consistently. It is never compared against the other
                            // tabs' CostBasket weight — BrowseRows is rebuilt per tab.
                            CostWeight = price,
                            ActionText = "Research",
                            Activate = () => Research(bId, pId),
                        });
                    }
                }
            }

            // NO SILENT EMPTY LIST (§12): say how many ladder buildings this town actually owns and
            // how many perk rows that produced, so an empty Research tab is read off a log instead
            // of a felt-test. owned==0 with placements present means no PLACED id matches a
            // building-tiers.json id — see the [Flow:Manage] "no upgrade ladder authored" warnings.
            FlowTrace.Step("Manage",
                "research browse (this town): " + placedThisTown.Count + " placed type(s), " + owned +
                " with a tier ladder -> " + (BrowseRows.Count - before) + " perk row(s) (" + locked + " locked).");
        }

        private void AddBrowseRow(string label, CoreCost cost, string actionText, Action activate)
        {
            bool affordable = CanAfford(cost);
            BrowseRows.Add(new BrowseRowVM
            {
                Label = label,
                CostText = DescribeCost(cost),
                // The point of the whole screen: whether it is buyable, and if not WHAT is short.
                // Reuses the resolver that actually charges so the screen cannot lie (WO-905 §4).
                StateText = affordable ? "Ready" : ShortfallOf(cost),
                Affordable = affordable,
                CostWeight = BuildTimerConfig.CostBasket(cost),
                ActionText = actionText,
                Activate = activate ?? (() => { }),
            });
        }

        // =====================================================================
        //  COMMANDS — every one acts on the EXPLICIT item the player picked
        // =====================================================================

        /// <summary>
        /// Ruling Q5 + Q11 — pay crystals to complete THIS ONE job (running or queued). Never a
        /// game-wide pass. On the broke case the notice is flagged so the View routes to the store.
        /// </summary>
        public void FinishNow(ChannelId channel, string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                // Q12 defence in depth: a stack header carries no JobId and must never get here.
                FlowTrace.Warn("Manage", "FinishNow called with no job id — an aggregate is not a target.");
                return;
            }
            var svc = BuildTimerService.Instance;
            if (svc == null) return;

            if (svc.TryInstantFinish(channel, jobId, out string failure))
            {
                Notice = "Finished.";
                NoticeIsBrokeCase = false;
            }
            else
            {
                Notice = failure ?? "Could not finish that.";
                // The CRYSTAL prefix only: NoticeIsBrokeCase routes the View to the crystal store,
                // and a gold shortfall (BuildTimerService.InsufficientGoldPrefix, WO-1372) must
                // never go there — gold is earned by raiding and selling, the store sells none.
                NoticeIsBrokeCase = failure != null &&
                                    failure.StartsWith(BuildTimerService.InsufficientCrystalsPrefix, StringComparison.Ordinal);
            }
            Rebuild();
        }

        /// <summary>Watch the rewarded ad to knock time off THIS running job.</summary>
        public void WatchAd(ChannelId channel, string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return;
            var svc = BuildTimerService.Instance;
            if (svc == null) return;
            // RELEASE BLOCKER GATE (2026-08-07): the flag is OFF and no ad SDK is wired, so this
            // entry point can only be reached by a stale row. Say so in plain ASCII rather than
            // reporting a skip that did not happen.
            if (!DeNelle.Core.FeatureFlags.RewardedAdSkip)
            {
                FlowTrace.Warn("Manage",
                    "WatchAd tapped while ff.rewardedadskip is OFF - a stale row survived a rebuild. " +
                    "No ad, no reward. See FeatureFlags.RewardedAdSkip.");
                Notice = "Ad rewards are not available in this build.";
                NoticeIsBrokeCase = false;
                Rebuild();
                return;
            }
            // WO-1125: ASYNC. The bool overload answers "was the reward earned", which is
            // unanswerable at return time once a real SDK is wired - this screen would tell a
            // player who just watched thirty seconds of video "No ad available right now."
            NoticeIsBrokeCase = false;
            svc.WatchAdToSkip(channel, jobId, result =>
            {
                if (result.Rewarded)
                    Notice = "Time skipped.";
                else if (result.Reason == DeNelle.Core.Ads.AdUnavailableReason.Abandoned)
                    Notice = "Ad closed early - no time skipped.";   // their choice, not a failure
                else if (result.Reason == DeNelle.Core.Ads.AdUnavailableReason.CappedByGame)
                    Notice = "You have used your ad skips for now.";  // OUR cap, said plainly
                else
                    Notice = "No ad available right now.";
                NoticeIsBrokeCase = false;
                Rebuild();
            });
            Rebuild();
        }

        /// <summary>
        /// Ruling Q1 + Q12 — cancel THIS ONE job and refund 100% of what was paid for it, flat.
        /// The remaining items close the gap automatically (an active cancel frees its slot and the
        /// next pending job starts; a pending cancel shifts the rest up).
        /// </summary>
        public void Cancel(ChannelId channel, string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                FlowTrace.Warn("Manage", "Cancel called with no job id — a collapsed stack is not a cancel target (Q12).");
                return;
            }
            var svc = BuildTimerService.Instance;
            if (svc == null) return;

            if (svc.CancelChannelJobWithRefund(channel, jobId, out JobCost refunded, out string unrefunded))
            {
                if (!refunded.IsZero)
                    Notice = "Cancelled. Refunded " + refunded.Describe() + ".";
                else
                    Notice = "Cancelled. Nothing to refund.";
            }
            else
                Notice = "Could not cancel that.";
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        /// <summary>The owner's "bump up the next item" — drives the existing ReorderPending.</summary>
        public void BumpUp(ChannelId channel, string jobId, int pendingIndex)
        {
            if (string.IsNullOrEmpty(jobId) || pendingIndex <= 0) return;
            var svc = BuildTimerService.Instance;
            if (svc == null) return;
            Notice = svc.ReorderPending(channel, jobId, pendingIndex - 1) ? "Moved up." : "Could not move that.";
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        /// <summary>
        /// WO-1253 — Manage "Buy builder" drops to the store focused on the permanent-builder SKU.
        /// Does NOT spend crystals. Crystal extra-slot (DEPTH) remains on the queue-full surface.
        /// </summary>
        public void BuySlot(ChannelId channel)
        {
            StoreFocusRequest.RequestFocusSku(PackCatalog.PermanentBuilderSku);
            if (!OpenRealmStoreFromManage("builder offer"))
            {
                Notice = "Store is not open right now.";
                FlowTrace.Warn("Manage", "RealmStore opener not registered - builder SKU route dead-ends.");
            }
            else
            {
                Notice = null;
                FlowTrace.Step("Manage", "buy builder from " + channel + " -> store sku=" + PackCatalog.PermanentBuilderSku);
            }
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        /// <summary>Ruling Q2 — the EXISTING instant repair, surfaced here. Never a queued job.</summary>
        public void RepairAll()
        {
            Guard.Try("Manage", "repair all", () =>
            {
                var repair = UnityEngine.Object.FindFirstObjectByType<WallRepairController>();
                if (repair == null) { Notice = "Nothing to repair."; return; }
                var result = repair.RepairAll();
                Notice = result.repairedCount > 0
                    ? "Repaired " + result.repairedCount + " structure(s)."
                    : "Nothing repaired - check resources.";
            });
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        /// <summary>The broke-case route the owner's rule requires: a way to GET crystals.</summary>
        public void OpenCrystalStore()
        {
            if (!OpenRealmStoreFromManage("crystal shortfall"))
                FlowTrace.Warn("Manage", "RealmStore opener not registered — the broke-case route dead-ends.");
        }

        /// <summary>Use the existing PanelManager return-door arbiter for every Manage-to-store handoff.</summary>
        private bool OpenRealmStoreFromManage(string source)
        {
            ManageTab sendingTab = Tab;
            string tab = sendingTab.ToString();
            PanelManager.SetReturnDoor("Manage tab=" + tab,
                () => PanelRouter.Open(PanelId.Manage, tab));
            FlowTrace.Step("Manage", "store handoff source=" + source + " returnTab=" + tab);
            if (PanelRouter.Open(PanelId.RealmStore, "manage")) return true;

            PanelManager.ClearReturnDoor("manage-store-open-failed");
            FlowTrace.Warn("Manage", "store handoff failed source=" + source + " returnTab=" + tab +
                " - return door cleared");
            return false;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static void OpenUpgradePanel(string id)
        {
            if (!PanelRouter.Open(PanelId.BuildingUpgrade, id))
                FlowTrace.Warn("Manage", $"BuildingUpgrade opener not registered — cannot drill into '{id}'.");
        }

        /// <summary>
        /// WO-2003 / WO-2017 — the door behind every "UPGRADE THE HEART" face.
        /// <para>Before today that face opened the BUILDING's upgrade page and relied on the player
        /// finding the VillageGated action band on it. That band is real and still works, but it is
        /// a control inside another building's screen — which is why the owner reported the gate as
        /// having "no way to trigger". The face now opens the thing it names. The subject id rides
        /// along so a capture says WHICH gated row sent the player.</para>
        /// </summary>
        private static void OpenHeartPanel(string subject)
        {
            if (!PanelRouter.Open(PanelId.Heart, subject))
                FlowTrace.Fail("Manage", $"Heart opener not registered — the 'UPGRADE THE HEART' door for " +
                    $"'{subject}' opened NOTHING. HeartPanelBootstrap did not run.");
        }

        private void UpgradePlaced(string jobKey)
        {
            using var _ = FlowTrace.Enter("Manage", $"Placed upgrade CTA '{jobKey}'");
            var result = Buildings.Progression.PlacedStructureUpgradeService.TryStart(jobKey);
            Notice = result.Success
                ? (result.Outcome == Buildings.Progression.PlacedUpgradeOutcome.Queued
                    ? "Upgrade queued."
                    : "Upgrade started.")
                : Ascii(string.IsNullOrEmpty(result.Message)
                    ? "Could not start that upgrade."
                    : result.Message);
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        private void UpgradeBuilding(string buildingId, int targetTier)
        {
            using var _ = FlowTrace.Enter("Manage", $"Building upgrade CTA '{buildingId}' -> T{targetTier}");
            bool started = Buildings.Progression.BuildingUpgradeService.TryUpgrade(buildingId, targetTier);
            Notice = started
                ? "Upgrade started."
                : "Could not start that upgrade - check requirements and resources.";
            NoticeIsBrokeCase = false;
            Rebuild();
        }

        /// <summary>
        /// Start ONE building perk's research (the Research tab's CTA). Routes through
        /// BuildingPerkService so the gate, the gold charge, the Research-channel enqueue and the
        /// depth cap all behave identically to the building panel's perk tile — this screen never
        /// charges or enqueues anything itself. Unlike the other tabs' CTAs this is an INSTANCE
        /// method: starting a research puts a row on the line the player is currently looking at,
        /// so it sets a Notice and rebuilds rather than leaving the screen stale.
        /// </summary>
        private void Research(string buildingId, string perkId)
        {
            using var _ = FlowTrace.Enter("Manage", $"Research CTA '{buildingId}:{perkId}'");

            if (!Buildings.Progression.BuildingPerkService.CanResearch(buildingId, perkId, out string reason))
            {
                FlowTrace.Warn("Manage", $"research '{buildingId}:{perkId}' refused: {reason}");
                Notice = ManageScreenVM.Ascii(string.IsNullOrEmpty(reason) ? "Cannot research that yet." : reason);
                NoticeIsBrokeCase = false;
                Rebuild();
                return;
            }

            if (Buildings.Progression.BuildingPerkService.TryResearch(buildingId, perkId))
            {
                Notice = "Research started.";
                NoticeIsBrokeCase = false;
            }
            else
            {
                // TryResearch only gets here on a spend failure, a missing service or a refused
                // enqueue - each of which has already left its own [Flow:Research] line naming
                // which one it was, so this message never has to guess in the log.
                Notice = "Could not start that research - check your gold.";
                NoticeIsBrokeCase = false;
            }
            Rebuild();
        }

        /// <summary>
        /// PROD-013 — the Troops tab's TRAIN CTA: enqueue ONE unit of <paramref name="troopId"/> on
        /// the Train channel. Routes through <see cref="BarracksService.EnqueueTraining(string,int,out string)"/>
        /// so the unlock gate, the army-cap check, the resource charge and the queue depth cap all
        /// behave identically to every other train path — this screen charges and enqueues nothing
        /// itself, exactly like <see cref="UpgradeTroop"/> and <see cref="Research"/>.
        ///
        /// An INSTANCE method (like Research, unlike UpgradeTroop) because a successful train puts a
        /// row on the very line the player is looking at: it sets a Notice and rebuilds rather than
        /// leaving the screen stale.
        /// </summary>
        private void TrainTroop(string troopId)
        {
            using var _ = FlowTrace.Enter("Manage", $"Train CTA '{troopId}'");

            int enqueued = BarracksService.EnqueueTraining(troopId, 1, out string stopReason);
            if (enqueued > 0)
            {
                // §12 proving line: the id, what it cost, and the job that now exists. BarracksService
                // logs the jobId itself at enqueue ("train job enqueued 1/1 ... jobId=barracks-train:...");
                // this line names the SCREEN the request came from so the two can be paired in a capture.
                // WO-1387: training charges NOTHING - the trace says so, so a device capture can never
                // read a "cost=[550 gold]" that was not charged (it did, on 2026-09-04 23:44).
                var def = TroopCatalog.Find(troopId);
                string costText = def != null
                    ? "time only, " + Mathf.RoundToInt(def.BuildSeconds) + "s"
                    : "unknown troop";
                FlowTrace.Step("Manage",
                    $"train enqueued from Manage: id={troopId} qty={enqueued} cost=[{costText}] " +
                    $"channel=Train jobIdPrefix={BarracksService.TrainPrefix}{troopId}");
                Notice = "Training started.";
                NoticeIsBrokeCase = false;
            }
            else
            {
                // Refused: locked / army full / unaffordable / queue depth full. BarracksService
                // hands back the ASCII sentence naming WHICH, so the notice never has to guess.
                FlowTrace.Warn("Manage",
                    $"train '{troopId}' refused: {(string.IsNullOrEmpty(stopReason) ? "no reason given" : stopReason)}");
                Notice = Ascii(string.IsNullOrEmpty(stopReason) ? "Could not start that training." : stopReason);
                NoticeIsBrokeCase = false;
            }
            Rebuild();
        }

        private static void UpgradeTroop(string troopId)
        {
            // Routes through the existing service so the charge, the queue and the cap all behave
            // identically to the barracks panel. This screen never charges anything itself.
            if (!BarracksService.CanUpgradeTroop(troopId, out string reason))
            {
                FlowTrace.Warn("Manage", $"troop upgrade '{troopId}' refused: {reason}");
                return;
            }
            BarracksService.UpgradeTroop(troopId);
        }

        private static int CrystalBalance()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return state != null ? state.Resources.Crystals : 0;
        }

        /// <summary>
        /// GOLD (economy Coins) — the currency building-perk research charges, and the ONLY one of
        /// the four tabs that uses it. Reads EconomyService.Coins, which is itself a view onto
        /// GameState.Resources.Coins, so the number shown is the number the spend will check; the
        /// direct-state read is the headless / pre-boot fallback (same shape as <see cref="CanAfford"/>).
        /// </summary>
        private static int GoldBalance()
        {
            var econ = EconomyService.Instance;
            if (econ != null) return econ.Coins;
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return state != null ? state.Resources.Coins : 0;
        }

        private static bool CanAfford(CoreCost cost)
        {
            var econ = EconomyService.Instance;
            if (econ != null) return econ.CanAfford(BuildModeController.ToEconomy(cost));
            // Headless / pre-boot: fall back to the ledger, which reads the same GameState fields.
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) return false;
            return state.Wood >= cost.wood && state.Iron >= cost.iron
                && state.Resources.Food >= cost.food && state.Resources.Crystals >= cost.crystals;
        }

        private static string ShortfallOf(CoreCost cost)
        {
            string msg = BuildModeController.ShortfallMessage(cost);
            return string.IsNullOrEmpty(msg) ? "Short on resources" : Ascii(msg);
        }

        /// <summary>
        /// The Finish CTA's cost sub-line (see <see cref="QueueRowVM.FinishCostText"/>): the currency
        /// SPELLED OUT and singular/plural correct, plus the shortfall in words when the player is
        /// short. ASCII only - a currency glyph renders as tofu in TMP.
        ///
        /// The shortfall is stated as a NUMBER OF CRYSTALS rather than left to a grey face, because
        /// the owner is red/green colourblind and no affordance may convey its meaning by colour
        /// alone: "cannot afford" has to be readable as text.
        /// </summary>
        public static string DescribeFinishCost(int price, int crystals)
            => DescribeFinishCost(price, crystals, paysGold: false);

        /// <summary>
        /// <see cref="DescribeFinishCost(int,int)"/> with the currency chosen by the caller from
        /// <see cref="BuildTimerService.FinishPaysGold(JobKind)"/> (WO-1372 Lane D): a gold-priced
        /// training row reads "120 gold" / "Short 40 gold", the same idiom as the research tab.
        /// <paramref name="balance"/> is the balance OF THAT CURRENCY.
        /// </summary>
        public static string DescribeFinishCost(int price, int balance, bool paysGold)
        {
            if (price <= 0) return "";
            int missing = price - balance;
            // "Short N <currency>" is THIS screen's existing shortfall idiom (BuildResearchBrowse
            // already says "Short 40 gold"), so the two tabs read alike. It also stays inside the
            // CTA's width budget, which "5 crystals - need 3 more" would not: the sub-line has only
            // ~313-350 reference px, and a 20+ character string auto-shrinks to the font floor and
            // then ellipsizes — which would put us right back at an unreadable face.
            return missing > 0 ? "Short " + Currency(missing, paysGold) : Currency(price, paysGold);
        }

        /// <summary>"1 crystal" / "5 crystals" — the currency SPELLED OUT, singular/plural correct.
        /// Never "5c": the owner's felt-test is that the abbreviation says nothing to a new player.</summary>
        private static string Crystals(int n) => n + (n == 1 ? " crystal" : " crystals");

        /// <summary>The spelled-out amount in the row's currency: "N gold" or <see cref="Crystals"/>.</summary>
        private static string Currency(int n, bool gold) => gold ? n + " gold" : Crystals(n);

        /// <summary>ASCII cost summary ("400 wood, 200 food"); "free" when nothing is charged.</summary>
        public static string DescribeCost(CoreCost c)
        {
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", c.wood), ("stone", "Stone", c.food), ("iron", "Iron", c.iron), ("crystal", "Crystals", c.crystals) });
            return parts.Count > 0 ? DeNelle.Core.UI.CostFormat.Words(parts) : "free";
        }

        private void AddGoldBrowseRow(string label, CoreCost materials, int gold, string actionText, Action activate)
        {
            bool affordable = CanAfford(materials) && GoldBalance() >= gold;
            string materialText = DescribeCost(materials);
            string costText = materialText == "free" ? gold + " gold" : materialText + ", " + gold + " gold";
            BrowseRows.Add(new BrowseRowVM {
                Label = label, CostText = costText, Affordable = affordable,
                StateText = affordable ? "Ready" : "Short on resources",
                CostWeight = materials.wood + materials.food + materials.iron + materials.crystals + gold,
                ActionText = actionText, Activate = activate
            });
        }

        private static CoreCost BuildingTierBasket(BuildingTierDef tier)
        {
            if (tier == null) return default;
            int primary = tier.PrimaryMaterialCost;
            return new CoreCost {
                wood = tier.Tier == 1 ? primary : 0,
                food = tier.Tier == 2 ? primary : 0,
                iron = tier.Tier >= 3 ? primary : 0,
            };
        }

        private static string NameOf(CatalogEntry entry, string fallbackId)
            => !string.IsNullOrEmpty(entry.displayName) ? Ascii(entry.displayName) : Ascii(fallbackId);

        private static string NameOfTroop(TroopDef def)
            => !string.IsNullOrEmpty(def.DisplayName) ? Ascii(def.DisplayName) : Ascii(def.Id);

        /// <summary>"1st" / "2nd" / "3rd" / "4th" — ASCII ordinal for the line position.</summary>
        /// <summary>
        /// WO-898 item 1 — live elapsed fraction for a RUNNING job, by id. The 1 Hz tick uses this
        /// so a bar advances while the screen is open without rebuilding a single row.
        /// Returns 0 when the job is not running or its duration is unknown.
        /// </summary>
        public static float ProgressOfLive(BuildTimerService svc, ChannelId channel, string jobId)
        {
            if (svc == null || string.IsNullOrEmpty(jobId)) return 0f;
            var active = svc.ActiveJobsOf(channel);
            for (int i = 0; i < active.Count; i++)
            {
                if (!string.Equals(active[i].StructureId, jobId, StringComparison.Ordinal)) continue;
                var job = active[i];
                if (job.DurationMs <= 0d || job.StartMs <= 0d) return 0f;
                double totalSec = job.DurationMs / 1000d;
                double rem = svc.RemainingSeconds(channel, jobId);
                return Mathf.Clamp01((float)((totalSec - rem) / totalSec));
            }
            return 0f;   // not in the active list => queued or finished; the bar stays empty
        }

        /// <summary>
        /// The percentage rendered IN WORDS, e.g. " (63% done)". The colourblind law forbids the
        /// fill being the only signal, so every bar is paired with this. Empty string when there is
        /// no meaningful progress to state.
        /// </summary>
        public static string PercentSuffix(BuildTimerService svc, ChannelId channel, string jobId)
        {
            float p = ProgressOfLive(svc, channel, jobId);
            if (p <= 0f) return "";
            return " (" + Mathf.RoundToInt(p * 100f) + "% done)";
        }

        /// <summary>
        /// Compact, colour-independent timing copy for a queue timeline row. Position is carried
        /// by <see cref="QueueRowVM.PositionText"/>; this line carries duration and progress only.
        /// </summary>
        public static string QueueTimingText(BuildTimerService svc, ChannelId channel, string jobId,
                                             bool queued, double remainingSeconds)
        {
            string time = FormatTime(remainingSeconds);
            if (queued) return time + " OF WORK";
            int percent = Mathf.RoundToInt(ProgressOfLive(svc, channel, jobId) * 100f);
            return time + " LEFT | " + percent + "% DONE";
        }

        internal static string Ordinal(int n)
        {
            if (n <= 0) return "next";
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        /// <summary>ASCII countdown ("2m 10s"). No non-ASCII: TMP renders it as tofu.</summary>
        public static string FormatTime(double seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            if (s >= 3600) return (s / 3600) + "h " + ((s % 3600) / 60) + "m";
            if (s >= 60) return (s / 60) + "m " + (s % 60) + "s";
            return s + "s";
        }

        /// <summary>Strip anything outside printable ASCII — the LiberationSans-SDF tofu rule.</summary>
        public static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= ' ' && c <= '~') sb.Append(c);
                else if (c == '→') sb.Append("->");
                else if (c == '×') sb.Append('x');
                else sb.Append(' ');
            }
            return sb.ToString();
        }

        // =====================================================================
        //  WO-2001 - THE MANAGE INFORMATION ARCHITECTURE
        // ---------------------------------------------------------------------
        //  Three tabs (BUILD / ARMY / RESEARCH), a SCREEN GRAPH, and a back stack
        //  that remembers WHY the player is on a screen, not only where.
        //
        //  ⛔ THE OWNER'S OWN FLOW MOCKUP IS THE LAYOUT AUTHORITY: the grid and the
        //  selected-item detail are SEPARATE SCREENS and never share one. That is not
        //  a taste call, it is arithmetic - stacking a 12-tile grid AND a selection
        //  card needs ~1454 reference px against the three MEASURED Manage wells of
        //  533 / 542 / 612px (ManageWorkspacePanel.cs header, WO-2002's hand-back).
        //  So a GRID screen composes tiles with Selection.Visible=false, and a DETAIL
        //  screen composes an EMPTY tile list with a visible selection card.
        //
        //  ⛔ THE VIEW DECIDES NOTHING HERE. Canon 9 forbids the View computing costs,
        //  locks, affordability, destinations or queue capacity; this file does all of
        //  it and hands ManageWorkspacePanel finished records. Every route is bound
        //  INTO a callback by ManageVmProjection, so the renderer never sees a
        //  ManageRoute at all.
        //
        //  ⛔ NOTHING IS RE-DERIVED. The tiles are composed from the choice VMs this
        //  class already builds (BuildingChoices / DefenseChoices / TroopChoices /
        //  ResearchChoices) plus BuildInventoryModel for the authoritative BUILD
        //  inventory. A second lock/cost/queue derivation is the duplicated state
        //  CLAUDE.md 2 / 5 / 16 keeps paying for.
        // =====================================================================

        /// <summary>PlayerPrefs key for the last-used tab (WO-2001 "Entry": open it again).</summary>
        public const string LastTabPrefKey = "manage.lasttab";

        /// <summary>Host command: open the global QUEUE overlay (ruling 17). Set by the panel.</summary>
        public Action OpenQueueRequested;
        /// <summary>Host command: open the Heart progression surface (ruling 10 / route HeartCard).</summary>
        public Action OpenHeartRequested;
        /// <summary>Host command: leave Manage entirely (back from a root grid).</summary>
        public Action CloseRequested;
        /// <summary>Host command: enter Town build mode (the door for a structure that is not placed yet).</summary>
        public Action OpenTownBuilderRequested;
        /// <summary>WO-1571 - host command: open PLACEMENT for ONE catalog id (the ghost, armed).
        /// The not-built card's BUILD button uses THIS, never <see cref="OpenTownBuilderRequested"/>:
        /// that one lands on the Build Collections root, which authors no ECONOMY/CRAFT/STORAGE
        /// collection, so a non-defence row could never be reached from its own button.</summary>
        public Action<string> PlaceStructureRequested;

        private ManageNavEntry _nav;
        private string _activeFilter = BuildFilter.All;
        private readonly List<ManageTabId> _availableTabs = new List<ManageTabId>(3);

        /// <summary>Depth cap on the jump chain. A cycle in the graph would otherwise grow it forever.</summary>
        private const int MaxOriginDepth = 8;

        /// <summary>The screen the player is on. Never null once <see cref="OpenDefaultScreen"/> has run.</summary>
        public ManageNavEntry Nav => _nav;

        /// <summary>The BUILD filter chip in force. One of <see cref="BuildFilter.Chips"/>.</summary>
        public string ActiveFilter => _activeFilter;

        /// <summary>The tabs this build actually offers, model-decided (WO-2001 "available tabs").</summary>
        public IReadOnlyList<ManageTabId> AvailableTabIds => _availableTabs;

        // ── tab identity crossing ────────────────────────────────────────────

        /// <summary>
        /// The ONE place the redesign's three tabs cross the four legacy content tabs.
        /// Ruling 4: Defense and Buildings MERGE into BUILD because they share the Builder
        /// queue - which is exactly what <see cref="ChannelOf"/> already says, so the merge
        /// costs the queue model nothing.
        /// </summary>
        public static ManageTab LegacyTabOf(ManageTabId id)
        {
            switch (id)
            {
                case ManageTabId.Army: return ManageTab.Troops;
                case ManageTabId.Research: return ManageTab.Research;
                default: return ManageTab.Buildings;   // BUILD == Buildings + Defense, one Builder line
            }
        }

        /// <summary>ASCII tab words. Supplied so the View never derives a label from an enum name.</summary>
        public static string TabWordOf(ManageTabId id)
        {
            switch (id)
            {
                case ManageTabId.Army: return "ARMY";
                case ManageTabId.Research: return "RESEARCH";
                default: return "BUILD";
            }
        }

        /// <summary>
        /// Which of the three tabs this build offers. BUILD is unconditional (the town always has
        /// structures); ARMY and RESEARCH follow <see cref="VisibleTabs"/>, which is derived from
        /// live placements (<c>CountPlacedThisTown</c>).
        ///
        /// <para>⚠ ARMY is gated rather than shown-and-empty for a MECHANICAL reason, not a design
        /// one: <see cref="Rebuild"/> snaps <see cref="Tab"/> back to <c>VisibleTabs[0]</c> whenever
        /// the selected tab is not visible, so an un-gated ARMY tab would silently render BUILD
        /// content under an ARMY heading. Un-gating it needs that snap-back to move first.</para>
        /// </summary>
        private void RefreshAvailableTabs()
        {
            _availableTabs.Clear();
            _availableTabs.Add(ManageTabId.Build);
            if (VisibleTabs.Contains(ManageTab.Troops) && BarracksUnlock.IsUnlocked)
                _availableTabs.Add(ManageTabId.Army);
            if (VisibleTabs.Contains(ManageTab.Research))
                _availableTabs.Add(ManageTabId.Research);
        }

        // ── entry ────────────────────────────────────────────────────────────

        /// <summary>
        /// WO-2001 "Entry": open the last-used tab when it is still available, otherwise BUILD.
        /// "Do not persist a stale tab that is no longer available because of feature gating" -
        /// hence the membership test against <see cref="AvailableTabIds"/>, not a blind read.
        /// The four-tile launcher is superseded; there is no chooser to land on.
        /// </summary>
        public void OpenDefaultScreen()
        {
            RefreshAvailableTabs();
            ManageTabId want = ManageTabId.Build;
            int stored = PlayerPrefs.GetInt(LastTabPrefKey, (int)ManageTabId.Build);
            if (stored >= 0 && stored <= (int)ManageTabId.Research)
            {
                var candidate = (ManageTabId)stored;
                if (_availableTabs.Contains(candidate)) want = candidate;
                else FlowTrace.Step("Manage", "last-used tab " + candidate + " is not available in this " +
                    "build (gating) - opening BUILD instead rather than persisting a stale tab");
            }
            EnterTab(want);
        }

        /// <summary>Switch tabs. Tabs are siblings: one tap from each other, and the stack resets.</summary>
        public void EnterTab(ManageTabId id)
        {
            RefreshAvailableTabs();
            if (!_availableTabs.Contains(id))
            {
                FlowTrace.Warn("Manage", "tab " + id + " was requested but is not available - staying on " +
                    (_nav != null ? _nav.Tab.ToString() : "BUILD"));
                return;
            }
            ClearStaleNotice("tab " + TabWordOf(id));
            _activeFilter = BuildFilter.All;
            _nav = new ManageNavEntry { Kind = ManageScreenKind.Grid, Tab = id, Filter = _activeFilter };
            PlayerPrefs.SetInt(LastTabPrefKey, (int)id);
            FlowTrace.Step("Navigation", "Manage -> " + TabWordOf(id) + " grid");
            SelectTabForId(id);
        }

        /// <summary>
        /// Point the legacy model at the tab that owns this id's content and rebuild.
        /// <see cref="SelectTab"/> early-returns when the tab has not changed, so a same-tab
        /// re-entry still needs an explicit rebuild to raise <see cref="Changed"/> for the View.
        /// </summary>
        private void SelectTabForId(ManageTabId id)
        {
            ManageTab legacy = LegacyTabOf(id);
            if (Tab == legacy) Rebuild();
            else SelectTab(legacy);
        }

        /// <summary>Change the BUILD filter chip. Membership lives in the data; this only selects.</summary>
        public void SetFilter(string chip)
        {
            if (!BuildFilter.IsChip(chip))
            {
                FlowTrace.Warn("Manage", "filter '" + chip + "' is not one of BuildFilter.Chips - ignored");
                return;
            }
            if (string.Equals(_activeFilter, chip, StringComparison.OrdinalIgnoreCase)) return;
            _activeFilter = chip;
            if (_nav != null) _nav.Filter = chip;
            FlowTrace.Step("Manage", "BUILD filter -> " + chip);
            Changed?.Invoke();
        }

        // ── the screen graph ─────────────────────────────────────────────────

        /// <summary>
        /// ⭐ WO-1518 - a REFUSAL SENTENCE NEVER FOLLOWS THE PLAYER TO ANOTHER SCREEN.
        ///
        /// <para>Owner device frame Logs/device/screens/owner-screen-20260906-201242.png is the
        /// ARMORER RESEARCH screen and it carries "Army is full." in the bottom-left. So does the
        /// Archer troop detail one minute earlier (owner-screen-20260906-201037.png). The ticket
        /// reads that as a global footer; it is not. It is <see cref="Notice"/> - the single band
        /// ManageScreenPanel.BuildNotice seats beside CLOSE - still holding the sentence
        /// BarracksService handed back when a TRAIN tap was refused
        /// (<see cref="TrainTroop"/>, "Refused: locked / army full / ..."). Nothing cleared it, so
        /// it rode the back stack onto a screen where the army cap refuses nothing at all.</para>
        ///
        /// <para>⛔ The fix is HERE, in the navigator, not in the band. A notice is about the verb
        /// the player just pressed on the screen they pressed it on; leaving the screen ends its
        /// scope. Suppressing it in the renderer instead would need the View to decide which
        /// sentences belong on which screen - exactly the derivation canon 9 forbids it, and it
        /// would leave the stale string alive to reappear somewhere else.</para>
        ///
        /// <para>WO-1517's ARMY FULL band is the replacement and it is a different mechanism: it is
        /// composed onto the TRAIN action itself (<see cref="ComposeTroopItem"/>), so it is painted
        /// on the button it refuses, before the tap, and it cannot travel.</para>
        /// </summary>
        private void ClearStaleNotice(string destination)
        {
            if (string.IsNullOrEmpty(Notice)) return;
            FlowTrace.Step("Manage", "notice '" + Notice + "' is dropped on the way to " + destination +
                " - a refusal belongs to the screen whose verb was refused (WO-1518)");
            ClearNotice();
        }

        private void GoTo(ManageNavEntry entry)
        {
            if (entry == null) { CloseRequested?.Invoke(); return; }
            ClearStaleNotice(TabWordOf(entry.Tab) + " " + entry.Kind);
            _nav = entry;
            if (!string.IsNullOrEmpty(entry.Filter)) _activeFilter = entry.Filter;
            PlayerPrefs.SetInt(LastTabPrefKey, (int)entry.Tab);
            SelectTabForId(entry.Tab);
        }

        /// <summary>
        /// A snapshot of the current screen, used as a jump's ORIGIN (ruling 28).
        ///
        /// <para>⚠ The chain is PRESERVED, not flattened. Ruling 28 asks that a nested jump
        /// "unwind ONE hop"; keeping each entry's own origin makes every single BACK press
        /// exactly one hop, all the way down, instead of teleporting the player two branches
        /// away on the second press. Depth is capped at <see cref="MaxOriginDepth"/> so a cycle
        /// cannot grow the chain forever.</para>
        /// </summary>
        private ManageNavEntry SnapshotForOrigin()
        {
            if (_nav == null) return null;
            var copy = new ManageNavEntry
            {
                Kind = _nav.Kind,
                Tab = _nav.Tab,
                ItemId = _nav.ItemId,
                SchoolId = _nav.SchoolId,
                Filter = _nav.Filter,
                Origin = _nav.Origin
            };
            int depth = 0;
            for (var walk = copy; walk != null; walk = walk.Origin)
            {
                if (++depth < MaxOriginDepth) continue;
                walk.Origin = null;   // truncate rather than recurse forever
                break;
            }
            return copy;
        }

        /// <summary>Open one item's DETAIL screen. <paramref name="origin"/> non-null means a JUMP.</summary>
        public void OpenDetail(ManageTabId tab, string itemId, string schoolId, ManageNavEntry origin)
        {
            GoTo(new ManageNavEntry
            {
                Kind = ManageScreenKind.Detail,
                Tab = tab,
                ItemId = itemId,
                SchoolId = schoolId,
                Filter = tab == ManageTabId.Build ? _activeFilter : null,
                Origin = origin
            });
        }

        /// <summary>Open one research school's perk list (canon 5: school first, then its perks).</summary>
        public void OpenSchool(string buildingId, ManageNavEntry origin)
        {
            GoTo(new ManageNavEntry
            {
                Kind = ManageScreenKind.ResearchPerks,
                Tab = ManageTabId.Research,
                SchoolId = buildingId,
                Origin = origin
            });
        }

        /// <summary>
        /// BACK, one hop. Owner ruling 28: back from a screen entered BY A JUMP returns to the
        /// screen that SENT the player there; back from the same screen reached by BROWSING walks
        /// the tree. That is why <see cref="ManageNavEntry.Origin"/> exists at all - a plain screen
        /// history returns to the grid, because that is literally where the player came from.
        ///
        /// <para>Tree parents: Detail -> its grid (or its school's perk list), ResearchPerks -> the
        /// RESEARCH school grid, Grid -> out of Manage. Back never routes through the retired
        /// four-tile launcher, because there is no longer one to route through.</para>
        /// </summary>
        public void Back()
        {
            if (_nav == null) { CloseRequested?.Invoke(); return; }

            if (_nav.Origin != null)
            {
                FlowTrace.Step("Navigation", "Manage BACK -> jump origin (" + _nav.Origin.Kind + " " +
                    TabWordOf(_nav.Origin.Tab) + " '" + (_nav.Origin.ItemId ?? _nav.Origin.SchoolId ?? "-") +
                    "') - ruling 28, the back stack remembers WHY");
                GoTo(_nav.Origin);
                return;
            }

            switch (_nav.Kind)
            {
                case ManageScreenKind.Detail:
                    if (_nav.Tab == ManageTabId.Research && !string.IsNullOrEmpty(_nav.SchoolId))
                        OpenSchool(_nav.SchoolId, null);
                    else
                        GoTo(new ManageNavEntry
                        {
                            Kind = ManageScreenKind.Grid, Tab = _nav.Tab, Filter = _activeFilter
                        });
                    return;
                case ManageScreenKind.ResearchPerks:
                    GoTo(new ManageNavEntry { Kind = ManageScreenKind.Grid, Tab = ManageTabId.Research });
                    return;
                default:
                    if (_nav.Tab == ManageTabId.Build &&
                        !string.Equals(_activeFilter, BuildFilter.All, StringComparison.OrdinalIgnoreCase))
                    {
                        FlowTrace.Step("Navigation", "Manage BACK from BUILD " + _activeFilter +
                            " -> category grid");
                        SetFilter(BuildFilter.All);
                        return;
                    }
                    FlowTrace.Step("Navigation", "Manage BACK from a root grid -> close");
                    CloseRequested?.Invoke();
                    return;
            }
        }

        /// <summary>
        /// The composer's route handler (ruling 18: every blocker names a door that opens).
        /// A screen entered through here carries the screen that SENT the player as its ORIGIN,
        /// which is what makes the jump a round trip (ruling 28).
        /// </summary>
        private void Navigate(ManageRoute route)
        {
            var origin = SnapshotForOrigin();
            switch (route.Kind)
            {
                case ManageRouteKind.BuildCard:
                    _activeFilter = BuildFilter.All;   // the target may not live in the current chip
                    OpenDetail(ManageTabId.Build, route.TargetId, null, origin);
                    return;
                case ManageRouteKind.BuildTab:
                    GoTo(new ManageNavEntry
                    {
                        Kind = ManageScreenKind.Grid, Tab = ManageTabId.Build,
                        Filter = BuildFilter.All, Origin = origin
                    });
                    return;
                case ManageRouteKind.ArmyTab:
                    GoTo(new ManageNavEntry
                    {
                        Kind = ManageScreenKind.Grid, Tab = ManageTabId.Army, Origin = origin
                    });
                    return;
                case ManageRouteKind.ResearchTab:
                    if (!string.IsNullOrEmpty(route.TargetId)) OpenSchool(route.TargetId, origin);
                    else GoTo(new ManageNavEntry
                    {
                        Kind = ManageScreenKind.Grid, Tab = ManageTabId.Research, Origin = origin
                    });
                    return;
                case ManageRouteKind.HeartCard:
                    // The Heart is its own panel, OUTSIDE the Manage screen graph, so there is no
                    // Manage screen to push. Its own Close returns the player here.
                    OpenHeartRequested?.Invoke();
                    return;
                case ManageRouteKind.Queue:
                    // The Queue is an OVERLAY over whatever screen the player is on (the owner's
                    // flow: "reachable from every screen's header"), so it never pushes either.
                    OpenQueueRequested?.Invoke();
                    return;
                default:
                    FlowTrace.Warn("Manage", "a CTA routed to ManageRouteKind.None - ruling 18 says every " +
                        "blocker names a destination that opens; nothing happened");
                    return;
            }
        }

        // ── the workspace VM ─────────────────────────────────────────────────

        /// <summary>
        /// Compose the whole workspace for <see cref="ManageWorkspacePanel"/>. Called by the View
        /// on every <see cref="Changed"/>; it reads live model state and returns finished records.
        /// </summary>
        public ManageWorkspaceVM ComposeWorkspace()
        {
            RefreshAvailableTabs();

            // ⛔ COMPOSE NEVER WRITES NAV STATE. This is a PROJECTION - it runs on every Changed,
            // and a projection that assigns _nav could clobber a legitimate Detail screen the
            // moment a Render fires between a navigation and its rebuild. EnterTab / OpenDetail /
            // OpenSchool / Back / GoTo are the ONLY writers. A screen that cannot be painted is
            // REPORTED and rendered as an explicit empty state, never silently redirected.
            ManageNavEntry nav = _nav;
            bool navPaintable = nav != null && _availableTabs.Contains(nav.Tab);
            if (nav != null && !navPaintable)
                FlowTrace.Warn("Manage", "the current screen sits on tab " + nav.Tab + ", which this build " +
                    "no longer offers - the workspace paints an empty state and says so rather than " +
                    "silently redirecting the player");
            else if (nav == null)
                FlowTrace.Warn("Manage", "ComposeWorkspace ran before any screen was entered - painting the " +
                    "empty state. OpenDefaultScreen is what chooses the opening tab (WO-2001 Entry)");

            var tabs = new List<ManageTabVM>(_availableTabs.Count);
            int activeIndex = 0;
            for (int i = 0; i < _availableTabs.Count; i++)
            {
                ManageTabId id = _availableTabs[i];
                bool isActive = navPaintable && id == nav.Tab;
                if (isActive) activeIndex = i;
                ManageTabId captured = id;
                var tab = new ManageTabVM
                {
                    Id = id,
                    Label = TabWordOf(id),
                    IsActive = isActive,
                    Activate = () => EnterTab(captured),
                    // ⛔ READ OFF THE OWNER'S MOCKUP, PANEL BY PANEL - not derived, not tuned.
                    // docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png:
                    //   screen 2 BUILD    - ten buildings as 5 columns x 2 rows
                    //   screen 4 ARMY     - "All 9 troops visible, no scrolling" => 3 x 3
                    //   screen 6 RESEARCH - four research buildings in one row => 4 x 1
                    // BUILD was 4 columns and its row count fell out of whatever band was left,
                    // which is how the capture showed FOUR tiles of seventeen under the ALL chip.
                    // ⛔ RESEARCH HAS TWO SCREENS AND THEY ARE DIFFERENT SHAPES. The tab alone does
                    // not decide this; the nav KIND does.
                    //   panel 6, the PICKER  - four research BUILDINGS in ONE row, art large
                    //   panel 7, the TREE    - a vertical LIST of that building's upgrades
                    // One column is what makes the renderer lay rows instead of cards, and it is the
                    // MODEL saying so - the View never decides a layout from an id.
                    // ⚠ The RESEARCH PICKER's 4 x 1 below is a SEED ONLY - it is overwritten from
                    // the live school count after FillActiveTab. See ApplyPickerCapacity (WO-1564).
                    GridColumns = id == ManageTabId.Build ? 5
                                : id == ManageTabId.Research
                                    ? (isActive && nav != null && nav.Kind == ManageScreenKind.ResearchPerks ? 1 : 4)
                                    : 3,
                    GridRows = id == ManageTabId.Build ? 2
                             : id == ManageTabId.Research
                                 ? (isActive && nav != null && nav.Kind == ManageScreenKind.ResearchPerks ? 4 : 1)
                                 : 3
                };
                if (isActive) FillActiveTab(tab, nav);
                if (isActive && id == ManageTabId.Build && nav != null &&
                    nav.Kind == ManageScreenKind.Grid &&
                    string.Equals(_activeFilter, BuildFilter.All, StringComparison.OrdinalIgnoreCase))
                {
                    tab.GridColumns = BuildFilter.Membership.Length;
                    tab.GridRows = 1;
                }
                if (isActive) ApplyPickerCapacity(tab, id, nav);
                tabs.Add(tab);
            }

            // The explicit empty state (see the projection note above). The renderer paints
            // Tabs[ActiveTabIndex] whatever IsActive says, so the seat that WILL be painted is the
            // one that carries the sentence - a blank screen with no words is the failure this
            // whole program exists to remove (canon 11 question 6).
            if (!navPaintable && tabs.Count > 0)
            {
                tabs[0].Tiles = Array.Empty<ManageTileVM>();
                tabs[0].EmptyText = "Pick a tab above to start.";
                // The sentence lives on the GRID's EmptyText (above), which is the band that is
                // actually empty. The selection band carries no copy at all - WO-1443 section 3.
                tabs[0].Selection = new ManageSelectionVM { Visible = false, EmptyText = null };
                tabs[0].Activity = new ManageActivityVM { Visible = false };
            }

            // ⭐ A DETAIL SCREEN IS TITLED WITH THE ITEM'S OWN NAME, not a breadcrumb.
            // Mockup panels 3, 5 and 9 are headed LUMBER MILL / ARCHER / OUTRIDER with the level
            // beneath - never "MANAGE / ARMY / DETAIL". The capture showed two defects from that one
            // cause: the breadcrumb was long enough to run under the QUEUE pill and clip, and the
            // troop's NAME appeared nowhere on the panel at all - a horseman, a description and a
            // requirement, with nothing saying which unit it was.
            // The name is taken from the ACTIVE TAB'S SELECTION, which is the model's own composed
            // title for the thing on screen; the View is not deriving it from an id.
            string headerTitle = HeaderTitle(navPaintable ? nav : null);
            if (activeIndex >= 0 && activeIndex < tabs.Count)
            {
                var activeSel = tabs[activeIndex].Selection;
                if (activeSel != null && activeSel.Visible && !string.IsNullOrEmpty(activeSel.Title))
                    // CAPS, like every other title on this screen and like the mockup's own detail
                    // headings (OUTRIDER / LUMBER MILL / ARCHER). The capture read "Outrider".
                    headerTitle = activeSel.Title.ToUpperInvariant();
            }
            // ⭐ THE RESEARCH TREE IS TITLED WITH ITS BUILDING - mockup panel 7 heads it
            // "CATHEDRAL OF MAGIC", not a breadcrumb. Same fix, same reason as the detail card: the
            // capture showed "MANAGE / RESEARCH / ..." truncated, and the building whose tree the
            // player is reading was named nowhere on the screen.
            // ⚠ A perks screen is a GRID, so its Selection is not visible and the branch above never
            // fires for it - this is the second case, not a duplicate of the first.
            // The name comes from the model's own ResearchChoiceVM.BuildingName ("Lumber Mill"),
            // never assembled from the school id.
            if (navPaintable && nav.Kind == ManageScreenKind.ResearchPerks && !string.IsNullOrEmpty(nav.SchoolId))
            {
                for (int i = 0; i < ResearchChoices.Count; i++)
                {
                    var rc = ResearchChoices[i];
                    if (rc == null || !string.Equals(rc.BuildingId, nav.SchoolId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrEmpty(rc.BuildingName)) break;
                    headerTitle = Ascii(rc.BuildingName).ToUpperInvariant();
                    break;
                }
            }

            return new ManageWorkspaceVM
            {
                HeaderTitle = headerTitle,
                Tabs = tabs,
                ActiveTabIndex = activeIndex,
                Queue = ComposeQueueDoor()
            };
        }

        /// <summary>
        /// The ONE separator between MANAGE and the screen word.
        /// <para>⭐ WO-1491 - IT IS A HYPHEN, NOT A SLASH. The mockup sheet heads panel 2
        /// "MANAGE - BUILD" and panel 6 "MANAGE - RESEARCH"; the device build read
        /// "MANAGE / BUILD" (Logs/device/screens/owner-screen-20260907-010356.png). A slash reads
        /// as a file path, a hyphen reads as a title - and the acceptance on this wave is the
        /// drawing, exactly.</para>
        /// <para>⛔ ONE constant, read by all three arms below. Typing " - " three times is how the
        /// next re-spelling lands on two of them.</para>
        /// </summary>
        private const string HeaderJoiner = " - ";

        private static string HeaderTitle(ManageNavEntry nav)
        {
            if (nav == null) return "MANAGE";
            if (nav.Kind == ManageScreenKind.Detail)
                return "MANAGE" + HeaderJoiner + TabWordOf(nav.Tab) + HeaderJoiner + "DETAIL";
            if (nav.Kind == ManageScreenKind.ResearchPerks)
                return "MANAGE" + HeaderJoiner + "RESEARCH" + HeaderJoiner + "SCHOOL";
            if (nav.Kind == ManageScreenKind.Grid && nav.Tab == ManageTabId.Build &&
                !string.IsNullOrEmpty(nav.Filter) &&
                !string.Equals(nav.Filter, BuildFilter.All, StringComparison.OrdinalIgnoreCase))
                return "MANAGE" + HeaderJoiner + nav.Filter;
            return "MANAGE" + HeaderJoiner + TabWordOf(nav.Tab);
        }

        // ⛔ HeaderSubtitle IS DELETED - WO-1443 section 1 (owner felt-test 2026-09-06, "remove the
        // manage army and sub line replace the manage top"). It used to return, per screen:
        //   Detail        -> "Back returns to where you came from."   (the BACK button says this)
        //   ResearchPerks -> "Pick a perk to see what it does."       (the tiles say this)
        //   Build         -> "Filter: <chip>"                         (the FILTER CHIP ROW says this
        //                                                              - ComposeFilters marks the
        //                                                              active chip IsActive, so no
        //                                                              information is lost)
        //   Army          -> "Every troop, unlocked or not."          (the tiles say this)
        //   Research      -> "Pick a school, then a perk."            (the tiles say this)
        // Every line restated something already on screen, which is the owner's whole objection.
        // The one line with real state - the Build filter - is carried by the chips.

        /// <summary>
        /// The channel the QUEUE OVERLAY is showing (mockup panel 8's selected tab). Defaults to
        /// the browse tab's channel on every Rebuild until the player picks a tab, after which the
        /// pick sticks for the life of the screen.
        /// </summary>
        public ChannelId QueueOverlayChannel { get; private set; } = ChannelId.Builder;
        private bool _queueOverlayChannelPinned;

        /// <summary>
        /// The player picked an overlay tab. Pins <see cref="QueueOverlayChannel"/> so the next
        /// Rebuild does not snap it back to the browse tab's line, then rebuilds so the rows follow.
        /// </summary>
        public void SelectQueueOverlayChannel(ChannelId channel)
        {
            _queueOverlayChannelPinned = true;
            QueueOverlayChannel = channel;
            FlowTrace.Step("Manage", "queue overlay -> " + BuildTimerService.ChannelWord(channel));
            Rebuild();
        }

        /// <summary>
        /// Panel 8's three tabs: BUILDERS (n/n) / TRAINING (n/n) / RESEARCH (n/n).
        ///
        /// <para>⛔ THE COUNTS ARE THE CHANNEL SUMMARIES' OWN, never recomputed and never
        /// literal. <c>ChannelSummary.Busy</c> / <c>.Slots</c> are already filled for all three
        /// channels by BuildChannelSummaries, which is the same source the three-line status strip
        /// reads - so a tab cannot drift from the strip beside it. The mockup's "2/2" is TODAY'S
        /// STATE, not the spec: hardcoding it would make the tab lie the moment the player buys a
        /// builder and the crew grows.</para>
        /// <para>⚠ THE SERVICE METHOD THAT DOES THAT IS DELIBERATELY NOT NAMED HERE.
        /// BuilderSkuRegression [manage] does a RAW Contains for its token on this file's text, with
        /// no comment stripping, to prove Manage never spends crystals on a slot itself - so writing
        /// the name in PROSE reds a monetization suite that this code has not actually violated.
        /// It cost a round; the rule it guards is real and intact (BuySlot routes to the store via
        /// StoreFocusRequest.RequestFocusSku(PackCatalog.PermanentBuilderSku), :2508).
        /// This is the SECOND time in two days a comment has tripped a source oracle here - the
        /// other was HudLabelFitRegression, caught at the gate. If a third appears, the fix is to
        /// make those suites strip comments, not to keep censoring prose.</para>
        /// </summary>
        private List<ManageQueueTabVM> ComposeQueueTabs()
        {
            var tabs = new List<ManageQueueTabVM>(Channels.Count);
            for (int i = 0; i < Channels.Count; i++)
            {
                // NO NULL GUARD: ChannelSummary is a STRUCT (:70), so a list entry can never be
                // null and `summary == null` does not compile (CS0019, caught at the gate
                // 2026-09-06). AddSummary fills all three channels, so every entry is real.
                var summary = Channels[i];
                ChannelId captured = summary.Channel;
                tabs.Add(new ManageQueueTabVM
                {
                    Channel = captured,
                    Label = Ascii(BuildTimerService.ChannelWord(captured)).ToUpperInvariant(),
                    CountText = summary.Busy + "/" + summary.Slots,
                    IsActive = captured == QueueOverlayChannel,
                    Activate = () => SelectQueueOverlayChannel(captured)
                });
            }
            return tabs;
        }

        /// <summary>
        /// ⭐ WO-1488 - THE QUEUE OVERLAY'S EMPTY STATE, IN THIS CHANNEL'S OWN VERB.
        ///
        /// <para>⛔ IT WAS A VIEW LITERAL AND IT NAMED THE WRONG VERB. ManageScreenPanel typed
        /// <i>"Nothing is queued on this line. Start an upgrade to see it here."</i> for all three
        /// channels, and the slot line under it said <i>"tap TRAIN to fill them"</i> on every one -
        /// so the owner's RESEARCH tab (Logs/device/screens/owner-screen-20260907-010257.png) told
        /// her to tap TRAIN. A sentence that names the wrong door is worse than no sentence: it
        /// sends the player to a screen that cannot help.</para>
        ///
        /// <para>The verb comes from <see cref="QueueChannelVerb"/>, ONE table, read by this
        /// sentence and by the slot-offer line so the two can never disagree.</para>
        /// </summary>
        public string QueueEmptyText { get; private set; } = string.Empty;

        /// <summary>
        /// The DOOR VERB for a queue line - what the player taps to put work on it.
        /// BUILD / TRAIN / RESEARCH, matching the three faces on the overlay's tab row.
        /// <para>⛔ A CHANNEL -&gt; VERB TABLE, MODEL-SIDE, ONE COPY. The View may not switch on a
        /// ChannelId to pick a word (canon 9), and a second copy of this table beside the slot
        /// offer is exactly the drift that let "tap TRAIN" reach the RESEARCH tab.</para>
        /// </summary>
        public static string QueueChannelVerb(ChannelId channel)
        {
            switch (channel)
            {
                case ChannelId.Train: return "TRAIN";
                case ChannelId.Research: return "RESEARCH";
                default: return "BUILD";
            }
        }

        private void BuildQueueEmptyText(ChannelId channel)
        {
            string verb = QueueChannelVerb(channel);
            QueueEmptyText = "Nothing is queued on this line. Tap " + verb + " to start something.";
        }

        /// <summary>Panel 8's tab list, rebuilt with the rows. The View binds it and picks nothing.</summary>
        public IReadOnlyList<ManageQueueTabVM> QueueTabs => _queueTabs;
        private List<ManageQueueTabVM> _queueTabs = new List<ManageQueueTabVM>(3);

        private ManageQueueVM ComposeQueueDoor()
        {
            ChannelId channel = ChannelOf(Tab);
            // `Busy` is deliberately NOT read any more: it fed the retired "n RUNNING" / "IDLE"
            // word on the deleted second line (WO-1443 section 1B).
            int depth = 0, cap = 0;
            for (int i = 0; i < Channels.Count; i++)
            {
                if (Channels[i].Channel != channel) continue;
                depth = Channels[i].Depth;
                cap = Channels[i].DepthCap;
                break;
            }
            // ⭐ WO-1443 section 1B, owner ruling 2026-09-06. The QUEUE affordance MOVED into the
            // tab row and the separate "IDLE . 0 OF 5" line under it is DELETED - the count rides
            // on the face. What survives is the information that changes a decision: how full the
            // line is, and the word FULL when it will refuse. "IDLE" said nothing "0 OF 5" did not
            // already say, which is the owner's standing objection to this whole screen.
            //
            // ⛔ Visible STAYS unconditionally true, and that is a DOOR guarantee, not a default.
            // MEASURED 2026-09-06: in workspace mode this is the ONLY live route to the queue when
            // nothing is running - ManageScreenPanel.ShowWorkspace deactivates the legacy header
            // toggle, the operational OPEN QUEUE bands live in a list band that is SetActive(false),
            // the activity strip is Visible=false while idle, and the HUD Builders chip's door was
            // retired in WO-911. Making this conditional strands the queue (WO-1430's defect class).
            // ⛔ KEEP FaceCountText SHORT - IT IS ONE QUARTER OF A TAB ROW, NOT A LINE OF ITS OWN.
            // MEASURED in Builds/ui-capture/ManageFlow_Troops_railtop_2670x1200.png (2026-09-06,
            // 14:59): the first draft composed "FULL 5 OF 5", the face read
            //   QUEUE  .  FULL 5 O...
            // and the count the field exists to show was the part that got cut. The face cannot be
            // fixed by shrinking the type - ElarionUiKit's FontFloor is a FLOOR and a band under
            // ~24px renders BLANK, not small (this file's renderer states the same law). So the fix
            // is FEWER CHARACTERS, exactly as HudKitController.cs:1866-1878 records for the rail
            // chip, where "Tap to collect" was authored down rather than scaled down.
            //   normal     -> "0/5"   => face "QUEUE 0/5"   (9 chars)
            //   at capacity-> "FULL"  => face "QUEUE FULL"  (10 chars)
            // FULL replaces the digits rather than joining them: "5/5" already implies it, and the
            // WORD is what survives greyscale and colourblindness. The exact depth is one tap away
            // in the drawer, which is what the door opens.
            bool full = cap > 0 && depth >= cap;
            return new ManageQueueVM
            {
                Visible = true,
                Label = "QUEUE",
                FaceCountText = cap > 0 ? (full ? "FULL" : depth + "/" + cap) : null,
                Open = () => OpenQueueRequested?.Invoke()
            };
        }

        private ManageActivityVM ComposeActivity()
        {
            ChannelId channel = ChannelOf(Tab);
            int queued = 0;
            QueueRowVM running = null;
            for (int i = 0; i < QueueRows.Count; i++)
            {
                var r = QueueRows[i];
                if (r == null || r.IsStackChild) continue;
                if (r.Queued) { queued += Mathf.Max(1, r.StackCount); continue; }
                if (running == null) running = r;
            }
            if (running == null)
                return new ManageActivityVM { Visible = false };
            return new ManageActivityVM
            {
                Visible = true,
                IconKey = null,
                Title = Ascii(running.Label ?? ""),
                TimerText = Ascii(running.StateText ?? ""),
                // No OpenQueue: the strip is a status glance and the tab-row door is the one entry
                // (WO-1443, after the 2026-09-06 capture showed both on screen at once).
                QueuedCountText = queued > 0 ? queued + " QUEUED" : null
            };
        }

        /// <summary>
        /// Fill the ACTIVE tab. Grid screens carry tiles and an invisible selection; detail screens
        /// carry a visible selection and NO tiles - the owner's flow mockup keeps them apart, and
        /// the band arithmetic makes that the only shape that fits (see this region's header).
        /// </summary>
        private void FillActiveTab(ManageTabVM tab, ManageNavEntry nav)
        {
            // ⛔ NO ACTIVITY STRIP ON A MANAGE SCREEN. Do not re-enable it here.
            // docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png draws NINE panels and not one of them
            // carries a "what is running" strip: screens 2/4/6 are title + chips + grid, and 3/5/7
            // are title + detail. Running work lives in TWO places in the mockup and both already
            // exist - the red count badge on the QUEUE pill, and the QUEUE overlay (screen 8) that
            // the pill opens. A third copy on every screen is the duplicated state this project
            // keeps paying for (CLAUDE.md 2 / 5 / 16).
            // It also cost 132px of the band on every screen, and the measured shortfall is the
            // whole defect: MANAGE_FLOW_INVENTORY ARMY reported content=590px in a 190px viewport
            // while the mockup's screen 4 says "All 9 troops visible, no scrolling". The strip was
            // spending the space the tiles need.
            // ComposeActivity is KEPT, not deleted: the overlay lane (screen 8) needs exactly that
            // projection, and deleting it would make the next seat re-derive it.
            tab.Activity = new ManageActivityVM { Visible = false };

            if (nav.Kind == ManageScreenKind.Detail)
            {
                tab.Tiles = Array.Empty<ManageTileVM>();
                tab.EmptyText = null;
                tab.Selection = ComposeDetail(nav);
                return;
            }

            switch (nav.Tab)
            {
                case ManageTabId.Army:
                    tab.Tiles = ComposeArmyTiles();
                    tab.EmptyText = "No troops are authored for this barracks yet.";
                    break;
                case ManageTabId.Research:
                    if (nav.Kind == ManageScreenKind.ResearchPerks)
                    {
                        tab.Tiles = ComposeResearchPerkTiles(nav.SchoolId);
                        tab.EmptyText = "This school authors no perks yet.";
                        // ⭐ WO-1567 PANEL ROW 8 - THE SCHOOL'S OWN PAINTING, left of the rows.
                        // Mockup panel 7 gives the tree a large square picture of the building
                        // whose perks it lists; the owner's capture
                        // (Logs/device/screens/owner-screen-20260907-010151.png) has no picture
                        // anywhere on the screen - just four rows against black.
                        // ⛔ ONE KEY PRODUCER: ManageArt.BuildingPortraitKey against the catalog
                        // ID at level 1, exactly as the research PICKER binds its tiles. The
                        // display-name slug that used to compose these keys is deleted (WO-1567
                        // section 5 item 3) and must not come back.
                        tab.HeaderArtKey = string.IsNullOrEmpty(nav.SchoolId)
                            ? null : ManageArt.BuildingPortraitKey(nav.SchoolId, 1);
                    }
                    else
                    {
                        tab.Tiles = ComposeResearchSchoolTiles();
                        tab.EmptyText = "Build a research structure to open a school.";
                    }
                    break;
                default:
                    // Owner ruling 2026-09-08: BUILD opens on categories, not the full inventory.
                    // ALL remains the internal root sentinel and is no longer a player-facing chip.
                    tab.Filters = Array.Empty<ManageFilterVM>();
                    tab.Tiles = string.Equals(_activeFilter, BuildFilter.All,
                        StringComparison.OrdinalIgnoreCase)
                        ? ComposeBuildCategoryTiles()
                        : ComposeBuildTiles();
                    tab.EmptyText = string.Equals(_activeFilter, BuildFilter.All,
                        StringComparison.OrdinalIgnoreCase)
                        ? "No building categories are authored yet."
                        : "Nothing in this category yet.";
                    break;
            }

            // ⛔ WO-1443 section 3 - NO HINT SENTENCE HERE. Owner felt-test 2026-09-06, verbatim:
            // "dont need the bottom line, close button is enough". The old EmptyText ("Pick one to
            // see what it does, what it costs and what you can do.") explained something the screen
            // already makes obvious - you tap a troop, you see the troop - and it was the ONLY
            // content in a bordered band worth roughly 40% of her screen. With the sentence gone the
            // band has nothing to hold, so ManageWorkspacePanel.Build COLLAPSES it to 0px whenever
            // Visible is false and the grid takes the room. Re-adding a sentence here silently
            // un-collapses nothing - the band is keyed on Visible - but it does put the copy back on
            // a screen the owner asked to have it removed from.
            tab.Selection = new ManageSelectionVM { Visible = false, EmptyText = null };
        }

        private List<ManageTileVM> ComposeBuildCategoryTiles()
        {
            var categories = BuildFilter.Membership; // the ONE ordering authority - never re-listed
            var tiles = new List<ManageTileVM>(categories.Length);
            for (int i = 0; i < categories.Length; i++)
            {
                string category = categories[i];
                var rows = BuildInventoryModel.Tiles(category);
                string portraitKey = null;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var item = rows[rowIndex] != null ? ComposeBuildItem(rows[rowIndex]) : null;
                    if (item == null || string.IsNullOrEmpty(item.IconId)) continue;
                    portraitKey = item.IconId;
                    break;
                }

                string captured = category;
                tiles.Add(new ManageTileVM
                {
                    Id = "build-category:" + category,
                    Title = category,
                    PortraitKey = portraitKey,
                    VisualState = ManageTileVisualState.Available,
                    FrameKey = ManageArt.FrameFor(ManageTileVisualState.Available),
                    Activate = () => SetFilter(captured)
                });
            }
            return tiles;
        }

        // ── BUILD ────────────────────────────────────────────────────────────

        private BuildingChoiceVM BuildingChoiceFor(string catalogEntryId, string ladderId)
        {
            for (int i = 0; i < BuildingChoices.Count; i++)
            {
                var c = BuildingChoices[i];
                if (c == null) continue;
                if (!string.IsNullOrEmpty(catalogEntryId) &&
                    string.Equals(c.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase)) return c;
                if (!string.IsNullOrEmpty(ladderId) &&
                    string.Equals(c.Id, ladderId, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        private DefenseChoiceVM DefenseChoiceFor(string catalogEntryId)
        {
            if (string.IsNullOrEmpty(catalogEntryId)) return null;
            for (int i = 0; i < DefenseChoices.Count; i++)
            {
                var c = DefenseChoices[i];
                if (c == null) continue;
                if (string.Equals(c.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase)) return c;
                if (string.Equals(c.Id, catalogEntryId, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        /// <summary>
        /// The BUILD grid. The AUTHORITATIVE inventory comes from
        /// <see cref="DeNelle.Village.BuildInventoryModel"/> (canon 3: "the model must expose the
        /// authoritative live list", ruling 20: reconcile before locking any numeric test), and the
        /// live per-structure state is joined on from the choice VMs this class already builds.
        /// A row with no matching upgrade choice is checked against BaseLayout before it is
        /// called unplaced. Civic singletons such as Store, Crafting Station, and Echo Hollow
        /// deliberately have no upgrade ladder, but are still owned structures in this town.
        /// </summary>
        /// <summary>
        /// The reconciled BUILD inventory for THIS rebuild and THIS chip.
        /// <see cref="DeNelle.Village.BuildInventoryModel.Rows"/> says in its own header that it is
        /// "Rebuilt on every call" - it walks the whole catalog and reconciles every entry - and a
        /// Manage rebuild fires on every BuildTimerService.QueueChanged, so an uncached read would
        /// reconcile the catalog several times a minute while the screen is open. Cleared by
        /// <see cref="Rebuild"/>, which is the only thing that can invalidate it: the inputs it reads
        /// (catalog + unlock flags + collections) cannot change without one.
        /// </summary>
        /// <remarks>
        /// ⛔ THE FILTER RULE IS NOT RE-IMPLEMENTED HERE. The cache holds what
        /// <see cref="DeNelle.Village.BuildInventoryModel.Tiles"/> returned for the chip in force -
        /// that method stays the ONE authority on membership and on which rows are live content.
        /// Re-deriving the membership test beside it, to make the cache chip-independent, would be
        /// the duplicated state CLAUDE.md 2 / 5 / 16 records three times over.
        /// </remarks>
        private List<BuildInventoryRow> _inventoryTiles;
        private string _inventoryChip;

        private List<BuildInventoryRow> InventoryTiles()
        {
            if (_inventoryTiles != null &&
                string.Equals(_inventoryChip, _activeFilter, StringComparison.OrdinalIgnoreCase))
                return _inventoryTiles;
            // ⛔ Tiles, NOT ManageTiles - WO-1516, OWNER RULING 2026-09-06 20:07, VERBATIM:
            // "manage build scren should only show items that are unlocked and avaliable to them".
            // She took Logs/device/screens/owner-screen-20260906-200741.png in the same minute:
            // eight DEFENSE tiles, five of them placeholder discs, no locked/unlocked distinction
            // visible anywhere on the screen.
            //
            // ⛔ THIS RETIRES THE PARAGRAPH THAT USED TO STAND HERE ("ManageTiles, not Tiles: the
            // Manage grid SHOWS not-yet-unlocked rows as locked tiles ... mockup panel 9"). That
            // reading of the mockup was an inference from a picture; this is the owner's sentence,
            // and a sentence outranks an inference drawn from a drawing. BuildInventoryModel
            // .ManageTiles is left in place and untouched - it is not deleted, because the ARMY
            // grid's locked-troop treatment (mockup panel 4) still stands and a future ruling may
            // want it back - but the BUILD grid no longer calls it.
            //
            // ⭐ ONE PREDICATE, AND IT IS THE PALETTE'S OWN. BuildInventoryModel.Tiles is the
            // accessor whose own doc comment records that it matches the browser
            // (BuildCollectionBrowser, "hide cards that are not unlocked"): it admits exactly
            // BuildAvailability.Offered, which Reconcile derives from the same unlock flags the
            // palette reads. The Manage grid and the Build palette therefore answer "is this
            // unlocked" from ONE authority. Never write a second unlock test beside this call.
            //
            // ⚠ AFFORDABILITY IS NOT AN UNLOCK (WO-1516 section 3). An unlocked row the player
            // cannot pay for STAYS on the grid and gets WORDS - a player must be able to see what
            // they are saving for. The filter here is unlock only.
            _inventoryTiles = BuildInventoryModel.Tiles(_activeFilter);
            _inventoryChip = _activeFilter;
            return _inventoryTiles;
        }

        private List<ManageTileVM> ComposeBuildTiles()
        {
            var rows = InventoryTiles();
            var tiles = new List<ManageTileVM>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                var item = ComposeBuildItem(row);
                if (item == null) continue;
                string id = item.ItemId;
                // ⭐ THE FIRST TILE IS SELECTED BY DEFAULT, on every grid in this file.
                // docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png never draws a grid with nothing
                // selected: screen 2 shows Lumber Mill with the gold border, screen 4 shows Archer,
                // screen 6 shows Cathedral of Magic. That is not decoration - the selected tile is
                // how the screen explains what a tile IS and what tapping one will do. The 2026-09-06
                // capture had no tile selected on any tab, which is a difference from the picture and
                // also a screen that teaches nothing on arrival.
                // `tiles.Count == 0` rather than `i == 0` deliberately: several of these loops skip
                // rows with `continue`, so the first INDEX is not always the first TILE.
                tiles.Add(ProjectAffordanceTile(item, tiles.Count == 0,
                    () => OpenDetail(ManageTabId.Build, id, null, null)));
            }
            return tiles;
        }

        /// <summary>
        /// <see cref="ManageVmProjection.ProjectTile"/> plus ONE model-side rule: the green
        /// up-arrow medallion is painted ONLY when the item can actually be acted on right now.
        ///
        /// <para>⭐ WO-1516 / 1517 / 1518, owner 2026-09-06. Her device frame
        /// (owner-screen-20260906-200741.png) shows the SAME green up-arrow on all eight BUILD
        /// tiles, and owner-screen-20260906-201242.png shows it again beside a research row reading
        /// SHORT. The acceptance line in all three tickets is identical: the badge "either states a
        /// REAL affordance (upgrade available / can build) or it is removed".</para>
        ///
        /// <para>⛔ THE TEST IS THE PRIMARY ACTION'S OWN AVAILABILITY, NOT A SECOND DERIVATION.
        /// <see cref="ManageArt.StatusFor"/> has four DISTINCT glyphs (locked / in-progress /
        /// queue / max) and one CATCH-ALL, <c>status-available</c>, which
        /// <see cref="ManageVmProjection.VisualStateFor"/> hands to READY, SHORT and HEART GATED
        /// alike - so on the four distinct states the medallion already tells the truth and is left
        /// exactly as it is. It is only the catch-all that lies, and it lies precisely when the
        /// primary action is refused. So: keep the glyph when the composer marked the primary
        /// action <see cref="ManageActionAvailability.Available"/>; drop it otherwise. The tile
        /// then loses NO information - the refusal already has a home on the detail card's why
        /// band, which is the affordance ruling 15 asks for.</para>
        ///
        /// <para>⚠ A null <c>StateIconKey</c> is a SUPPORTED value, not a hole:
        /// ManageWorkspacePanel.PaintSprite (:1234-1243) resolves a null key to a null sprite and
        /// sets the Image fully transparent, so the slot simply carries nothing. Verified at source,
        /// not assumed.</para>
        ///
        /// <para>⚠ NOT used by <see cref="ComposeResearchSchoolTiles"/>. A school row carries no
        /// ManageAction at all by design ("a school TILE is pure navigation"), so this rule would
        /// strip every school medallion - a change to a screen nobody reported, on a row whose word
        /// ("2 READY" / "3 PERKS") is already the real state. Left alone deliberately.</para>
        /// </summary>
        private static ManageTileVM ProjectAffordanceTile(ManageItemState item, bool isSelected,
            Action onSelect)
        {
            var tile = ManageVmProjection.ProjectTile(item, isSelected, onSelect);
            if (tile == null || item == null) return tile;
            if (ManageVmProjection.VisualStateFor(item.Badge) != ManageTileVisualState.Available)
                return tile;                       // locked / queue / running / max: a real glyph
            var primary = item.PrimaryAction;
            if (primary != null && primary.Availability == ManageActionAvailability.Available)
                return tile;                       // "you can press this now" - the arrow is true
            tile.StateIconKey = null;
            FlowTrace.Step("Manage", "tile '" + (item.ItemId ?? "?") + "' badge=" +
                (item.BadgeText ?? "") + " carries no live affordance - the status medallion is " +
                "withheld rather than painting the green up-arrow on a refused item (WO-1516)");
            return tile;
        }

        private ManageItemState ComposeBuildItem(BuildInventoryRow row)
        {
            var building = BuildingChoiceFor(row.Id, row.TierLadderId);
            if (building != null) return ComposeBuildingItem(building, row);
            var defense = DefenseChoiceFor(row.Id);
            if (defense != null) return ComposeDefenseItem(defense, row);
            if (IsPlacedThisTown(row.Id)) return ComposeOwnedNoUpgradeItem(row);
            return ComposeUnplacedItem(row);
        }

        /// <summary>Raw catalog-id ownership for structures with no level ladder. This is the
        /// same per-town BaseLayout truth CountPlacedThisTown reads; it intentionally does not
        /// use the monotonic ever-built ledger or a surfaced scene bake.</summary>
        private static bool IsPlacedThisTown(string catalogEntryId)
        {
            if (string.IsNullOrEmpty(catalogEntryId)) return false;
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null) return false;
            for (int i = 0; i < state.BaseLayout.Count; i++)
                if (string.Equals(state.BaseLayout[i].itemId, catalogEntryId,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>An owned civic structure with no authored upgrade track. It has no BUILD
        /// action because the singleton already exists; BUILT is state, not a disabled button.</summary>
        private static ManageItemState ComposeOwnedNoUpgradeItem(BuildInventoryRow row)
        {
            return new ManageItemState
            {
                ItemId = row.Id,
                DisplayName = Ascii(string.IsNullOrEmpty(row.DisplayName) ? row.Id : row.DisplayName),
                IconId = ManageArt.BuildingPortraitKey(row.Id, 0),
                Ownership = ManageOwnership.Owned,
                UpgradeTrack = ManageUpgradeTrack.NotApplicable,
                Level = 0,
                MaxLevel = 0,
                Badge = ManageTileBadge.Idle,
                BadgeText = "BUILT",
                NextRungLine = "Built in this town. No upgrade track is authored."
            };
        }

        /// <summary>Authored, offered, not on the map yet. Owned=NotUnlocked with a door that opens.</summary>
        private ManageItemState ComposeUnplacedItem(BuildInventoryRow row)
        {
            string rowId = row.Id;   // WO-1571: captured by the BUILD action below, by VALUE
            var item = new ManageItemState
            {
                ItemId = row.Id,
                DisplayName = Ascii(string.IsNullOrEmpty(row.DisplayName) ? row.Id : row.DisplayName),
                // ⛔ OWNER RULING 2026-09-06 (Option A): building art is ONE folder, keyed by CATALOG
                // ID. This used to pass the row's manageArtKey straight through - which the catalog's
                // own note calls "the Sheet A tile name for this row", a DELIVERY LABEL and not a
                // Resources key. Being a bare name with no folder, Resources.Load searched the
                // Resources root and every not-yet-built tile rendered the placeholder disc: the four
                // civic tiles the owner captured. manageArtKey stays as the art-to-id join.
                // Pinned by ManagePortraitCoverageRegression [unplaced-uses-building-portrait-key],
                // which greps this method body - so do not name the retired field here, in any comment.
                IconId = ManageArt.BuildingPortraitKey(row.Id, 0),
                Ownership = ManageOwnership.NotUnlocked,
                UpgradeTrack = ManageUpgradeTrack.NotApplicable,
                Level = 0,
                MaxLevel = 0,
                // ⚠ DELIBERATELY *NOT* ManageTileBadge.Locked, and this is an owner question flagged
                // in the hand-back rather than a silent call. Ruling 15 forbids labelling an OWNED
                // item locked; this row is not owned, so the letter of it does not apply - but its
                // reasoning does. ManageArt.StatusFor(Locked) paints a PADLOCK, which says "you
                // cannot have this", while the truth is "tap BUILD". Idle paints the Available
                // medallion and the WORD carries the difference, which is the colourblind-safe
                // channel this project uses everywhere else.
                Badge = ManageTileBadge.Idle,
                BadgeText = "NOT BUILT",
                LockReason = "Not built yet - place one in Town."
            };
            item.Add(new ManageAction
            {
                Kind = ManageActionKind.Build,
                Availability = ManageActionAvailability.Available,
                Cta = "BUILD",
                CostLine = null,
                IsPrimary = true,
                // ⛔ WO-1571 - THIS CARRIES THE ID. It used to be OpenTownBuilderRequested, which
                // drops it and lands on the Build Collections ROOT; that root offers Towers /
                // Walls and Gates / Manage Placed only, so a CRAFT / ECONOMY / STORAGE row was
                // unreachable from its own BUILD button (device 358872, arcane-tower).
                Invoke = () => RequestPlacement(rowId)
            });
            return item;
        }

        /// <summary>
        /// WO-1571 - raise the direct-placement door, and NEVER fail silently (§12). A host that
        /// bound only the legacy town-builder command still gets a working (if root-landing) button
        /// and says so in the trace; a host that bound neither is a dead button and that is a Fail,
        /// not a no-op.
        /// </summary>
        private void RequestPlacement(string structureId)
        {
            if (PlaceStructureRequested != null)
            {
                PlaceStructureRequested.Invoke(structureId);
                return;
            }
            if (OpenTownBuilderRequested != null)
            {
                FlowTrace.Warn("Manage", "BUILD door for '" + structureId + "' fell back to the Build " +
                    "Collections ROOT: this host never bound PlaceStructureRequested. The root authors no " +
                    "ECONOMY/CRAFT/STORAGE collection, so a non-defence row is a DEAD END here (WO-1571).");
                OpenTownBuilderRequested.Invoke();
                return;
            }
            FlowTrace.Fail("Manage", "BUILD door for '" + structureId + "' is WIRED TO NOTHING - the host " +
                "bound neither PlaceStructureRequested nor OpenTownBuilderRequested. The button is dead.");
        }

        private ManageItemState ComposeBuildingItem(BuildingChoiceVM c, BuildInventoryRow row)
        {
            bool atMax = string.Equals(c.StateWord, "Max", StringComparison.OrdinalIgnoreCase)
                         || (c.MaxLevel > 0 && c.Level >= c.MaxLevel);
            var item = new ManageItemState
            {
                ItemId = c.Id,
                DisplayName = Ascii(c.Name),
                IconId = c.IconKey,
                Ownership = ManageOwnership.Owned,
                Level = c.Level,
                MaxLevel = c.MaxLevel,
                UpgradeTrack = c.MaxLevel > 1
                    ? (atMax ? ManageUpgradeTrack.Max : ManageUpgradeTrack.Upgradable)
                    : ManageUpgradeTrack.NotApplicable,
                NextRungLine = Ascii(c.AfterUpgradeText)
            };
            if (item.UpgradeTrack == ManageUpgradeTrack.NotApplicable) item.MaxLevel = 0;

            string costLine = CostFormat.Words(c.UpgradeCostParts);
            bool running = string.Equals(c.StateWord, "Building", StringComparison.OrdinalIgnoreCase);
            item.Add(ComposeUpgradeAction(
                atMax: atMax,
                running: running,
                jobChannel: ChannelId.Builder,
                jobId: c.Id,
                locked: c.Locked,
                lockReason: string.IsNullOrEmpty(c.LockReason)
                    ? "This upgrade needs a higher Heart Level."
                    : Ascii(c.LockReason),
                lockCta: string.IsNullOrEmpty(c.LockCtaLabel) ? "VIEW HEART" : Ascii(c.LockCtaLabel),
                ready: c.UpgradeReady,
                costLine: costLine,
                invoke: c.Activate));

            ApplyBuildBadge(item, atMax, running, c.Locked, c.UpgradeReady, c.UpgradeCostParts);
            return item;
        }

        private ManageItemState ComposeDefenseItem(DefenseChoiceVM c, BuildInventoryRow row)
        {
            bool atMax = string.Equals(c.StateWord, "Max", StringComparison.OrdinalIgnoreCase)
                         || (c.MaxLevel > 0 && c.Level >= c.MaxLevel);
            var item = new ManageItemState
            {
                ItemId = c.Id,
                DisplayName = Ascii(c.Name),
                IconId = c.PortraitKey,
                Ownership = ManageOwnership.Owned,
                Level = c.Level,
                MaxLevel = c.MaxLevel,
                UpgradeTrack = c.MaxLevel > 1
                    ? (atMax ? ManageUpgradeTrack.Max : ManageUpgradeTrack.Upgradable)
                    : ManageUpgradeTrack.NotApplicable,
                NextRungLine = Ascii(c.AfterUpgradeText)
            };
            if (item.UpgradeTrack == ManageUpgradeTrack.NotApplicable) item.MaxLevel = 0;

            bool running = string.Equals(c.StateWord, "Building", StringComparison.OrdinalIgnoreCase);
            item.Add(ComposeUpgradeAction(
                atMax: atMax,
                running: running,
                jobChannel: ChannelId.Builder,
                jobId: c.JobKey,
                locked: false,           // ruling 3.5: a placed defence carries no Heart gate today
                lockReason: null,
                lockCta: null,
                ready: c.UpgradeReady,
                costLine: CostFormat.Words(c.UpgradeCostParts),
                invoke: c.Activate));

            ApplyBuildBadge(item, atMax, running, false, c.UpgradeReady, c.UpgradeCostParts);
            return item;
        }

        /// <summary>
        /// ONE upgrade-action composer for both BUILD families. The precedence is deliberate and it
        /// is the model's call, never the View's: MAX, then RUNNING, then the PREREQUISITE gate,
        /// then the QUEUE, then affordability.
        /// </summary>
        private ManageAction ComposeUpgradeAction(bool atMax, bool running, ChannelId jobChannel,
            string jobId, bool locked, string lockReason, string lockCta, bool ready,
            string costLine, Action invoke)
        {
            if (atMax)
                return ManageAction.NotApplicable(ManageActionKind.Upgrade);

            if (running)
            {
                LiveJob(jobChannel, jobId, out float progress, out float remaining);
                return new ManageAction
                {
                    Kind = ManageActionKind.Upgrade,
                    Availability = ManageActionAvailability.InProgress,
                    Cta = "UPGRADING",
                    Progress01 = progress,
                    RemainingSeconds = remaining,
                    IsPrimary = true
                };
            }

            if (locked)
                return new ManageAction
                {
                    Kind = ManageActionKind.Upgrade,
                    Availability = ManageActionAvailability.PrerequisiteBlocked,
                    Cta = "UPGRADE",
                    BlockerReason = lockReason,
                    Route = ManageRoute.ToHeart(lockCta),
                    CostLine = costLine,
                    IsPrimary = true
                };

            if (LineIsFull(jobChannel))
                return new ManageAction
                {
                    Kind = ManageActionKind.Upgrade,
                    Availability = ManageActionAvailability.QueueBlocked,
                    Cta = "UPGRADE",
                    BlockerReason = "The " + BuildTimerService.ChannelWord(jobChannel) + " line is full.",
                    Route = ManageRoute.ToQueue(),
                    CostLine = costLine,
                    IsPrimary = true
                };

            if (!ready)
                return new ManageAction
                {
                    Kind = ManageActionKind.Upgrade,
                    Availability = ManageActionAvailability.Unaffordable,
                    Cta = "UPGRADE",
                    BlockerReason = string.IsNullOrEmpty(costLine)
                        ? "You cannot pay for this upgrade yet."
                        : "Needs " + costLine + ".",
                    CostLine = costLine,
                    IsPrimary = true
                };

            return new ManageAction
            {
                Kind = ManageActionKind.Upgrade,
                Availability = ManageActionAvailability.Available,
                Cta = "UPGRADE",
                CostLine = costLine,
                IsPrimary = true,
                Invoke = invoke
            };
        }

        /// <summary>
        /// Canon 8: every tile carries one actionable indicator, chosen by the MODEL.
        /// ⛔ Ruling 15: an OWNED structure whose next rung is Heart-gated is NEVER badged Locked -
        /// the item is built and operating; it is the upgrade ACTION that is blocked, and the
        /// action already carries the sentence and the door.
        /// </summary>
        private static void ApplyBuildBadge(ManageItemState item, bool atMax, bool running,
            bool locked, bool ready, IReadOnlyList<CostPart> costParts)
        {
            if (atMax) { item.Badge = ManageTileBadge.Max; item.BadgeText = "MAX"; return; }
            if (running) { item.Badge = ManageTileBadge.Upgrading; item.BadgeText = "UPGRADING"; return; }
            if (locked) { item.Badge = ManageTileBadge.Idle; item.BadgeText = "HEART GATED"; return; }
            if (LineIsFull(ChannelId.Builder))
            { item.Badge = ManageTileBadge.QueueBlocked; item.BadgeText = "QUEUE FULL"; return; }
            item.Badge = ready ? ManageTileBadge.UpgradeAffordable : ManageTileBadge.UpgradeUnaffordable;
            // WO-1518: the bare word "SHORT" named neither the resource nor the amount. It now
            // carries both, from the item's OWN cost basket - see ShortBadgeText.
            item.BadgeText = ready ? "READY" : ShortBadgeText(costParts);
            // ⭐ THE TILE GETS THE CLOSED WORD (mockup panel 2, WO-1567 panel row 2).
            // WO-1518's amounts are RIGHT where there is room for them - the research list row's
            // state column and the detail card - and WRONG in a grid cell: the owner's own capture
            // (Logs/device/screens/owner-screen-20260907-004825.png) reads "SHORT 28..." on Crystal
            // Mine and "SHORT 72..." on Healing Caravan, which names neither a resource nor an
            // amount and so buys nothing over the bare word it replaced.
            // ⛔ BOTH FACES ARE COMPOSED HERE, from the same basket. Nothing downstream splits or
            // truncates the long one - see ManageItemState.BadgeWord.
            item.BadgeWord = ready ? "READY" : "SHORT";
        }

        // ── ARMY ─────────────────────────────────────────────────────────────

        private List<ManageTileVM> ComposeArmyTiles()
        {
            var tiles = new List<ManageTileVM>(TroopChoices.Count);
            for (int i = 0; i < TroopChoices.Count; i++)
            {
                var c = TroopChoices[i];
                if (c == null) continue;
                var item = ComposeTroopItem(c);
                string id = c.Id;
                // First tile selected by default - see ComposeBuildTiles' note (mockup screen 4).
                var tile = ProjectAffordanceTile(item, tiles.Count == 0,
                    () => OpenDetail(ManageTabId.Army, id, null, null));
                if (tile != null) tile.ContainPortrait = true;
                tiles.Add(tile);
            }
            return tiles;
        }

        private ManageItemState ComposeTroopItem(TroopChoiceVM c)
        {
            bool atMax = !c.HasNextLevel;
            var item = new ManageItemState
            {
                ItemId = c.Id,
                DisplayName = Ascii(c.Name),
                // RpgUiCatalog serves troop art from Resources/RpgUi/troop/<iconId>; the contract
                // addresses every visual as a RESOURCES KEY, so the folder is named here once.
                IconId = string.IsNullOrEmpty(c.IconId) ? null : "RpgUi/troop/" + c.IconId,
                Ownership = c.Unlocked ? ManageOwnership.Owned : ManageOwnership.NotUnlocked,
                Level = c.Level,
                MaxLevel = 0,
                UpgradeTrack = atMax ? ManageUpgradeTrack.Max : ManageUpgradeTrack.Upgradable,
                NextRungLine = Ascii(c.NextUnlockText)
            };
            // MaxLevel is deliberately left at 0: TroopChoiceVM authors no ceiling, and asserting
            // one here would be a second reading of a ladder this VM does not own (ruling 13's
            // [track-mismatch] invariant is exactly that trap).

            // ⭐ THE TRAIN FACE NAMES WHAT IT TRAINS AND HOW MANY - mockup panel 5, "TRAIN 1
            // ARCHER". The bare word "TRAIN" left the count implicit on the one button that spends
            // an army slot, and the View was welding the DURATION onto it to compensate, which is
            // how the owner's capture came to read "TRAIN . 1M 0S"
            // (Logs/device/screens/owner-screen-20260907-005222.png). The time now has its own
            // clock band; the face states the ACTION.
            // ⛔ ONE is not a guess: TrainTroop(troopId) enqueues exactly one unit, and
            // TroopDialogueCommands.SlotOf is the per-unit slot cost the cap test already uses. If
            // a batch verb is ever added, this string moves WITH it.
            string trainFace = "TRAIN 1 " + (Ascii(c.Name) ?? string.Empty).ToUpperInvariant();

            if (!c.Unlocked)
            {
                item.LockReason = string.IsNullOrEmpty(c.Requirement)
                    ? "Locked until the Barracks reaches Tier " + c.LockTier + "."
                    : Ascii(c.Requirement);
                item.Badge = ManageTileBadge.Locked;
                item.BadgeText = "LOCKED";
                // Ruling 21: the barracks BUILDING tier gates troop unlocks, so the door is the
                // barracks BUILD card - a screen that already exists and already works.
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Train,
                    Availability = ManageActionAvailability.PrerequisiteBlocked,
                    Cta = trainFace,
                    BlockerReason = item.LockReason,
                    Route = ManageRoute.ToBuildCard("barracks", "VIEW BARRACKS"),
                    IsPrimary = true
                });
                return item;
            }

            string troopId = c.Id;
            bool trainLineFull = LineIsFull(ChannelId.Train);
            if (c.TrainReady)
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Train,
                    Availability = ManageActionAvailability.Available,
                    Cta = trainFace,
                    CostLine = Ascii(c.TrainTimeText),
                    IsPrimary = true,
                    Invoke = () => TrainTroop(troopId)
                });
            else
                // ⭐ WO-1517 - A REFUSED TRAIN NOW SAYS ITS REASON ON ITS OWN FACE.
                // The ARMY-FULL arm is deliberately QueueBlocked WITH ManageRoute.None, and both
                // halves of that are the ruling:
                //   * QueueBlocked, not Unaffordable - the enum's own words are "affordable and
                //     permitted, but the relevant capacity has none left", which is exactly the
                //     army cap. Unaffordable means the WALLET is short and training charges
                //     nothing at all since WO-1387, so it would state something untrue.
                //     PrerequisiteBlocked is not available either: the invariant
                //     [lock-without-a-door] requires a routable Route, and raising the army cap is
                //     not one destination (ArmyStorage.MaxArmySize is a base plus a SUMMED
                //     armyCapBonus off any owning perk or tier), so any single door we named here
                //     would be a guess dressed as a fact.
                //   * Route.None so ProjectAction leaves the face reading TRAIN, disabled, with
                //     the reason on the why band - which is the ruling's "ARMY FULL becomes a band
                //     on the TRAIN button, replacing the footnote". A routable blocked action gets
                //     its LABEL REPLACED by the route's CTA, so giving it a door would delete the
                //     very verb the band is explaining.
                // The LINE-FULL arm is untouched and keeps its queue door.
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Train,
                    Availability = (trainLineFull || c.ArmyFull)
                        ? ManageActionAvailability.QueueBlocked
                        : ManageActionAvailability.Unaffordable,
                    Cta = trainFace,
                    BlockerReason = string.IsNullOrEmpty(c.TrainStateText)
                        ? "Training cannot start right now."
                        : Ascii(c.TrainStateText),
                    Route = (trainLineFull && !c.ArmyFull) ? ManageRoute.ToQueue() : ManageRoute.None,
                    CostLine = Ascii(c.TrainTimeText),
                    IsPrimary = true
                });

            // ⚠ Ruling 13: MAX belongs to the TRACK. A maxed troop is still TRAINABLE, which is why
            // the Train action above is composed before this and is never suppressed by atMax.
            if (!atMax)
            {
                if (c.UpgradeInProgress)
                    item.Add(ManageAction.NotApplicable(ManageActionKind.Upgrade));
                else if (c.UpgradeReady)
                    item.Add(new ManageAction
                    {
                        Kind = ManageActionKind.Upgrade,
                        Availability = ManageActionAvailability.Available,
                        Cta = "UPGRADE",
                        CostLine = Ascii(c.UpgradeCostText),
                        Invoke = () => UpgradeTroop(troopId)
                    });
                else
                    item.Add(new ManageAction
                    {
                        Kind = ManageActionKind.Upgrade,
                        Availability = ManageActionAvailability.Unaffordable,
                        Cta = "UPGRADE",
                        BlockerReason = string.IsNullOrEmpty(c.UpgradeStateText)
                            ? "This upgrade cannot start right now."
                            : Ascii(c.UpgradeStateText),
                        CostLine = Ascii(c.UpgradeCostText)
                    });
            }

            // ⭐ WO-1517 - THE TILE'S ONE WORD, and the two caps now have their own.
            // Owner, 20:10: "should show if queue is full and army is full also should show if a
            // troop type can be upgraded". Precedence is BLOCKERS FIRST, because a word that says
            // "TRAINABLE" on a troop the service will refuse is worse than no word at all - and
            // that is precisely what the 20:10 frame showed. ARMY FULL leads QUEUE FULL because
            // EnqueueTraining tests the cap before the line (see FillTrainFacts).
            // The UPGRADE word rides the tile only when nothing is blocking the train verb, so one
            // tile still says exactly one thing (canon 8).
            //
            // ⭐ AMENDED 2026-09-06 (WO-1541 lane, cause captured, NOT inferred). The ARMY_max
            // capture frame threw and MANAGE_FLOW_MAP_FAIL'd at frames=14/15:
            //   Builds/cap-manage-wave3.log:3832 - "no max item reachable on the ARMY tab ...
            //   States actually present: QueueBlocked,Locked over 9 tiles"
            // while the same run traced (:3775) "troop state id=troop-footman word=Max
            // upgrading=False hasNext=False" against "lineFull=True". A MAXED troop was wearing
            // QUEUE FULL, so the screen could not show a Max tile at all.
            //
            // ⛔ THE STATE WORD DESCRIBES THE ITEM, NOT THE LINE. QUEUE FULL and ARMY FULL are
            // properties of the SHARED train line and the roster cap - identical on all nine tiles,
            // so as a tile word they carry no per-item information at all. MAX is a property of
            // THIS troop (!HasNextLevel) and is the one word that tells the player something the
            // neighbouring tile does not. Canon 8 asks each tile to say exactly one thing; this is
            // the one thing worth saying.
            //
            // ⚠ THIS DOES NOT WEAKEN WO-1517, and the distinction matters. Its rule is "blockers
            // first" for a specific reason, written above: a word saying "TRAINABLE" on a troop the
            // service will REFUSE is worse than no word. MAX makes no such promise - it states the
            // upgrade ladder is finished, which stays true whatever the line is doing. The refusal
            // reason is untouched and still rides the TRAIN button's why-band, which is where
            // ruling 3.10 / WO-1517 actually put it ("ARMY FULL becomes a band on the TRAIN
            // button"), so nothing the player needs is lost.
            //
            // ⚠ UPGRADING DELIBERATELY STILL LEADS: an in-flight job is also item-specific, and it
            // is the more urgent fact. Locked troops never reach here - they return at :4383.
            if (c.UpgradeInProgress) { item.Badge = ManageTileBadge.Training; item.BadgeText = "UPGRADING"; }
            else if (atMax) { item.Badge = ManageTileBadge.Max; item.BadgeText = "MAX"; }
            else if (c.ArmyFull) { item.Badge = ManageTileBadge.QueueBlocked; item.BadgeText = "ARMY FULL"; }
            else if (trainLineFull) { item.Badge = ManageTileBadge.QueueBlocked; item.BadgeText = "QUEUE FULL"; }
            else if (string.Equals(c.UpgradeWord, "UPGRADE AVAILABLE", StringComparison.Ordinal))
            { item.Badge = ManageTileBadge.UpgradeAffordable; item.BadgeText = "UPGRADE AVAILABLE"; }
            else if (string.Equals(c.UpgradeWord, "MAX", StringComparison.Ordinal))
            { item.Badge = ManageTileBadge.Max; item.BadgeText = "MAX"; }
            else if (c.UpgradeWord.StartsWith("NEEDS ", StringComparison.Ordinal))
            // Idle, NOT Locked: ruling 15 - the troop is owned and trainable; it is the UPGRADE
            // that is blocked, and the word says which.
            { item.Badge = ManageTileBadge.Idle; item.BadgeText = c.UpgradeWord; }
            else if (c.TrainReady) { item.Badge = ManageTileBadge.Trainable; item.BadgeText = "TRAINABLE"; }
            // ⚠ KEPT, and it is now a BACKSTOP rather than the primary Max arm: the hoisted
            // `atMax` branch above catches every !HasNextLevel troop first. It is left in place
            // deliberately - it costs nothing, and deleting the last Max arm from the tail would
            // mean a future edit to the hoisted branch could silently drop MAX entirely.
            else if (atMax) { item.Badge = ManageTileBadge.Max; item.BadgeText = "MAX"; }
            else { item.Badge = ManageTileBadge.Idle; item.BadgeText = "IDLE"; }
            return item;
        }

        // ── RESEARCH ─────────────────────────────────────────────────────────

        /// <summary>
        /// WO-1564 part 1 - the RESEARCH PICKER's capacity, DERIVED from the live school count.
        ///
        /// <para>⛔ THE DEFECT THIS REPLACES. The picker was authored <c>GridColumns = 4,
        /// GridRows = 1</c> from a comment reading "four research BUILDINGS in ONE row". FIVE
        /// schools exist. <c>ManageFlow_RESEARCH_gridtop</c> showed the result: four across, the
        /// Lumber Mill ALONE on a second row beside three empty cells, and roughly 60% of the well
        /// black. WO-2010's acceptance ("all schools visible without scrolling") PASSES on that
        /// frame - the ticket was satisfied while the screen read as broken.</para>
        ///
        /// <para>⛔ AND A LITERAL 5 WOULD BE THE SAME DEFECT ONE SCHOOL LATER. The model owns
        /// school membership (canon 5), so it already knows the count; the geometry follows it.
        /// The shape is <c>columns = ceil(sqrt(n))</c>, <c>rows = ceil(n / columns)</c> - the
        /// squarest grid that seats every school, with no literal column or row count anywhere:
        ///   4 -&gt; 2x2, 5 -&gt; 3x2, 6 -&gt; 3x2, 9 -&gt; 3x3.
        /// It leaves at most <c>columns - 1</c> empty cells, so a school can never be orphaned
        /// alone on a ragged row the way the Lumber Mill was.</para>
        ///
        /// <para>⚠ WHY TWO ROWS RATHER THAN ONE WIDE ROW, stated so the next seat does not undo
        /// it: the renderer caps a cell at <c>ManageWorkspacePanel.MaxTileHeightPx</c> (190), so a
        /// ONE-row picker can only ever paint 190px of a much taller well - that cap, not the
        /// column count, is why 60% of the well was black. Two rows double the vertical fill
        /// without touching the renderer, which is another lane's file this wave.</para>
        ///
        /// <para>⛔ PICKER ONLY. The perk TREE (<see cref="ManageScreenKind.ResearchPerks"/>) keeps
        /// its authored ONE column - a single column is what makes the renderer lay ROWS instead
        /// of cards, and that is a shape decision, not a capacity one. BUILD and ARMY keep the
        /// mockup's authored capacities untouched.</para>
        /// </summary>
        private void ApplyPickerCapacity(ManageTabVM tab, ManageTabId id, ManageNavEntry nav)
        {
            if (tab == null || id != ManageTabId.Research) return;
            if (nav != null && nav.Kind == ManageScreenKind.ResearchPerks) return;

            int schools = tab.Tiles != null ? tab.Tiles.Count : 0;
            if (schools <= 0)
            {
                FlowTrace.Warn("Manage", "research picker composed ZERO schools - the authored seed " +
                    "capacity stands, because a derived one would be 0x0 and the renderer would " +
                    "refuse the band rather than say the list is empty");
                return;
            }

            // ⭐ ONE ROW WHILE THEY FIT (owner mockup panel 6, re-measured 2026-09-07).
            // ⚠ THIS SUPERSEDES THE ceil(sqrt(n)) SHAPE DESCRIBED ABOVE, AND THE PARAGRAPH THAT
            // DEFENDS TWO ROWS IS RETIRED WITH IT. Measured on the owner's own device
            // (Logs/device/screens/owner-screen-20260907-005358.png): FOUR schools -> sqrt gives
            // 2x2, and the frame shows four SHORT WIDE tiles stacked in the top 40% of the well
            // with roughly 60% of it black - the very defect ceil(sqrt) was introduced to cure,
            // reproduced in a different shape. The mockup draws ONE ROW of SQUARE tiles.
            // ⛔ THE OLD "why two rows" ARGUMENT WAS TRUE AND ITS PREMISE IS GONE. It said a
            // one-row picker could only paint MaxTileHeightPx(190) of a taller well. That cap now
            // yields to the CELL WIDTH on a single-row grid (ManageWorkspacePanel: a tile may grow
            // to square, never past it), so one row fills the well instead of hovering in it.
            // ⛔ THE CAP IS FIVE, NOT A COLUMN COUNT. Five is the BUILD grid's own authored width
            // at this reference well (ManageScreenVM: GridColumns = 5 for Build), so it is the
            // measured number of tiles that fit across, reused rather than re-guessed. Past five
            // the picker wraps, and the squarest-grid behaviour returns for free.
            int columns = Mathf.Clamp(schools, 1, 5);
            int rows = Mathf.Max(1, Mathf.CeilToInt(schools / (float)columns));
            tab.GridColumns = columns;
            tab.GridRows = rows;
            FlowTrace.Step("Manage", "research picker capacity derived from " + schools +
                " live school(s) -> " + columns + "x" + rows + " (" + (columns * rows - schools) +
                " empty cell(s))");
        }

        /// <summary>Canon 5: schools first. Membership is the model's; the View never infers it from an id.</summary>
        private List<ManageTileVM> ComposeResearchSchoolTiles()
        {
            var seen = new List<string>(8);
            var tiles = new List<ManageTileVM>(8);
            for (int i = 0; i < ResearchChoices.Count; i++)
            {
                var c = ResearchChoices[i];
                if (c == null || string.IsNullOrEmpty(c.BuildingId)) continue;
                if (seen.Contains(c.BuildingId)) continue;
                seen.Add(c.BuildingId);

                int total = 0, ready = 0, locked = 0, researched = 0, researching = 0;
                for (int j = 0; j < ResearchChoices.Count; j++)
                {
                    var p = ResearchChoices[j];
                    if (p == null || !string.Equals(p.BuildingId, c.BuildingId, StringComparison.OrdinalIgnoreCase)) continue;
                    total++;
                    if (string.Equals(p.StateWord, "Researched", StringComparison.OrdinalIgnoreCase)) researched++;
                    else if (string.Equals(p.StateWord, "Researching", StringComparison.OrdinalIgnoreCase)) researching++;
                    else if (p.Locked) locked++;
                    else if (p.Ready) ready++;
                }

                bool complete = total > 0 && researched == total;

                var item = new ManageItemState
                {
                    ItemId = c.BuildingId,
                    DisplayName = Ascii(string.IsNullOrEmpty(c.BuildingName) ? c.BuildingId : c.BuildingName),
                    // ⭐ THE SCHOOL WEARS ITS BUILDING PORTRAIT - the same key, the same folder and
                    // the same loader as the BUILD grid (WO-1567 panel row 7).
                    // ⛔ IT USED TO READ "HudIcons/BuildingUpgrades/" + c.IconName, WHICH RESOLVED
                    // THE RETIRED LANDSCAPE CARD STRIPS. Measured on the owner's device
                    // (Logs/device/screens/owner-screen-20260907-005358.png): every school tile
                    // painted a 1963x789 strip stretched into a tall cell behind a jagged oval mask
                    // - ManageScreenPanel already documents those strips as drawn for the retired
                    // wide 2x2 seat and wrong for a tall one, and this was the last live caller.
                    // ⛔ ONE PRODUCER, LIKE EVERY OTHER BUILDING KEY. ManageArt.BuildingPortraitKey
                    // off the catalog id is the single producer WO-1567 section 5 item 3 records;
                    // a school id IS a ladder id (armorer / barracks / forge / lumbermill), so this
                    // adds no second spelling. A tier sheet that is missing falls back to the base
                    // sheet and logs, and a genuine miss is announced by key - never silent.
                    IconId = ManageArt.BuildingPortraitKey(c.BuildingId, 1),
                    Ownership = ManageOwnership.Owned,
                    UpgradeTrack = ManageUpgradeTrack.NotApplicable,
                    Badge = complete ? ManageTileBadge.Max
                        : researching > 0 ? ManageTileBadge.Upgrading
                        : ready > 0 ? ManageTileBadge.UpgradeAffordable
                        : locked > 0 ? ManageTileBadge.Locked
                        : ManageTileBadge.Idle,
                    BadgeText = complete ? "COMPLETE"
                        : researching > 0 ? researching + " RESEARCHING"
                        : ready > 0 ? ready + " READY"
                        : locked > 0 ? locked + " LOCKED"
                        : total + " PERKS",
                    NextRungLine = total + " perk" + (total == 1 ? "" : "s") + " in this school."
                };
                // No action record: a school TILE is pure navigation and its command is the tile's
                // own Activate below. A ManageAction here would be a button nothing ever paints -
                // dead code that looks like a shipped feature (ManageQueueDrawerRegression:103-113).
                string school = c.BuildingId;
                // First tile selected by default - see ComposeBuildTiles' note (mockup screen 6).
                tiles.Add(ManageVmProjection.ProjectTile(item, tiles.Count == 0, () => OpenSchool(school, null)));
            }
            return tiles;
        }

        private List<ManageTileVM> ComposeResearchPerkTiles(string schoolId)
        {
            var tiles = new List<ManageTileVM>(8);
            for (int i = 0; i < ResearchChoices.Count; i++)
            {
                var c = ResearchChoices[i];
                if (c == null) continue;
                if (!string.IsNullOrEmpty(schoolId) &&
                    !string.Equals(c.BuildingId, schoolId, StringComparison.OrdinalIgnoreCase)) continue;
                var item = ComposeResearchItem(c);
                string perk = c.PerkId;
                string school = c.BuildingId;
                // First tile selected by default - see ComposeBuildTiles' note (mockup screen 7).
                tiles.Add(ProjectAffordanceTile(item, tiles.Count == 0,
                    () => OpenDetail(ManageTabId.Research, perk, school, null)));
            }
            return tiles;
        }

        private ManageItemState ComposeResearchItem(ResearchChoiceVM c)
        {
            var item = new ManageItemState
            {
                ItemId = c.PerkId,
                DisplayName = Ascii(c.Name),
                IconId = string.IsNullOrEmpty(c.IconName) ? null : "HudIcons/BuildingUpgrades/" + c.IconName,
                Ownership = c.Locked ? ManageOwnership.NotUnlocked : ManageOwnership.Owned,
                UpgradeTrack = ManageUpgradeTrack.NotApplicable,
                // ⭐ THE PERK'S AUTHORED EFFECT SENTENCE, which the tile projection uses as the row's
                // second line (mockup panel 7: "Arcane Basics" / "Mage spell power +5%"). It was
                // TierText ("TIER 2") - a fact the STATE column and the lock sentence already carry
                // twice over, while the one line the player actually decides on was nowhere.
                NextRungLine = Ascii(string.IsNullOrEmpty(c.Description) ? c.TierText : c.Description)
            };

            bool researching = string.Equals(c.StateWord, "Researching", StringComparison.OrdinalIgnoreCase);
            bool researched = string.Equals(c.StateWord, "Researched", StringComparison.OrdinalIgnoreCase);
            string costLine = CostFormat.Words(c.CostParts);

            if (c.Locked)
            {
                item.LockReason = string.IsNullOrEmpty(c.LockReason)
                    ? "Locked until its building reaches Tier " + c.UnlockTier + "."
                    : Ascii(c.LockReason);
                item.Badge = ManageTileBadge.Locked;
                // ⭐ WO-1518 - the LOCKED face now NAMES ITS BLOCKER AND SAYS WHAT THE TAP DOES.
                // Owner, 2026-09-06 20:12: "if locked what is blocking and link to it", and at
                // 20:19: "the logic is there on some if i click takes me there but should tell
                // them that". Both halves were already composed and neither reached a face:
                // ResearchChoiceVM.LockReason (:428) carries BuildingPerkService.CanResearch's
                // reason verbatim and Activate (:444) is already the door. The DEFECT WAS THE
                // WORDS, so nothing here rebuilds the routing.
                //
                // TWO CHANNELS, because the row has two and neither alone can hold both facts:
                //  * the STATE column (x 0.71-0.985 of the row, ~a quarter of its width, 18px
                //    FitSingleLine floor) takes the SHORT form "LOCKED - TAP". A whole sentence
                //    there shrinks to the floor and TMP culls it blank - the law
                //    ManageWorkspacePanel states three separate times.
                //  * the row's SECOND LINE (NextRungLine -> Subtitle, x 0.12-0.60, roughly half
                //    the row) takes the BLOCKER SENTENCE, which is where there is room for it.
                // So the row reads: name / "Requires Barracks Tier 3" / [padlock] LOCKED - TAP.
                //
                // ⚠ "- TAP" is appended ONLY when a door actually exists. ProjectAction turns a
                // PrerequisiteBlocked action with a routable Route into a live door, and the action
                // below always carries ManageRoute.ToBuildCard, so every locked perk has one today
                // - but the word is derived from the route rather than assumed, so a future locked
                // row WITHOUT a door can never advertise a tap that does nothing.
                bool hasDoor = !string.IsNullOrEmpty(c.BuildingId);
                item.BadgeText = hasDoor ? "LOCKED - TAP" : "LOCKED";
                // ⛔ NO LONGER JOINED. WO-1567 panel row 8, owner capture
                // Logs/device/screens/owner-screen-20260907-010151.png:
                //     "Wood +8%, offline bucket +8% . Upgrade the building to Tier 3 f..."
                // The join put a BENEFIT and a REQUIREMENT on one line with a floating period
                // between them, and the line was too long, so the half the player needs in order
                // to act got ellipsised away. Both facts survive, on the two channels the mockup
                // draws them on: NextRungLine -> Subtitle is the effect under the name, and
                // LockReason -> ManageTileVM.RequirementText is the padlock row beneath it
                // (ManageVmProjection.ProjectTile / ManageWorkspacePanel.BuildListRow).
                // ⚠ The joiner's own reasoning was right - "dropping either trades one missing
                // fact for another" - and nothing is dropped here. Only the glue is.
                // ⚠ AND THERE IS DELIBERATELY NO FALLBACK COPYING LockReason INTO NextRungLine
                // when a perk authors no effect sentence. The requirement already has its own row;
                // writing it into the subtitle as well would paint the identical sentence twice,
                // one above the other, which is the state the join was hiding rather than fixing.
                // ComposeResearchItem already falls back to c.TierText, so the line is rarely
                // empty - and when it is, an empty band is honest.
                if (!hasDoor)
                    FlowTrace.Warn("Manage", "locked perk '" + (c.PerkId ?? "?") + "' names no owning " +
                        "building, so no door can be routed - its face says LOCKED with no tap " +
                        "affordance rather than promising a navigation that would go nowhere");
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Research,
                    Availability = ManageActionAvailability.PrerequisiteBlocked,
                    Cta = string.IsNullOrEmpty(c.CtaLabel) ? "RESEARCH" : Ascii(c.CtaLabel),
                    BlockerReason = item.LockReason,
                    // Ruling 18 - the door is the school's own BUILD card, which exists and opens.
                    Route = ManageRoute.ToBuildCard(c.BuildingId,
                        string.IsNullOrEmpty(c.DoorLabel) ? "VIEW BUILDING" : Ascii(c.DoorLabel)),
                    CostLine = costLine,
                    IsPrimary = true
                });
                return item;
            }

            if (researched)
            {
                item.Badge = ManageTileBadge.Max;
                item.BadgeText = "RESEARCHED";
                item.Add(ManageAction.NotApplicable(ManageActionKind.Research));
                return item;
            }

            if (researching)
            {
                LiveJob(ChannelId.Research, "building-research:" + c.BuildingId + ":" + c.PerkId,
                    out float progress, out float remaining);
                item.Badge = ManageTileBadge.Upgrading;
                item.BadgeText = "RESEARCHING";
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Research,
                    Availability = ManageActionAvailability.InProgress,
                    Cta = "RESEARCHING",
                    Progress01 = progress,
                    RemainingSeconds = remaining,
                    IsPrimary = true
                });
                return item;
            }

            bool full = LineIsFull(ChannelId.Research);
            if (full)
            {
                item.Badge = ManageTileBadge.QueueBlocked;
                item.BadgeText = "QUEUE FULL";
                item.Add(new ManageAction
                {
                    Kind = ManageActionKind.Research,
                    Availability = ManageActionAvailability.QueueBlocked,
                    Cta = string.IsNullOrEmpty(c.CtaLabel) ? "RESEARCH" : Ascii(c.CtaLabel),
                    BlockerReason = "The Research line is full.",
                    Route = ManageRoute.ToQueue(),
                    CostLine = costLine,
                    IsPrimary = true
                });
                return item;
            }

            item.Badge = c.Ready ? ManageTileBadge.UpgradeAffordable : ManageTileBadge.UpgradeUnaffordable;
            // WO-1518, owner 2026-09-06 20:12: "short doesnt help, i need to know waht im short".
            item.BadgeText = c.Ready ? "READY" : ShortBadgeText(c.CostParts);
            var action = c.Ready
                ? new ManageAction
                {
                    Kind = ManageActionKind.Research,
                    Availability = ManageActionAvailability.Available,
                    Cta = string.IsNullOrEmpty(c.CtaLabel) ? "RESEARCH" : Ascii(c.CtaLabel),
                    CostLine = costLine,
                    IsPrimary = true,
                    Invoke = c.Activate
                }
                : new ManageAction
                {
                    Kind = ManageActionKind.Research,
                    Availability = ManageActionAvailability.Unaffordable,
                    Cta = string.IsNullOrEmpty(c.CtaLabel) ? "RESEARCH" : Ascii(c.CtaLabel),
                    BlockerReason = string.IsNullOrEmpty(costLine)
                        ? "You cannot pay for this perk yet."
                        : "Needs " + costLine + ".",
                    CostLine = costLine,
                    IsPrimary = true
                };
            item.Add(action);
            return item;
        }

        // ── the DETAIL screen ────────────────────────────────────────────────

        private ManageSelectionVM ComposeDetail(ManageNavEntry nav)
        {
            ManageItemState item = null;
            string description = null;
            IReadOnlyList<ManageStatVM> stats = Array.Empty<ManageStatVM>();
            IReadOnlyList<ManageCostVM> costs = Array.Empty<ManageCostVM>();
            // ⭐ WO-1567 panel rows 3 and 5 - the cost band's CAPTION and the CLOCK, which mockup
            // panels 3 ("Upgrade Cost" / 45m) and 5 ("Train Cost" / 40s) both draw as a labelled
            // block under the numbers. The duration is NOT pushed into the cost basket: it has no
            // bank and no affordability verdict (see ManageSelectionVM.TimeText).
            string costCaption = null;
            string timeText = null;
            // WO-1517 §1B item 3 - the troop card's SECOND live face. Null on every other screen.
            ManageAction troopUpgradeFace = null;

            switch (nav.Tab)
            {
                case ManageTabId.Army:
                {
                    var c = TroopChoiceById(nav.ItemId);
                    if (c != null)
                    {
                        item = ComposeTroopItem(c);
                        description = Ascii(c.Description);
                        stats = TroopStatRows(c);
                        // ⭐ THE TRAIN COST BAND (mockup panel 5, WO-1567 panel row 5). The Archer
                        // card shipped with NO cost band at all - the owner's capture
                        // (Logs/device/screens/owner-screen-20260907-005222.png) shows the price
                        // nowhere and the TIME welded onto the button face as "TRAIN . 1M 0S".
                        // ⛔ REUSED, NOT RE-COMPOSED. TrainTimeText is the WO-1517 train fact this
                        // same file already composes; nothing here re-prices a troop, and the View
                        // is handed a finished string.
                        //
                        // ⛔ AND THERE IS NO PRICE TO PAINT, WHICH IS A DELIBERATE DIVERGENCE FROM
                        // THE MOCKUP - NOT AN OMISSION. Mockup panel 5 draws "Train Cost / 550
                        // gold / 40s". TRAINING IS FREE IN THIS BUILD: owner ruling WO-1387
                        // (2026-09-04 23:16), verbatim <i>"training free ... just time"</i>, and
                        // FillTrainFacts sets TrainCostText = "" for exactly that reason with the
                        // ruling quoted beside it. CAPTURE_LOOP_GOAL 3.0c says the mockup wins over
                        // a text ruling - but that is a rule about how the screen LOOKS, and
                        // inventing 550 gold to fill a band would be a PRICE the game does not
                        // charge, i.e. a lie on the one screen a player uses to decide. So the band
                        // carries the caption and the CLOCK, and the missing half is reported to
                        // the owner (WO-1567 section 6, panel row 5) rather than fabricated.
                        costCaption = "Train Time";
                        timeText = Ascii(c.TrainTimeText);
                        troopUpgradeFace = item.ActionOf(ManageActionKind.Upgrade);
                    }
                    break;
                }
                case ManageTabId.Research:
                {
                    var c = ResearchChoiceById(nav.SchoolId, nav.ItemId);
                    if (c != null)
                    {
                        item = ComposeResearchItem(c);
                        description = Ascii(c.Description);
                        // The duration LEAVES the stats table and takes the clock band, exactly as
                        // it does on the building and troop cards - one shape for all three, so a
                        // player learns where to look for a time once. See ManageSelectionVM.TimeText.
                        stats = TwoFacts("Tier", Ascii(c.TierText), null, null);
                        costs = CostVms(c.CostParts);
                        costCaption = "Research Cost";
                        timeText = Ascii(c.TimeText);
                    }
                    break;
                }
                default:
                {
                    var b = BuildingChoiceFor(nav.ItemId, nav.ItemId);
                    if (b != null)
                    {
                        item = ComposeBuildingItem(b, null);
                        description = Ascii(b.Description);
                        // ⭐ THE MOCKUP'S CURRENT -> NEXT TABLE (panel 3), wired 2026-09-07.
                        // "Production 120 / hour -> 180 / hour" comes from the ONE producer
                        // ResourceBuildingProgression.ProductionPerHour (which ResourceCollector's
                        // runtime ThroughputScale now also calls); "Storage 2,000 -> 3,000" from
                        // TownBankCapacity.CapacityAtLevel. A building with neither keeps the
                        // honest prose row. See BuildingStatRows' summary for why the production
                        // level is the HARVEST level and the delta is the TIER's multiplier.
                        stats = BuildingStatRows(b);
                        costs = CostVms(b.UpgradeCostParts);
                        costCaption = "Upgrade Cost";
                        timeText = Ascii(b.UpgradeTimeText);
                        break;
                    }
                    var d = DefenseChoiceFor(nav.ItemId);
                    if (d != null)
                    {
                        item = ComposeDefenseItem(d, null);
                        description = Ascii(d.Description);
                        stats = TwoFacts("Placed", Ascii(d.PlacedText), null, null);
                        costs = CostVms(d.UpgradeCostParts);
                        costCaption = "Upgrade Cost";
                        timeText = Ascii(d.UpgradeTimeText);
                        break;
                    }
                    var row = InventoryRowById(nav.ItemId);
                    if (row != null)
                    {
                        // ⛔ WO-2007 - THE CARD ASKS THE SAME OWNERSHIP QUESTION AS THE TILE.
                        // This was a bare ComposeUnplacedItem(row), and that is the whole
                        // MANAGE_BUILD_DOOR_FAIL: the GRID routes through ComposeBuildItem, which
                        // asks IsPlacedThisTown and hands a placed, no-ladder civic singleton to
                        // ComposeOwnedNoUpgradeItem - while the CARD assumed "no upgrade ladder
                        // means never built". So 'market', 'workshop' and 'pet-house', all three
                        // physically recorded in BaseLayout, painted BUILT on their tile and offered
                        // BUILD on their card, and the placement gate then refused that very button
                        // as "Already built". The card lied about state the town already knows.
                        //
                        // ⚠ IsPlacedThisTown IS CALLED HERE RATHER THAN ComposeBuildItem, AND THAT
                        // IS DELIBERATE, NOT A SECOND DECIDER. ComposeBuildItem opens with
                        // BuildingChoiceFor(row.Id, row.TierLadderId); TierLadderId is the FAMILY id
                        // from CatalogRegistry.ResolveUpgradeId (BuildInventoryModel.cs:302-305) and
                        // need not equal row.Id, so it can match a BuildingChoiceVM that the
                        // BuildingChoiceFor(nav.ItemId, nav.ItemId) branch above already missed -
                        // and that branch is the ONLY one that fills stats / costs / costCaption /
                        // timeText, so routing wholesale would paint a laddered building's card with
                        // an empty cost band. The decider being shared is the OWNERSHIP one
                        // (IsPlacedThisTown), and no family-resolution site is added:
                        // docs/ARCHITECTURE.md section 6, "never add a second family-resolution or
                        // upgrade-start site".
                        bool placedNoLadder = IsPlacedThisTown(row.Id);
                        item = placedNoLadder ? ComposeOwnedNoUpgradeItem(row) : ComposeUnplacedItem(row);
                        description = Ascii(row.Description);
                        FlowTrace.Step("Manage", "build detail '" + row.Id + "' projects the word '" +
                            (item != null ? (item.BadgeText ?? "") : "<none>") + "' because it is " +
                            (placedNoLadder
                                ? "recorded in this town's BaseLayout and authors no upgrade ladder, so ownership IS the state and the card carries no BUILD action"
                                : "not in this town's BaseLayout, so the card carries the direct-placement BUILD door") +
                            " (WO-2007)");
                    }
                    break;
                }
            }

            if (item == null)
            {
                FlowTrace.Warn("Manage", "detail screen asked for '" + (nav.ItemId ?? "<null>") +
                    "' on " + TabWordOf(nav.Tab) + " and the model has no such item - the card says so " +
                    "rather than painting a blank");
                return new ManageSelectionVM
                {
                    Visible = false,
                    EmptyText = "That item is no longer in this town. Go back and pick another."
                };
            }

            var selection = ManageVmProjection.ProjectSelection(item, description, stats, costs, Navigate);

            // ⭐ THE COST BAND'S CAPTION AND THE CLOCK (mockup panels 3 and 5).
            // Seated here, on the composed selection, for the SAME reason the troop UPGRADE face is
            // (see the note below): ManageVmProjection is a shared projection and every screen would
            // have to grow the same two arguments to carry two facts only a composer knows - WHICH
            // verb is being paid for, and which duration belongs to it. The composer says so.
            if (selection != null && selection.Visible)
            {
                selection.CostCaption = costCaption;
                selection.TimeText = timeText;
                selection.TimeIconKey = string.IsNullOrEmpty(timeText) ? null : ManageArt.IconTime;
            }

            // ⭐ WO-1517 §1B item 3 - "An UPGRADE button beside TRAIN whenever upgrade is Ready,
            // with its time and cost on its face."
            //
            // ⛔ WHY IT IS SEATED HERE AND NOT IN THE PROJECTION. ComposeTroopItem has ALWAYS
            // added an Upgrade action beside the Train one, and it has never been painted: the
            // card has three face slots and ProjectSelection fills them as requirement-door /
            // Cancel / primary (ManageVmProjection: SecondaryAction = item.ActionOf(Cancel)).
            // A troop has no Cancel, so the SECONDARY SLOT WAS EMPTY ON EVERY TROOP CARD while the
            // composed Upgrade action fell on the floor - which is why the owner's frame says
            // "Upgrade: 12m 0s . Ready" on a row and offers no button. The renderer already lays
            // the secondary face beside the primary in reading order
            // (ManageWorkspacePanel.VisibleFaces), so seating it is the whole fix.
            // ⚠ Widening the projection's own slot rule to "Cancel, else the first non-primary
            // action" would change every Build and Research card in the same stroke, and
            // ManageVmProjection is another lane's file. Composers own which action goes in which
            // slot; this is the composer saying so, for the one screen the ruling is about.
            if (troopUpgradeFace != null && selection != null && selection.Visible)
            {
                var face = ManageVmProjection.ProjectAction(troopUpgradeFace, Navigate);
                if (face != null && face.Visible)
                {
                    selection.SecondaryAction = face;
                    FlowTrace.Step("Manage", "troop detail '" + nav.ItemId + "' seats an UPGRADE face " +
                        "beside TRAIN: label='" + face.Label + "' cost='" + face.CostText +
                        "' enabled=" + face.Enabled);
                }
            }

            return selection;
        }

        private TroopChoiceVM TroopChoiceById(string id)
        {
            for (int i = 0; i < TroopChoices.Count; i++)
                if (TroopChoices[i] != null &&
                    string.Equals(TroopChoices[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return TroopChoices[i];
            return null;
        }

        private ResearchChoiceVM ResearchChoiceById(string buildingId, string perkId)
        {
            for (int i = 0; i < ResearchChoices.Count; i++)
            {
                var c = ResearchChoices[i];
                if (c == null) continue;
                if (!string.Equals(c.PerkId, perkId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(buildingId) &&
                    !string.Equals(c.BuildingId, buildingId, StringComparison.OrdinalIgnoreCase)) continue;
                return c;
            }
            return null;
        }

        /// <summary>
        /// The detail screen's row. Reads the CACHED chip list first (the usual case: the player
        /// tapped a tile that is on screen), and only falls back to a full reconcile when a jump
        /// landed on a row outside the current chip. The fallback is the expensive path, so it is
        /// the one that is conditional.
        /// </summary>
        private BuildInventoryRow InventoryRowById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var cached = InventoryTiles();
            for (int i = 0; i < cached.Count; i++)
                if (cached[i] != null && string.Equals(cached[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return cached[i];
            var rows = BuildInventoryModel.Rows();
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && string.Equals(rows[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return rows[i];
            return null;
        }

        /// <summary>
        /// Two labelled facts. ⭐ WO-1517 §1B item 4 - THE LABELS ARE THE CALLER'S, because the
        /// hardcoded pair was wrong on every screen that used it.
        ///
        /// <para>Owner device frame owner-screen-20260906-201037.png, verbatim off the Archer
        /// detail: row 1 <c>Next -&gt; "Train one: 1m 0s . Ready"</c>, row 2
        /// <c>Time -&gt; "Upgrade: 12m 0s . Ready"</c>. Neither label names its value; "Next" was
        /// labelling a TRAIN fact and "Time" an UPGRADE fact. The Research card had the same defect
        /// one screen over ("Next" over "TIER 2"). A label that contradicts its value is worse than
        /// no label - the player reads it and learns something false.</para>
        /// </summary>
        private static IReadOnlyList<ManageStatVM> TwoFacts(string labelA, string a, string labelB, string b)
        {
            var list = new List<ManageStatVM>(2);
            if (!string.IsNullOrWhiteSpace(a)) list.Add(new ManageStatVM { Label = labelA, Value = a });
            if (!string.IsNullOrWhiteSpace(b)) list.Add(new ManageStatVM { Label = labelB, Value = b });
            return list;
        }

        /// <summary>
        /// ⭐ WO-1517 §1B items 1 and 2 - THE TROOP DETAIL'S STATS, CURRENT LEVEL AND LEVEL+1.
        ///
        /// <para>Owner ruling 2026-09-06 20:10, verbatim: <i>"see screen needs clear should show
        /// stats and what upgrade will promote to"</i>. Her frame
        /// (owner-screen-20260906-201037.png) carries NO stat of any kind - a portrait, a flavour
        /// line and two mislabelled timer rows - so a player cannot answer either of the two
        /// questions the screen exists for.</para>
        ///
        /// <para>⛔ ONE AUTHORITY: <see cref="TroopStatResolver.Effective"/>, which is the SAME
        /// resolver TroopDeployer applies to the live unit when it spawns
        /// (TroopDeployer.cs:58-59) - it folds troops.json's baseline with troop-upgrades.json's
        /// reach/strength curves. So the number on the card is the number that fights. Never
        /// re-multiply a curve here.</para>
        ///
        /// <para>⭐ THE BEFORE -&gt; AFTER IS THE EXISTING TABLE, NOT A SECOND PATTERN.
        /// <see cref="ManageStatVM.DeltaText"/> already renders as
        /// <c>value  -&gt;  delta</c> in gold (ManageWorkspacePanel.BuildStatRows), which is the
        /// shape the mockup draws for buildings. The ruling asks for the SAME table, so this fills
        /// that field rather than inventing a next-level layout.</para>
        ///
        /// <para>⚠ FIVE ROWS, DELIBERATELY. BuildStatRows seats <c>Mathf.Min(count, 5)</c> rows and
        /// WARNS when the band cannot hold them all, so a sixth would silently be the one dropped.
        /// Health / Damage / Range / Speed are the four the curves actually move (strength scales
        /// MaxHp + DPS, reach scales AttackRange + AggroRadius, per troop-upgrades.json's own
        /// header); Train time is the fifth because it is the price. COST is not a row: training
        /// charges nothing since WO-1387, so a cost row would read "free" forever.</para>
        /// </summary>
        private static IReadOnlyList<ManageStatVM> TroopStatRows(TroopChoiceVM c)
        {
            var def = c != null ? TroopCatalog.Find(c.Id) : null;
            if (def == null)
            {
                FlowTrace.Warn("Manage", "troop detail for '" + (c != null ? c.Id : "<null>") +
                    "' has no TroopDef - the card shows its timers only, with no stat table, rather " +
                    "than inventing numbers");
                return c == null
                    ? Array.Empty<ManageStatVM>()
                    : TwoFacts("Train one", Ascii(c.TrainFactText), "Upgrade", Ascii(c.UpgradeFactText));
            }

            int level = Mathf.Max(1, c.Level);
            var now = TroopStatResolver.Effective(def, level);
            var next = c.HasNextLevel ? TroopStatResolver.Effective(def, level + 1) : null;

            var rows = new List<ManageStatVM>(5);
            rows.Add(StatRow("Health", now.MaxHp, next != null ? (float?)next.MaxHp : null, "0"));
            rows.Add(StatRow("Damage", now.AttackDamage, next != null ? (float?)next.AttackDamage : null, "0.0"));
            rows.Add(StatRow("Range", now.AttackRange, next != null ? (float?)next.AttackRange : null, "0.0"));
            rows.Add(StatRow("Speed", now.MoveSpeed, next != null ? (float?)next.MoveSpeed : null, "0.0"));
            // Training time does NOT scale with troop level (BarracksService prices a train at
            // TroopDef.BuildSeconds flat), so this row carries no delta - stating one would be a
            // promotion the game does not deliver.
            // ⛔ NO "Train time" ROW HERE ANY MORE - IT MOVED, IT WAS NOT DELETED.
            // ManageSelectionVM.TimeText now carries it into the clock band under the costs, which
            // is where mockup panel 5 draws it. Leaving the row as well would print the duration
            // TWICE on one card, and the contract says so in as many words ("the composer supplies
            // this INSTEAD, not as well"). The value is unchanged: ComposeDetail reads the same
            // TrainTimeText, which FillTrainFacts sets from this same def.BuildSeconds.
            // ⚠ A stat row is a CURRENT -> NEXT pair (WO-1517); a train time has no next, and it
            // never did carry a delta - it was the one row in this table that was not a promotion.
            FlowTrace.Step("Manage", "troop detail stats id=" + c.Id + " L" + level +
                (next != null ? " -> L" + (level + 1) : " (max)") +
                " hp=" + now.MaxHp.ToString("0") + " dmg=" + now.AttackDamage.ToString("0.0"));
            return rows;
        }

        /// <summary>One before -&gt; after stat row. The arrow is omitted at max level.</summary>
        /// <summary>
        /// ⭐ WO-1567 PANEL ROW 3 - THE BUILDING DETAIL'S CURRENT -&gt; NEXT TABLE.
        /// Mockup panel 3 draws two numeric rows under the name ("Production 120 / hour -&gt;
        /// 180 / hour", "Storage 2,000 -&gt; 3,000"); the card shipped with ONE prose row
        /// ("Next level: ...") and the owner's capture shows no number pair anywhere.
        ///
        /// <para>⛔ ONE PRODUCER PER NUMBER, AND ONLY THE NUMBERS THAT HAVE ONE.
        /// <b>STORAGE is wired</b>: <c>TownBankCapacity.CapacityAtLevel(repo, level)</c> is already
        /// the single authority for a container's ceiling at a placed level (it folds
        /// StorageCapsCatalog's multiplier ladder) and it is called here at the LIVE level and at
        /// the next rung. Nothing is re-derived and no multiplier is copied.
        /// <br/>⛔ THE <c>RepoProps</c> OVERLOAD - NEVER REACH FOR THE RAW CAPACITY FIELD ON A
        /// CATALOG ROW HERE. The [one-reader] law says only TownBankCapacity may read that seam,
        /// and it is machine-enforced by <c>TownBankCapRegression.CheckOneReader</c>, which greps
        /// every file under <c>_Modules</c> for the dotted field access - <b>including inside
        /// comments</b>, which is why this paragraph does not spell it. This composer FAILED that
        /// guard on Builds/reg-wave4a.log by passing the field into the int overload; the row now
        /// hands the catalog row over and lets the one reader do its own field read - the same
        /// passthrough shape <c>TownBankCapacity.IsStorageContainer(repo)</c> already uses, and for
        /// the same reason (routing AROUND the one reader is how two ceilings disagree).</para>
        ///
        /// <para>⭐ <b>PRODUCTION IS NOW WIRED</b> (2026-09-07), and it has the same shape:
        /// <c>ResourceBuildingProgression.ProductionPerHour(id, level, productionMult, echoMult)</c>
        /// is the ONE producer, and <c>ResourceCollector.ThroughputScale</c> - which used to hold
        /// that formula privately - now calls it too. So the number on the card is produced by the
        /// same function that scales the collector at runtime. Every state-dependent term is passed
        /// IN by this composer (the live perk multiplier and the live echo multiplier); nothing is
        /// re-derived here and no multiplier is copied.</para>
        ///
        /// <para>⛔ THE LEVEL IS <c>ResourceBuildingState.GetLevel</c>, <b>NOT</b> <c>b.Level</c>,
        /// AND THAT IS DELIBERATE - IT IS THE ONLY HONEST NUMBER. farm / lumbermill / forge are
        /// DUAL-FAMILY: <c>UpgradeFamilyResolver.Resolve</c> sends all three to the CITY ladder
        /// (<c>GameState.BuildingTiers</c>, which is what <c>b.Level</c> reports), so nothing has
        /// written the legacy resource-ladder key <c>dotr.resbuilding.level.*</c> since that
        /// precedence was fixed, and <c>DualFamilyLevelResetMigration</c> reset the residue to 1.
        /// The harvester still ticks on THAT level. Painting
        /// <c>ProductionPerHour(id, b.Level, ...)</c> would therefore state an income the game does
        /// not pay - a lie on the one screen a player uses to decide (CLAUDE.md section 11B).
        /// Income moves with the TIER instead, through the tier's authored <c>*ProductionMult</c>
        /// (building-tiers.json: lumbermill 1.1 / 1.18 / 1.28 / 1.4), which is exactly what the
        /// NEXT column asks <c>ModifierService.ProductionMultForTier</c> for.</para>
        ///
        /// <para>Falls back to the honest prose row when neither numeric row can be composed, so a
        /// card never loses the "what changes next" line it already had.</para>
        /// </summary>
        private IReadOnlyList<ManageStatVM> BuildingStatRows(BuildingChoiceVM b)
        {
            if (b == null) return Array.Empty<ManageStatVM>();

            var rows = new List<ManageStatVM>(3);

            // ⭐ PRODUCTION - mockup panel 3's FIRST numeric row, so it is added first.
            if (Buildings.Progression.ResourceBuildingProgression.IsResourceBuilding(b.Id))
            {
                int prodLevel = Buildings.Progression.ResourceBuildingState.GetLevel(b.Id);
                double echo = Buildings.Progression.ResourceBuildingHarvester.EchoHarvestMultiplier();
                double nowPerHour = Buildings.Progression.ResourceBuildingProgression.ProductionPerHour(
                    b.Id, prodLevel, ModifierService.ProductionMultFor(b.Id), echo);

                int tier = Mathf.Max(0, b.Level);
                int nextTier = Mathf.Max(0, b.NextTier);
                double? thenPerHour = nextTier > tier
                    ? (double?)Buildings.Progression.ResourceBuildingProgression.ProductionPerHour(
                        b.Id, prodLevel, ModifierService.ProductionMultForTier(b.Id, nextTier), echo)
                    : null;

                if (nowPerHour > 0.0)
                {
                    // The unit rides in the LABEL: StatRow emits a bare number, and "1,008" with no
                    // unit is the mockup's "180 / hour" stripped of the half that gives it meaning.
                    rows.Add(StatRow("Production / hr", (float)nowPerHour,
                        thenPerHour.HasValue ? (float?)thenPerHour.Value : null, "N0"));
                    FlowTrace.Step("Manage", "building detail production id=" + (b.Id ?? "?") +
                        " harvest-level " + prodLevel + " tier " + tier +
                        " now=" + nowPerHour.ToString("0") + "/hr" +
                        (thenPerHour.HasValue
                            ? " next(tier " + nextTier + ")=" + thenPerHour.Value.ToString("0") + "/hr"
                            : " (no further tier)"));
                }
                else
                {
                    FlowTrace.Warn("Manage", "building detail for '" + (b.Id ?? "?") + "' is a resource " +
                        "building but ProductionPerHour returned 0 at harvest-level " + prodLevel +
                        " - no Production row is drawn rather than printing a zero the game does not mean");
                }
            }

            var entry = string.IsNullOrEmpty(b.CatalogEntryId)
                ? null : DeNelle.Core.Catalog.CatalogRegistry.Get(b.CatalogEntryId);
            var repo = entry != null ? entry.repo : null;
            if (DeNelle.Core.Economy.TownBankCapacity.IsStorageContainer(repo))
            {
                int level = Mathf.Max(1, b.Level);
                int next = Mathf.Max(level, b.NextTier);
                int now = DeNelle.Core.Economy.TownBankCapacity.CapacityAtLevel(repo, level);
                int then = DeNelle.Core.Economy.TownBankCapacity.CapacityAtLevel(repo, next);
                if (now > 0)
                    rows.Add(StatRow("Storage", now, next > level ? (float?)then : null, "N0"));
            }

            if (rows.Count > 0)
            {
                // The prose line still says what a NUMBER cannot - "auto-gathers wood over time".
                // It rides beneath the table rather than replacing it.
                if (!string.IsNullOrWhiteSpace(b.AfterUpgradeText))
                    rows.Add(new ManageStatVM { Label = "Next level", Value = Ascii(b.AfterUpgradeText) });
                return rows;
            }

            FlowTrace.Once("Manage", "building-stat-prose:" + (b.Id ?? "?"),
                "the building card for '" + (b.Id ?? "?") + "' has no numeric current->next pair " +
                "to draw (mockup panel 3), so it keeps its prose 'Next level' row. It is neither a " +
                "resource building (no per-hour production) nor a storage container (no capacity " +
                "ladder), so there is no number to state. Reported, not faked.");
            return TwoFacts("Next level", Ascii(b.AfterUpgradeText), null, null);
        }

        private static ManageStatVM StatRow(string label, float now, float? next, string format)
        {
            return new ManageStatVM
            {
                Label = label,
                Value = now.ToString(format),
                // Only when it actually CHANGES: an arrow pointing at the same number reads as a
                // promotion that buys nothing.
                DeltaText = next.HasValue && Mathf.Abs(next.Value - now) > 0.01f
                    ? next.Value.ToString(format)
                    : null
            };
        }

        /// <summary>
        /// Cost rows with a PER-RESOURCE affordability verdict. Canon 9 forbids the View inspecting
        /// player resources, so the comparison happens here, against the SAME GameState fields
        /// <see cref="CanAfford"/> reads - never a second ledger.
        /// </summary>
        private static IReadOnlyList<ManageCostVM> CostVms(IReadOnlyList<CostPart> parts)
        {
            if (parts == null || parts.Count == 0) return Array.Empty<ManageCostVM>();
            var list = new List<ManageCostVM>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                list.Add(new ManageCostVM
                {
                    Label = p.Word,
                    AmountText = p.AmountText,
                    // ⭐ THE GLYPH, AT LAST. This read `null` on every row until 2026-09-07, so the
                    // owner's Lumber Mill card (Logs/device/screens/owner-screen-20260907-004903.png)
                    // showed two bare numbers - "2600  970" - naming neither resource. Mockup panel 3
                    // draws a wood glyph beside 1,200 and an iron glyph beside 600. The five sprites
                    // have been on disk since the art wave landed (WO-1567 section 1).
                    // ⛔ THE MAPPING IS THE MODEL'S. A View switching on a concept id to pick art
                    // would be canon-9 derivation, and the id vocabulary is this file's, not the
                    // renderer's.
                    IconKey = CostIconFor(p.ConceptId),
                    Affordable = BankOf(p.ConceptId) >= p.Amount
                });
            }
            return list;
        }

        /// <summary>
        /// The delivered glyph for a cost concept, or null when none was drawn for it.
        /// <para>⛔ THE CONCEPT VOCABULARY IS <see cref="BankOf"/>'s, DELIBERATELY - the same cases
        /// in the same spellings, so a concept can never have a bank reader and no glyph without
        /// the two switches visibly disagreeing. A miss returns null and the row paints its NAME
        /// and amount with no picture, which is honest; the trace names the concept once so an
        /// unglyphed currency becomes an art ask rather than a silent blank.</para>
        /// </summary>
        private static string CostIconFor(string conceptId)
        {
            switch (conceptId)
            {
                case "wood": return ManageArt.ResWood;
                case "iron": return ManageArt.ResIron;
                case "stone":
                case "food": return ManageArt.ResStone;
                case "crystal":
                case "crystals": return ManageArt.ResCrystal;
                case "gold": return ManageArt.ResGold;
                default:
                    FlowTrace.Once("Manage", "cost-glyph:" + conceptId,
                        "cost concept '" + conceptId + "' has no delivered glyph - its row paints " +
                        "the resource NAME and the amount with no picture. That is an ART ASK, not " +
                        "a defect: the name still says which resource it is.");
                    return null;
            }
        }

        /// <summary>
        /// The live bank for one cost concept. Returns <see cref="int.MaxValue"/> for a concept this
        /// method does not know, so an unknown currency never renders as "you cannot afford it" -
        /// a false refusal is worse than a missing one, and the trace names it.
        /// </summary>
        /// <summary>
        /// ⭐ WO-1518 - the SHORT state word WITH ITS NUMBERS: "SHORT 120 IRON",
        /// "SHORT 120 IRON, 40 WOOD". Owner ruling 2026-09-06 20:12, verbatim:
        /// <i>"see screen, short doesnt help, i need to know waht im short"</i>
        /// (Logs/device/screens/owner-screen-20260906-201242.png - "Reinforced Plating / Troop
        /// health +5% / SHORT", naming no resource and no amount).
        ///
        /// <para>⛔ NO SECOND AFFORDABILITY PREDICATE (ruling section 3). The shortfall is
        /// <c>Amount - BankOf(ConceptId)</c> over the SAME <see cref="CostPart"/> list the cost
        /// basket paints and the SAME bank reader <see cref="CostVms"/> uses for its per-row
        /// verdict, which in turn reads the identical GameState fields as
        /// <see cref="CanAfford"/>. One ledger, three faces.</para>
        ///
        /// <para>⚠ It falls back to the bare word "SHORT" when nothing measures short - which
        /// happens legitimately when the refusal is gold-priced through a bank this reader does not
        /// know (<see cref="BankOf"/> returns int.MaxValue and says so once). A bare SHORT is the
        /// old, weaker face; it is never a WRONG number, and the trace names the concept.</para>
        ///
        /// <para>⚠ LENGTH IS LOAD-BEARING. This word lands in the research row's STATE column,
        /// which ManageWorkspacePanel.BuildListRow seats at x 0.71-0.985 of the row - roughly a
        /// quarter of its width - and FitSingleLine there floors at 18px. "SHORT 120 IRON" is 14
        /// characters and fits; a whole sentence would shrink to the floor and be culled blank
        /// (the same MinTextBandPx law the renderer states three times). So this stays a WORD plus
        /// NUMBERS. The sentence form lives on the detail card's why band, which has the room.</para>
        /// </summary>
        private static string ShortBadgeText(IReadOnlyList<CostPart> parts)
        {
            if (parts == null || parts.Count == 0) return "SHORT";
            string text = null;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Amount <= 0) continue;
                int bank = BankOf(p.ConceptId);
                int missing = p.Amount - bank;
                if (missing <= 0) continue;
                string term = missing + " " + (p.Word ?? p.ConceptId ?? "").ToUpperInvariant();
                text = text == null ? term : text + ", " + term;
            }
            if (text == null)
            {
                FlowTrace.Step("Manage", "a row reads SHORT but no cost part measured short against " +
                    "its bank - the face keeps the bare word rather than inventing an amount");
                return "SHORT";
            }
            return "SHORT " + text;
        }

        private static int BankOf(string conceptId)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) return 0;
            switch (conceptId)
            {
                case "wood": return state.Wood;
                case "iron": return state.Iron;
                case "stone": return state.Resources.Food;   // stone is banked on the Food field
                case "food": return state.Resources.Food;
                case "crystal":
                case "crystals": return state.Resources.Crystals;
                case "gold": return GoldBalance();
                default:
                    FlowTrace.Once("Manage", "bank-concept:" + conceptId,
                        "cost concept '" + conceptId + "' has no bank reader - its row is reported " +
                        "AFFORDABLE rather than falsely refused");
                    return int.MaxValue;
            }
        }

        /// <summary>True when the channel's queue has no depth left (ruling 14 - first-class state).</summary>
        private static bool LineIsFull(ChannelId channel)
        {
            var svc = BuildTimerService.Instance;
            if (svc == null) return false;
            // The SERVICE's own verdict (BuildTimerService.IsLineFull, :929) - authored depth plus
            // the crystal-bought slots. Never a second capacity calculation, and never the View's.
            return svc.IsLineFull(channel);
        }

        /// <summary>Live progress + remaining seconds for one job. 0/0 when nothing is running.</summary>
        private static void LiveJob(ChannelId channel, string jobId, out float progress, out float remaining)
        {
            progress = 0f;
            remaining = 0f;
            var svc = BuildTimerService.Instance;
            if (svc == null || string.IsNullOrEmpty(jobId)) return;
            remaining = Mathf.Max(0f, (float)svc.RemainingSeconds(channel, jobId));
            progress = Mathf.Clamp01(ProgressOfLive(svc, channel, jobId));
        }
    }

    /// <summary>
    /// WO-2001 - which SCREEN of the Manage graph the player is on. The grid and the detail are
    /// separate screens and never share one (the owner's flow mockup, and the band arithmetic).
    /// </summary>
    public enum ManageScreenKind
    {
        /// <summary>The tab's tile grid. RESEARCH's grid is its school list (canon 5).</summary>
        Grid = 0,
        /// <summary>One item's detail card.</summary>
        Detail = 1,
        /// <summary>One research school's perk grid.</summary>
        ResearchPerks = 2
    }

    /// <summary>
    /// One screen in the Manage graph, plus WHY the player is on it.
    ///
    /// <para>⛔ <see cref="Origin"/> is the whole point (owner ruling 28). A plain screen history
    /// returns the player to the grid, because that is literally where they came from; a
    /// prerequisite JUMP has to return them to the screen that SENT them - the locked Outrider they
    /// were shopping for, not the Build grid they passed through. So the stack pushes an ORIGIN,
    /// not a breadcrumb.</para>
    ///
    /// <para>⚠ Ruling 28 asks for the origin to sit on <c>ManageRoute</c>. It cannot: ManageRoute is
    /// a readonly struct in DeNelle.Core.Manage, which WO-2001 is forbidden to edit. The origin sits
    /// beside the route here instead - still model-side, still never the View's (canon 9). Recorded
    /// as a deviation rather than done quietly.</para>
    /// </summary>
    public sealed class ManageNavEntry
    {
        public ManageScreenKind Kind;
        public ManageTabId Tab;
        /// <summary>Detail subject: building id / troop id / perk id.</summary>
        public string ItemId;
        /// <summary>Research school (building id) this screen belongs to. Null elsewhere.</summary>
        public string SchoolId;
        /// <summary>The BUILD chip in force when this screen was entered.</summary>
        public string Filter;
        /// <summary>The screen that SENT the player here, or null when they browsed to it.</summary>
        public ManageNavEntry Origin;
    }
}

// =============================================================================
// ManageVmProjection - WO-2002. The ONE model-side mapping from Wave 0's
// ManageItemState onto Wave 1's presentation contract.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Manage
//
// WHY A PROJECTION EXISTS AT ALL. Canon 10: "Do not create three independent UI
// systems with duplicated lock/cost/queue logic." If BUILD, ARMY and RESEARCH each
// turned their own ManageItemState into tiles, each would choose its own badge
// word, its own frame, its own "which action goes in the primary slot" rule - and
// the three would drift, which is the same duplicated-state failure that produced
// the stale WO-number block and the retired dependency table in CLAUDE.md. The
// three tab VMs compose ManageItemState (they own the game rules); THIS file turns
// it into pixels-facing records, once.
//
// ⛔ THIS IS MODEL-SIDE CODE, NOT VIEW CODE, and the distinction is load-bearing.
// It may collapse states, pick art keys and format a duration - all of which canon
// 9 forbids the VIEW from doing. It runs in DeNelle.Core, it touches no service,
// no catalog and no GameState, and the renderer never calls it: the composer does,
// and hands the renderer the finished VMs.
//
// ⛔ IT DECIDES NOTHING THE MODEL ALREADY DECIDED. Every WORD it emits
// (BadgeText, Cta, BlockerReason, Route.Cta, DisplayName) is carried VERBATIM off
// the ManageItemState. It never derives a label from an enum name - if the composer
// left BadgeText empty, the tile gets an empty state word and ManageStateInvariants
// says so out loud, which is the correct outcome. Inventing a fallback word here
// would hide exactly the defect Wave 0's validator exists to surface.
//
// INSTRUMENTED, NEVER SILENT (CLAUDE.md 12): every item is run through
// ManageStateInvariants.Validate and each violation is announced through FlowTrace
// under [Flow:Manage]. The projection still returns a tile - refusing to render is
// worse than rendering a flagged one - but the trace names the ruling that broke.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Manage
{
    /// <summary>Wave 0 state -> Wave 1 presentation. Pure mapping, no game rules.</summary>
    public static class ManageVmProjection
    {
        // ── the badge collapse (9 authored badges -> 5 painted states) ────────

        /// <summary>
        /// Collapses <see cref="ManageTileBadge"/> onto the five canon-7 states the delivered
        /// medallion set can paint. This is a NARROWING, not a second truth - the badge stays
        /// the authored state on the item.
        ///
        /// <para>⚠ <see cref="ManageTileBadge.UpgradeUnaffordable"/> maps to
        /// <see cref="ManageTileVisualState.Available"/>, NOT to Locked. Owner ruling 15 and the
        /// precedent already shipped at HeartPanel.cs:420-440: an owned thing you cannot afford
        /// yet is not locked, and a padlock teaches "you can never get there". The shortfall is
        /// carried by the CTA's DisabledReasonText.</para>
        ///
        /// <para>⚠ <see cref="ManageTileBadge.Max"/> maps to Max and says NOTHING about training
        /// (ruling 13). A maxed Footman still projects a Train action reading Available.</para>
        /// </summary>
        public static ManageTileVisualState VisualStateFor(ManageTileBadge badge)
        {
            switch (badge)
            {
                case ManageTileBadge.Locked: return ManageTileVisualState.Locked;
                case ManageTileBadge.QueueBlocked: return ManageTileVisualState.QueueBlocked;
                case ManageTileBadge.Upgrading: return ManageTileVisualState.InProgress;
                case ManageTileBadge.Training: return ManageTileVisualState.InProgress;
                case ManageTileBadge.Max: return ManageTileVisualState.Max;
                default: return ManageTileVisualState.Available;
            }
        }

        // ── one action ────────────────────────────────────────────────────────

        /// <summary>
        /// Projects one Wave-0 <see cref="ManageAction"/> onto a button.
        ///
        /// <para><b>The route is bound INTO the callback here</b>, which is the mechanism that
        /// lets the View stay dumb: a PrerequisiteBlocked or QueueBlocked action becomes a
        /// VISIBLE, ENABLED button whose words are the route's CTA ("VIEW HEART") and whose
        /// Activate walks the player to the blocker's home. Ruling 18 - a lock without a route
        /// is the defect this program exists to kill - and it is satisfied without the renderer
        /// ever seeing a <see cref="ManageRoute"/>.</para>
        ///
        /// <para>The ONE exception is <see cref="ManageAction.LockedFace"/>: the route stays in the
        /// model (the validator demands one) but the face keeps the action's own Cta and renders
        /// disabled - WO-1668, the owner's ruling on WO-1566 row 6.3.</para>
        ///
        /// <para>A blocked action with NO routable route stays visible and DISABLED, carrying
        /// its BlockerReason. That combination is itself a violation the Wave-0 validator
        /// reports; the projection surfaces it rather than hiding the button.</para>
        /// </summary>
        /// <param name="action">The composed action. Null yields a hidden button.</param>
        /// <param name="navigate">
        /// The composer's route handler. Null is legitimate (a surface with no navigation yet)
        /// and downgrades a routable CTA to a disabled one WITH a trace line - never a silent
        /// dead button.
        /// </param>
        public static ManageActionVM ProjectAction(ManageAction action, Action<ManageRoute> navigate)
        {
            if (action == null) return ManageActionVM.Hidden;
            if (action.Availability == ManageActionAvailability.NotApplicable) return ManageActionVM.Hidden;

            bool blocked = action.Availability == ManageActionAvailability.Unaffordable ||
                           action.Availability == ManageActionAvailability.PrerequisiteBlocked ||
                           action.Availability == ManageActionAvailability.QueueBlocked;

            var vm = new ManageActionVM
            {
                Visible = true,
                CostText = action.CostLine,
                Label = action.Cta,
                StyleRole = StyleRoleFor(action.Kind, blocked)
            };

            if (blocked && action.Route.IsRoutable)
            {
                // ⭐ WO-1668 (owner ruling 2026-09-10, closing WO-1566 audit row 6.3) - A LOCK MAY
                // DECLINE ITS OWN DOOR. The model still names a destination (ruling 18 and the
                // [lock-without-a-door] invariant both require it), but a face flagged LockedFace
                // keeps ITS OWN words and renders DISABLED rather than becoming a live route.
                // ⛔ Measured, not assumed: Builds/ui-capture/ManageFlow_ARMY_locked_2670x1200.png
                // showed a gold, enabled "VIEW BARRACKS" under a "LOCKED" state chip - the branch
                // below is what produced it. The unlock sentence is NOT lost with the door: a
                // NotUnlocked item's LockReason is promoted onto the hint band by ProjectSelection,
                // and that frame already painted it ("Requires Barracks Tier 4").
                // Enabled stays false and StyleRole stays the blocked default (Secondary ->
                // ButtonKind.Quiet), so the renderer's `btn.interactable = face.Enabled` gets the
                // kit's own disabledColor - no colour is decided here.
                if (action.LockedFace)
                {
                    vm.Enabled = false;
                    vm.DisabledReasonText = action.BlockerReason;
                    return vm;
                }

                // The blocker HAS a door. The button becomes the door, in the model's words.
                vm.Label = action.Route.Cta;
                vm.StyleRole = ManageActionStyleRole.Navigate;
                if (navigate != null)
                {
                    ManageRoute route = action.Route;
                    vm.Enabled = true;
                    vm.Activate = () => navigate(route);
                    vm.DisabledReasonText = null;
                }
                else
                {
                    FlowTrace.Warn("Manage", "action " + action.Kind + " is " + action.Availability +
                        " and routes to " + action.Route.Kind + " but the composer supplied no route " +
                        "handler - the CTA renders DISABLED rather than pointing at a phantom (ruling 18)");
                    vm.Enabled = false;
                    vm.DisabledReasonText = action.BlockerReason;
                }
                return vm;
            }

            if (blocked)
            {
                vm.Enabled = false;
                vm.DisabledReasonText = action.BlockerReason;
                return vm;
            }

            if (action.Availability == ManageActionAvailability.InProgress)
            {
                // Running work is not a button the player presses again. It is reported by the
                // activity strip and the tile timer; the face stays visible and inert so the
                // layout does not jump when a job starts.
                vm.Enabled = false;
                vm.DisabledReasonText = null;
                return vm;
            }

            vm.Enabled = true;
            Action invoke = action.Invoke;
            vm.Activate = invoke;
            return vm;
        }

        private static ManageActionStyleRole StyleRoleFor(ManageActionKind kind, bool blocked)
        {
            if (kind == ManageActionKind.Cancel) return ManageActionStyleRole.Destructive;
            if (kind == ManageActionKind.Navigate) return ManageActionStyleRole.Navigate;
            if (blocked) return ManageActionStyleRole.Secondary;
            return ManageActionStyleRole.Primary;
        }

        // ── one tile ──────────────────────────────────────────────────────────

        /// <summary>
        /// Projects an item onto a grid tile. <paramref name="onSelect"/> is the composer's
        /// selection command - the tile never decides what selection means.
        /// </summary>
        public static ManageTileVM ProjectTile(ManageItemState item, bool isSelected, Action onSelect)
        {
            if (item == null)
            {
                // A null item is a FAILURE, not an empty tile (same stance as
                // ManageStateInvariants.Validate). Say so and paint a placeholder that reads
                // as broken rather than as an ordinary empty slot.
                FlowTrace.Fail("Manage", "a null ManageItemState reached ProjectTile - the grid " +
                    "would show a blank cell with no explanation");
                return new ManageTileVM
                {
                    Id = null,
                    Title = "MISSING",
                    VisualState = ManageTileVisualState.Locked,
                    StateText = "NO DATA",
                    StateIconKey = ManageArt.StatusFor(ManageTileVisualState.Locked),
                    FrameKey = ManageArt.FrameFor(ManageTileVisualState.Locked)
                };
            }

            Report(item);

            ManageTileVisualState state = VisualStateFor(item.Badge);
            ManageAction running = FirstRunning(item);

            return new ManageTileVM
            {
                Id = item.ItemId,
                Title = item.DisplayName,
                // ⭐ THE SECOND LINE FALLS BACK TO THE ITEM'S OWN EFFECT SENTENCE.
                // Mockup panel 7 gives every research row a name AND a one-line effect beneath it -
                // "Arcane Basics" / "Mage spell power +5%" - and that sentence is the entire point
                // of the panel: it is how a player decides what to research. The rows rendered
                // half-empty because this expression emitted a subtitle only for LADDERED items, and
                // research has no level at all (ruling 3.7 - never paint "LEVEL 0").
                // NextRungLine is already "an ASCII one-line summary of what changes at the next
                // rung", which is exactly what an effect sentence is, so the fallback reuses the
                // field rather than adding a second one that would say the same thing.
                Subtitle = item.MaxLevel > 0 && item.Level > 0
                    ? "LEVEL " + item.Level
                    : (string.IsNullOrEmpty(item.NextRungLine) ? null : item.NextRungLine),
                PortraitKey = item.IconId,
                IsSelected = isSelected,
                VisualState = state,
                StateText = item.BadgeText,
                // ⭐ THE GRID CELL GETS THE CLOSED WORD (mockup panel 2). The fallback is the full
                // BadgeText, so a composer that never sets BadgeWord is unchanged - this adds a
                // SHORTER face where one exists, it does not require every composer to author one.
                // ⛔ The shortening is the COMPOSER'S, never this projection's and never the View's:
                // truncating "SHORT 280 STONE" here would be exactly the derivation canon 9 bans,
                // and it would guess wrong the first time a word carried a space of its own.
                StateWord = string.IsNullOrEmpty(item.BadgeWord) ? item.BadgeText : item.BadgeWord,
                StateIconKey = ManageArt.StatusFor(state),
                FrameKey = ManageArt.FrameFor(state),
                Progress01 = running != null ? (float?)running.Progress01 : null,
                TimerText = running != null ? FormatDuration(running.RemainingSeconds) : null,
                Activate = onSelect,
                // `item.PrimaryAction` read inline, not via a local: the sibling
                // ProjectSelection declares `ManageAction primary` and this method never did, so a
                // bare `primary` here was a compile error (CS0103, caught at the gate 2026-09-06).
                RowAction = ProjectRowAction(item.PrimaryAction),
                // ⭐ THE REQUIREMENT IS ITS OWN CHANNEL (mockup panel 7's padlock row).
                // Carried VERBATIM off the item's LockReason, and ONLY for an item the composer
                // actually marked NotUnlocked - ruling 15 forbids a lock sentence on an owned
                // thing, and the Wave-0 validator enforces that, so reading Ownership here keeps
                // this projection agreeing with the validator instead of second-guessing it.
                RequirementText = item.Ownership == ManageOwnership.NotUnlocked
                    ? item.LockReason : null
            };
        }

        /// <summary>
        /// The inline action a LIST ROW may offer (mockup panel 7's gold RESEARCH button and its
        /// price). AVAILABLE only.
        /// <para>⛔ A blocked action returns <see cref="ManageActionVM.Hidden"/> rather than a
        /// disabled face or a route. Panel 7 draws a PADLOCK and the requirement on a locked row,
        /// and the state column already carries both; a greyed button beside them would be a third
        /// telling of the same fact. The door for a blocker lives on the DETAIL card, which has room
        /// for the sentence that explains it - and ProjectAction's route branch needs a navigate
        /// handler this projection deliberately does not take.</para>
        /// </summary>
        private static ManageActionVM ProjectRowAction(ManageAction action)
        {
            if (action == null) return ManageActionVM.Hidden;
            if (action.Availability != ManageActionAvailability.Available) return ManageActionVM.Hidden;
            if (string.IsNullOrEmpty(action.Cta)) return ManageActionVM.Hidden;

            ManageAction captured = action;
            return new ManageActionVM
            {
                Visible = true,
                Enabled = true,
                Label = action.Cta,
                CostText = action.CostLine,
                StyleRole = ManageActionStyleRole.Primary,
                Activate = () => captured.Invoke?.Invoke()
            };
        }

        // ── the selected-item card ────────────────────────────────────────────

        /// <summary>
        /// Projects an item onto the selection card. The composer supplies the two sentences
        /// that are its own to write (<paramref name="description"/> and any
        /// <paramref name="auxiliaryText"/>) plus the stat and cost rows; everything else
        /// comes off the item.
        /// </summary>
        public static ManageSelectionVM ProjectSelection(
            ManageItemState item,
            string description,
            IReadOnlyList<ManageStatVM> stats,
            IReadOnlyList<ManageCostVM> costs,
            Action<ManageRoute> navigate,
            string auxiliaryText = null)
        {
            if (item == null)
            {
                return new ManageSelectionVM { Visible = false, EmptyText = null };
            }

            Report(item);

            ManageTileVisualState state = VisualStateFor(item.Badge);
            ManageAction primary = item.PrimaryAction;
            ManageAction running = FirstRunning(item);

            // The requirement CTA is the FIRST blocked action that has a door. It is a separate
            // slot from the primary so the player can always see the exit even when the primary
            // face is a priced action they cannot pay for (canon 11 question 7).
            ManageAction blockedWithDoor = FirstBlockedWithRoute(item);

            var vm = new ManageSelectionVM
            {
                Visible = true,
                Title = item.DisplayName,
                // ⭐ EVERY DETAIL PANEL IN THE MOCKUP SHOWS A LEVEL UNDER THE NAME - panel 3
                // "Level 2", panel 5 "Level 1", panel 9 "Level 1". The capture showed troops with
                // NO level line at all, and the cause was this expression: it emitted a line only
                // when a CEILING was known, and ComposeTroopItem sets MaxLevel = 0 on purpose
                // ("TroopChoiceVM authors no ceiling, and asserting one here would be a second
                // reading of a ladder this VM does not own"). That reasoning is right and stands -
                // so the fallback states the level WITHOUT inventing a maximum.
                // ⛔ RESEARCH IS EXCLUDED, and deliberately: ManageResearchCardRegression's
                // [no-level-zero] case records that research has no level, and its items project
                // UpgradeTrack.NotApplicable. Gating on the TRACK rather than on the level keeps
                // that true instead of relying on a perk happening to have Level 0.
                // Case matches the mockup ("Level 2"), not the old shouted "LEVEL 2 OF 6".
                // ⭐ WO-1657 ITEM B - A FOUNDING BUILDING NO LONGER CLAIMS TO BE "Level 0".
                // See FoundingLevelText and the block above it for the proof and the reasoning.
                LevelText = item.MaxLevel > 0
                    ? (item.Level > 0
                        ? "Level " + item.Level + " of " + item.MaxLevel
                        : FoundingLevelText(item.MaxLevel))
                    : (item.Level > 0 && item.UpgradeTrack != ManageUpgradeTrack.NotApplicable
                        ? "Level " + item.Level
                        : null),
                Description = description,
                State = state,
                StateText = item.BadgeText,
                StateIconKey = ManageArt.StatusFor(state),
                PortraitKey = item.IconId,
                Stats = stats ?? Array.Empty<ManageStatVM>(),
                Costs = costs ?? Array.Empty<ManageCostVM>(),
                PrimaryAction = ProjectAction(primary, navigate),
                SecondaryAction = ProjectAction(item.ActionOf(ManageActionKind.Cancel), navigate),
                RequirementAction = blockedWithDoor != null && blockedWithDoor != primary
                    ? ProjectAction(blockedWithDoor, navigate)
                    : ManageActionVM.Hidden,
                Progress = running != null ? (float?)running.Progress01 : null,
                ProgressText = running != null ? FormatDuration(running.RemainingSeconds) : null,
                AuxiliaryText = auxiliaryText
            };

            // "What changes next" (canon 11 question 3) is authored on the item; surface it as
            // the auxiliary line when the composer did not supply one of its own.
            if (string.IsNullOrEmpty(vm.AuxiliaryText)) vm.AuxiliaryText = item.NextRungLine;

            // The lock sentence belongs to a NotUnlocked item (ruling 15 forbids it on an owned
            // one, and the validator enforces that), so it is safe to prefer it here.
            if (item.Ownership == ManageOwnership.NotUnlocked && !string.IsNullOrEmpty(item.LockReason))
                vm.AuxiliaryText = item.LockReason;

            return vm;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        // =====================================================================
        //  WO-1657 ITEM B - "Level 0 of 4" ON A PLACED, PRODUCING, READY BUILDING
        // ---------------------------------------------------------------------
        //  THE FRAME: Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png
        //  (APK 2026.09.10.363722, Seeker SM02G4061955851). The Quarry card reads "Level 0 of 4"
        //  beside a READY chip, a live UPGRADE face, and "Production / hr  1,872 -> 2,016". A
        //  placed, producing building presenting as level ZERO reads as un-built to a player.
        //
        //  THE PRODUCER, PROVEN END TO END - NOT GREPPED (CLAUDE.md section 12):
        //    ModifierService.TierOf (ModifierService.cs:44-47) returns 0 when GameState.
        //      BuildingTiers has NO ENTRY for the id - a DICTIONARY MISS, not a stored level
        //    -> BuildBuildingChoices sets BuildingChoiceVM.Level = ModifierService.TierOf(id)
        //    -> ManageScreenVM.ComposeBuildingItem sets ManageItemState.Level = c.Level
        //    -> THIS expression painted "Level " + 0 + " of " + 4.
        //  THE DECIDING CAPTURED LINE, Builds/wave6-manageflow1 (fresh, 2026-09-10 09:00):
        //    [Flow:Manage] building choice id=farm level=0/4 state=Upgradable next=1 ready=True
        //    icon='Portraits/Buildings/farm' benefit='Stone production +10%.'
        //  ("farm" is the Quarry's LADDER id; the catalog row is collector_farm, displayName
        //  "Quarry" - and that benefit string is the frame's "Next level" line verbatim, which is
        //  what ties the log line to the frame.) Siblings on the same run read level=1/4, 3/4 and
        //  4/4, so the zero is this building's state, not a broken read.
        //
        //  ⭐ THE VERDICT IS WO-1657's B2, NOT B1 - LEVEL 0 IS REAL AND MUST NOT BE "CORRECTED"
        //  TO 1. building-tiers.json authors the farm ladder as tiers 1,2,3,4 (read at source
        //  2026-09-10; tier 1 authors foodProductionMult 1.1, i.e. the "+10%" the card offers to
        //  BUY). ModifierService.TierProductionMult says the same thing in code and in its own
        //  words - "A tier below 1 contributes identity" (ModifierService.cs:106, `if (tier < 1)
        //  return 1f;`). So the founding state genuinely sits BELOW the ladder: the building
        //  produces at base and has not bought rung 1. Writing 1 there would claim a purchased
        //  multiplier the player has not paid for - the WO's B1 reading, and it is wrong.
        //  ⛔ THEREFORE THE DEFECT IS THE WORDING, NOT THE NUMBER, and the fix is display-only.
        //  Nothing upstream is touched: no default is changed, no tier is seeded, no economy
        //  moves. That matters more than usual - this game is live on the Solana dApp Store.
        //
        //  PRECEDENT, so this is not an invented rule: ruling 3.7 already forbids painting a
        //  level zero on the Research card, pinned by ManageResearchCardRegression's
        //  [no-level-zero] ("Never paint LEVEL 0"). This applies the SAME ruling to the building
        //  card, which is the other surface that owns the level slot.
        //
        //  ⛔ THE CEILING IS NOT RE-DERIVED HERE. It is item.MaxLevel, which the composer already
        //  carries from BuildingTierCatalog.MaxTier - CLAUDE.md section 8 forbids a second
        //  hardcoded level ceiling and this line adds none.
        // =====================================================================

        /// <summary>
        /// ⚠ THE WORDING IS AN OWNER CALL - WO-1657 section 3, reading B2. CHANGE THIS CONSTANT,
        /// NEVER THE BRANCH. The structural rule (a dictionary-miss sentinel is not a level, and
        /// ruling 3.7 forbids painting level zero) is settled; the exact words a founding building
        /// shows are the owner's to overrule, and they are isolated here so overruling them is a
        /// one-token edit that cannot disturb the proven logic above.
        /// <para>Kept SHORT deliberately: this lands in the detail card's level band beside the
        /// state chip, which TMP will cull rather than wrap. It states the LADDER DEPTH so the
        /// player still sees what they are buying into, and it never claims a level.</para>
        /// </summary>
        private const string FoundingLevelWord = "Not yet upgraded";

        /// <summary>The level line for a building that is placed and producing but has bought no
        /// rung yet. See the block above for why this is not "Level 0" and not "Level 1".</summary>
        private static string FoundingLevelText(int maxLevel)
        {
            return maxLevel > 0
                ? FoundingLevelWord + " . " + maxLevel + " levels"
                : FoundingLevelWord;
        }

        private static ManageAction FirstRunning(ManageItemState item)
        {
            for (int i = 0; i < item.Actions.Count; i++)
            {
                var a = item.Actions[i];
                if (a != null && a.Availability == ManageActionAvailability.InProgress) return a;
            }
            return null;
        }

        private static ManageAction FirstBlockedWithRoute(ManageItemState item)
        {
            for (int i = 0; i < item.Actions.Count; i++)
            {
                var a = item.Actions[i];
                if (a == null || !a.Route.IsRoutable) continue;
                if (a.Availability == ManageActionAvailability.PrerequisiteBlocked ||
                    a.Availability == ManageActionAvailability.QueueBlocked ||
                    a.Availability == ManageActionAvailability.Unaffordable) return a;
            }
            return null;
        }

        /// <summary>
        /// Runs the Wave-0 validator and announces every violation. Never throws, never
        /// swallows, never suppresses the tile - a flagged tile the player can see beats a
        /// missing tile nobody can diagnose.
        /// </summary>
        private static void Report(ManageItemState item)
        {
            var failures = new List<string>();
            if (ManageStateInvariants.Validate(item, failures)) return;
            for (int i = 0; i < failures.Count; i++)
                FlowTrace.Warn("Manage", "projection saw an invalid item state: " + failures[i]);
        }

        /// <summary>
        /// ASCII countdown words. MODEL-SIDE ON PURPOSE: canon 9 forbids the VIEW deriving
        /// text, and a duration is derived text. It lives here so all three tabs read the same
        /// countdown grammar rather than three near-identical formatters drifting apart.
        /// The WO-2002 oracle bans this shape inside the renderer.
        /// </summary>
        public static string FormatDuration(float seconds)
        {
            if (seconds <= 0f) return "READY";
            int total = (int)(seconds + 0.5f);
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;
            int secs = total % 60;
            if (hours > 0) return hours + "h " + minutes + "m";
            if (minutes > 0) return minutes + "m " + secs + "s";
            return secs + "s";
        }
    }
}

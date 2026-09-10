// =============================================================================
// ManageScreenPanel — the unified MANAGE / QUEUES screen (WO-911, absorbs WO-905).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.UI
//
// ONE screen, opened by ONE bar face, holding all THREE production lines.
// It SUPERSEDES the old ObsidianQueueHud modal and the undiscoverable
// Builders-chip double-tap (WO-911 §3c / B4).
//
// CONSTRUCTION LAW (non-negotiable, learned the hard way):
//   • UXML DOES NOT WORK IN BUILDS — this is code-built uGUI via ElarionUiKit.
//   • ASCII ONLY in every TMP string. LiberationSans-SDF renders anything else as
//     tofu, so: "->" not an arrow, "..." not an ellipsis glyph, "x5" not a
//     multiplication sign. ManageScreenVM.Ascii() is the belt-and-braces filter.
//   • NEVER convey meaning by COLOUR ALONE — the owner is red/green colourblind.
//     Every state on this screen is a SENTENCE ("Queued - 3rd in line",
//     "Short 150 wood", "Extra slot: locked - awaken a 3rd Echo"). Button tints
//     are decoration on top of text that already says it.
//   • Fixed-pixel row bands (LayoutElement preferredHeight AND rt.sizeDelta.y),
//     never fractions of parent — the documented root cause of the WO-841/852
//     culling bugs, and the scroll column does not control child height.
//   • MinTouchPx (112) on every tappable row and control.
//
// CHEAP TICK (WO-836/864 lesson): the 1s tick rewrites only the countdown STRINGS
// on rows already built. Rows are rebuilt ONLY on BuildTimerService.QueueChanged
// or a tab change — never per second, which is what caused per-frame layout churn
// and fit-guard re-arm in the old queue HUD.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Jobs;
using DeNelle.Core.Manage;   // WO-2001 - ManageTabId / ManageWorkspacePanel / ManageArt.
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village.UI
{
    /// <summary>
    /// The Manage / Queues screen. Registered on <see cref="PanelId.Manage"/> and on the legacy
    /// <see cref="ObsidianQueueGate.ToggleRequested"/> verb (which the re-pointed bar face raises),
    /// so there is exactly ONE door.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManageScreenPanel : MonoBehaviour
    {
        // =====================================================================
        //  THE BAND TABLE — fixed reference pixels, summed against the measured well
        // ---------------------------------------------------------------------
        // BUILD-1 DEFECT (owner felt-test 2026-08-07, WO-905 §2.7 #5): every band was a
        // FRACTION of the body well, and the well is SMALL. At 2670x1200 the FrameCore body
        // resolves to ~533 reference px, so the old fractions gave the rail header 0.055 of it
        // (~29px) and the tab row 0.09 (~48px) — while ClampMinTouch grows every kit button to
        // MinTouchPx (112) and QueueRailView pins itself at a FIXED 200px. Three elements each
        // 2-7x taller than the band they were given, all painting over each other: exactly the
        // reported overprinting.
        //
        // THE LAW (the same one EndStateView holds): every band owns a FIXED PIXEL height, the
        // heights are SUMMED, the sum is subtracted from the measured well, and the scrolling
        // list takes the REMAINDER. If the fixed bands alone exceed the well we say so in px
        // (FlowTrace.Warn) and shrink deliberately — bands never share pixels.
        //
        // Stacking order (WO-905 §2.7 / §2.6), each in its own band, gutter between every pair:
        //   1 rails (status strip + the active line's card rail)
        //   2 extra-slot / Buy-slot row
        //   3 content tabs
        //   4 scrolling list   <- the flexible band; absorbs the remainder
        //   5 Close            <- the kit's shared Close, in its reserved bottom band
        // =====================================================================
        private const float RowHeightPx = 132f;     // >= MinTouchPx (112) with room for three text lines
        private const float SectionHeaderPx = 64f;
        // WO-1382 ruling #1 (2026-09-04): "Training becomes tappable and opens the existing queue
        // drawer." A tap target must clear MinTouchPx (112) WITHOUT ClampMinTouch growing it into
        // the list band below (a growth is a WO-1060 Assert A failure), so the strip is now a
        // touch-height band. Was 56 (one FontLabel line box + air); the 64px difference comes out
        // of the scrolling list on every tab, which is the honest price of a real tap target.
        private const float StripBandPx = 120f;     // band 1a: chips at 0.02-0.98 = 115px >= MinTouchPx
        private const float SlotBandPx = 120f;      // band 2: 0.96 * 120 = 115px button >= MinTouchPx
        private const float TabsBandPx = 0f;        // destination is already named in the title; Queue lives in that title row
        private const float NoticeBandPx = 56f;     // in-body fallback seat for the notice line
        private const float NoticeCloseBandPx = 96f;// beside-the-Close seat (two lines of FontLabel)
        private const float BandGapPx = 12f;        // guaranteed gutter — no two bands ever touch
        private const float MinListPx = 240f;       // band 4 floor: one 132px row under its 64px header
        // WO-2003 - the panel TITLE's anchors INSIDE the frame's header zone, shared by the launcher
        // and operational modes so the title clears BOTH header controls in BOTH states. FrameCore's
        // header zone is content x 0.24-0.88 (ElarionUiKit.cs:442), so local 0.30-0.78 resolves to
        // content 0.432-0.739: clear of the HEART face (ends 0.395) and of QUEUE (starts 0.795).
        // ⚠ RIGHT EDGE PULLED IN 2026-09-06: the title was CLIPPED under the QUEUE pill.
        // MEASURED, not inferred - the two rects genuinely overlapped. FrameCore's header zone is
        // content x 0.24-0.88 (ElarionUiKit.cs:443), so a local 0.78 resolved to content 0.739;
        // the chrome row spans content 0.055-0.945 and the pill sits at 0.72-0.95 OF THAT ROW, i.e.
        // content 0.696-0.900. 0.739 > 0.696, so the title ran 4.3% of the panel underneath it.
        // Local 0.68 resolves to content 0.675 and clears the pill by 0.021.
        // The left edge widens to 0.10 (content 0.304) so the title still reads centred over the
        // body now that the back arrow and the Heart face no longer sit beside it.
        private const float TitleLocalX0 = 0.10f, TitleLocalX1 = 0.68f;

        // The BACK / HEART / QUEUE chrome row, and the body's ceiling. It sits between the body and
        // the frame's own header zone (which starts at 0.900), so raising the body to meet it costs
        // nothing and hands the grid the strip that used to hold nothing. WO-1443, 2026-09-06.
        // ⛔ THE TOP EDGE IS SET BY THE FRAME ART, MEASURED - NOT BY EYE, AND NOT BY A ROUND NUMBER.
        // The QUEUE pill's red badge was sitting ON the frame's ornate top border. Rather than nudge
        // it, the frame was sampled: an alpha walk down frame_core.png (1230x1833) at the pill's own
        // x range finds the interior beginning at y 62-63px, i.e. the border owns everything above
        //   v = 0.966   (x 0.75 -> 0.9662, x 0.85 -> 0.9662, x 0.90 -> 0.9656)
        // The row ran to 0.975, so its top 0.9% was over the border art, and the badge - authored at
        // 0.94 of the row's height - resolved to 0.845 + 0.94*0.130 = 0.9672, just past the edge.
        // 0.962 clears it with margin. Y0 drops to 0.838 in the same breath so the row keeps its
        // touch height: 0.124 x ~923px = ~114px, still clear of MinTouchPx (112). AUTHORED to the
        // floor, not left to ClampMinTouch - the auditor caught this whole row at 110.4px once
        // already, because a nominal band was inset until it fell under.
        // ⚠ The body's ceiling follows Y0 (see BuildChrome), so this also hands the grid 0.007 of
        // the panel back. Move BOTH numbers together or the row loses its floor.
        // Panel 8's tab row inside the queue overlay. 120px so each face clears MinTouchPx (112)
        // at full band height - authored to the floor, not left to ClampMinTouch.
        private const float QueueTabsBandPx = 120f;

        /// <summary>The count badge's square size inside the QUEUE pill, in reference px. Read by
        /// SizeQueuePillToLabel so the badge's room is reserved rather than stolen from the word.</summary>
        private const float QueueBadgePx = 56f;

        private const float WorkspaceHeaderY0 = 0.838f;
        private const float WorkspaceHeaderY1 = 0.962f;

        // ⭐ THE CONSTANT EXIT'S GEOMETRY - owner ruling 2026-09-07: "on all the manage screens
        // there is no way to exit. can we add a const exit button top right".
        // ⛔ THREE NUMBERS, AND EVERY SEAT ON THE RIGHT OF THE CHROME ROW IS DERIVED FROM THEM.
        // The X owns the right end of the header band; the QUEUE pill is pinned exactly
        // ManageExitGapPx to its left (SeatQueuePillLeftOfExit). Neither is typed twice, so the
        // two can never drift into each other the way this screen's coordinates already have
        // nine times over (see SizeQueuePillToLabel's note).
        /// <summary>The exit square's side, in reference px. AUTHORED AT THE TOUCH FLOOR, exactly
        /// as the queue overlay's own X is (BuildQueueDrawer's ClosePx) - not a fraction of a band
        /// whose height varies, and nothing for ClampMinTouch to have to rescue.</summary>
        private const float ManageExitPx = ElarionUiKit.MinTouchPx;
        /// <summary>The gutter between the X and the QUEUE pill. Same number as BandGapPx, said
        /// separately because it guards a HORIZONTAL neighbour rather than a band.</summary>
        private const float ManageExitGapPx = 12f;
        /// <summary>⛔ THE CHROME ROW'S RIGHT EDGE, AS A FRACTION OF THE PANEL - the ONE place it is
        /// written. FrameCore's body zone is content x 0.055-0.945 (ElarionUiKit.cs:458) and an
        /// alpha walk over Assets/Resources/RpgUi/frame/frame_core.png puts the interior's edge at
        /// x 0.950 at this height, so 0.945 is inside the art with margin. `ManageHeaderActions`
        /// and the exit BOTH read this, because a control seated past the border is drawn over the
        /// frame or clipped by it - the defect that cost the QUEUE pill four coordinate rounds.</summary>
        private const float ManageChromeRightX = 0.945f;

        private const float CloseBandY0 = 0.050f;   // ElarionUiKit's DefaultCloseZone.y (the Close band)
        private const float CloseGapY = 0.020f;     // body floor clears the Close box by this much
        /// <summary>
        /// ⭐ THE BODY WELL'S FLOOR ON A NON-HUB SCREEN (WO-1567 round 25). The obsidian frame's own
        /// inner edge and nothing more - CLOSE is not rendered on these screens (WO-1491), so its
        /// band is not reserved on them either. See the geometry pass for the measured reclaim.
        /// </summary>
        private const float WorkspaceBodyFloorY = 0.020f;
        private const float RowCtrlY0 = 0.06f;      // 0.88 * RowHeightPx = 116px >= MinTouchPx (112),
        private const float RowCtrlY1 = 0.94f;      // so an in-row button is never GROWN out of its row

        // =====================================================================
        //  WO-1058 — ONE PRIMARY SLOT PER ROW. THE X-BANDS ARE THE WHOLE FIX.
        // ---------------------------------------------------------------------
        // The owner asked to "reuse the same button and make it finish now so you don't have to
        // move", and ruled the double-tap a FEATURE ("they can double click and be done"). The
        // arithmetic said the same gesture was ALSO destructive: `Upgrade` sat at 0.84-0.98 on a
        // browse row and `Cancel` at 0.885-0.98 on a queue row — the same strip of glass — and
        // starting a job INSERTS a queue row above the browse list, sliding a different row under
        // a finger that has not moved.
        //
        // THE INVARIANT, and it is the only thing that makes a sanctioned double-tap safe:
        //   EVERY row type puts exactly ONE control in PrimaryX0..PrimaryX1, it is ALWAYS the
        //   action the player wants (Upgrade / Finish Now / Expand), and it is NEVER destructive
        //   and NEVER free — the price is printed on the face BEFORE the finger arrives.
        // So whichever row slides under the second tap, the worst outcome is a priced action the
        // player could read, and `Cancel` is unreachable from that strip by construction.
        //
        // ⛔ NOT solved by a confirm dialog, a cooldown or a tap lockout (§2.2 forbids all three —
        //    the fast path IS the feature), and NOT by raising BuildTimerConfig.freeBuildSlots to
        //    guarantee a RUNNING job (queueDepthPerLine and freeBuildSlots are different axes and
        //    that config says so in its own comment).
        //
        // Left of the primary sits a DEAD GAP that nothing may occupy, then the secondary cluster.
        // The cluster is laid by EVEN SPLIT (ClusterSlot) rather than hand-authored bands: at the
        // narrowest supported aspect (1920x1080) the list row resolves to ~1490 reference px, so
        // three controls sharing 0.455-0.72 get 0.0817 each = ~122px — over MinTouchPx (112), so
        // ClampMinTouch is a NO-OP. Hand-authored uneven bands could not clear the floor for three
        // controls, and a clamp that fires is exactly WO-1056's root cause on the panel next door.
        //
        // ⚠ ClusterX0 is 0.455 and NOT the ticket's literal 0.40: the row's TEXT column owns
        //   x <= 0.44 (name / state / refund), and a control authored at 0.40 would sit ON that
        //   text — the "BUTTON OVER TEXT" failure the WO-1060 oracle exists to catch. The ticket's
        //   §2.3 table was authored without the text column in view; the ORDER it specifies
        //   (Ad, Cancel, Move up, then the primary) is preserved exactly, so Cancel is never
        //   adjacent to the primary slot.
        // =====================================================================
        private const float PrimaryX0 = 0.76f;      // THE primary slot — identical on every row
        private const float PrimaryX1 = 0.98f;      // 0.22 * ~1490px = ~328px: "Finish Now" fits flat
        private const float PrimaryGuardX = 0.04f;  // dead gap — nothing tappable may enter it
        /// <summary>
        /// Secondary cluster's left edge - it must start clear of the TEXT column.
        /// <para>⭐ WO-1488 (2026-09-07): 0.455 -&gt; 0.415, and the text column's right edge moves
        /// with it (<see cref="QueueTextX1"/>). MEASURED on the owner's device frame
        /// Logs/device/screens/owner-screen-20260907-010356.png: the CANCEL face reads
        /// <c>"CANC..."</c>. The arithmetic behind that ellipsis: three cluster controls split
        /// (0.72 - 0.455) = 0.265 of a ~1490px row, i.e. ~131px each, and the word CANCEL needs
        /// ~102px of glyph inside a plate that spends ~20px on its own border art. It was ~10px
        /// short, every time.
        /// ⛔ THE FIX IS WIDTH, NOT A SMALLER WORD. The WO-1058 law on this row is that TEXT may
        /// shrink and CONTROLS may not, and an ellipsised verb on a DESTRUCTIVE control is the
        /// worst case of all - "CANC..." and "CANCEL" are the same to a reader only until they are
        /// wrong.</para>
        /// </summary>
        private const float ClusterX0 = 0.415f;
        private const float ClusterX1 = PrimaryX0 - PrimaryGuardX;   // 0.72
        private const float ClusterGapX = 0.010f;

        /// <summary>The queue row's TEXT column right edge - name / state / refund all stop here,
        /// and <see cref="ClusterX0"/> starts clear of it. ONE number, so the two columns cannot
        /// drift into each other (they did: build 1 put the refund line under the button block).</summary>
        private const float QueueTextX1 = 0.40f;

        /// <summary>
        /// ⭐ THE AD CHIP IS COMPACT, AND THAT IS WHAT PAYS FOR THE FULL WORD CANCEL.
        /// <para>It carries a two-letter face ("Ad") where its neighbours carry six and seven, so
        /// an even three-way split spent the same ~131px on a word that needs ~34px. Authored AT
        /// <c>ElarionUiKit.MinTouchPx</c> as a fraction of the reference row width (112 / ~1490),
        /// so it is exactly a compliant tap target and not one pixel of the cluster more.</para>
        /// <para>⛔ THE CHIP IS NOT REMOVED, AND THE RULING SAYS SO PER CHANNEL. WO-911
        /// (WorkOrders/WORK_ORDER_911_timer_speedup_crystals_all_channels.md:84-85) minted
        /// <c>CanWatchAdToSkip(ChannelId, string)</c> / <c>WatchAdToSkip(ChannelId, string)</c>
        /// precisely so ad-skip is offered on ANY channel, and BuildTimerService.cs:1160 is that
        /// signature. The per-row offer is the MODEL's (ObsidianQueueVM.cs:208 calls
        /// <c>svc.CanWatchAdToSkip(channel, job.StructureId)</c>), so BUILD, TRAIN and RESEARCH
        /// each answer for themselves and this View never decides. Cited rather than removed.</para>
        /// </summary>
        private const float AdChipWidthX = 0.075f;

        // Queue-row TEXT bands (WO-1058 clipping pass). Each band now HOLDS its line box
        // (~1.16 * fontSize) instead of crowding it: the name line was authored at FontLabel(40)
        // — a ~46px box — inside a 0.72-1.00 band that resolves to 37px, so every title bled ~5px
        // over its band into the row above. Re-banded, NOT re-heighted: RowHeightPx stays 132
        // because vertical is the scarce axis in landscape.
        //   name   0.679-0.996 -> 41.8px, holds a 36px line box (41.8)   OK
        //   state  0.386-0.671 -> 37.6px, holds a 32px line box (37.1)   OK
        //   refund 0.093-0.378 -> 37.6px, holds a 32px line box (37.1)   OK
        //   bar    0.012-0.085 ->  9.6px  (a progress strip should be thin)
        // 126.6px of 132 spent, ~1px gutter between bands. Both text sizes stay at or above the
        // kit's FontFloor (30) — this shrinks TEXT to fit its authored band, never a CONTROL.
        private const float QueueNameFontPx = 36f;
        private const float QueueLineFontPx = 32f;  // == ElarionUi.FontMicro, an authored role

        /// <summary>
        /// ⭐ WO-1488 — THE STATE LINE'S OWN AUTOSIZE FLOOR, and it is deliberately below the kit's
        /// FontFloor(30).
        /// <para>THE DEFECT, measured: the capture reads
        /// <c>"Building - 11m 0s left (0% do..."</c> on row 2 while row 1's
        /// <c>"Building - 7m 0s left (0% done)"</c> renders whole. FitSingleLine WAS already on this
        /// label — with <c>minSize: 0</c>, which the kit resolves to FontFloor(30) against a
        /// maxSize of 32. Two points of shrink is not a fit; past it TMP ellipsises, which is what
        /// the owner is looking at. So the timer was not un-fitted, it was fitted into a floor it
        /// could not reach — and one extra character (11m vs 7m) is the whole difference.</para>
        /// <para>THE ARITHMETIC: 30 chars fit at 30px, so a char costs ~1px of size. The longest
        /// string this line ever carries is the queued form —
        /// <c>"Queued - 3rd in line (12h 30m of work)"</c>, 38 chars — which needs ~24px. Hence 24,
        /// not a rounder 26 that would still clip the worst case. It sits 4pt above the kit's
        /// FontHardFloor(20), and its 27.8px line box still clears the 37.6px band.</para>
        /// <para>⚠ THIS SHRINKS TEXT, NEVER A CONTROL — MinTouchPx and every CTA box are untouched
        /// (the WO-1058 rule). And the line is the SECONDARY one: the row's name is 36px bold and
        /// the progress bar carries the same fact for anyone who cannot read it, so the floor
        /// trades a small line against a truncated one, which is the trade the kit's explicit
        /// sub-floor parameter exists for.</para>
        /// <para>⚠ RESIDUAL, stated rather than hidden: the authored FORMAT is the real length
        /// problem and this lane cannot shorten it. The build-time string is ManageScreenVM's
        /// (<c>QueueRowVM.StateText</c>, off-silo for WO-1488) and the panel's own Update() tick
        /// re-authors the identical wording a second later, so the two must stay in step. If 24px
        /// ever proves too small on a device, the fix is the shared format, not this number and
        /// not the plate.</para>
        /// </summary>
        private const float QueueStateFontFloorPx = 24f;
        private const float QRowNameY0 = 0.679f, QRowNameY1 = 0.996f;
        private const float QRowStateY0 = 0.386f, QRowStateY1 = 0.671f;
        private const float QRowRefundY0 = 0.093f, QRowRefundY1 = 0.378f;
        private const float QRowBarY0 = 0.012f, QRowBarY1 = 0.085f;

        /// <summary>Tail spacer under the last list row (WO-1058). At max scroll the last row then
        /// clears the viewport's RectMask2D completely instead of being sliced mid-glyph at the
        /// Close band edge — the "content runs under Close" the owner photographed. It lives INSIDE
        /// the scrolling content, so the panel's fixed-band budget is untouched.</summary>
        private const float ListTailPx = 28f;

        // =====================================================================
        //  WO-1382 (owner ruling 2026-09-04 22:50) — the TROOPS workspace bands, fixed px.
        // ---------------------------------------------------------------------
        // Rail (left, scrolls) + selected-troop card (right) share ONE row host; the
        // TRAINING NOW band is its own header row + one informational row per job. No mode
        // switch exists any more ("That should not be a mode switch"): the two verbs are two
        // buttons with two different words, TRAIN 1 <NAME> and UPGRADE TO L<n>.
        // =====================================================================
        // THE FOLD ARITHMETIC (measured off Builds/manage-capture.log, 2026-09-04): at 2670x1200
        // the list viewport is LIST=401 ref px (well 533 - fixed 132); the scroll zone pads 10 and
        // gaps rows by 8. Everything the "screen visibly reacts" ruling (#5) needs must sit above
        // that fold at scroll 0:  10 + 260 (workspace) + 8 + 120 (TRAINING NOW band with its first
        // job and OPEN QUEUE) = 398 <= 401. Only extra jobs (88 each) and the Saved-armies row
        // fall under the fold. 2340x1080 gives LIST=410 and 1920x1080 LIST=480, so it fits everywhere.
        private const float TroopWorkspacePx = 260f;      // rail + card row
        private const float TroopRailRowPx = 112f;        // one troop per rail row, == MinTouchPx
        private const float TrainingNowBandPx = 120f;     // label + first job + OPEN QUEUE, one row
        private const float TrainingNowRowPx = 88f;       // extra jobs, informational only - no control
        private const float TroopCtaY0 = 0.01f, TroopCtaY1 = 0.445f;   // 0.435 * 260 = 113.1px >= MinTouchPx
        private const float BandCtrlY0 = 0.03f, BandCtrlY1 = 0.97f;   // 0.94 * 120 = 112.8px >= MinTouchPx

        // =====================================================================
        //  ⭐ WO-1541 - THE ARMY CARD IS TALLER THAN THE OTHER THREE, ON PURPOSE.
        // ---------------------------------------------------------------------
        //  OWNER RULING 2026-09-06 (via the question tool), on the three-way collision this
        //  file recorded earlier the same day: "RAISE THE CARD, TAPPABLE ROW."
        //  The army/camp line grows into a full MinTouchPx(112) row carrying a chevron and
        //  becomes the DOOR to the raid grid; the ARMY card grows past its 256px floor to pay
        //  for it; NOTHING ELSE SHRINKS.
        //
        //  ⛔ WHY THIS IS A SEPARATE SET OF CONSTANTS AND NOT AN EDIT TO TroopCtaY0/Y1.
        //  Those two are shared by FOUR card renderers - Buildings, Troops, Defense and
        //  Research - and TroopWorkspacePx sizes all four workspaces. Widening them would
        //  resize three screens this ruling never mentioned and invalidate the band arithmetic
        //  each of those methods records in its own comment. The ARMY card gets its own ladder;
        //  the other three are byte-for-byte untouched.
        //
        //  ⛔ AND THIS HONOURS WO-1422 RULING 3.10 RATHER THAN BREAKING IT. That ruling forbids
        //  SQUEEZING a door in beside TRAIN + UPGRADE and says a third face "needs a taller
        //  card, which is the Phase 2 unification WO". This IS the taller card. The door is
        //  also NOT a third CTA face - it is its own row above the CTA band, so TRAIN and
        //  UPGRADE keep their 113.1px each, unchanged.
        //
        //  ⛔ EVERY FRACTION BELOW IS DERIVED FROM PIXELS, NOT TYPED. The 2026-09-06 "bare
        //  plate" RCA on this very card (see FillTroopCard) was caused by a hand-typed pair of
        //  fractions resolving to an 18.2px band, under TMP's cull threshold. Stacking the
        //  bands in px and dividing makes that class of defect arithmetic instead of luck: to
        //  change a band, change ITS px constant and every fraction re-derives.
        //
        //  THE LADDER, bottom-up. Each band keeps EXACTLY the pixels it had at 260px; the only
        //  growth is the army band, 26px -> 112px, and the card absorbs all 86px of it.
        //      gap 2.6 | CTA 113.1 | gap 2.6 | fact 31.2 | gap 2.6 | desc 39.0
        //              | gap 1.3 | ARMY DOOR 112.0 | gap 1.3 | name 40.3   = 346.0
        // =====================================================================
        private const float TroopArmyDoorRowPx = 112f;    // == ElarionUiKit.MinTouchPx, authored not clamped
        private const float ArmyGapPx = 2.6f;             // 0.01 * 260, the card's original band gap
        private const float ArmyTightGapPx = 1.3f;        // 0.005 * 260, the two original tight gaps
        private const float ArmyCtaPx = 113.1f;           // 0.435 * 260 - TRAIN / UPGRADE, unchanged
        private const float ArmyFactPx = 31.2f;           // 0.12  * 260 - the train fact + benefit row
        private const float ArmyDescPx = 39.0f;           // 0.15  * 260 - description + status word
        private const float ArmyNamePx = 40.3f;           // 0.155 * 260 - NAME + LEVEL n
        /// <summary>The ARMY card's height. 260 + (112 - 26): the army band's growth, and nothing else.</summary>
        private const float TroopCardPx = ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx +
                                          ArmyDescPx + ArmyTightGapPx + TroopArmyDoorRowPx +
                                          ArmyTightGapPx + ArmyNamePx;   // = 346
        private const float ArmyCtaY0 = ArmyGapPx / TroopCardPx;
        private const float ArmyCtaY1 = (ArmyGapPx + ArmyCtaPx) / TroopCardPx;
        private const float ArmyFactY0 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx) / TroopCardPx;
        private const float ArmyFactY1 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx) / TroopCardPx;
        private const float ArmyDescY0 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx) / TroopCardPx;
        private const float ArmyDescY1 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx +
                                          ArmyDescPx) / TroopCardPx;
        private const float ArmyDoorY0 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx +
                                          ArmyDescPx + ArmyTightGapPx) / TroopCardPx;
        private const float ArmyDoorY1 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx +
                                          ArmyDescPx + ArmyTightGapPx + TroopArmyDoorRowPx) / TroopCardPx;
        private const float ArmyNameY0 = (ArmyGapPx + ArmyCtaPx + ArmyGapPx + ArmyFactPx + ArmyGapPx +
                                          ArmyDescPx + ArmyTightGapPx + TroopArmyDoorRowPx +
                                          ArmyTightGapPx) / TroopCardPx;

        // =====================================================================
        //  WO-1393 (2026-09-05) - THE QUEUE DRAWER AS ITS OWN BAND ON THE TROOPS TAB.
        // ---------------------------------------------------------------------
        // PROVEN (docs/qa/UI_REVIEW_2026-09-05/10-troops-after-upgrade.png): OPEN QUEUE put the
        // full-body drawer OVER the selected-troop card; the UPGRADE TO L4 tap hit the drawer, the
        // top-right QUEUE tap closed nothing (the toggle was hidden while open), and the
        // "IN QUEUE - TRAINING" header rendered clipped under the drawer's own 200px rail.
        //
        // THE ARITHMETIC, read off the captured bands(px) line (Builds/manage-capture.log,
        // 2670x1200): well=533, strip 120 + gap 12 => LIST=401. A 260px workspace plus a usable
        // drawer (64px header + one 132px row + 20px scroll padding = 216) cannot share 401px
        // with the 120px TRAINING NOW band, and cannot share it at all if the whole workspace
        // stays in view (401 - 280 - 12 = 109 < 216). So on the Troops tab, while the drawer is
        // open:
        //   * the TRAINING NOW band collapses - the drawer SUPERSEDES it (it is the line's mirror);
        //   * the list band keeps a viewport of DrawerModeListKeepPx: the scroll padding plus
        //     everything ABOVE the card's CTA line (rail rows, portrait, name, level, facts),
        //     unsquashed - the row keeps its 260px inside the scroll content, so TRAIN / UPGRADE
        //     are one drag away and are NEVER under the drawer (ruling #6: the card stays
        //     readable; queue verbs live in the drawer);
        //   * the drawer is a sibling band BELOW that viewport, top-anchored at
        //     listTop + keep + BandGapPx, running to the well floor: 401 - 154 - 12 = 235px at
        //     2670x1200 (244 at 2340x1080, 314 at 1920x1080), which seats the header and the
        //     first row at rest; further rows and the slot offer scroll;
        //   * the drawer carries NO rail in this mode (the workspace rail and the strip's counts
        //     are already on screen; a 200px rail is exactly what clipped the header).
        // Other tabs keep the WO-1368 full-body drawer. Both modes put the slot offer LAST in the
        // drawer's scroll list, which is what frees the full-body list zone (0.02-0.86) so the
        // header sits inside the well under the rail at rest (0.84 * 437 - 20 = 347 >= 272).
        // ManageQueueDrawerRegression [queue-toggle-closes] / [drawer-clear-of-card] pin this.
        // =====================================================================
        /// <summary>Scroll padding + everything above the card's CTA line: 10 + 260 * (1 - 0.445)
        /// = 154.3px. The CTA's top edge is exactly the viewport's floor at rest.</summary>
        private const float DrawerModeListKeepPx = 10f + TroopWorkspacePx * (1f - TroopCtaY1);
        /// <summary>The least the drawer band may be given: header + one row + scroll padding.</summary>
        private const float DrawerModeMinPx = SectionHeaderPx + RowHeightPx + 20f;
        private const string TrainingNowPrefix = "TroopTrainingNow";   // band + its extra rows
        private const string BuildingNowPrefix = "BuildingNow";       // building band + its extra rows
        // WO-1422: Research rides its OWN channel, so it needs its own collapse prefix. Defence
        // deliberately has none - it reuses the Builder band verbatim (ruling 3.3), so
        // BuildingNowPrefix already collapses it and a second name for one queue is never minted.
        private const string ResearchNowPrefix = "ResearchNow";       // research band + its extra rows

        private ManageScreenVM _vm;
        // WO-1422: _browsePage is GONE with the pager it indexed. Its only reader was the paged
        // browse block in RenderList; leaving a field three call sites still reset would be state
        // nothing consumes - the same dead-code-that-looks-alive shape ruling 3.4 deletes.
        private string _selectedTroopId;
        private string _selectedBuildingId;
        private string _selectedDefenseId;      // WO-1422: one selection per TYPE (ruling 3.1)
        private string _selectedResearchKey;    // WO-1422: "<buildingId>:<perkId>" (ResearchKeyOf)
        private GameObject _ui;
        private RectTransform _listContent;
        private GameObject _operationalListBand;
        private RectTransform _operationalWell;
        private RectTransform _launcherHost;
        /// <summary>True while the HUB (mockup panel 1) owns the screen. The ONE authority - read
        /// it, never `_launcherHost.activeSelf`, and change it only through ShowLauncher /
        /// ShowWorkspace so ApplyScreenVisibility stays the single writer.</summary>
        private bool _hubShowing;
        /// <summary>The model's nav entry at the moment the hub went up. A different reference means
        /// the player asked for a screen; see ShowLauncher.</summary>
        private ManageNavEntry _hubNav;
        private RectTransform _launcherGrid;
        /// <summary>The HEART chip's floor inside the hub host, MEASURED by BuildLauncher off the
        /// same host height the card band is derived from - so the chip's band and the cards' band
        /// can never be computed against two different rects (which is exactly how the chip ended
        /// up sitting inside all three cards).</summary>
        private float _hubHeartY0 = 0.85f;
        /// <summary>The host height the pair above was derived from, kept so BuildHubHeartDoor can
        /// state the chip's resolved px in the trace rather than assert them.</summary>
        private float _hubHeartHostH;
        /// <summary>⭐ WO-1597 - THE ONE ANSWER TO "does the hub draw a HEART chip". Written ONLY by
        /// BuildLauncher, from <c>ManageScreenVM.HeartUpgradeAvailable</c>, in the same breath as
        /// the card band it complements; read by BuildHubHeartDoor. Two writers deciding this in
        /// two places is how the chip ended up seated inside all three cards.</summary>
        private bool _hubHeartShown;

        /// <summary>
        /// ⭐ THE MANAGE PANEL FILLS THE SCREEN. Owner ruling 2026-09-07 01:14, verbatim:
        /// <i>"i expect these images to fill the screen, not 60% of it"</i>.
        /// <para>The inset is the DEVICE SAFE AREA on every edge - it keeps the obsidian frame's
        /// border off a rounded corner and out of a notch - and it is deliberately small enough
        /// that the panel clears the 0.95-of-safe-area floor the fixture pins on BOTH axes.
        /// It replaced x 0.18-0.82 (64% of the canvas); see BuildObsidianPanel's call site for the
        /// retired reasoning and where its real problem is now solved instead.</para>
        /// </summary>
        private const float ManagePanelInsetF = 0.02f;

        // ── THE HUB'S GEOMETRY, IN PX (mockup panel 1, WO-1567 panel row 1) ──────────────
        // ⛔ EVERY FRACTION THE HUB USES IS DERIVED FROM THESE, AND NONE IS TYPED AT ITS SITE.
        // The band used to read `0.055f .. 0.695f` - two numbers that meant "keep the CLOSE band"
        // and "keep the title band" without saying so, and that reserved a DIFFERENT number of
        // pixels on every surface height. Stating the reservation in px and dividing by the
        // measured host makes it the same on all of them, and makes the intent readable.
        /// <summary>
        /// The band at the TOP of the hub host. ⛔ IT IS NOT THE MANAGE TITLE'S - the title lives in
        /// the frame's own chrome row, ABOVE this host entirely, so reserving 96px for it here was a
        /// DOUBLE reservation and it is one of the two reasons the cards rendered small (measured on
        /// Builds/ui-capture/ManageFlow_BUILD_hub_2670x1200.png: three ~245x270 plates in an
        /// otherwise empty full-bleed well).
        /// <para>⭐ IT IS THE HEART CHIP'S BAND NOW, and it is authored AT
        /// <see cref="ElarionUiKit.MinTouchPx"/> rather than at a typed number. The chip used to be
        /// seated at 0.70-0.83 of the host - which resolved 440.5x75.4 ref px, 36.6px UNDER the
        /// touch floor, INSIDE the card band, and produced all seven of the non-queue geometry and
        /// touch failures on Builds/cap-manage-wave4.log (one SUB-TOUCH-FLOOR BAND, three BUTTONS
        /// OVERLAP and three BUTTON OVER TEXT, every one of them naming ManageHeartFace against a
        /// ManageCard_*). A header band is the mockup's own answer: panel 1's top strip is chrome,
        /// and nothing there touches a card.</para>
        /// </summary>
        private const float HubTitleBandPx = ElarionUiKit.MinTouchPx;
        /// <summary>The bottom CLOSE button's band. It is shared chrome, so the cards must clear it.</summary>
        private const float HubCloseBandPx = 140f;
        /// <summary>The gutter between the cards and each of those bands - never zero, so no two
        /// tappable things can touch.</summary>
        private const float HubBandGapPx = 24f;
        /// <summary>Side margin, as a fraction, because the host's WIDTH is the reference the
        /// mockup's own side margin is proportional to (~3% of the panel on both edges).</summary>
        private const float HubSideInsetF = 0.03f;
        /// <summary>
        /// The card's width:height, MEASURED off the owner's mockup. Without it the three cards
        /// stretch to a third of the band's width each and read as wide plaques rather than the
        /// portrait cards she drew; with it the row centres itself and keeps the drawn shape at any
        /// well height.
        /// <para>⭐ RE-MEASURED 2026-09-07 (WO-1597) off the DEDICATED panel she sent,
        /// <c>docs/mockups/manage/MANAGE_MOCKUP_panel1_hub.png</c> (464x287): the BUILD card runs
        /// x 33..165 by y 53..222, i.e. 132 x 169 = 0.781. The retired 145/160 (0.906) came off the
        /// eight-screen contact sheet, where panel 1 is a sixth of the width and a card is ~30px
        /// wide - a rounding error there is a whole ratio here. The dedicated panel is the better
        /// ruler and it is the one the owner is judging against (ruling 29).</para>
        /// </summary>
        private const float HubCardAspect = 132f / 169f;
        /// <summary>
        /// The art well's share of the card's height - the top block in panel 1, above the name and
        /// the description.
        /// <para>⭐ 0.65, MEASURED OFF THE SHEET (WO-1567 round 25). In mockup panel 1 the
        /// illustration occupies roughly the top two thirds of each card and the name plus its
        /// two-line description share the bottom third; 0.46 gave the picture less than half and
        /// left the copy a band it could not use. The title and description bands below are
        /// DERIVED from this constant, so raising it moves all three together.</para>
        /// </summary>
        private const float HubArtWellF = 0.60f;
        /// <summary>The card's NAME band, as a fraction of the card. One line at the deck's 36px
        /// face with room above and below it.</summary>
        private const float HubTitleBandF = 0.15f;
        /// <summary>
        /// The card's DESCRIPTION band, as a fraction of the card.
        /// <para>⛔ SIZED FOR TWO LINES AT <see cref="ElarionUi.FontFloorMobile"/> (30), WHICH IS
        /// THE WHOLE POINT. The owner's device capture ellipsised all three descriptions and the
        /// headless frame truncated them mid-word ("upgrade your to", "manage your tr", "powerful
        /// advan"): FitBlock had a band it could not seat two floor-height lines in, so it cut. At
        /// the hub's card height this resolves to ~80 ref px - two 30px lines plus leading - and
        /// <see cref="BuildLauncher"/> WARNS in px if the card ever gets too short to honour it,
        /// rather than silently cutting the one sentence that says what the card does.</para>
        /// </summary>
        private const float HubDescBandF = 0.19f;
        // WO-2001 - the three-tab workspace. It owns the WHOLE body well (the largest well this
        // chrome can offer) because the redesign's grid + selection band stack does not fit the
        // 533/542/612px wells the rail path was authored against; see ManageWorkspacePanel's
        // header, which hands that arithmetic to THIS work order by name.
        private RectTransform _workspaceHost;
        private DeNelle.Core.Manage.ManageWorkspacePanel _workspace;
        private Button _workspaceBack;
        private TextMeshProUGUI _workspaceTitle;
        private readonly TextMeshProUGUI[] _launcherBadges = new TextMeshProUGUI[4];
        private bool _categoryNavigationCommitted;
        private RectTransform _railBand;            // non-null only while the rail is PINNED
        // WO-1368 — the drawer's OWN scroll content. The queue VERBS live here, never in the
        // browse list (see RenderQueueDrawer).
        private RectTransform _drawerContent;
        // WO-1368 — the row factory's current parent. Null => the browse list (_listContent).
        // Set for the duration of a drawer render so AddQueueRow &c. can be reused verbatim
        // instead of being forked into a second, drift-prone copy.
        private RectTransform _rowParent;
        private GameObject _queueDrawer;
        private Button _queueDrawerToggle;
        private bool _queueDrawerOpen;
        // WO-1393 - the drawer's band placement. The three px numbers are captured in the ONE
        // geometry pass (the same cursor that seats every band), so the drawer band is placed
        // from the measured well, never from a second copy of the arithmetic.
        private float _wellPx;
        private float _listBandTopPx;
        private float _listBandPx;
        private RectTransform _drawerList;        // the drawer's scroll zone host
        private RectTransform _drawerSlotOffer;   // Buy-Builder offer; the drawer list's LAST row
        // The overlay's header: a centred "QUEUE" title and a corner X (mockup panel 8). Both are
        // full-body mode only - band mode hides them (ApplyDrawerPlacement).
        // ⚠ The names are legacy. _drawerHeading read "BUILDERS / QUEUE" and _drawerHide was a HIDE
        // button seated over the first queue card's verb until WO-1443; the FIELDS were left named
        // as they were rather than renamed in the same change that fixed the geometry, so a reader
        // of ApplyDrawerPlacement still finds them. Rename them when panel 8's rows are built.
        private GameObject _drawerHeading;
        private GameObject _drawerHide;
        // WO-1648 - the CLOSE read-back's own budget. Two states, three frames each: enough to
        // clear the un-laid-out first frame, few enough that the log never floods.
        private const int CloseReadbackMaxPerState = 3;
        private const int CloseReadbackMaxOccluders = 12;
        private int _closeReadbackHubCount;
        private int _closeReadbackDrawerCount;

        /// <summary>The shared kit CLOSE. WO-1491: visible on the HUB only (mockup panel 1).</summary>
        private Button _chromeClose;
        /// <summary>
        /// ⭐ THE CONSTANT EXIT. Owner ruling 2026-09-07, verbatim: <i>"on all the manage screens
        /// there is no way to exit. can we add a const exit button top right"</i>.
        /// <para>⛔ IT IS NOT A CHILD OF <c>_tabsHost</c>, AND THAT IS THE WHOLE POINT.
        /// <see cref="ApplyDrawerPlacement"/> does <c>_tabsHost.gameObject.SetActive(!chromeHidden)</c>
        /// while the queue overlay is up, so anything seated in that row is GONE on panel 8 - the
        /// one screen the owner reached with no way back to town. This control is a direct child of
        /// <c>chrome.content</c>, built last so it is the top sibling, and NOTHING ever deactivates
        /// it. "Constant" is a state guarantee, not a coordinate.</para>
        /// <para>⛔ ONE ROUTE, NOT A SECOND CLOSE PATH: it is handed the SAME <c>Action</c> instance
        /// that <see cref="ElarionUiKit.BuildObsidianPanel"/> takes as its <c>onClose</c> and that
        /// the scrim takes as its tap-out - see <c>exitRoute</c> in BuildChrome. The hub's CLOSE and
        /// this X cannot diverge because there is only one delegate.</para>
        /// </summary>
        private Button _manageExit;
        /// <summary>Panel 8's tab row zone - fixed chrome above the list, never a scroll row.</summary>
        private RectTransform _drawerTabs;
        /// <summary>Panel 8's title band. The X is a CHILD of this, so it cannot leave the overlay.</summary>
        private RectTransform _drawerHeader;

        // ⛔ THE QUEUE OVERLAY'S BAND TABLE. ONE SOURCE, READ BY BOTH WRITERS.
        // These were `const float` LOCALS inside BuildQueueDrawer, and that is precisely how the
        // last fault happened: ApplyDrawerPlacement could not see them, so it re-seated the list to
        // its own literal 0.02-0.86 AFTER the render and wiped the band table AND the whole-row
        // trim with it. MEASURED by MANAGE_QUEUE_LAYOUT: the list's top resolved to 0.859 of the
        // drawer instead of the authored 0.665, which put its ceiling 0.19 into the tab band and
        // produced the 688..791 overlap. Two writers, one piece of state - the same shape as the
        // dead subtree and the title rect, and the third time this file has paid for it.
        // Anything that seats a child of the drawer reads THESE. Do not re-type a fraction.
        // ⛔ THE BANDS ARE PIXELS. THE FRACTIONS ARE DERIVED FROM THEM, NEVER TYPED.
        // The fractions were authored directly and the audit caught what a fraction cannot promise:
        //   'ManageQueueTab_Builder' resolves 369.1x95.1 -- 16.9px UNDER MinTouchPx (112)
        //   'ManageQueueOverlayClose' (120px, fixed) overflowed a 0.09 header band worth 42.8px
        //     and spilled DOWN into the tab row, covering "RESEARCH 2/2" by 110x31px
        // Both are the same mistake: a control whose size is a PX FLOOR seated inside a zone whose
        // size is a FRACTION of a drawer whose height varies. 0.20 of 475px is 95px, and no amount
        // of nudging that fraction makes it a promise about pixels.
        // This is the band law ManageWorkspacePanel's header already states, applied here: heights
        // are fixed px constants, they are SUMMED, and the list takes the remainder.
        // ⛔ THE TITLE CONSUMES NO BAND. DO NOT GIVE IT ONE BACK.
        // It had 132px, and the capture showed that band EMPTY: the word QUEUE renders above the
        // drawer's visible top edge, because the drawer's sliced content-panel art does not reach
        // its own rect. So 132px of the overlay was reserved for something that was not drawn in it,
        // and it was the difference between one visible row and two:
        //   list 175px (1 row)  ->  reclaiming 132px  ->  307px (2 rows)
        // The title is now an OVERLAY label pinned at the drawer's top - legible exactly where it
        // already is, costing the rows nothing. A band is for a control that sits IN it.
        /// <para>⭐ WO-1567 ROUND 26 - IT TAKES A BAND AGAIN, AND THE OVERLAY SEAT IS RETIRED.
        /// ⛔ THE MEASURED REASON, off Builds/cap-manage-wave5.log, on all three *_queue frames:
        /// <c>TEXT OFF PLATE ... 'Drawer_Header/Label' ("QUEUE") overflows its layout.body
        /// ZoneBacking by 112 ref px -- text y 313.2..425.2 vs plate y -444.8..313.2</c>, and the
        /// same for the X's label. Round 25 raised <see cref="DrawerOverlayY1"/> to 1.0 and stood
        /// the chrome row down, so the pivot-0 overlay grew the header 112px ABOVE the well - which
        /// is off the body's black plate, the founding-Echo-card defect the oracle is named for.
        /// <para>⛔ AND EXTENDING THE DRAWER'S OWN PLATE OVER THAT BAND DOES NOT SATISFY IT. READ
        /// THE RULE: <c>UICaptureLaunch.ZoneBodyAbove</c> walks to the ancestor literally named
        /// <c>Zone_Body</c> and <c>PlateOf</c> takes THAT zone's ZoneBacking child - not the nearest
        /// plate. A drawer-owned plate 112px tall in the chrome band is still outside the body's.
        /// The only conforming seat is INSIDE the body, so the band comes out of the list.</para>
        /// <para>⚠ THE COST IS ONE ROW, STATED NOT HIDDEN: the list goes 614px -> 502px and the
        /// overlay seats FOUR whole rows where mockup panel 8 draws five. Five need a 612px list and
        /// the body well is 758px against 256px of the mockup's own chrome (title 112 + tabs 128 +
        /// gaps 16). Nothing here shrinks a row under the touch floor to manufacture the fifth, and
        /// SeatQueueListToWholeRows still WARNs the shortfall in px.</para>
        /// <para>The band is <see cref="DrawerTitleOverlayPx"/> - the same number, because the thing
        /// it must seat has not changed: the word QUEUE beside a MinTouchPx X.</para></para>
        private const float DrawerTitlePx = DrawerTitleOverlayPx;
        /// <summary>The title's own height, drawn ABOVE the drawer's ceiling (SeatDrawerTitleOverlay).
        /// <para>⭐ WO-1488 - IT NOW HOLDS THE X AS WELL, so it is sized to the TOUCH FLOOR rather
        /// than to a line of text. It was 56px, which cleared the ~24px TMP cull floor for the word
        /// QUEUE and nothing else; the X could not live here and so it lived in the TAB BAND, where
        /// the capture shows it reading as a FOURTH TAB beside "RESEARCH 2/2"
        /// (Builds/ui-capture/ManageFlow_BUILD_queue_2670x1200.png, 18:39). 116px seats a
        /// MinTouchPx(112) square with a 2px gutter, and it is what the drawer's ceiling
        /// (DrawerOverlayY1) is authored to leave free above itself. It is NOT part of the band
        /// sum - it is drawn above the ceiling and costs the rows nothing.</para></summary>
        /// <para>⭐ WO-1567 ROUND 25 - 112, WHICH IS <see cref="ElarionUiKit.MinTouchPx"/> EXACTLY,
        /// AND THAT IS THE WHOLE CONSTRAINT ON IT. The row exists to seat a MinTouchPx X beside the
        /// word QUEUE; 116 was four px of margin over that floor. Its band is now the CHROME ROW's
        /// (WorkspaceHeaderY0..Y1 = 0.124 of the panel = ~115 ref px at the reference surface),
        /// which the overlay stands down while it is up - so the row must FIT that band, and 116
        /// does not. Nothing here is under a floor: the X is authored at 112 and the zone is 112.</para>
        private const float DrawerTitleOverlayPx = ElarionUiKit.MinTouchPx;

        // 132, not 120: the band's tab faces fill it, so they are 132px >= MinTouchPx (112),
        // authored not clamped.
        // ⚠ WO-1488: this band NO LONGER HOLDS THE X (it moved to the title overlay, see
        // DrawerTitleOverlayPx). The size stays 132 because that is what the FACES need; the
        // "larger than the 120px X it contains" reason is retired, not the number.
        // ⭐ WO-1567 ROUND 25 - 128, AND THE FOUR PIXELS ARE SPENT ON A ROW, NOT SAVED FOR NEATNESS.
        // ⛔ IT IS STILL ABOVE THE TOUCH FLOOR AND THAT IS THE ONLY CONSTRAINT ON IT: 128 >=
        // ElarionUiKit.MinTouchPx (112) with 16px of margin, and a tab face inset to the standard
        // 0.88 of its band still resolves 112.6px. Below 128 it would stop clearing the floor with
        // that inset, so this is the bottom of the range, not an arbitrary trim.
        private const float DrawerTabsPx = 128f;
        // ⭐ WO-1567 ROUND 25 - 8, FOR THE SAME REASON, AND THE ARITHMETIC IS WRITTEN OUT BELOW.
        // ⛔ THIS IS A GUTTER BETWEEN BANDS, NOT A CONTROL: it has no touch floor and nothing is
        // typeset in it. It is read TWICE by SetDrawerBands (title->tabs, tabs->list), so the pair
        // costs 2x. The five-row budget, at the Seeker target and with the CLOSE band reclaimed:
        //   well               758px   (0.838 - 0.020 of a 927px panel; see the geometry pass)
        //   drawer             758px   (DrawerOverlayY0..Y1 = 0..1)
        //   - plate inset        0px   (flat plate + GoldPerimeter; it was 96 x 2 = 192)
        //   - title              0px   (DrawerTitlePx; the word is drawn ABOVE the ceiling)
        //   - tabs             128px
        //   - gaps              16px   (2 x this constant)
        //   = list             614px
        // and five rows need 5 x MinTouchPx + 4 x spacing(8) + padding(20) = 612px. 614 >= 612.
        // ⚠ IT CLEARS BY 2px, AND THAT IS STATED RATHER THAN RELIED ON. SeatQueueListToWholeRows
        // still DERIVES the count from the measured band and still WARNS in px when it seats fewer
        // than five - nothing here asserts five, and no row is ever shrunk under the touch floor to
        // manufacture one. If a future surface gives the well less, the screen honestly seats four.
        private const float DrawerBandGapPx = 8f;

        // =====================================================================
        //  WO-1488 — THE FULL-BODY OVERLAY'S OWN RECT, AUTHORED ONCE.
        // ---------------------------------------------------------------------
        // ⛔ TWO WRITERS SEAT THIS DRAWER AND THEY DISAGREED. BuildQueueDrawer authored
        // -0.25..0.99 of the well and ApplyDrawerPlacement's else-branch re-seated it to
        // 0.02..0.84 on the very next frame, so the build-time estimate handed to SetDrawerBands
        // (1.24 * _wellPx) described a rect that never existed. MEASURED, off the r24 log:
        //   MANAGE_QUEUE_BANDS drawer=719px   <- the build-time estimate, from the -0.25..0.99 pair
        //   MANAGE_QUEUE_BANDS drawer=475px   <- what actually renders, from the 0.02..0.84 pair
        // Two numbers for one rect is the duplicated-state defect this screen has now paid for
        // four times (the 0.86 list literal, the band-table locals, the whole-row trim, this).
        // ONE pair of constants, read by both writers and by the build-time estimate.
        //
        // THE CEILING IS 0.79, NOT 0.84, AND THE 0.05 IS THE X'S SEAT. The overlay title row is
        // drawn ABOVE the ceiling, so the room above it is (1 - DrawerOverlayY1) * well. The well
        // measures 579px (475 / 0.82, off the same log line), so 0.21 leaves 122px - enough for
        // the 116px DrawerTitleOverlayPx row and the MinTouchPx(112) X inside it, with margin.
        // At 0.84 the room was 93px and a compliant X could not be seated there AT ALL, which is
        // the whole reason it was living in the tab row and reading as a fourth tab.
        //
        // ⛔ THE FLOOR STAYS INSIDE THE WELL. The authored -0.25 hung the drawer over the shared
        // CLOSE band, and the plate's bottom 96px is TRANSPARENT MARGIN (DrawerPlateInsetPx), so
        // CLOSE would render straight through it while the drawer's own raycast swallowed the tap:
        // a visible button that does nothing. Buying rows with that is not buying them.
        // ⭐ WO-1488 (2026-09-07) — THE FLOOR IS THE WELL'S FLOOR, WHICH IS THE TOP OF THE CLOSE
        // BAND. It was 0.02, and that 2% was pure loss: the well's own floor already sits a
        // CanonCtaHeight + gutter above the panel's bottom edge (the close-band reservation, see
        // the `bodyFloor` arithmetic in Build), so nothing is under it to collide with. The task
        // for this pass is the owner's: the drawer fills from under the tab plates to the CLOSE
        // band, and 0.02 of a ~579px well is ~12px of rows given away for nothing.
        // ⛔ IT STAYS AT 0 AND DOES NOT GO NEGATIVE. A negative floor hangs the drawer over the
        // shared CLOSE through 96px of transparent plate margin - a visible button whose tap the
        // drawer's raycast eats. WO-1491 now HIDES that CLOSE on every non-hub screen, which
        // removes the collision but not the reason: the panel's frame art is down there, and a
        // plate drawn over a frame border reads as a rendering fault.
        // ⭐ WO-1567 ROUND 25 - THE CEILING IS THE WELL'S CEILING, AND THE TITLE ROW MOVES ONTO
        // THE CHROME BAND THE OVERLAY HIDES.
        // ⛔ THE 0.79 ABOVE IS SUPERSEDED, AND ITS REASONING IS KEPT because only its ARITHMETIC
        // changed: the title row and the X still need ~122px, and they still sit ABOVE the drawer's
        // ceiling. What changed is WHERE that room comes from. Mockup panel 8 draws the queue as a
        // FULL modal - QUEUE centred, an X top-right, three tabs, five numbered rows - and it draws
        // NO back arrow and NO queue pill, because the overlay IS the queue. So the chrome row is
        // hidden while the overlay is up (ApplyDrawerPlacement) and the title row takes its band,
        // instead of the rows paying 21% of the well for it.
        // MEASURED, off Builds/cap-manage-wave4.log: at 0.79 of a 580px well the drawer resolved
        // 458px, its chrome took 252px (plate inset 96x2 + tabs 132 + gaps 24) and the list got
        // 206px - ONE whole row where the mockup draws five, with the harness saying in its own
        // words that five rows need a 612px list band.
        private const float DrawerOverlayY0 = 0f;
        private const float DrawerOverlayY1 = 1f;

        /// <summary>
        /// ⭐ HOW MANY QUEUE ROWS THE MOCKUP ASKS TO SEE AT REST. Panel 8 draws FIVE numbered rows.
        /// <para>⛔ IT IS A TARGET THE ROW HEIGHT IS DERIVED FROM, NOT A PROMISE THE LAYOUT MAKES.
        /// <see cref="SeatQueueListToWholeRows"/> divides the MEASURED list band by this and floors
        /// the result at <c>ElarionUiKit.MinTouchPx</c>; when the well cannot seat five compliant
        /// rows it seats as many whole ones as it can and NAMES the shortfall in px. Squeezing five
        /// rows under the touch floor would trade a scroll gesture for five untappable rows.</para>
        /// </summary>
        private const int QueueRowsVisibleTarget = 5;

        /// <summary>
        /// The height ONE queue row is actually built at. Seeded to the authored
        /// <see cref="RowHeightPx"/> and re-derived from the measured list band by
        /// <see cref="SeatQueueListToWholeRows"/>, which runs BEFORE the rows are added
        /// (RenderQueueDrawer calls it first, then AddQueueRow).
        /// <para>⚠ A FIELD, BECAUSE IT IS A MEASUREMENT. RowHeightPx stays the authored constant
        /// every OTHER row type on this screen uses; only the queue overlay's rows chase the well,
        /// because only the queue overlay has a drawn capacity to honour.</para>
        /// </summary>
        private float _queueRowPx = RowHeightPx;

        /// <summary>
        /// ⭐ THE PIXELS THE BODY WELL GAVE BACK when it stopped reserving the shared CLOSE band on
        /// screens that do not draw one (WO-1567 round 25). The HUB - the ONE screen that renders
        /// CLOSE - re-takes exactly this much inside its own host, so the reservation lives in one
        /// place and follows <c>ElarionUiKit.CanonCtaHeight</c> instead of a second typed constant.
        /// <para>⚠ MEASURED IN THE GEOMETRY PASS, not authored: it is a function of the panel's
        /// resolved height, which is a function of the target. A const here would be right on one
        /// surface and wrong on every other.</para>
        /// </summary>
        private float _hubCloseReservePx;

        /// <summary>
        /// ⭐ THE CONTROL BAND INSIDE ONE QUEUE ROW, as fractions of <see cref="_queueRowPx"/>.
        ///
        /// <para>⛔ THIS IS THE WHOLE OF THE 40 SUB-TOUCH-FLOOR FAILURES ON
        /// Builds/cap-manage-wave4.log, and the arithmetic is exact. Every queue verb was authored
        /// at <see cref="RowCtrlY0"/>..<see cref="RowCtrlY1"/> = 0.88 of the row, whose own comment
        /// reasons from <see cref="RowHeightPx"/> (132): 0.88 x 132 = 116 >= MinTouchPx. But WO-1488
        /// made the row a MEASUREMENT clamped into [MinTouchPx, RowHeightPx], and in a short well it
        /// sits AT the floor - so 0.88 x 112 = <b>98.6</b>, the number on all forty lines
        /// (`ObsBtn_SPEED UP` 372.5x98.6, `ObsBtn_CANCEL` 516.4x98.6, `ObsBtn_Move up` 249.7x98.6).
        /// A fraction of a MEASURED height cannot promise a px floor - the identical mistake the
        /// detail CTA made at 104.1px and the queue X made at 57.7px, and the identical cure: take
        /// the px first, then convert to the fraction THIS row needs.</para>
        ///
        /// <para>⛔ AND THE ROW IS NOT GROWN TO SUIT. Raising the row to 128 so 0.88 clears the floor
        /// would spend ~80px of the list band and cost a visible row, which is the budget mockup
        /// panel 8's five rows are already short of. At the floor the control simply takes the WHOLE
        /// row (inset 0); above it, it keeps the authored 0.88 breathing room. Both are >= 112 by
        /// construction.</para>
        /// </summary>
        private float QueueCtrlY0
        {
            get
            {
                float row = Mathf.Max(1f, _queueRowPx);
                float want = Mathf.Max(ElarionUiKit.MinTouchPx, row * (RowCtrlY1 - RowCtrlY0));
                if (want >= row) return 0f;
                return Mathf.Clamp01((row - want) * 0.5f / row);
            }
        }

        private float QueueCtrlY1 { get { return 1f - QueueCtrlY0; } }

        /// <summary>
        /// ⭐ WO-1488 — THE ROW BAND IS DERIVED FROM THE PLATE, NOT FROM THE DRAWER'S RECT.
        /// <para>THE DEFECT: <c>_drawerListY0</c> was <c>gap</c> — 12px above the drawer's rect
        /// floor — while the plate the player SEES is <c>frames/content-panel</c> drawn SLICED, and
        /// its 9-slice border is 96px (content-panel.png.meta: <c>spriteBorder {96,96,96,96}</c>).
        /// The gold line lives inside that border, so the visible frame's interior floor is ~96px
        /// above the rect floor and the list overhung it by ~84px. MEASURED, r24:
        /// <c>MANAGE_QUEUE_LIST seats 2 whole rows: 292px of 307px</c> — the trim was correct and
        /// the rows were whole; they were simply whole rows seated OUTSIDE the frame. That is
        /// exactly what the capture shows: row 2's title, its CANCEL and its progress bar all
        /// painted below the gold line.</para>
        /// <para>⚠ A whole-row trim cannot catch this. It measures the list against itself; the
        /// list was never wrong. The rect and the ART were different rects.</para>
        /// <para>The value is a FALLBACK. <see cref="ResolveDrawerBands"/> re-reads it from the
        /// live sprite so a re-authored frame moves it without a code edit.</para>
        /// </summary>
        /// <para>⭐ WO-1567 ROUND 25 - 0, BECAUSE THE OVERLAY NO LONGER PAINTS A 9-SLICED FRAME.
        /// ⛔ THE REASONING ABOVE IS UNCHANGED AND STILL BINDING - a row must never overhang the
        /// art the player sees - but the ART changed: the overlay is a FLAT plate with a drawn
        /// GoldPerimeter (BuildQueueDrawer), which is what mockup panel 8 shows and which has no
        /// border to stay inside of. This value is only the FALLBACK
        /// <see cref="ResolveDrawerBands"/> uses before it has measured a sprite, and it measures a
        /// null sprite as 0 - so a stale 96 here would reserve 192px for a frame nothing draws,
        /// which is exactly the 206px list band on Builds/cap-manage-wave4.log. Re-point a
        /// SLICED plate and ResolveDrawerBands reads its real border back with no edit here.</para>
        private const float DrawerPlateInsetPx = 0f;

        /// <summary>The measured plate inset in reference px — 0 in band mode, where the drawer
        /// paints a FLAT plate with no sprite and therefore no frame art to stay inside of.</summary>
        private float _drawerPlateInsetPx = DrawerPlateInsetPx;

        // The resolved seats, computed once from the MEASURED drawer height (SetDrawerBands) and
        // read by BOTH writers - BuildQueueDrawer and ApplyDrawerPlacement. Fields, not consts,
        // because the drawer's height is a measurement; the PX above are the authored part.
        private float _drawerTitleY0 = 0.90f, _drawerTitleY1 = 0.99f;
        private float _drawerTabsY0 = 0.685f, _drawerTabsY1 = 0.885f;
        private float _drawerListY0 = 0.02f, _drawerListY1 = 0.665f;

        /// <summary>
        /// Turn the px band table into this drawer's fractions. Called once, from BuildQueueDrawer,
        /// with the drawer's own height in reference px.
        /// <para>⚠ It REPORTS a shortfall rather than silently squeezing a band under its floor: a
        /// tab row that is 95px instead of 112px is a control the player misses, and the only honest
        /// answers are a taller overlay or fewer bands - never a quieter number.</para>
        /// </summary>
        /// <summary>
        /// ⭐ RE-RESOLVE THE BANDS AGAINST THE DRAWER'S MEASURED HEIGHT, then re-seat the three
        /// zones. Called after a layout pass, from ApplyDrawerPlacement.
        ///
        /// <para>⛔ THIS IS WHY IT EXISTS: BuildQueueDrawer can only ESTIMATE the drawer's height
        /// (its rect is zero on the creation frame), and the estimate was out by 1.5x - 719px
        /// guessed against 476px real. Every fraction derived from it inherited the error, so an
        /// authored 120px tab band rendered 79.4px. Measuring the container and re-deriving is the
        /// only thing that makes a px band table mean pixels.</para>
        ///
        /// <para>⚠ The same discipline that settled the QUEUE pill: read the rendered control back
        /// rather than authoring a number at it.</para>
        /// </summary>
        private void ResolveDrawerBands()
        {
            if (_queueDrawer == null) return;
            var drawer = _queueDrawer.transform as RectTransform;
            if (drawer == null) return;
            Canvas.ForceUpdateCanvases();
            float drawerPx = drawer.rect.height;
            if (drawerPx < 1f) return;                    // no layout yet: keep the estimate

            // ⭐ WO-1488 — MEASURE THE PLATE, DO NOT ASSUME IT. The inset comes off the LIVE
            // sprite's 9-slice border, so a re-authored frame moves the row band with it and this
            // file never carries a copy of a number that lives in a .meta. Band mode paints a flat
            // plate with no sprite at all (ApplyDrawerPlacement) - no frame art, no inset.
            // Vector4 border is (left, bottom, right, top); the vertical pair is what bounds rows.
            // Sliced art draws its border at sprite-px / pixelsPerUnit * pixelsPerUnitMultiplier,
            // so the conversion is read from the Image rather than assumed to be 1:1.
            var plateImage = _queueDrawer.GetComponent<Image>();
            var plateSprite = plateImage != null ? plateImage.sprite : null;
            if (plateSprite == null)
            {
                _drawerPlateInsetPx = 0f;
            }
            else
            {
                // content-panel.png imports at 100 PPU against the canvas's own 100 reference PPU,
                // so a border pixel is a reference pixel; pixelsPerUnitMultiplier is the only thing
                // that rescales it, and it divides.
                float mult = Mathf.Max(0.0001f, plateImage.pixelsPerUnitMultiplier);
                var b = plateSprite.border;
                _drawerPlateInsetPx = Mathf.Max(b.y, b.w) / mult;
            }
            FlowTrace.Step("Manage", "MANAGE_QUEUE_PLATE sprite=" +
                (plateSprite != null ? plateSprite.name : "NONE (flat band plate)") +
                " inset=" + _drawerPlateInsetPx.ToString("0") + "px - the row band is derived from " +
                "THIS, not from the drawer's rect");

            SetDrawerBands(drawerPx);

            SeatDrawerTitleOverlay();
            if (_drawerTabs != null)
            {
                _drawerTabs.anchorMin = new Vector2(0.02f, _drawerTabsY0);
                _drawerTabs.anchorMax = new Vector2(0.98f, _drawerTabsY1);
                _drawerTabs.offsetMin = _drawerTabs.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// ⭐ THE TITLE SITS ABOVE THE DRAWER'S CEILING AND CONSUMES NO BAND.
        ///
        /// <para>⛔ AND IT CANNOT BE A ZERO-HEIGHT ZONE, which is the trap this method exists to
        /// avoid. TMP CULLS AN ENTIRE LINE whose fontSizeMin cannot seat in its rect - a band under
        /// about 24px renders BLANK, not small, and 0px renders nothing at all. Reclaiming the
        /// title's 132px by collapsing its zone would have deleted the word QUEUE rather than
        /// freeing space.</para>
        ///
        /// <para>So the zone is anchored to the drawer's TOP EDGE with pivot 0 and a fixed px
        /// height, which places it ABOVE that edge: it has a real rect to typeset in, and it takes
        /// nothing from the bands below. That is not a trick - it is where the title ALREADY
        /// rendered. The drawer's sliced content-panel art does not reach its own rect, so the word
        /// has been sitting in that margin every round; the band reserved for it inside the drawer
        /// was empty, and it was the difference between one visible row and two.</para>
        /// </summary>
        private void SeatDrawerTitleOverlay()
        {
            if (_drawerHeader == null) return;
            _drawerHeader.anchorMin = new Vector2(0.03f, 1f);
            _drawerHeader.anchorMax = new Vector2(0.97f, 1f);
            // ⭐ WO-1567 ROUND 26 - PIVOT 1, SO IT GROWS **DOWNWARD** INTO THE DRAWER.
            // ⛔ IT USED TO BE PIVOT 0 (grow UPWARD, out of the drawer) and that is the whole of
            // Builds/cap-manage-wave5.log's six remaining geometry failures: once
            // DrawerOverlayY1 reached 1.0, "upward out of the drawer" became "off the body's black
            // plate", which RULE 1 [text-off-plate] fails by name and by 112 ref px exactly. The
            // zone still has a REAL rect of DrawerTitleOverlayPx - which is the property this
            // method exists for, since TMP culls a line whose rect cannot seat its font floor - it
            // simply takes that rect from the list now, and DrawerTitlePx is no longer 0 to match.
            _drawerHeader.pivot = new Vector2(0.5f, 1f);
            _drawerHeader.sizeDelta = new Vector2(0f, DrawerTitleOverlayPx);
            _drawerHeader.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Print the tab BAND's rect beside one tab FACE's        /// <summary>
        /// Print the tab BAND's rect beside one tab FACE's, on one line, so the chrome between them
        /// is a number rather than a theory. If they differ, the difference IS the prefab's inset -
        /// the same gap that made the QUEUE pill look clipped for nine rounds.
        /// </summary>
        private void TraceQueueTabFit()
        {
            if (_drawerTabs == null) return;
            Canvas.ForceUpdateCanvases();
            RectTransform face = null;
            for (int i = 0; i < _drawerTabs.childCount; i++)
            {
                var c = _drawerTabs.GetChild(i) as RectTransform;
                if (c == null || !c.gameObject.activeSelf) continue;
                face = c;
                break;
            }
            FlowTrace.Step("Manage", "MANAGE_QUEUE_TABFIT authored=" + DrawerTabsPx.ToString("0") +
                "px" + RectLine("band", _drawerTabs) + RectLine("face", face) +
                " floor=" + ElarionUiKit.MinTouchPx.ToString("0"));
        }

        private void SetDrawerBands(float drawerPx)
        {
            if (drawerPx < 1f) return;
            float need = DrawerTitlePx + DrawerTabsPx + 2f * DrawerBandGapPx;
            if (need + RowHeightPx > drawerPx)
                FlowTrace.Warn("Manage", "the queue overlay is " + drawerPx.ToString("0") +
                    "px and its chrome alone needs " + need.ToString("0") +
                    "px (title " + DrawerTitlePx + " + tabs " + DrawerTabsPx + " + gaps " +
                    (2f * DrawerBandGapPx) + ") - fewer than one " + RowHeightPx +
                    "px row is left. The overlay needs to be taller");

            float gap = DrawerBandGapPx / drawerPx;
            // The title's zone is a hairline at the very top: it PAINTS there (the label overflows
            // it upward into the frame's own margin, which is where the capture already showed it)
            // but it RESERVES nothing, so the tab row starts at the drawer's ceiling.
            _drawerTitleY1 = 1f;
            _drawerTitleY0 = 1f - DrawerTitlePx / drawerPx;
            _drawerTabsY1 = _drawerTitleY0 - gap;
            _drawerTabsY0 = _drawerTabsY1 - DrawerTabsPx / drawerPx;

            // ⛔ WO-1488 — THE LIST IS BOUNDED BY THE PLATE, THEN BY THE TABS. Both, always, and
            // in that order. The floor is the plate's inner edge (never `gap`, which measured the
            // wrong rect); the ceiling is whichever of the tab row's underside and the plate's
            // inner ceiling is LOWER, so a row can no more cross the frame's top than its bottom.
            // The tabs deliberately sit OVER the frame's top margin — they read as tabs ON the
            // panel — so on the live rect the tab term is the one that governs the ceiling.
            float plate = Mathf.Max(0f, _drawerPlateInsetPx) / drawerPx;
            _drawerListY0 = plate;
            _drawerListY1 = Mathf.Min(_drawerTabsY0 - gap, 1f - plate);
            if (_drawerListY1 <= _drawerListY0)
            {
                // Degenerate rather than silently inverted: an inverted band builds rows into a
                // zero-height rect and reads on a capture as "the queue is empty".
                FlowTrace.Fail("Manage", "the queue overlay's list band inverted at " +
                    drawerPx.ToString("0") + "px (plate inset " + _drawerPlateInsetPx.ToString("0") +
                    "px x2 + tabs " + DrawerTabsPx + " + gaps leave nothing) - the rows have no band");
                _drawerListY1 = _drawerListY0;
            }

            float listPx = (_drawerListY1 - _drawerListY0) * drawerPx;
            FlowTrace.Step("Manage", "MANAGE_QUEUE_BANDS drawer=" + drawerPx.ToString("0") +
                "px title=" + DrawerTitlePx + " tabs=" + DrawerTabsPx + " plateInset=" +
                _drawerPlateInsetPx.ToString("0") + " list=" + listPx.ToString("0") + "px");
            if (listPx < RowHeightPx + 20f)
                FlowTrace.Warn("Manage", "the queue overlay's list band is " + listPx.ToString("0") +
                    "px INSIDE THE PLATE - under the " + (RowHeightPx + 20f).ToString("0") +
                    "px one row plus scroll padding needs. The rows still scroll, but none is fully " +
                    "visible at rest. Grow the overlay (DrawerOverlayY0/Y1); do not push the band " +
                    "back over the frame art");
        }
        private bool _drawerBandMode;             // true while the drawer is seated as a band
        private RectTransform _tabsHost;
        private readonly TextMeshProUGUI[] _stripCells = new TextMeshProUGUI[3];
        private RectTransform _stripHost;
        private readonly TextMeshProUGUI[] _launcherSummaries = new TextMeshProUGUI[3];
        private TextMeshProUGUI _slotLabel;
        private TextMeshProUGUI _noticeLabel;
        private Button _slotButton;
        private PanelHandle _panelHandle;
        private QueueRailView _rail;
        private ChannelId _railChannel = ChannelId.Builder;
        private bool _railPinned;                   // false => the rail rides the scroll list
        private float _railBandPx = 200f;           // QueueRailView.HeightOf(Options.Default)
        private float _tickAt;

        // Live countdown cells: the cheap tick rewrites ONLY these strings.
        private readonly List<TickCell> _tickCells = new List<TickCell>(16);

        // WO-1382 — the TRAINING NOW band's "<n>s left" cells. Its own list, NOT _tickCells:
        // the queue-row tick writes the drawer's "Building - 2m 10s left (63% done)" grammar and
        // the band's cell is the short form the owner's mockup shows. Same 1 Hz tick, strings only.
        private readonly List<TrainingNowCell> _trainingNowCells = new List<TrainingNowCell>(8);
        // WO-2001 - the per-key sprite cache MOVED to DeNelle.Core.Manage.ManageArt.LoadSprite.
        // There was one loader with two implementations (this file's and ManageArt's, which could
        // not call this one because it is `internal` to DeNelle.Village); that is duplicated state
        // of exactly the shape CLAUDE.md 2 / 5 / 16 keeps paying for. ManageArt is now the ONE
        // loader, the ONE Texture2D fallback and the ONE cache - and it also caches MISSES and
        // announces them once through FlowTrace, which this copy never did.
        private static readonly HashSet<string> ManageBuildingPortraitGaps =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "arcane-tower", "armorer", "barracks", "forge", "lumbermill", "farm",
            };

        private struct TrainingNowCell
        {
            public TextMeshProUGUI Text;
            public ChannelId Channel;
            public string JobId;
        }

        private struct TickCell
        {
            public TextMeshProUGUI Text;
            public ChannelId Channel;
            public string JobId;
            public bool Queued;
            public int PendingIndex;
        }

        /// <summary>WO-898 item 1 — progress bars advanced by the same 1 Hz tick as the timers.</summary>
        private readonly List<ProgressCell> _progressCells = new List<ProgressCell>(16);

        private struct ProgressCell
        {
            public ElarionUiKit.BarHandle Handle;
            public ChannelId Channel;
            public string JobId;
            public bool Queued;
        }

        /// <summary>True while the screen is up (the panel is built on open, destroyed on close).</summary>
        public bool IsOpen => _ui != null;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Manage", Close, () => IsOpen);
            PanelRouter.Register(PanelId.Manage, (Action)Open);
            PanelRouter.Register(PanelId.Manage, (Action<string>)Open);

            // The re-pointed bar face raises the EXISTING gate verb, so this screen is the single
            // door onto the queues and HudKitController keeps calling ObsidianQueueGate.RequestToggle
            // (the oracle at ObsidianQueueRegression that requires that call still passes).
            ObsidianQueueGate.ToggleRequested += Toggle;
        }

        private void OnDestroy()
        {
            ObsidianQueueGate.ToggleRequested -= Toggle;
            PanelRouter.Unregister(PanelId.Manage, (Action)Open);
            PanelRouter.Unregister(PanelId.Manage, (Action<string>)Open);
            if (_vm != null) _vm.Changed -= Render;
            var svc = BuildTimerService.Instance;
            if (svc != null) svc.QueueChanged -= OnQueueChanged;
            if (_ui != null) Destroy(_ui);
            _ui = null;
        }

        /// <summary>Open if closed, close if open.</summary>
        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>Build and show the screen.</summary>
        public void Open()
        {
            Close();                                  // never stack two canvases

            _vm = new ManageScreenVM();
            _vm.Changed += Render;
            // WO-2001 - the HOST's four verbs. The model owns every destination decision (canon 9
            // forbids the View deciding "which destination a prerequisite CTA should open"); these
            // are the only things the model cannot do for itself, because they leave its own graph.
            _vm.OpenQueueRequested = () => { if (!_queueDrawerOpen) ToggleQueueDrawer(); };
            _vm.OpenHeartRequested = OpenHeartSurface;
            _vm.CloseRequested = OnModelWantsOut;   // root BACK returns to the hub (mockup panel 1)
            _vm.OpenTownBuilderRequested = OpenTownBuilder;
            _vm.PlaceStructureRequested = OpenPlacementFor;   // WO-1571

            var svc = BuildTimerService.Instance;
            if (svc != null) svc.QueueChanged += OnQueueChanged;

            if (!Guard.Try("Manage", "build manage chrome", BuildChrome))
            {
                FlowTrace.Fail("Manage", "chrome build threw — screen not shown.");
                Close();
                return;
            }

            _vm.Rebuild();
            // ⭐ MANAGE OPENS ON THE HUB. Owner mockup panel 1 - "MANAGE (MAIN) - Simple entry with
            // three core options" - and CAPTURE_LOOP_GOAL.md 3.0c item 2 states in words that this
            // supersedes WO-2001's launcher retirement for this screen.
            // ⚠ WO-2001's "Entry" rule (open the last-used tab, never a chooser) is therefore
            // RETIRED, and the model's OpenDefaultScreen is still what picks the tab once a card is
            // tapped - the UI has not started deciding destinations. What changed is that the
            // player is asked WHICH destination first, because the owner's picture asks them.
            RenderLauncherCards();
            _vm.OpenDefaultScreen();
            ShowLauncher();

            // WO-465: a panel that never notifies reads as an invisible scrim and PanelRouter
            // reports the open as failed.
            if (!PanelManager.NotifyOpened(_panelHandle))
                FlowTrace.Warn("Manage", "PanelManager refused the open (another exclusive panel holds the screen).");
            FlowTrace.Step("Manage", "Manage/Queues screen opened.");
        }

        /// <summary>Contextual doorway used by Build Collections to land directly on Defense.</summary>
        public void Open(string requestedTab)
        {
            Open();
            if (_vm == null) return;
            if (string.Equals(requestedTab, "Defense", StringComparison.OrdinalIgnoreCase))
            {
                ShowOperational(ManageTab.Defense);
                // WO-1422: the destination is the Defence rail + card workspace. The old heading
                // this line echoed retired with the paged list (ruling 3.2 - it claimed "towers"
                // while the tab also lists walls, the mine, the caravan and three containers).
                FlowTrace.Step("Manage", "context open -> Defense destination (rail + selected card).");
                return;
            }
            if (string.Equals(requestedTab, "Buildings", StringComparison.OrdinalIgnoreCase))
            {
                ShowOperational(ManageTab.Buildings);
                FlowTrace.Step("Manage", "context open -> Buildings tab.");
                return;
            }
            if (string.Equals(requestedTab, "Research", StringComparison.OrdinalIgnoreCase))
            {
                ShowOperational(ManageTab.Research);
                FlowTrace.Step("Manage", "context open -> Research tab.");
                return;
            }
            // WO-1389: the post-first-raid beat's doors land on TROOPS, optionally with a troop
            // pre-selected ("Troops" or "Troops:troop-footman"). The Barracks lock is honoured
            // exactly as a card tap honours it: ShowOperational is not called for a locked tab.
            if (!string.IsNullOrEmpty(requestedTab) &&
                requestedTab.StartsWith("Troops", StringComparison.OrdinalIgnoreCase))
            {
                if (!BarracksUnlock.IsUnlocked)
                {
                    FlowTrace.Warn("Manage", "context open -> Troops REFUSED: the Barracks is locked; " +
                        "left on the launcher (the Troops card shows its own reason).");
                    return;
                }
                int colon = requestedTab.IndexOf(':');
                if (colon >= 0 && colon + 1 < requestedTab.Length)
                    _selectedTroopId = requestedTab.Substring(colon + 1);
                ShowOperational(ManageTab.Troops);   // raises manage.troops_shown once the rows exist
                FlowTrace.Step("Manage", "context open -> TRAIN & UPGRADE TROOPS (Troops tab" +
                    (string.IsNullOrEmpty(_selectedTroopId) ? "" : ", preselect '" + _selectedTroopId + "'") + ").");
                // WO-1389: a PRESELECTED troop is a selection that landed - the card is built and the
                // UPGRADE face exists - so it raises the same route-hop id a rail tap raises, AFTER
                // manage.troops_shown, so the post-raid beat's spotlight walks row -> UPGRADE face
                // in that order and never ends on a row the player has already passed.
                if (!string.IsNullOrEmpty(_selectedTroopId))
                {
                    // WO-2001: a PRESELECT is a DETAIL screen reached by BROWSING (no jump origin),
                    // so its BACK returns to the ARMY grid - ruling 28's ordinary case, which must
                    // not regress just because the door was contextual.
                    _vm?.OpenDetail(ManageTabId.Army, _selectedTroopId, null, null);
                    string preselected = _selectedTroopId;
                    Guard.Try("Manage", "raise preselect troop-selected signal", () =>
                        DeNelle.Core.Tutorial.TutorialSignals.Raise(
                            DeNelle.Core.Tutorial.TutorialSignals.ManageTroopSelectedPrefix + preselected));
                }
            }
        }

        /// <summary>Tear the screen down.</summary>
        public void Close()
        {
            if (_vm != null) { _vm.Changed -= Render; _vm = null; }
            var svc = BuildTimerService.Instance;
            if (svc != null) svc.QueueChanged -= OnQueueChanged;

            _tickCells.Clear();
            _trainingNowCells.Clear();
            _rail = null;
            _listContent = null;
            _operationalListBand = null;
            _operationalWell = null;
            _launcherHost = null;
            _launcherGrid = null;
            if (_workspace != null) { _workspace.Clear(); _workspace = null; }
            _workspaceHost = null;
            _workspaceBack = null;
            _workspaceTitle = null;
            for (int i = 0; i < _launcherBadges.Length; i++) _launcherBadges[i] = null;
            _categoryNavigationCommitted = false;
            _railBand = null;
            _drawerContent = null;
            _rowParent = null;
            _queueDrawer = null;
            _queueDrawerToggle = null;
            _queueDrawerOpen = false;
            _drawerList = null;
            _drawerSlotOffer = null;
            _drawerHeading = null;
            _drawerHide = null;
            _drawerBandMode = false;
            _wellPx = _listBandTopPx = _listBandPx = 0f;
            _railPinned = false;
            _tabsHost = null;
            _manageExit = null;
            for (int i = 0; i < _stripCells.Length; i++) _stripCells[i] = null;
            _stripHost = null;
            for (int i = 0; i < _launcherSummaries.Length; i++) _launcherSummaries[i] = null;
            _slotLabel = null;
            _noticeLabel = null;
            _slotButton = null;
            _sessionCompleteShown = false;   // WO-1027: the "you're set" line is per-open state

            if (_ui != null) { Destroy(_ui); _ui = null; }
            PanelManager.NotifyClosed(_panelHandle);
        }

        private void OnQueueChanged()
        {
            // A job started / finished / was added / removed / reordered: the SHAPE moved, so the
            // rows must be rebuilt. This is the only rebuild trigger besides a tab change.
            if (IsOpen) _vm?.Rebuild();
        }

        // =====================================================================
        //  CHROME
        // =====================================================================

        private void BuildChrome()
        {
            _ui = ElarionUiKit.BuildModalCanvas("ManageScreenUI", 31200);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            // ⛔ ONE EXIT ROUTE OUT OF MANAGE, HELD AS ONE DELEGATE INSTANCE, AND EVERY DOOR TAKES
            // IT. The scrim's tap-out, the kit chrome's CLOSE (the hub's drawn exit) and the
            // constant top-right X (owner ruling 2026-09-07) are all handed THIS `exitRoute`. A
            // second `Close` method group written at a second call site is a second close path in
            // everything but name - it can be re-pointed on its own, and then the hub and the X
            // leave Manage differently. The ruling asked for one route; this is how it stays one.
            Action exitRoute = Close;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: () => exitRoute());

            // ⛔ THE PANEL IS DELIBERATELY NEAR-FULL-BLEED, AND THAT IS A CAPACITY DECISION.
            // Owner mockup docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png: the GRID dominates every
            // panel and the chrome is thin. The measured gap on 2026-09-06 was one number -
            // MANAGE_FLOW_INVENTORY reported ARMY content=590px in a 190px viewport while screen 4
            // says in words "All 9 troops visible, no scrolling". At 0.05-0.95 the panel took 90% of
            // a ~921px reference canvas and the workspace well measured 533px, which cannot seat
            // three 150px rows (470px) once anything else is on screen. 0.02-0.98 lifts the panel to
            // 96%, and the well with it. This is the "(b) the modal's own chrome must go full-bleed"
            // half of the hand-back ManageWorkspacePanel's header has been naming since WO-2002.
            // ⛔⛔ SUPERSEDED 2026-09-07 BY AN OWNER RULING. THE PARAGRAPH BELOW IS HISTORY.
            // Owner, 01:14, verbatim: ***"i expect these images to fill the screen, not 60% of
            // it"***. Every device frame that night shows the Manage plate centred at roughly 64%
            // of the screen with the town visible around it; every panel in her mockup is
            // full-frame. Owner statements are ground truth and this one is unambiguous, so the
            // 0.18-0.82 band is retired and the panel goes FULL BLEED inside the safe area.
            //
            // ⚠ THE RETIRED REASONING WAS SOUND AND ITS ANSWER WAS PUT IN THE WRONG PLACE - that
            // is worth stating, because the problem it names is REAL and still has to be solved.
            // FrameCore genuinely is a 1210x1815 PORTRAIT frame and a landscape canvas genuinely
            // does give it an 8:1 body strip; the old fix was to shrink the PANEL until the strip
            // became 2:1. But the thing that must be ~2:1 is the GRID, not the modal - and the
            // grid can be centred inside a wide band at the cell level for free. So the cure moved
            // one layer down: ManageWorkspacePanel now clamps a cell's WIDTH to the mockup's tile
            // aspect and centres the row (see MaxTileAspect there). The panel fills the screen, the
            // tiles keep their drawn shape, and the scrim no longer eats a third of the display.
            //
            // ⛔ ANYONE NARROWING THIS AGAIN OWES THE OWNER A REASON, not a geometry argument -
            // the geometry argument has an answer one layer down.
            //
            // ── history, kept because it records what the numbers were and why ──
            // ⛔ THE PANEL IS NARROW ON PURPOSE. Do not stretch it back across the canvas.
            // THE MEASUREMENT THAT FORCED IT: FrameCore is a 1210x1815 PORTRAIT frame
            // (ElarionUiKit.cs:437-458, pixel-measured 2026-07-03) and Manage was stretching it
            // across a 2670x1200 landscape canvas. Its body zone is x 0.055-0.945 - so at full
            // width the workspace band came out roughly 2400 x 450px, an 8:1 strip, and NO tile
            // shape can be right inside it. Round 4 proved both horns: square cells filling the
            // height left the grid a narrow strip with the panel black on either side (22% of the
            // width on ARMY); wide cells filling the width read as BARS at 793x134 (5.9:1).
            // The owner's mockup panels are content areas of roughly 2:1, and their tiles are
            // ~1:1 on BUILD and ~2.3:1 on ARMY. That is only reachable by making the PANEL the
            // shape the design assumes instead of asking the grid to absorb the difference:
            //   0.18-0.82 of the canvas width = 64% -> content ~1218px against a ~583px well.
            // ARMY 3 cols x 3 rows then lands near 399x188 (2.1:1) once the tab row leaves the
            // body, and BUILD 5x2 near 236x220 (1.07:1) - both inside the mockup's proportions.
            // The scrim owns the margins either side, which is what a modal is for.
            var chrome = ElarionUiKit.BuildObsidianPanel(
                _ui.transform, "MANAGE",
                // FULL BLEED (owner ruling 2026-09-07 01:14). The 0.02 inset on every edge is the
                // device safe area, not a margin: it keeps the obsidian frame's own border off a
                // rounded corner and out of a notch. 0.96 x 0.96 clears the 0.95 floor the
                // fixture pins, with 1% of headroom on each axis.
                new Vector2(ManagePanelInsetF, ManagePanelInsetF),
                new Vector2(1f - ManagePanelInsetF, 1f - ManagePanelInsetF),
                // ⛔ THE SAME `exitRoute` INSTANCE THE SCRIM AND THE CONSTANT X TAKE. This is what
                // makes "the X leaves Manage the way the hub's CLOSE does" true by construction
                // rather than by two call sites happening to name the same method today.
                exitRoute, frameName: RpgUiCatalog.FrameCore);
            if (chrome == null)
            {
                FlowTrace.Fail("Manage", "BuildObsidianPanel returned no chrome — the screen has no host.");
                return;
            }
            // Presentation is shared with Pause/Settings rather than reimplemented per screen.
            // Commands, zones, timers, and authoritative queue state remain owned here.
            MedievalUiSkin.ApplyShell(chrome);
            _workspaceTitle = chrome.title;
            // WO-1491: held so ApplyScreenVisibility can show CLOSE on the hub and hide it on the
            // seven screens the mockup draws without one. Never re-found by name at paint time.
            _chromeClose = chrome.close;
            // ⭐ WO-1597 - CLOSE READS AS A LIVE BUTTON, BECAUSE IT IS ONE.
            // ⛔ IT WAS NEVER DISABLED. The ticket calls it "a dimmed, non-interactive plate" and the
            // owner's frame does read that way, but the CONTROL is fully wired: ObsidianCloseButton
            // hands it the SAME `exitRoute` delegate the scrim and the constant X take, so the only
            // defect is CONTRAST - the kit seats it Style1/Gray with a grey label on a near-black
            // panel, and MANAGE_MOCKUP_panel1_hub.png draws a plain plate with a light CLOSE on it.
            // Fixing the appearance rather than rebuilding the button keeps the one-delegate
            // guarantee intact; a second close path is what WO-1491 spent a round removing.
            // ⚠ A DEAD-LOOKING BUTTON IS A REAL DEFECT, not a cosmetic one: the player reads it as
            // broken and stops trusting the screen's other affordances. Colour is not the channel
            // here (the owner is red/green colourblind) - LUMINANCE is, and that is what moves.
            if (_chromeClose != null)
            {
                _chromeClose.interactable = true;
                var closeLabel = _chromeClose.GetComponentInChildren<TMP_Text>(true);
                if (closeLabel != null)
                {
                    closeLabel.text = "CLOSE";
                    closeLabel.color = ElarionUi.Parchment;
                }
                ElarionUiKit.GoldPerimeter((RectTransform)_chromeClose.transform);
                FlowTrace.Step("Manage", "MANAGE_HUB_CLOSE the shared CLOSE is live and legible - " +
                    "interactable, label 'CLOSE' at Parchment with a gold perimeter, routed through " +
                    "the same exitRoute delegate as the constant X (WO-1597; it was never disabled, " +
                    "only unreadable)");
            }

            // The approved Manage modal is one continuous obsidian field. FrameCore is
            // border-heavy and its transparent centre exposed the world around the troop
            // workspace, especially below/right of the scroll content. Seat a full content
            // backing behind every drop-zone; the ornate outer frame remains untouched.
            if (chrome.content != null)
            {
                var fill = ElarionUiKit.AddImage(chrome.content.transform, "ManageBodyFill",
                    Vector2.zero, Vector2.one, ElarionUiKit.ObsidianFill, rounded: false);
                var fillImage = fill != null ? fill.GetComponent<Image>() : null;
                if (fillImage != null) fillImage.raycastTarget = false;
                if (fill != null) fill.transform.SetAsFirstSibling();
            }

            // =================================================================
            //  ONE OWNED GEOMETRY PASS — measure the well, then spend it
            // -----------------------------------------------------------------
            // Read the panel height the DETERMINISTIC way: a live rect read on the canvas's
            // creation frame returns RAW SCREEN pixels (the CanvasScaler has not applied yet).
            // PostScaleCanvasHeight replays the scaler's own math, so every number below is in
            // the reference-px space the anchors will really resolve against.
            // =================================================================
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(
                chrome.root != null ? chrome.root.transform : _ui.transform);
            float panelFracH = 0.90f, panelFracW = 0.92f;
            if (chrome.root != null)
            {
                var rootRt = (RectTransform)chrome.root.transform;
                panelFracH = Mathf.Max(0.05f, rootRt.anchorMax.y - rootRt.anchorMin.y);
                panelFracW = Mathf.Max(0.05f, rootRt.anchorMax.x - rootRt.anchorMin.x);
            }
            float panelPx = Mathf.Max(1f, canvasH * panelFracH);
            float panelWpx = Mathf.Max(1f, CanvasWidthPx(canvasH) * panelFracW);

            // The kit reserves the bottom of EVERY framed panel for the ONE shared Close (a fixed
            // CanonCtaHeight box growing up from y=0.050) and then parks the frame's designed
            // FOOTER band above it. This screen uses NO footer zone, so that relocated band is
            // dead space between the list and the Close — reclaim it by dropping the body floor
            // straight onto the Close band + a gap. That is the whole of band 5's arithmetic.
            float closeBandTop = CloseBandY0 + ElarionUiKit.CanonCtaHeight / panelPx;
            // ⭐ WO-1567 ROUND 25 - THE CLOSE BAND IS RECLAIMED ON EVERY NON-HUB SCREEN.
            // ⛔ CLOSE IS THE HUB'S ALONE (WO-1491, ApplyScreenVisibility) - it is SetActive(false)
            // on BUILD, ARMY, RESEARCH, every detail screen and the queue overlay. The body floor
            // nevertheless kept reserving its whole band on all of them, so ~150 reference px of
            // the well was held for a button that is not rendered. MEASURED on
            // Builds/cap-manage-wave4.log: the workspace well resolved 580px, the grid took all of
            // it, and both the 5x2 tile band and the queue overlay were short by exactly that
            // reservation - the grid painted its two rows into the TOP HALF of the well
            // (ManageFlow_BUILD_gridtop_2670x1200.png) and the drawer seated ONE row of five.
            // The floor is now the frame's own inner edge; the HUB re-takes the reservation INSIDE
            // its own host (BuildLauncher), which is the one screen that draws CLOSE.
            float bodyFloor = WorkspaceBodyFloorY;
            // ⚠ DERIVED, NEVER TYPED. The hub's bottom reservation is the band this floor just gave
            // up, in px, so the two can never disagree: raise CanonCtaHeight or CloseBandY0 and the
            // hub's cards move with the button instead of overprinting it.
            _hubCloseReservePx = Mathf.Max(0f, (closeBandTop + CloseGapY - bodyFloor) * panelPx);
            FlowTrace.Step("Manage", "MANAGE_CLOSE_BAND reclaimed " + _hubCloseReservePx.ToString("0") +
                "px for every non-hub screen (body floor " + bodyFloor.ToString("0.###") +
                " instead of " + (closeBandTop + CloseGapY).ToString("0.###") +
                ") - the hub re-reserves it inside ManageCategoryLauncher, where CLOSE actually renders");

            RectTransform bodyRt = chrome.layout != null ? chrome.layout.body : null;
            float bodyTop = bodyRt != null ? bodyRt.anchorMax.y : 0.835f;
            // ⭐ RECLAIM THE DEAD STRIP BETWEEN THE BODY AND THE TITLE.
            // The frame's header zone starts at y 0.900 (ElarionUiKit.cs:442,
            // `z.header = new Vector4(0.24f, 0.900f, 0.88f, 0.972f)`), and the body stops at 0.835 -
            // so 6.5% of the panel between them belonged to nothing. WorkspaceHeaderY0 (0.865) puts
            // the BACK / HEART / QUEUE row there at 0.865-0.975 and hands the body everything below
            // it. The row is 0.11 x ~884px = ~97px... see WorkspaceHeaderY0's own note: it is sized
            // against MinTouchPx and the buttons inside it are authored to the band, not clamped.
            // MEASURED REASON: the grid band is the screen (mockup), and every px above it that
            // holds nothing is a px the tiles do not get.
            if (bodyRt != null && WorkspaceHeaderY0 > bodyTop && WorkspaceHeaderY0 - bodyFloor > 0.05f)
                bodyTop = WorkspaceHeaderY0;
            if (bodyRt != null && bodyTop - bodyFloor > 0.05f)
            {
                bodyRt.anchorMin = new Vector2(bodyRt.anchorMin.x, bodyFloor);
                bodyRt.anchorMax = new Vector2(bodyRt.anchorMax.x, bodyTop);
                bodyRt.offsetMin = new Vector2(bodyRt.offsetMin.x, 0f);
                bodyRt.offsetMax = new Vector2(bodyRt.offsetMax.x, 0f);
            }
            // Parent to layout.body, NOT chrome.content — the proven idiom (WO-778): content dropped
            // straight onto chrome.content clips under the title band and the shared Close button.
            // Without a layout (procedural fallback frame) mint the same well by hand so the band
            // cursor below still measures from a real body top.
            RectTransform well = bodyRt ?? MakeZone(
                chrome.content != null ? chrome.content.transform : _ui.transform, "Zone_Body_Manage",
                new Vector2(0.055f, bodyFloor), new Vector2(0.945f, bodyTop));
            _operationalWell = well;
            float wellPx = Mathf.Max(0f, (bodyTop - bodyFloor) * panelPx);

            // ── Band 1a: the ALL-THREE-LINES strip. Every channel stays glanceable on every tab,
            //    as TEXT, so the player never loses sight of a line the current tab does not own.
            //    It seats in the frame's own SUB-HEADER band when the frame has one (free real
            //    estate ABOVE the well — it costs the list nothing); otherwise it takes a band.
            // The approved Manage language treats the three production lines as real status
            // cards directly beneath the title. The legacy frame's sub-header seat is too shallow
            // and is partially covered by the ornate shell, which made the strip disappear in
            // Seeker captures. Spend an explicit body band so the summaries remain visible and
            // stable at every supported ratio.
            RectTransform subHeader = null;
            bool stripInBody = true;
            float stripPx = StripBandPx;

            // ── Band 5b: the NOTICE line. Same reclaim: the Close band is CanonCtaHeight tall and
            //    the Close box is only CanonCtaWidth wide and centred, so the column to its LEFT is
            //    dead space. Seat the notice there when it clears the box; fall back to a body band.
            //    ⚠ Not a toast. ElarionUiKit.ShowToast renders at sorting order ~720 and this modal
            //    sorts at 31200, so a toast raised from here would be drawn UNDERNEATH the screen the
            //    player is looking at — i.e. a refusal would LOOK like a silent no-op, which is exactly
            //    the failure §12 forbids and exactly the bug WO-911 is fixing on the Finish button.
            float noticeX1 = 0.5f - (0.5f * ElarionUiKit.CanonCtaWidth / panelWpx) - 0.02f;
            bool noticeBesideClose = chrome.content != null && noticeX1 >= 0.24f;

            // ── THE SUM. Every band costs its height PLUS the gutter that follows it; the list is
            //    last (or second-last when the notice is in-body), so it pays no trailing gutter.
            _railBandPx = QueueRailView.HeightOf(QueueRailView.Options.Default);
            float stripCost = stripInBody ? StripBandPx + BandGapPx : 0f;
            float noticeCost = noticeBesideClose ? 0f : NoticeBandPx + BandGapPx;
            // F8 2026-08-31: the queue rail and permanent-builder offer are secondary controls.
            // They live in an on-demand side drawer and spend no vertical tower-browse space.
            float fixedNoRail = stripCost + noticeCost;

            // The rail is the ONE elastic element: 200 fixed px of card art whose every fact is
            // already on the strip (line status) and on the rows below (per-job label, countdown,
            // controls). It keeps its own PINNED band only while the well can still seat a usable
            // list underneath; otherwise it is demoted into the scroll list as its first row —
            // deliberately scrolled, never overlapped, and said out loud in the trace below.
            _railPinned = true; // pinned inside the drawer, never injected into the browse list
            float fixedPx = fixedNoRail;
            float listPx = wellPx - fixedPx;

            if (!_railPinned)
                FlowTrace.Warn("Manage", string.Format(
                    "rail NOT pinned: it needs {0:0}px + {1:0}px gutter, and pinning it would leave the " +
                    "list {2:0}px (floor {3:0}px). Demoted to the FIRST ROW OF THE SCROLL LIST — it scrolls, " +
                    "nothing overlaps, and the three-line status strip stays pinned above.",
                    _railBandPx, BandGapPx, wellPx - fixedNoRail - (_railBandPx + BandGapPx), MinListPx));
            if (listPx < 0f)
            {
                FlowTrace.Warn("Manage", string.Format(
                    "BAND OVERFLOW: the fixed bands need {0:0}px but the well is only {1:0}px — short by " +
                    "{2:0}px. The list is clamped to 0 rather than letting bands overprint each other.",
                    fixedPx, wellPx, fixedPx - wellPx));
                listPx = 0f;
            }
            else if (listPx < MinListPx)
                FlowTrace.Warn("Manage", string.Format(
                    "list well is {0:0}px, under the {1:0}px floor (one {2:0}px row under its {3:0}px header) — " +
                    "the list still scrolls, but fewer than one row is visible at rest.",
                    listPx, MinListPx, RowHeightPx, SectionHeaderPx));

            // §12: the geometry is PROVEN by a capture, not by an eyeball. One line, every band.
            float gapsPx = fixedPx
                         - (stripInBody ? StripBandPx : 0f)
                         - TabsBandPx
                         - (noticeBesideClose ? 0f : NoticeBandPx);
            FlowTrace.Step("Manage", string.Format(
                "bands(px): canvas={0:0} panel={1:0} well={2:0} || strip={3:0}[{4}] rail={5:0}[{6}] " +
                "slot={7:0} tabs={8:0} notice={9:0}[{10}] gaps={11:0} => fixed={12:0} LIST={13:0} (floor {14:0})",
                canvasH, panelPx, wellPx, stripPx, stripInBody ? "body" : "sub-header",
                _railBandPx, "side-drawer",
                0f, TabsBandPx,
                noticeBesideClose ? NoticeCloseBandPx : NoticeBandPx,
                noticeBesideClose ? "close-band" : "body",
                gapsPx, fixedPx, listPx, MinListPx));

            // ── LAY THE BANDS. One cursor, top-down, gutter after every band. Nothing here can
            //    overlap anything else: each band's height is pixels it OWNS.
            float cursor = 0f;
            BuildStrip(stripInBody ? Band(well, "Band_ChannelStrip", ref cursor, StripBandPx) : subHeader);
            // The title already reads MANAGE - {DESTINATION}. Repeating that destination in a
            // full touch-height body band spent the first fold without adding information.
            // Queue is a title-row action in the approved reference, opposite Back.
            // ⛔ THE CHROME ROW SPANS THE FRAME'S OWN BODY X-ZONE, NOT 0..1 OF THE PANEL.
            // This is the fix for the clipped QUEUE pill, and it is MEASURED rather than inferred:
            // ElarionUiKit.cs:458 authors FrameCore's body as x 0.055-0.945, i.e. the ornate border
            // owns the outer 5.5% on each side. The row used to span 0..1, so a pill seated at
            // 0.925 of the ROW was at 0.925 of the PANEL - inside the border art, where the frame
            // clipped "QUEUE" mid-word and pushed its badge outside the panel entirely. Pulling the
            // pill in twice did not help because the row itself was the wrong rect; the second
            // attempt (0.775-0.925) was still reading a rendered edge instead of the frame's zone.
            // Anchoring the ROW to the body zone makes every child's fraction a fraction of the
            // usable content, which is what those fractions were always meant to be - and it lines
            // the back arrow up with the grid's left edge for free.
            // ⛔ THE RIGHT EDGE IS ManageChromeRightX, NOT A REPEATED 0.945f. The constant exit is
            // seated against the same number on chrome.content, so the row and the X share one
            // edge by construction instead of by two literals agreeing today.
            _tabsHost = MakeZone(chrome.content.transform, "ManageHeaderActions",
                new Vector2(0.055f, WorkspaceHeaderY0), new Vector2(ManageChromeRightX, WorkspaceHeaderY1));
            BuildTabs();

            // WO-1393: the drawer band is seated from these three numbers (ApplyDrawerPlacement).
            _wellPx = wellPx;
            _listBandTopPx = cursor;
            _listBandPx = listPx;
            var listBand = Band(well, "Band_List", ref cursor, listPx);
            _operationalListBand = listBand.gameObject;
            var scroll = ElarionUiKit.MakeScrollZone(listBand, spacing: 8f, padding: 10);
            _listContent = scroll != null ? scroll.content : null;
            if (_listContent == null)
                FlowTrace.Fail("Manage", "MakeScrollZone returned no content — the list host is missing.");

            BuildNotice(noticeBesideClose
                ? NoticeSeatBesideClose(chrome.content.transform, noticeX1)
                : Band(well, "Band_Notice", ref cursor, NoticeBandPx));

            // WO-2001 - the workspace host is created BEFORE the drawer so the drawer is a LATER
            // sibling and paints over it. QUEUE is an OVERLAY (the owner's flow), and the host is
            // deactivated outright while it is open, so nothing shows through and nothing under it
            // stays tappable.
            BuildWorkspaceHost(well);

            BuildQueueDrawer(well);

            BuildLauncher(well);
            // ⭐ THE BACK CONTROL IS A `<-` ARROW AT TOP-LEFT, not a BACK word-button.
            // docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png draws it on all eight numbered panels;
            // CAPTURE_LOOP_GOAL.md 3.0b states it. It is a SMALL SQUARE seat (0.035-0.115 of the
            // panel width) rather than the old 0.035-0.205 word slab, because in the mockup the
            // chrome is thin and the grid is the screen.
            // ⭐ WO-1491 (2026-09-07): THE FACE IS THE DELIVERED SPRITE ManageArt.IconBack, bound
            // by ApplyBackGlyph. The ASCII "<-" survives ONLY as the fallback when that sprite is
            // unresolved - the door must never render blank.
            // ⚠ THE "left arrow" CHARACTER IS STILL BANNED: this project's fonts render non-ASCII
            // as tofu (CLAUDE.md 7's canon-strings note; ManageScreenVM.Ascii exists for exactly
            // this). A SPRITE is not a character, which is why this is not a reversal of that rule.
            // Assets/Resources/RpgUi/button/arrow.png stays rejected as the face - it is a filled
            // RIGHT-pointing PLAY triangle, and mirroring a play glyph reads as "rewind".
            // ⛔ THE BACK ARROW IS BUILT IN BuildTabs, NOT HERE. Do not construct it at chrome time.
            // MEASURED 2026-09-06 in ManageFlow_ARMY_gridtop: the arrow was GONE - the row started
            // with the HEART face and the screen had no visible way back. Cause, and it is mine:
            // round 5 re-parented the arrow from chrome.content to _tabsHost so its fractions would
            // mean fractions of usable content - and BuildTabs DESTROYS every child of _tabsHost on
            // entry (its rebuild loop), on the first Render. A control built once into a host that
            // is cleared every paint is a control that exists for exactly one frame.
            // It is now rebuilt with its siblings, which is what the heart face and the queue pill
            // already did and why they survived.

            // ⭐ AND THE CONSTANT EXIT, BUILT LAST SO IT IS THE TOP SIBLING OF chrome.content.
            // Owner ruling 2026-09-07: "on all the manage screens there is no way to exit. can we
            // add a const exit button top right".
            BuildConstantExit(chrome, exitRoute);
        }

        /// <summary>
        /// ⭐ THE ONE EXIT FROM MANAGE, TOP-RIGHT, ON EVERY SCREEN. Owner ruling 2026-09-07,
        /// verbatim: <i>"on all the manage screens there is no way to exit. can we add a const exit
        /// button top right"</i>.
        ///
        /// <para>⛔ THIS SUPERSEDES THE WO-1491 PASS FOR THE TOP-RIGHT SEAT, AND ONLY FOR IT. That
        /// pass read the mockup sheet correctly - CLOSE is drawn on panel 1 alone - and stood the
        /// shared bottom CLOSE down on the other seven screens
        /// (<see cref="ApplyScreenVisibility"/>, which is UNTOUCHED). What the sheet did not carry
        /// was that the back arrow walks the model's screen graph and therefore never LEAVES
        /// Manage: on BUILD / ARMY / RESEARCH, on a detail card, on the research tree and on the
        /// queue overlay, the player had no route back to town at all. The arrow navigates WITHIN
        /// Manage; this X exits it. Two controls, two jobs, and the hub keeps its drawn CLOSE.</para>
        ///
        /// <para>⛔ ONE ROUTE. <paramref name="exitRoute"/> is the SAME <c>Action</c> instance the
        /// kit was handed as <c>onClose</c> (so it is literally what the hub's CLOSE invokes) and
        /// the scrim was handed as its tap-out. There is no second close path to keep in step.</para>
        ///
        /// <para>⛔ PARENTED TO <c>chrome.content</c>, NOT TO <c>_tabsHost</c>. Two reasons and
        /// either alone is enough: (1) <see cref="ApplyDrawerPlacement"/> deactivates the whole
        /// chrome row under the queue overlay, so a child of it is absent on panel 8 - the exact
        /// screen the ruling names; (2) <see cref="BuildTabs"/> DESTROYS every child of the row on
        /// entry, which is how the back arrow vanished for a round (see BuildChrome's note). A
        /// control that must be constant cannot live in a host that is cleared and hidden.</para>
        ///
        /// <para>⛔ AUTHORED IN PX AT THE TOUCH FLOOR, pinned to the row's right edge with pivot 1 -
        /// the same construction the queue overlay's X uses (BuildQueueDrawer's ClosePx) and for
        /// the same reason: <c>chrome.content.rect</c> is ZERO on the frame this runs, so any
        /// fraction derived from it would be a guess, and a fraction of a band whose height varies
        /// cannot promise a px floor anyway.</para>
        ///
        /// <para>⚠ THE TITLE. <c>TitleLocalX0/X1</c> are NOT moved. DERIVED, NOT CAPTURED: the pill
        /// keeps its measured width and its right edge moves left by exactly
        /// <c>ManageExitPx + ManageExitGapPx</c> = 124 ref px = 0.060 of the reference panel width
        /// (2062px, from ManageMockupConformanceRegression's RefWellWidthPx 1835 / 0.89). The pill's
        /// LEFT edge therefore moves from content 0.764 to 0.749 while the title's right edge stays
        /// at content 0.675 - clearance narrows from 0.089 to 0.074 of the panel and no overlap
        /// opens. Pulling the title in as well would narrow a rect whose longest breadcrumb
        /// ("MANAGE - RESEARCH - SCHOOL") is already recorded UNVERIFIED, so the smaller change is
        /// the honest one. MANAGE_EXIT_RECT prints the answer beside MANAGE_QUEUE_PILL_RECT and
        /// MANAGE_TITLE_RECT so the next capture NAMES it instead of anyone theorising.</para>
        /// </summary>
        private void BuildConstantExit(ElarionUiKit.PanelChrome chrome, Action exitRoute)
        {
            if (chrome == null || chrome.content == null || exitRoute == null)
            {
                FlowTrace.Fail("Manage", "MANAGE_EXIT not built - no chrome content or no route. " +
                    "Every Manage screen is now without a way back to town, which is the exact " +
                    "defect the 2026-09-07 ruling closes.");
                return;
            }

            var exit = ElarionUiKit.BuildObsidianButton(chrome.content.transform, "X",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                // A first seat only - the px pin below is authoritative. Anchors are collapsed to
                // the row's right edge at the header band's mid-height so sizeDelta means pixels.
                new Vector2(ManageChromeRightX, WorkspaceHeaderY0),
                new Vector2(ManageChromeRightX, WorkspaceHeaderY1),
                () => exitRoute());
            if (exit == null)
            {
                FlowTrace.Fail("Manage", "MANAGE_EXIT BuildObsidianButton returned null - the " +
                    "constant top-right exit does not exist on any Manage screen.");
                return;
            }
            exit.gameObject.name = "ManageConstantExit";
            _manageExit = exit;

            var rt = (RectTransform)exit.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(ManageChromeRightX,
                0.5f * (WorkspaceHeaderY0 + WorkspaceHeaderY1));
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(ManageExitPx, ManageExitPx);
            rt.anchoredPosition = Vector2.zero;
            ElarionUiKit.ClampMinTouch(exit);

            var glyph = exit.GetComponentInChildren<TMP_Text>(true);
            if (glyph != null)
            {
                // ASCII "X", never a multiplication-sign glyph - this project's fonts render
                // non-ASCII as tofu (the same rule that keeps BACK an arrow SPRITE rather than a
                // character, and that the queue overlay's own X already obeys).
                glyph.text = "X";
                glyph.raycastTarget = false;
            }

            FlowTrace.Step("Manage", "MANAGE_EXIT built: constant top-right exit at " +
                ManageExitPx.ToString("0") + "px, right edge x=" +
                ManageChromeRightX.ToString("0.###") + " of the panel, band " +
                WorkspaceHeaderY0.ToString("0.###") + ".." + WorkspaceHeaderY1.ToString("0.###") +
                "; QUEUE seats " + ManageExitGapPx.ToString("0") +
                "px to its left. Route = the panel chrome's own onClose.");

            // ⛔ PRINT THE RECT, DO NOT REASON ABOUT IT. This screen has twice ended a multi-round
            // coordinate hunt by printing a rectangle (MANAGE_QUEUE_PILL_RECT, MANAGE_TITLE_RECT)
            // after several rounds of theorising about one. Same units - world corners - so the
            // three lines can be compared to each other directly in any capture.
            Canvas.ForceUpdateCanvases();
            var exitCorners = new Vector3[4];
            rt.GetWorldCorners(exitCorners);
            FlowTrace.Step("Manage", "MANAGE_EXIT_RECT world x " +
                exitCorners[0].x.ToString("0.#") + ".." + exitCorners[2].x.ToString("0.#") +
                " y " + exitCorners[0].y.ToString("0.#") + ".." + exitCorners[2].y.ToString("0.#") +
                " | size " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") +
                "px | floor " + ElarionUiKit.MinTouchPx.ToString("0") + "px");
        }

        // =====================================================================
        //  WO-2001 - THE THREE-TAB WORKSPACE (BUILD / ARMY / RESEARCH)
        // ---------------------------------------------------------------------
        //  Manage opens DIRECTLY on the last-used tab (BUILD by default). The
        //  four-tile launcher is superseded and is never shown; BACK walks the
        //  model's screen graph and can no longer route through it.
        //
        //  ⛔ THE HOST GIVES THE RENDERER THE WHOLE BODY WELL. ManageWorkspacePanel's
        //  header states the arithmetic and hands the call here: the fixed band stack
        //  alone (header 120 + tabs 120 + selection floor 256 + gaps) is 532px against
        //  MEASURED Manage wells of 533 / 542 / 612px, so a grid cannot exist inside
        //  the old rail path's list band. It therefore takes the well itself, and the
        //  strip band, the browse list band and the header QUEUE toggle stand down.
        //
        //  ⚠ STILL SHORT, AND SAID OUT LOUD RATHER THAN PAPERED OVER. Even the whole
        //  well is not the ~1454px canon 3's twelve tiles imply. Two things close it
        //  and BOTH are named in the hand-back: (a) the renderer must skip the
        //  selection band when Selection.Visible is false and the grid band when the
        //  tab has no tiles - the shape this composer already emits, screen by screen;
        //  (b) this modal's own chrome must go full-bleed. Until (a) lands the grid is
        //  clamped and scrolls, and ManageWorkspacePanel reports the shortfall in px.
        //  Nothing here silently re-columns or shrinks a text band to fake it.
        // =====================================================================

        /// <summary>True once the workspace renderer owns the body well.</summary>
        private bool WorkspaceActive => _workspace != null && _workspaceHost != null;

        private void BuildWorkspaceHost(RectTransform well)
        {
            if (well == null) return;
            var go = new GameObject("ManageWorkspace", typeof(RectTransform));
            _workspaceHost = (RectTransform)go.transform;
            _workspaceHost.SetParent(well, false);
            _workspaceHost.anchorMin = Vector2.zero;
            _workspaceHost.anchorMax = Vector2.one;
            _workspaceHost.offsetMin = _workspaceHost.offsetMax = Vector2.zero;
            _workspace = new DeNelle.Core.Manage.ManageWorkspacePanel(_workspaceHost);
        }

        /// <summary>
        /// Show the workspace and stand the retired chrome down. The launcher host is built (its
        /// cards are still the source-of-record for the 2026-08-31 approved art and copy) but it is
        /// never made visible again - WO-2001 removes the required chooser, and BACK no longer has
        /// a launcher to return to.
        /// </summary>
        /// <summary>
        /// ⛔ THE ONE WRITER OF "WHICH SCREEN IS ON". Every SetActive on the hub host, the
        /// operational well and the workspace host goes through here, and nowhere else.
        ///
        /// <para>THE BUG THIS EXISTS TO END, measured 2026-09-06 in
        /// Builds/ui-capture/ManageFlow_ARMY_gridtop_2670x1200.png plus
        /// <c>MANAGE_FLOW_INVENTORY ARMY: tiles=9 rendered=9 viewport=586px content=0px</c>:
        /// the model composed nine tiles, the renderer reported building nine, and the grid content
        /// measured ZERO height, because the tiles were built into a subtree whose ANCESTOR was
        /// inactive. <c>_workspaceHost</c> is a child of <c>_operationalWell</c>; ShowLauncher
        /// deactivated the WELL, and RenderWorkspace only ever re-activated the HOST. A
        /// SetActive(true) on a child of an inactive parent changes nothing on screen and runs no
        /// layout - so every tab rendered the hub and Manage had three doors onto nothing.</para>
        ///
        /// <para>Two independent writers deciding one piece of state by last-write-wins is the
        /// whole defect. There is now one flag and one writer, and the well is always re-asserted
        /// with the host it contains.</para>
        /// </summary>
        private void ApplyScreenVisibility()
        {
            // ⭐ WO-1573 - THE QUEUE OVERLAY IS A THIRD SCREEN, NOT A WORKSPACE DECORATION.
            // The hub's cards stand down under it for the same reason the workspace host does: the
            // overlay carries DESTRUCTIVE and PAID verbs, and a card grid left actionable beneath an
            // opaque modal is a mis-tap surface (the WO-1368 ruling, applied to the hub).
            if (_launcherHost != null) _launcherHost.gameObject.SetActive(_hubShowing && !_queueDrawerOpen);
            // The WELL is the workspace host's PARENT. It must follow the same state, or the host's
            // own flag is meaningless - that is exactly what produced content=0px.
            // ⛔ ...AND IT IS THE QUEUE OVERLAY'S PARENT TOO. `BuildQueueDrawer(well)` parents
            // ManageQueueDrawer to THIS transform, so `!_hubShowing` alone held the overlay's whole
            // ancestor chain inactive on the hub. That is WO-1573 exactly: the pill IS live on the
            // hub (`_tabsHost` hangs off chrome.content, a SIBLING of the well, and nothing here
            // hides it), so the tap fired ToggleQueueDrawer, the flag flipped, `SetActive(true)`
            // landed on a child of an inactive parent - and the player saw nothing happen.
            // ⚠ THE SAME DEAD-SUBTREE FAULT THIS METHOD'S OWN HEADER RECORDS, one screen over: a
            // SetActive(true) on a child of an inactive parent changes nothing on screen and runs no
            // layout. The cure is the same one - the well is re-asserted with everything it CONTAINS,
            // and this stays the single writer of that flag.
            if (_operationalWell != null)
                _operationalWell.gameObject.SetActive(!_hubShowing || _queueDrawerOpen);
            if (_workspaceHost != null)
                _workspaceHost.gameObject.SetActive(!_hubShowing && !_queueDrawerOpen);
            // BACK belongs to the workspace: the hub is the root and CLOSE is its way out.
            if (_workspaceBack != null) _workspaceBack.gameObject.SetActive(!_hubShowing);
            // ⭐ WO-1491 - CLOSE IS THE HUB'S, AND ONLY THE HUB'S.
            // The mockup sheet draws CLOSE on panel 1 alone; the owner's device walk found it on
            // panels 2, 4, 6, 7 and 8 as well, beside a back arrow that already leaves the screen.
            // Two exits on one panel teach neither, and on the queue overlay the shared CLOSE sat
            // under the drawer's own X.
            // ⛔ SetActive, NOT a build-time flag: this panel builds ONE chrome and swaps SCREENS
            // inside it, so a `withClose: false` at construction would delete the hub's exit too.
            // ElarionUiKit.BuildObsidianPanel's `withClose` is the per-panel lever for surfaces
            // that never show a close at all; this is the same ruling applied per SCREEN.
            // ⚠ WO-1573 DELIBERATELY LEFT THIS LINE ALONE, and the reason is worth the sentence.
            // Now that QUEUE opens from the hub, the shared CLOSE is on screen underneath the
            // overlay - the state the paragraph above calls out ("on the queue overlay the shared
            // CLOSE sat under the drawer's own X"). It is COVERED rather than merely overdrawn:
            // BuildQueueDrawer seats the overlay down to DrawerOverlayY0 = -0.25 of the well
            // expressly so it spans the Close band, and the drawer carries an opaque Image, so it
            // eats the raycast. If a capture ever shows CLOSE readable or tappable on the queue
            // overlay, gate it here - but that would be a NEW measurement, and
            // ManageMockupConformanceRegression.cs:718 pins this line's exact text, so the two
            // changes belong in one edit with that suite's owner.
            if (_chromeClose != null) _chromeClose.gameObject.SetActive(_hubShowing);
            // ⭐ AND THE CONSTANT EXIT IS RE-ASSERTED ON, UNCONDITIONALLY, ON EVERY SCREEN.
            // Owner ruling 2026-09-07: "on all the manage screens there is no way to exit. can we
            // add a const exit button top right".
            // ⛔ THIS IS NOT THE SAME CONTROL AS _chromeClose AND MUST NEVER BE GATED LIKE IT. The
            // line above keeps the mockup's drawn CLOSE on panel 1 alone (WO-1491, unchanged); this
            // line is the ruling that supersedes it for the TOP-RIGHT seat, because the back arrow
            // walks WITHIN Manage and never leaves it. If this ever becomes conditional, the
            // BUILD / ARMY / RESEARCH grids, the detail cards, the research tree and the queue
            // overlay all lose their only route back to town again.
            if (_manageExit != null) _manageExit.gameObject.SetActive(true);

            // ⭐ WO-1648 - THE READ-BACK FIRES HERE, AFTER THE SetActive FLIPS ABOVE.
            // It reports what RENDERS, never what was set; see TraceHubCloseReadback.
            if (_hubShowing && !_queueDrawerOpen) TraceHubCloseReadback("hub");
        }

        // =====================================================================
        //  WO-1648 - THE CLOSE READ-BACK. INSTRUMENT FIRST (CLAUDE.md §12).
        // ---------------------------------------------------------------------
        //  THE MEASUREMENT THAT FORCED IT (device, build 2026.09.10.363660, ONE session):
        //    Builds/device-frames/2026-09-10_0814_363660_manage_hub.png          CLOSE plate
        //      (box 1119,975,1553,1079) max Rec.709 luma  13/255, brightest RGB (13,13,13)
        //    Builds/device-frames/2026-09-10_0815_363660_manage_queue_drawer.png SAME box
        //      max luma 254/255, brightest RGB (255,255,246)
        //  ⛔ IT IS THE SAME GameObject IN BOTH FRAMES, NOT TWO CONSTRUCTION PATHS. A x12
        //  brightness boost of the hub crop reproduces the drawer crop's art intact - the
        //  gold perimeter, the plate's bevel, the word CLOSE - so nothing is missing, mis-
        //  coloured or mis-seated. The whole subtree is attenuated to ~0.05 of itself
        //  (per-pixel ratio over the plate caps at 0.0512 = 13/254), and `_chromeClose`
        //  stays ACTIVE with the drawer open because ApplyScreenVisibility gates it on
        //  `_hubShowing` alone - the drawer opens FROM the hub. The drawer's OWN close is a
        //  different control entirely (BuildQueueDrawer's "X", top-right).
        //  ⛔ THE EXISTING TRACE CANNOT SEE THIS. MANAGE_HUB_CLOSE prints what the code SET
        //  (interactable, "CLOSE", Parchment, a gold perimeter) and every one of those is
        //  true on the dark frame. A setter echo is not a read-back, which is precisely why
        //  a green source lint sat beside a 13/255 control.
        //
        //  THE TWO LIVE CANDIDATES, and the field that separates them:
        //    A. OCCLUSION - a later-drawn near-opaque graphic overlaps the plate on the hub
        //       and is SetActive(false) once the drawer opens. `_launcherHost`
        //       (ManageCategoryLauncher, BuildLauncher :2002-2008) carries an Image at
        //       (0.012,0.014,0.018, alpha 0.995), is parented to `operationalWell.parent`
        //       with the WELL's own anchors/offsets - and the well's floor was dropped onto
        //       the CLOSE band by the geometry pass (:1417-1436), the hub re-reserving that
        //       band INSIDE this host as empty space. It is built AFTER the kit chrome's
        //       close, so it draws over it, and it flips exactly the right way: active on
        //       the hub, SetActive(false) when the drawer opens (:1798).
        //       ⚠ Consistent with, NOT proof of: a single 0.995 layer transmits 0.005 and
        //       the measured ratio is ~0.05. Blend space (linear vs gamma) and 8-bit
        //       rounding put both inside the plausible band, so the pixels alone cannot
        //       close it - the OCCLUDERS list below names the graphic or it does not exist.
        //    B. SUBTREE ALPHA - an inherited CanvasGroup / tint sits near 0.05 on the hub
        //       and 1.0 with the drawer open. `inhAlpha` (CanvasRenderer.GetInheritedAlpha)
        //       and `crColor` are the discriminator: at ~1.0 on BOTH lines candidate B is
        //       DEAD and the answer is in OCCLUDERS; below ~0.1 on the hub line it is B and
        //       the occluder list is noise.
        //  ⛔ NO PAINT EDIT UNTIL ONE OF THOSE TWO LINES NAMES IT. Two live causes, one
        //  cheap read - guessing here is the banned move.
        // =====================================================================
        /// <summary>
        /// Read BACK what the shared CLOSE actually renders as - resolved colour, canvas-renderer
        /// colour, INHERITED alpha, cull state, material, draw order, and every graphic drawn after
        /// it that overlaps its rect. Fires in both states (hub, drawer-open) so the log carries a
        /// DELTA rather than a snapshot; the capping counter keeps it off the per-frame path.
        /// </summary>
        private void TraceHubCloseReadback(string when)
        {
            if (_chromeClose == null) return;
            // Bounded, not once: the first call can land on a frame whose layout has not run
            // (this file's own geometry pass says rects read ZERO there), so a strict Once would
            // pin the useless frame. Three per state settles it and still never floods.
            bool hubState = string.Equals(when, "hub", StringComparison.Ordinal);
            if (hubState)
            {
                if (_closeReadbackHubCount >= CloseReadbackMaxPerState) return;
            }
            else
            {
                if (_closeReadbackDrawerCount >= CloseReadbackMaxPerState) return;
            }

            // ⛔ THE BUDGET IS SPENT ON A MEASURED FRAME, NEVER ON AN UNBUILT ONE, and that is the
            // difference between this read-back answering the ticket and mis-answering it.
            // ApplyScreenVisibility null-checks `_launcherHost` (:1798), i.e. it is designed to be
            // callable BEFORE BuildLauncher exists. Three early calls would burn the whole hub
            // budget on frames where candidate A's graphic HAS NOT BEEN BUILT YET - every line
            // would read `OVERLAPPING: NONE`, which is the exact answer that clears candidate A.
            // A read-back that can report the absence of a thing that does not exist yet is worse
            // than no read-back: it is confidently wrong. Same for a zero-area rect - a rect under
            // an unrun layout resolves to ZERO however many canvas updates are forced at it (this
            // file says so at ResolveDrawerBands), and every overlap test against it is false.
            if (hubState && _launcherHost == null) return;

            try
            {
                Canvas.ForceUpdateCanvases();

                var plateRt = _chromeClose.transform as RectTransform;
                Rect budgetRect = WorldAabb(plateRt);
                if (budgetRect.width <= 0f || budgetRect.height <= 0f) return;
                if (hubState) _closeReadbackHubCount++;
                else _closeReadbackDrawerCount++;
                Graphic plate = _chromeClose.targetGraphic as Graphic;
                if (plate == null) plate = _chromeClose.GetComponent<Graphic>();
                TMP_Text label = _chromeClose.GetComponentInChildren<TMP_Text>(true);

                string plateLine = DescribeCloseGraphic("plate", plate);
                string labelLine = DescribeCloseGraphic("label", label);

                // The plate's own world-space AABB (measured above, before the budget was spent),
                // so the occluder test below is a rect test and not a parenthood guess.
                Rect plateRect = budgetRect;
                string rectPart = plateRect.xMin.ToString("0") + ".." + plateRect.xMax.ToString("0") +
                                  " x " + plateRect.yMin.ToString("0") + ".." + plateRect.yMax.ToString("0");

                int drawnAfter;
                string occluders = DescribeCloseOccluders(plate, plateRect, out drawnAfter);
                string countPart = drawnAfter.ToString();

                FlowTrace.Step("Manage", "MANAGE_HUB_CLOSE_READBACK[" + when + "] " + plateLine +
                    " || " + labelLine + " || worldAabb " + rectPart + " || " + countPart +
                    " graphic(s) drawn AFTER the plate in this canvas; OVERLAPPING: " + occluders);
            }
            catch (Exception e)
            {
                // No silent failures (§12.2): a read-back that threw is a finding, not a gap.
                FlowTrace.Warn("Manage", "MANAGE_HUB_CLOSE_READBACK[" + when + "] THREW " +
                    e.GetType().Name + ": " + e.Message + " - the CLOSE render state is UNMEASURED " +
                    "on this frame; do not read its absence as green.");
            }
        }

        /// <summary>One graphic's RESOLVED render state. Every part is computed into a local first,
        /// so no quote ever lands inside an interpolation hole (CLAUDE.md §1 gate brace rule).</summary>
        private static string DescribeCloseGraphic(string role, Graphic g)
        {
            if (g == null) return role + "=<none>";
            Color c = g.color;
            string colorPart = c.r.ToString("0.###") + "," + c.g.ToString("0.###") + "," +
                               c.b.ToString("0.###") + ",a" + c.a.ToString("0.###");
            var cr = g.canvasRenderer;
            string crPart = "<no canvasRenderer>";
            string inhPart = "?";
            string cullPart = "?";
            if (cr != null)
            {
                Color crc = cr.GetColor();
                crPart = crc.r.ToString("0.###") + "," + crc.g.ToString("0.###") + "," +
                         crc.b.ToString("0.###") + ",a" + crc.a.ToString("0.###");
                inhPart = cr.GetInheritedAlpha().ToString("0.####");
                cullPart = cr.cull ? "CULLED" : "drawn";
            }
            string matPart = g.material != null ? g.material.name : "<null material>";
            string activePart = g.gameObject.activeInHierarchy ? "active" : "INACTIVE";
            string enabledPart = g.enabled ? "enabled" : "DISABLED";
            string sibPart = g.transform.GetSiblingIndex().ToString();
            return role + "='" + PathFromCanvas(g.transform) + "' color=" + colorPart +
                   " crColor=" + crPart + " inhAlpha=" + inhPart + " " + cullPart + " " +
                   activePart + " " + enabledPart + " sibling=" + sibPart + " mat=" + matPart;
        }

        /// <summary>Every ACTIVE graphic drawn after <paramref name="plate"/> in the same canvas whose
        /// world AABB overlaps it. Pre-order DFS is uGUI's draw order - a bare sibling index would
        /// miss a plate parented one level up, which is exactly candidate A's shape.</summary>
        private static string DescribeCloseOccluders(Graphic plate, Rect plateRect, out int drawnAfter)
        {
            drawnAfter = 0;
            if (plate == null) return "<no plate graphic to compare against>";
            var canvas = plate.GetComponentInParent<Canvas>();
            if (canvas == null) return "<the plate has no Canvas ancestor>";

            var order = new List<Graphic>();
            CollectGraphicsPreOrder(canvas.transform, order);
            int mine = order.IndexOf(plate);
            if (mine < 0) return "<the plate is not in its own canvas walk - report this line>";

            var hits = new List<string>();
            for (int i = mine + 1; i < order.Count; i++)
            {
                var g = order[i];
                if (g == null || !g.gameObject.activeInHierarchy || !g.enabled) continue;
                drawnAfter++;
                Rect r = WorldAabb(g.transform as RectTransform);
                if (r.width <= 0f || r.height <= 0f) continue;
                if (!r.Overlaps(plateRect)) continue;
                if (hits.Count >= CloseReadbackMaxOccluders) continue;
                Color c = g.color;
                string colorPart = c.r.ToString("0.###") + "," + c.g.ToString("0.###") + "," +
                                   c.b.ToString("0.###") + ",a" + c.a.ToString("0.###");
                string alphaPart = g.canvasRenderer != null
                    ? g.canvasRenderer.GetInheritedAlpha().ToString("0.###") : "?";
                hits.Add("'" + PathFromCanvas(g.transform) + "' color=" + colorPart +
                         " inhAlpha=" + alphaPart);
            }
            if (hits.Count == 0) return "NONE (nothing drawn after the plate overlaps it)";
            return string.Join(" ; ", hits.ToArray());
        }

        private static void CollectGraphicsPreOrder(Transform t, List<Graphic> into)
        {
            if (t == null) return;
            var g = t.GetComponent<Graphic>();
            if (g != null) into.Add(g);
            for (int i = 0; i < t.childCount; i++) CollectGraphicsPreOrder(t.GetChild(i), into);
        }

        private static Rect WorldAabb(RectTransform rt)
        {
            if (rt == null) return new Rect(0f, 0f, 0f, 0f);
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[2].x);
            float maxX = Mathf.Max(corners[0].x, corners[2].x);
            float minY = Mathf.Min(corners[0].y, corners[2].y);
            float maxY = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static string PathFromCanvas(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            var p = t.parent;
            int guard = 0;
            while (p != null && guard < 12)
            {
                path = p.name + "/" + path;
                if (p.GetComponent<Canvas>() != null) break;
                p = p.parent;
                guard++;
            }
            return path;
        }

        private void ShowWorkspace()
        {
            _hubShowing = false;
            ApplyScreenVisibility();
            if (_stripHost != null) _stripHost.gameObject.SetActive(false);
            if (_operationalListBand != null) _operationalListBand.SetActive(false);
            // ⛔ THE QUEUE PILL STAYS UP IN WORKSPACE MODE. Do not re-add a SetActive(false) here.
            // It used to be hidden because the workspace painted its own queue face; the owner's
            // mockup retires that face and makes this pill the one door, on every panel. Hiding it
            // now would strand the queue outright: measured 2026-09-06, with the workspace up the
            // operational OPEN QUEUE bands are in a list band that is SetActive(false), the activity
            // strip is retired, and the HUD Builders chip's door went in WO-911. This IS the door.
            if (_queueDrawerToggle != null) _queueDrawerToggle.gameObject.SetActive(true);
            // The title's RECT is set by ApplyWorkspaceTitle, not here - see its note. "MANAGE" is
            // only what stands in for the frame between Show and the first Render.
            ApplyWorkspaceTitle("MANAGE");
        }

        /// <summary>
        /// ⭐ WO-1443 section 1 - THE ONE HEADING ON THIS SCREEN, and it lives HERE, in the host
        /// chrome's panel title. Owner felt-test 2026-09-06, verbatim: <i>"remove the manage army and
        /// sub line replace the manage top"</i>.
        /// <para>Before this ruling the screen stacked THREE: this title read the bare word "MANAGE",
        /// and <see cref="DeNelle.Core.Manage.ManageWorkspacePanel"/> then painted the breadcrumb
        /// ("MANAGE / ARMY") and a sub line ("Every troop, unlocked or not.") down the top of the
        /// body. The breadcrumb MOVED UP into this title; the renderer paints neither, and
        /// <c>HeaderSubtitle</c> is deleted from the contract outright.</para>
        /// <para>⚠ The model still owns the string - this method binds
        /// <c>ManageWorkspaceVM.HeaderTitle</c> and never composes a breadcrumb of its own. A second
        /// place that knows how to spell "MANAGE / ARMY" is the duplicated state that produced this
        /// defect in the first place.</para>
        /// <para>⚠ FIT, AND IT IS NOT PROVEN. The title zone is content x 0.432-0.739 (~31% of the
        /// content width), and the longest breadcrumb is "MANAGE / RESEARCH / SCHOOL" at 26
        /// characters against the old "MANAGE" at 6. FitSingleLine autosizes 34-52pt, so it should
        /// seat - but that is arithmetic, not a capture. WO-1443 acceptance requires a headless
        /// capture with the PNG opened; until then treat the long breadcrumbs as UNVERIFIED.</para>
        /// </summary>
        private void ApplyWorkspaceTitle(string headerTitle)
        {
            if (_workspaceTitle == null) return;

            // ⛔ THE TITLE'S RECT IS NARROWED HERE, NOT IN ShowWorkspace. ONE WRITER, EVERY PATH.
            //
            // THE DEFECT, measured by the capture auditor on EVERY frame:
            //   BUTTON OVER TEXT 'ManageHeaderActions/ManageQueueDrawerToggle' (x 269.2..550.6)
            //   covers 'Zone_Header/Label' ("MANAGE / BUILD") (x -357.4..522.4) by 253.2x66.7 ref px
            // The load-bearing number is the LABEL'S OWN LEFT EDGE: -357.4, i.e. its rect reached
            // 357px LEFT OF THE PANEL. That is the kit frame's default full-width header rect - it
            // had never been narrowed on those frames. The visible TEXT is centred inside it and
            // looks clear (the last three captures confirm the title is not clipped), so this reads
            // as a rect overlap rather than an ink overlap - but a rect that wide will sit under any
            // control in the header row, and the auditor is right to fail it.
            //
            // WHY IT WAS NEVER NARROWED: the anchors were set in ShowWorkspace, which the CARD-TAP
            // path runs and the MODEL path does not. `panel.Open()` then `vm.EnterTab(tab)` -
            // the harness's own route (UICaptureLaunch.cs:7101-7111) and every deep-link door -
            // dismisses the hub inside RenderWorkspace and never touches ShowWorkspace at all.
            // ⚠ THIS IS THE SAME BUG AS THE DEAD SUBTREE, IN A DIFFERENT PLACE: one piece of state
            // owned by two paths, correct on the one that was tested by hand and wrong on the one
            // the harness uses. The cure is the same - the writer moves to where the value is set.
            //
            // ⛔ AND THERE IS NO SECOND QUEUE TOGGLE. Searched at source this round:
            // `ManageQueueDrawerToggle` is constructed exactly ONCE (BuildTabs), and its 281px width
            // in the audit matches the pill's authored 0.72-0.95 of a 1223px row exactly. The name
            // is legacy - it IS the pill. The "exactly one queue affordance" claim holds here; what
            // did not hold was my claim about the title's geometry.
            var titleRt = _workspaceTitle.rectTransform;
            titleRt.anchorMin = new Vector2(TitleLocalX0, titleRt.anchorMin.y);
            titleRt.anchorMax = new Vector2(TitleLocalX1, titleRt.anchorMax.y);
            titleRt.offsetMin = new Vector2(0f, titleRt.offsetMin.y);
            titleRt.offsetMax = new Vector2(0f, titleRt.offsetMax.y);

            string text = string.IsNullOrEmpty(headerTitle) ? "MANAGE" : headerTitle;
            _workspaceTitle.text = text;
            ElarionUiKit.FitSingleLine(_workspaceTitle, 34f, 52f);

            // Print the rect the auditor measures, so the next capture NAMES it rather than leaving
            // anyone to reason about a label they cannot see the bounds of. Same reason the queue
            // pill prints its own rect: four rounds of coordinate theories ended with one printed
            // rectangle, and this title has now produced a defect nobody could see either.
            var corners = new Vector3[4];
            titleRt.GetWorldCorners(corners);
            FlowTrace.Step("Manage", "MANAGE_TITLE_RECT '" + text + "' world x " +
                corners[0].x.ToString("0.#") + ".." + corners[2].x.ToString("0.#") +
                " | size " + titleRt.rect.width.ToString("0") + "x" +
                titleRt.rect.height.ToString("0") + "px | anchors " +
                titleRt.anchorMin.x.ToString("0.###") + ".." + titleRt.anchorMax.x.ToString("0.###"));
        }

        /// <summary>
        /// BACK. The MODEL owns the graph (owner ruling 28: the stack remembers WHY, not only
        /// where), so this hands the press over and does nothing else. When the model reports it is
        /// standing on a root grid it raises CloseRequested, which is wired to <see cref="Close"/>.
        /// </summary>
        private void OnBackPressed()
        {
            if (_queueDrawerOpen) { ToggleQueueDrawer(); return; }   // the overlay closes first
            if (_vm == null) { Close(); return; }
            _vm.Back();
        }

        /// <summary>
        /// Paint the workspace. One <c>Bind</c> per model change - never per second: the panel's
        /// header records the WO-836/864 lesson that a per-tick rebuild is what caused per-frame
        /// layout churn, and <see cref="DeNelle.Core.Manage.ManageWorkspacePanel.Bind"/> is a full
        /// Clear + rebuild with no partial-update path.
        /// </summary>
        private void RenderWorkspace()
        {
            // The legacy row factories own these cells; with no legacy rows on screen they must
            // still be emptied before the drawer builds its own (the ordering RenderList held).
            _tickCells.Clear();
            _progressCells.Clear();
            _trainingNowCells.Clear();

            if (_operationalListBand != null) _operationalListBand.SetActive(false);
            if (_stripHost != null) _stripHost.gameObject.SetActive(false);
            // The QUEUE pill is the ONE door and stays up on every paint - see ShowWorkspace's note.
            if (_queueDrawerToggle != null) _queueDrawerToggle.gameObject.SetActive(true);

            // ⭐ THE MODEL MOVED => LEAVE THE HUB. See ShowLauncher's note: EnterTab mints a fresh
            // nav entry every time, so a changed reference means the player (or the harness, or a
            // deep-link door) asked for a SCREEN. Without this, `panel.Open()` followed by
            // `vm.EnterTab(tab)` left the hub in front of a fully-built grid - three doors onto
            // nothing, and `content=0px` on every tab.
            if (_hubShowing && _vm != null && !ReferenceEquals(_vm.Nav, _hubNav))
            {
                _hubShowing = false;
                FlowTrace.Step("Manage", "hub dismissed - the model entered a screen");
            }
            ApplyScreenVisibility();
            if (_hubShowing) return;           // the hub owns the screen; the workspace paints nothing
            if (_workspace == null || _vm == null) return;
            if (_queueDrawerOpen) return;      // the overlay owns the screen; nothing under it paints

            var nav = _vm.Nav;
            var workspaceVm = _vm.ComposeWorkspace();
            // WO-1443 section 1: the breadcrumb is bound into the PANEL TITLE, once, from the model.
            ApplyWorkspaceTitle(workspaceVm.HeaderTitle);
            _workspace.Bind(workspaceVm);
            FlowTrace.Step("Manage", "workspace screen=" +
                (nav != null ? nav.Kind + "/" + ManageScreenVM.TabWordOf(nav.Tab) : "<none>") +
                " item='" + (nav != null ? (nav.ItemId ?? nav.SchoolId ?? "-") : "-") +
                "' origin=" + (nav != null && nav.Origin != null ? nav.Origin.Kind.ToString() : "browse") +
                " bands(px): well=" + _workspace.LastWellPx.ToString("0") +
                " grid=" + _workspace.LastGridPx.ToString("0"));
        }

        private void BuildLauncher(RectTransform operationalWell)
        {
            if (operationalWell == null || operationalWell.parent == null) return;
            var go = new GameObject("ManageCategoryLauncher", typeof(RectTransform), typeof(Image));
            _launcherHost = (RectTransform)go.transform;
            _launcherHost.SetParent(operationalWell.parent, false);
            _launcherHost.anchorMin = operationalWell.anchorMin;
            _launcherHost.anchorMax = operationalWell.anchorMax;
            _launcherHost.offsetMin = operationalWell.offsetMin;
            _launcherHost.offsetMax = operationalWell.offsetMax;

            // ⭐⭐ WO-1648 - THE HOST'S FLOOR STOPS ABOVE THE CLOSE BAND, AND THIS LINE IS THE FIX.
            //
            //  MEASURED, NOT REASONED (device build 2026.09.10.363660 + the read-back this file
            //  carries at TraceHubCloseReadback):
            //    device hub frame  -> the CLOSE plate maxes at  13/255, brightest RGB (13,13,13)
            //    device drawer frame, same screen box -> 254/255, (255,255,246)
            //    UI_LUMA_FAIL x2, ManageFlow_BUILD_hub_2670x1200: close max=14/255 mean=0.67
            //      rect 1115..1555 x 86..242 vs reference 'BUILD' max=174.3/255, floor=61
            //    MANAGE_HUB_CLOSE_READBACK[hub]:
            //      plate color=1,1,1,a1 crColor=1,1,1,a1 inhAlpha=1 drawn active enabled
            //      label color=0.953,0.918,0.827,a1 inhAlpha=1
            //      OVERLAPPING: ... ; 'ManageScreenUI/ObsidianPanel/PanelContent/
            //                         ManageCategoryLauncher' color=0.012,0.014,0.018,a0.995
            //
            //  ⛔ THE CONTROL WAS NEVER DIM. `inhAlpha=1` on every line kills the whole family of
            //  alpha/tint theories outright: nothing faded the button. THIS Image - the launcher
            //  host's own near-black backing - is drawn AFTER the kit chrome's close (the close is
            //  built inside BuildObsidianPanel, this host is built later, so it is a later sibling
            //  in the same parent) and its rect COVERED the close band, because it copied the
            //  operational well's rect verbatim and the geometry pass deliberately drops that
            //  well's floor ONTO the close band on every screen (see the CLOSE-band reclaim above).
            //  A 0.995-alpha plate over a button is a button the player cannot see.
            //
            //  THE CURE IS THE RECT, NOT THE PAINT. The host now stops at the top of the close
            //  band, so the plate physically cannot reach the control:
            //    * NOT by lowering this Image's alpha - the hub would go translucent over the
            //      town, which is the exact thing ManageBodyFill exists to prevent.
            //    * NOT by re-ordering siblings - putting the close on top would leave a near-black
            //      plate drawn THROUGH the band it does not own, and the next control seated there
            //      would be swallowed in exactly the same way. Draw order hides the symptom; the
            //      rect is the defect.
            //  ⛔ THE CARD GEOMETRY IS UNCHANGED BY CONSTRUCTION. The band this removes from the
            //  host is the SAME `closeReserve + HubBandGapPx` the card band already subtracted
            //  INSIDE the host (`bottomF`), so the reservation simply moved out of the grid's
            //  fractions and into the host's rect - and `bottomF` is now 0 for that reason. Change
            //  one without the other and the hub reserves the band twice, which is a half-height
            //  card row - the very defect WO-1597 measured at ~583px.
            // ⛔ THIS IS THE HUB'S **ONE** CLOSE-BAND RESERVATION. `bottomF` in the card-band
            // derivation below is 0 BECAUSE of this line - grep either name and you land on both.
            // Verified 2026-09-10: `bottomF` reaches nothing but `grid.anchorMin` (and its degenerate
            // fallback), and `_launcherGrid` is only ever a parent for cards - no other seat derives
            // a rect from the grid's floor expecting it to be the close band.
            float hubCloseBandInsetPx = Mathf.Max(HubCloseBandPx, _hubCloseReservePx) + HubBandGapPx;
            _launcherHost.offsetMin = new Vector2(_launcherHost.offsetMin.x,
                                                  _launcherHost.offsetMin.y + hubCloseBandInsetPx);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.012f, 0.014f, 0.018f, 0.995f);

            // ⛔ NO SUMMARY PANELS ON THE HUB. Do not call BuildLauncherSummaries here again.
            // TWO independent reasons, and either alone is enough:
            //  1. Mockup panel 1 is the title, three cards and CLOSE. There is no status row.
            //  2. MEASURED: the summaries seat at y 0.835-0.985 of the host = 0.15 x ~583px = ~87px,
            //     against ElarionUiKit.MinTouchPx (112). They have been ~25px short the whole time
            //     and nobody saw it, because the hub has not been rendered since WO-2001 deleted
            //     ShowLauncher - the geometry auditor only started reporting them when round 5
            //     accidentally put the hub on every frame.
            // They also sat exactly where the chrome row now lives (0.845-0.975 of the panel), so
            // they would have overprinted the back arrow and the queue pill.
            // ⛔ NO 'Choose a path' HEADING (single quotes here on purpose: the oracle that forbids
            // it matches the double-quoted LITERAL, and a comment must not read as the defect).
            // Mockup panel 1 has the title MANAGE and nothing else
            // above the cards - the three cards, with their own descriptions, already say what the
            // choice is. It is the same "a line that restates what the screen shows" the owner had
            // removed from every other Manage screen on 2026-09-06 ("remove the manage army and sub
            // line"), and it was only still here because the hub had not been shown since WO-2001.
            var gridGo = new GameObject("ManageCategoryGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            var grid = (RectTransform)gridGo.transform;
            _launcherGrid = grid;
            grid.SetParent(_launcherHost, false);
            // The launcher shares chrome with the standard bottom Close. Reserve
            // that thumb band explicitly; measured captures proved .04 let row two
            // occupy the same glass as Close.
            // ⭐ THE CARD BAND IS DERIVED FROM PX CONSTANTS, NOT TYPED (WO-1567 panel row 1).
            // It read 0.055..0.695 - two magic fractions that meant "leave room for CLOSE" and
            // "leave room for the title" without saying so, and that silently change what they
            // reserve every time the host's height changes. The two bands are now stated in PX,
            // once each, and divided by the measured host - so the reservation is the same number
            // of pixels on every surface and the intent is readable.
            Canvas.ForceUpdateCanvases();
            float hostH = _launcherHost.rect.height;
            if (hostH <= 1f)
            {
                // ⛔ REPORTED, NOT SILENTLY DEFAULTED. A zero-height host means the fractions below
                // would be nonsense; saying so names the real problem instead of painting cards at
                // a plausible-looking wrong size (CLAUDE.md section 12).
                FlowTrace.Warn("Manage", "the hub host measured " + hostH.ToString("0.##") +
                    "px when its card band was derived - the band falls back to the whole host, and " +
                    "the title and CLOSE reservations cannot be honoured");
                hostH = 1f;
            }
            // ⭐ WO-1567 ROUND 25 - THE CLOSE RESERVATION IS THE MEASURED ONE, NOT A TYPED GUESS.
            // The body well no longer holds the shared CLOSE band (it is hidden on every other
            // screen - see the geometry pass), so the HUB, which is the one screen that renders
            // CLOSE, takes it back here. _hubCloseReservePx is the exact number of px the floor
            // gave up, so a change to CanonCtaHeight or CloseBandY0 moves the cards WITH the button.
            // HubCloseBandPx stays as the FLOOR: it is what the band needs even if the measured
            // reclaim came back small (a fallback frame with no layout, for instance).
            float closeReserve = Mathf.Max(HubCloseBandPx, _hubCloseReservePx);
            // ⭐ WO-1648 - THE RESERVATION MOVED UP ONE LEVEL, TO THE HOST'S RECT, and this is its
            // other half. `hubCloseBandInsetPx` above raised the HOST's floor by exactly
            // `closeReserve + HubBandGapPx`, because the host's near-black backing was measured
            // covering the CLOSE plate at 14/255. The band the cards get is therefore the WHOLE
            // host now, and the pixels the cards occupy are unchanged - they simply stop being
            // described as a fraction of a taller rect.
            // ⛔ DO NOT RESTORE `(closeReserve + HubBandGapPx) / hostH` HERE while the host inset
            // stands. That reserves the same band TWICE and hands back the half-height card row
            // WO-1597 measured. The two lines move together or not at all; `closeReserve` is kept
            // as the single number both of them read, and the trace below still reports it.
            float bottomF = 0f;
            // ⭐ WO-1597 - THE HEART BAND IS RESERVED ONLY WHEN THE CHIP IS DRAWN.
            // ⛔ THE PREDICATE IS READ ONCE, HERE, AND HANDED TO BOTH WRITERS. The band and the chip
            // used to be decided in two places on two different frames (this method at chrome time,
            // BuildHubHeartDoor inside the render), which is the two-writers-one-state shape this
            // file has now paid for four times - the dead subtree, the title rect, the drawer band
            // and the chip's own 0.70-0.83 seat. `_hubHeartShown` is the single answer.
            // MEASURED CONSEQUENCE, and it is why the cards were half-height on the owner's frame:
            // a ~583px host was giving 112px + 24px to a chip she does not want and 140px + 24px to
            // CLOSE - 300px, more than half the host, to chrome. With the chip stood down the card
            // band takes those 136px back and the cards fill the rest of the band outright.
            _hubHeartShown = DeNelle.Village.UI.ManageScreenVM.HeartUpgradeAvailable;
            float heartBandPx = _hubHeartShown ? HubTitleBandPx + HubBandGapPx : 0f;
            float topF = Mathf.Clamp01(1f - heartBandPx / hostH);
            if (topF <= bottomF) { bottomF = 0.05f; topF = 0.95f; }
            _hubHeartY0 = Mathf.Clamp01(1f - HubTitleBandPx / hostH);
            _hubHeartHostH = hostH;
            grid.anchorMin = new Vector2(HubSideInsetF, bottomF);
            grid.anchorMax = new Vector2(1f - HubSideInsetF, topF);
            grid.offsetMin = grid.offsetMax = Vector2.zero;
            // ⭐ THREE CARDS IN ONE ROW - the owner's mockup, panel 1 ("MANAGE (MAIN) - Simple entry
            // with three core options"): BUILD / ARMY / RESEARCH, each with a one-line description,
            // CLOSE beneath. CAPTURE_LOOP_GOAL.md 3.0c item 2 supersedes WO-2001's launcher
            // retirement FOR THIS SCREEN, and says so in those words.
            // It was a 2x2 of FOUR (Defense / Buildings / Troops / Research). Defense and Buildings
            // are ONE destination since WO-2001 merged them into BUILD, so the fourth card had been
            // pointing at half of a tab that no longer exists on its own.
            var layout = gridGo.GetComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.spacing = new Vector2(24f, 0f);
            layout.padding = new RectOffset(14, 14, 14, 14);
            Canvas.ForceUpdateCanvases();
            float width = Mathf.Max(1f, grid.rect.width - layout.padding.horizontal - layout.spacing.x * 2f);
            float height = Mathf.Max(1f, grid.rect.height - layout.padding.vertical);
            // ⭐ THE CARD KEEPS THE MOCKUP'S PROPORTIONS (WO-1567 panel row 1). A third of the band
            // is much wider than the drawn card is tall, so the width is CLAMPED to the aspect and
            // the row centres itself in what is left. Without this the three cards read as wide
            // plaques - which is what the owner's capture shows
            // (Logs/device/screens/owner-screen-20260907-004724.png: cards about 2.2:1 against her
            // 0.9:1) - and it is why the descriptions had nowhere to wrap.
            float cellW = Mathf.Min(width / 3f, height * HubCardAspect);
            layout.cellSize = new Vector2(cellW, height);
            layout.childAlignment = TextAnchor.MiddleCenter;
            // ⭐ THE FILL FRACTION IS REPORTED, so "the cards fill the well" is a number and not a
            // claim (CAPTURE_LOOP_GOAL's criterion zero, applied one level down from the panel).
            float cardsPx = 3f * cellW + layout.spacing.x * 2f + layout.padding.horizontal;
            float fillW = cardsPx / Mathf.Max(1f, _launcherHost.rect.width);
            float fillH = height / Mathf.Max(1f, hostH);
            FlowTrace.Step("Manage", "MANAGE_HUB_CARDS band " + grid.rect.width.ToString("0") + "x" +
                grid.rect.height.ToString("0") + "px -> cell " + cellW.ToString("0") + "x" +
                height.ToString("0") + "px (aspect " + (cellW / Mathf.Max(1f, height)).ToString("0.##") +
                ", mockup " + HubCardAspect.ToString("0.##") + ") filling " +
                fillW.ToString("0.##") + " of the host's width and " + fillH.ToString("0.##") +
                " of its height; close reserve " + closeReserve.ToString("0") + "px, heart band " +
                heartBandPx.ToString("0") + "px (chip " + (_hubHeartShown ? "SHOWN" : "hidden") +
                " - HeartProgression.State is " +
                DeNelle.Village.Buildings.Progression.HeartProgression.State + ")");
            // ⭐ THE CARD'S SHARE OF ITS OWN BAND, REPORTED. WO-1597's acceptance is "the cards fill
            // the band"; a source oracle cannot measure a rect, so the NUMBER is printed here and
            // judged on the capture. cellH IS the band height by construction, so anything under 1.0
            // means the GridLayoutGroup overrode the cell - which is the only way this can regress.
            float bandFill = height / Mathf.Max(1f, grid.rect.height - layout.padding.vertical);
            if (bandFill < 0.9f)
                FlowTrace.Warn("Manage", "MANAGE_HUB_CARD_FILL the hub card takes " +
                    bandFill.ToString("0.##") + " of its band's height, under the 0.9 floor WO-1597 " +
                    "sets. The cards are not filling the band and the screen reads as a strip of " +
                    "plaques in an empty well, which is the owner's 2026-09-07 10:21 frame");
            else
                FlowTrace.Step("Manage", "MANAGE_HUB_CARD_FILL card " + height.ToString("0") +
                    "px of a " + (grid.rect.height - layout.padding.vertical).ToString("0") +
                    "px band = " + bandFill.ToString("0.##") + " (floor 0.9)");
            // ⛔ THE DESCRIPTION'S BAND IS CHECKED IN PX, NOT ASSUMED. Two lines at
            // ElarionUi.FontFloorMobile is the floor FitBlock cannot go under; if the card is too
            // short to hold them the sentence is CUT, which is the exact defect on the owner's
            // capture and on ManageFlow_BUILD_hub_2670x1200.png. Saying so beats cutting silently.
            float descPx = height * HubDescBandF;
            if (descPx < 2f * ElarionUi.FontFloorMobile)
                FlowTrace.Warn("Manage", "the hub card is " + height.ToString("0") + "px, so its " +
                    "description band is " + descPx.ToString("0") + "px - under the " +
                    (2f * ElarionUi.FontFloorMobile).ToString("0") + "px two lines at " +
                    "ElarionUi.FontFloorMobile need. FitBlock will TRUNCATE the sentence rather " +
                    "than go sub-legible; the CARD has to grow, and nothing here will shrink text " +
                    "to hide it");
            // ⛔ THE LOCKED ARMY FACE WRAPS TO TWO LINES (WO-1636), SO ITS BAND IS CHECKED IN PX TOO
            // - the same way and for the same reason as the description above, and with a threshold
            // that is a MEASURED FONT CONSTANT rather than a rule of thumb. The face is fonted from
            // Assets/Resources/RpgUi/font/font_title.asset (Merriweather), m_LineHeight 80.448 /
            // m_PointSize 64 = 1.257, so two lines at the 30px floor need 75.4px. wave5-capture1
            // proved that number to the pixel: a 76.2px band wrapped and a 74.4px band did not.
            // ⚠ 2f * ElarionUi.FontFloorMobile (60) is NOT the right threshold here and is
            // deliberately not reused from the line above - it is 15px slacker than this font
            // actually needs, and it is exactly the kind of near-miss that shipped "BUILD BARRACK".
            // The CONSERVATIVE of TMP's two two-line extents: ascent + lineHeight + |descent|,
            // scaled to the 30px floor. The other reading (2 x lineHeight = 75.4) is the one the
            // captures measured; this is 3.7px stricter and the guard takes the stricter of the two.
            const float faceTwoLineReqPx = (70.4f + 80.448f + 17.92f) * 30f / 64f;   // 79.1
            float faceTwoLinePx = height * (1f - HubArtWellF + 0.015f - 0.02f - HubDescBandF);
            if (faceTwoLinePx < faceTwoLineReqPx)
                FlowTrace.Warn("Manage", "the hub card is " + height.ToString("0") + "px, so the " +
                    "locked ARMY face's two-line band is " + faceTwoLinePx.ToString("0") + "px - " +
                    "under the " + faceTwoLineReqPx.ToString("0.#") + "px two lines of font_title " +
                    "need at the 30px floor. TMP will not break the line at all: it keeps ONE line " +
                    "and Truncate cuts the tail, shipping 'BUILD BARRACK' - a different instruction " +
                    "from the owner's ruling. The CARD has to grow; no nudge here can fix it");
        }

        private void RenderLauncherCards()
        {
            if (_launcherGrid == null || _vm == null) return;
            for (int i = _launcherGrid.childCount - 1; i >= 0; i--)
                Destroy(_launcherGrid.GetChild(i).gameObject);

            // ⛔ THREE, IN THIS ORDER, AND DEFENSE IS NOT ONE OF THEM.
            // Mockup panel 1 draws BUILD / ARMY / RESEARCH. `ManageTab.Buildings` IS the mockup's
            // BUILD: ShowOperational maps Defense and Buildings alike onto ManageTabId.Build
            // (:1189-1191), because WO-2001 merged them. A Defense card here would open the same
            // destination as the Build card and read as a fourth place to go.
            ManageTab[] tabs =
            {
                ManageTab.Buildings, ManageTab.Troops, ManageTab.Research
            };
            for (int i = 0; i < tabs.Length; i++)
            {
                ManageTab captured = tabs[i];
                bool available = captured == ManageTab.Research
                    || _vm.VisibleTabs.Contains(captured) || _vm.VisibleTabs.Contains(ManageTab.Defense);
                // BarracksUnlock is the one runtime authority used by the building,
                // drillmaster and training door. Do not derive this from a second flag.
                if (captured == ManageTab.Troops) available = BarracksUnlock.IsUnlocked;
                // The card WORDS are the mockup's, not the legacy tab labels: panel 1 reads
                // BUILD / ARMY / RESEARCH. "Buildings" and "Troops" are this host's internal
                // vocabulary and the player never sees them anywhere else on this screen.
                string title = HubTitleFor(captured);
                string purpose = captured == ManageTab.Troops && !available
                    ? "Build a Barracks to unlock" : PurposeFor(captured);
                // ── OWNER RULING 2026-09-10, verbatim: "Manage ARMY copy: BUILD BARRACKS, keep
                // the cook fire". The locked ARMY face drops the article. The WORDS were the one
                // lever the WO-1636 lane could not take on its own (they are pinned by
                // ManageApprovedLauncherRegression, WO-1406) and the cell width is height-clamped
                // by HubCardAspect, so the ruling is what closes the truncation. See the block at
                // the face-label rect below for the measurement, and note the PURPOSE line
                // ("Build a Barracks to unlock", just above) is NOT touched - it is a different
                // string in a wrapped band and is pinned by two suites.
                string faceText = captured == ManageTab.Troops && !available
                    ? "BUILD BARRACKS" : title;
                var card = ElarionUiKit.BuildObsidianButton(_launcherGrid, faceText,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    !available ? ElarionUiKit.ObsidianButtonColor.Gray
                               : ElarionUiKit.ObsidianButtonColor.Yellow,
                    Vector2.zero, Vector2.one, () => ActivateLauncherCard(captured));
                if (card == null) continue;
                // Locked cards remain tappable so the refusal is explicit. Navigation
                // is blocked in ActivateLauncherCard; a disabled Button would fail silently.
                card.interactable = true;
                card.gameObject.name = "ManageCard_" + title;
                card.transition = Selectable.Transition.ColorTint;
                var colors = card.colors;
                colors.normalColor = available ? Color.white : new Color(0.42f, 0.42f, 0.42f, 1f);
                colors.highlightedColor = available ? new Color(1f, 0.94f, 0.78f, 1f) : colors.normalColor;
                colors.pressedColor = available ? new Color(0.78f, 0.68f, 0.48f, 1f) : new Color(0.50f, 0.50f, 0.50f, 1f);
                card.colors = colors;

                // The approved kit cards are text-safe layered faces: illustration and
                // border are art, while title, purpose, count and interaction remain live.
                // Put the sprite on the Button's own target graphic so its full rectangle
                // remains the hit target and ColorTint supplies focus/press feedback.
                // ⛔ THE CARD IS STACKED - ART ON TOP, NAME, THEN THE DESCRIPTION - which is what
                // mockup panel 1 draws. The previous seat was art-LEFT / text-RIGHT (title at
                // x 0.49-0.96), correct for the 2x2 of wide cards and wrong for three tall ones.
                // ⚠ ART GAP, NAMED NOT FAKED: the delivered card plates
                // (Assets/Resources/UI/ElarionMedieval/cards/*.png) are 1963x789 LANDSCAPE strips
                // with the illustration in their left third - drawn for the old wide seat. They are
                // painted preserveAspect INSIDE the art zone so a tall card letterboxes them rather
                // than stretching them, which is honest but is not what the mockup shows. The three
                // portrait-shaped hub images the mockup draws (a building, a helmet, a book) do not
                // exist on disk, and `cards/troops.png` - the UNLOCKED army card - has never
                // existed at all; only `cards/troops-locked.png` does. That is an art request.
                // ⛔ THE HUB CARDS CARRY NO ILLUSTRATION, ON PURPOSE, UNTIL THE ART EXISTS.
                // OWNER-FACING REASON, and the coordinator's call after seeing it: the delivered
                // plates (Assets/Resources/UI/ElarionMedieval/cards/*.png) are 1963x789 LANDSCAPE
                // strips with the illustration in their left third, drawn for the retired wide
                // 2x2 seat. preserveAspect-ing one into a tall card letterboxes it, leaving
                // two-thirds of the card black - which reads as BROKEN, not as art-pending, and was
                // the most visible thing on the screen. Text-only cards read as deliberate.
                // ⚠ THIS IS AN INTERIM, AND THE ART IS STILL OWED: mockup panel 1 draws three
                // PORTRAIT images filling each card (a building, a helmet, a book). They do not
                // exist on disk, and `cards/troops.png` - the UNLOCKED army card - has never existed
                // at all; only `cards/troops-locked.png` does. Restore the art here the day those
                // three files land, and not by re-pointing at the landscape strips.
                // LauncherArtPath is kept: it still answers for the locked-card badge path and it is
                // the name an art request should quote.
                // ⛔ THE CARD TEXT FORMAT IS SHARED WITH THE HERO DECK AND IS PINNED ACROSS BOTH.
                // HudLabelFitRegression's [deck-card-labels] case reads THIS FILE as the REFERENCE
                // and requires PlayerDeckWorkspace's Hero cards to match it, line for line:
                // It parities four things: the title's font SIZE, its ALIGNMENT, its FitSingleLine
                // range, and the description's FitSingleLine range.
                //
                // ⛔ DO NOT WRITE THOSE SEARCH TOKENS INTO THIS COMMENT. (CLI, at the gate
                // 2026-09-06.) The suite scans this file for the token and reads to the next ';',
                // and it does NOT strip comments first - so a comment quoting `face.` + `fontSize`
                // made the parser read the COMMENT instead of the assignment forty lines below,
                // reporting the value as '", "'. That is a whole regression round spent on prose.
                // The identical failure hit the wallet lane the same afternoon, where a `/api/...*`
                // path inside a comment opened a false block comment and ate 73% of a file before
                // its oracle searched it. Describe the tokens; never spell them.
                //
                // The WO-1443 hub rewrite quietly broke all four - it deleted the title's size line,
                // moved the title fit to 30f/48f and replaced the description's FitSingleLine with a
                // fixed fontSize. The suite caught it, which is exactly what the coupling is for:
                // two card surfaces drifting apart is invisible until someone opens both.
                // REALIGNED TO THE DECK rather than re-pointed away: 36f / Center / 30f-40f /
                // 24f-30f are the deck's proven numbers, they are legible at the hub's card width,
                // and Manage stays the reference. If these ever need to move, MOVE BOTH FILES.
                // ⭐ WO-1597 - THE CARD IS ONE FRAMED PLATE, AND THE TITLE AND COPY LIVE INSIDE IT.
                // MEASURED off the owner's device frame (screen-20260907-1021-manage-hub.png,
                // 2670x1200): the card's RECT runs y 299..667 (art well, title band and description
                // band all inside it), but the ornate button art only paints y 310..565 - it is a
                // wider-than-tall sprite drawn preserveAspect, so in a PORTRAIT card it can only
                // ever cover a centred horizontal slice. The description therefore rendered on bare
                // black BELOW the plate, which is the "title under each card" the ticket reports and
                // is not what MANAGE_MOCKUP_panel1_hub.png draws: her card is one plate containing
                // the painting, the name and the line of copy.
                // ⛔ THE PLATE IS DRAWN, NOT A SPRITE, FOR THE REASON BuildHubArtWell ALREADY
                // RECORDS: the kit's frame PNGs declare a zero spriteBorder, so nothing here can be
                // 9-sliced to an arbitrary aspect, and preserveAspect is what produced the defect in
                // the first place. A flat fill plus ElarionUiKit.GoldPerimeter fills the card rect
                // EXACTLY at any aspect and is the same border cue the grid tile and the art well
                // already use.
                BuildHubCardPlate(card.transform, available);
                // ⭐ THE ART WELL - mockup panel 1's top block, now WITH A PICTURE IN IT.
                // ⛔ NEVER A BLANK WELL (WO-1597). ManageArt.LoadHubArt paints the owed painting
                // first and a resolving stand-in portrait second, so the geometry is identical the
                // day the three illustrations land and nothing here has to move.
                BuildHubArtWell(card.transform, i, available);

                var face = card.GetComponentInChildren<TMP_Text>();
                if (face != null)
                {
                    var rt = face.rectTransform;
                    // The title sits directly under the art well - see HubArtWellF. The band is
                    // 0.16 of the card, which at the hub's card height clears the TMP cull floor
                    // with room to spare.
                    // ── WO-1636: THE SIDE INSET GOES 0.04 -> 0.02 (0.92 -> 0.96 OF THE CARD) ────
                    // MEASURED (Builds/wave3-navcapture, 2026-09-10): the locked ARMY card's face,
                    // "BUILD A BARRACKS", drew 11 of 14 printable glyphs at BOTH captured aspects -
                    // rect x -144.2..144.2 (288.4 px) @2340x1080 and -140.8..140.8 (281.6 px)
                    // @2670x1200, at font 30, the FitSingleLine floor armed three lines below.
                    // 288.4 / 0.92 back-solves the CELL to 313.5 px, so this widening buys the face
                    // 12.6 px. It is the ONLY width lever on this card that is not somebody else's
                    // ruling, and it is deliberately taken to the edge of the gold perimeter.
                    // ⚠ AND THE WIDENING ALONE WAS NOT ENOUGH, WHICH IS RECORDED RATHER THAN
                    // GLOSSED. A glyph-advance estimate calibrated against that same rect put the
                    // then-current copy at ~324 px at font 30 bold, against ~301 px of lane after
                    // this widening - still ~7% over. Both remaining levers were pinned: the WORDS
                    // by ManageApprovedLauncherRegression (WO-1406, the locked card's door), and
                    // the CELL WIDTH by HubCardAspect (132/169), height-clamped off the owner's own
                    // device frame - `cellW = Min(width/3, height * HubCardAspect)` is already
                    // taking the aspect branch here, so a wider band cannot help. So the lane
                    // stopped and asked, rather than nudging again.
                    // ── THE RULING LANDED. OWNER, 2026-09-10, verbatim: "Manage ARMY copy: BUILD
                    // BARRACKS, keep the cook fire". The article is dropped at the faceText site
                    // above.
                    // ⛔ AND THE ESTIMATE THAT SAID THE SHORTER COPY WOULD THEN FIT ON ONE LINE WAS
                    // WRONG - BY ONE GLYPH. It read ~293 px against a ~301 px lane; the capture on
                    // the shipped copy (Builds/wave4-capture3) measured 12 of 13 glyphs drawn at
                    // BOTH aspects, so the true width is over 300.8 px. ~324 and ~293 were
                    // glyph-advance ESTIMATES calibrated off a single rect - the oracle logs a rect
                    // only for the labels it FAILS - and an estimate is what this file said it was.
                    // The lesson is kept rather than deleted: it is why the fix below is sized off
                    // a band that is PROVEN to work rather than off another calculation.
                    // The measured numbers, and the two-line band they forced, are in the block
                    // immediately below.
                    // ── WO-1636 ROUND 2: THE LOCKED ARMY FACE IS THE ONE TWO-LINE CAPTION ───────
                    // MEASURED on the ruling's own copy (Builds/wave4-capture3, 2026-09-10):
                    // "BUILD BARRACKS" drew 12 of 13 printable glyphs at BOTH aspects - x
                    // -150.4..150.4 (300.8 px lane) @2340x1080 and -146.9..146.9 (293.8 px)
                    // @2670x1200, at font 30 with autosize 30..40 resolving to the FLOOR. So the
                    // ruling's copy still does not fit on ONE line, and the ~293 px estimate that
                    // said it would was ONE GLYPH SHORT. It was an estimate; this is a measurement,
                    // and the measurement wins.
                    // ⛔ EVERY SINGLE-LINE LEVER IS SPENT, WHICH IS WHY THE SHAPE CHANGES INSTEAD:
                    // the copy is the owner's ruling, the font is already AT ElarionUiKit.FontFloor,
                    // the side inset is already at the gold perimeter (0.02/0.98 = 0.96 of the cell)
                    // and the cell is HEIGHT-clamped by HubCardAspect off the owner's device frame.
                    // So the caption WRAPS: the band is made two lines tall and FitBlock breaks it
                    // at the copy's own space. The string is NOT re-authored with a line break, so
                    // the literal stays exactly the ruling's and the WO-1406 pin keeps reading it.
                    // ⭐ THE BAND'S HEIGHT IS A MEASURED FONT CONSTANT, NOT AN ESTIMATE. Round 2's
                    // band was 0.19 of the card and wave5-capture1 SPLIT on it - which is what
                    // pinned the number down to the pixel:
                    //   2340x1080  band 76.2 px -> WRAPPED, 13 of 13, two lines. CLEAN.
                    //   2670x1200  band 74.4 px -> did NOT wrap; one line, "BUILD BARRACK", 12 of 13
                    //              (captured rect y -146.2..-71.8 = 74.4 px, exactly as predicted).
                    // The requirement therefore sits in (74.4, 76.2], and the font asset names it
                    // exactly: this face is fonted from Assets/Resources/RpgUi/font/font_title.asset
                    // (Merriweather) whose m_FaceInfo reads m_PointSize 64 / m_LineHeight 80.448, a
                    // line factor of 1.257. TWO LINES AT THE 30 px FLOOR NEED 2 x 30 x 1.257 =
                    // 75.4 px. 76.2 >= 75.4 wraps; 74.4 < 75.4 does not. Both captures, and the
                    // asset, agree - the 2670 band was short by ONE PIXEL.
                    // ⛔ AND THE FIRST GUESS AT THIS NUMBER WAS THE WRONG ASSET, WHICH IS RECORDED
                    // RATHER THAN QUIETLY CORRECTED: font_body (Alata, 88.32/64 = 1.38) would put
                    // two lines at 82.8 px and would have "proved" the wrap impossible here. The
                    // PNG showing 2340 already wrapped is what caught it. Read the ASSET the label
                    // is actually fonted from; a plausible line factor is still a guess.
                    // ⚠ AND THE BAND IS SIZED TO CLEAR THE *CONSERVATIVE* READING OF THAT METRIC.
                    // TMP's two-line extent is 2 x lineHeight = 75.4 px on one reading and
                    // ascent + lineHeight + |descent| = (70.4 + 80.448 + 17.92) x 30/64 = 79.1 px
                    // on the other. The captures side with 75.4 (76.2 wrapped, 74.4 did not), so
                    // that is the binding number - but the band below clears 79.1 as well, because
                    // the cost of being wrong by a pixel here is a caption that reads "BUILD".
                    // ⭐ THE FIX IS ~6 px OF HEIGHT, TAKEN WHERE NOTHING IS DRAWN. The band's floor
                    // is the description top (0.02 + HubDescBandF = 0.21) and its old ceiling was
                    // the art well (1 - HubArtWellF = 0.40). The ceiling goes 0.015 INTO the well:
                    // band 0.21..0.415 = 0.205 of the card = 80.3 px @2670x1200 and 82.2 px
                    // @2340x1080 - clearing 75.4 by +4.9 / +6.8 px, and 79.1 by +1.2 / +3.1 px.
                    // ⛔ AND IT STILL CLEARS THE PADLOCK, WHICH IS THE OTHER CONSTRAINT ON THIS ONE
                    // CARD. BuildLockBadge mounts a SQUARE sprite (lock-badge.png, 1254x1254,
                    // preserveAspect) in a 0.345..0.50 x 0.20..0.76 rect, so it draws card-width
                    // x 0.155 centred at y 0.48 - its bottom edge is y 0.4195. The new band tops out
                    // at 0.41, so the caption's BAND, not merely its ink, stays entirely below the
                    // padlock. Going to 0.42 would have put the band under it for 0.5 px of nothing.
                    // ⛔ HubArtWellF, HubTitleBandF, HubDescBandF AND BuildLockBadge ARE ALL
                    // UNTOUCHED. The 0.01 is taken by ONE card's face rect, so no constant moves, no
                    // other card moves, and the mockup-conformance cases that parse those constants
                    // read exactly what they read before.
                    // ⛔ THE OTHER FACES DO NOT MOVE. BUILD / ARMY / RESEARCH are single words that
                    // never wrapped and were never flagged; they keep this band and the single-line
                    // fitter verbatim, so HudLabelFitRegression's deck/Manage parity cases on the
                    // face's size, its alignment and its single-line fit call read exactly what
                    // they read before. Only the locked ARMY card takes the taller band and the
                    // block fitter.
                    // ⛔ AND THE SYMBOL NAMES THOSE CASES ANCHOR ON ARE DELIBERATELY NOT SPELT OUT
                    // ANYWHERE ABOVE THE STATEMENTS THEMSELVES. HudLabelFitRegression.ArgsOf takes
                    // the FIRST IndexOf of its anchor in the whole source and reads to the next
                    // terminator - it has no comment model - so an anchor quoted in a comment that
                    // sits above the real statement makes the case parse prose instead of the
                    // value. Naming them here would break the very parity this block promises.
                    bool faceIsLongCta = captured == ManageTab.Troops && !available;
                    float faceTopF = faceIsLongCta
                        ? 1f - HubArtWellF + 0.015f
                        : 1f - HubArtWellF - 0.02f;
                    float faceBottomF = faceIsLongCta
                        ? 0.02f + HubDescBandF
                        : 1f - HubArtWellF - 0.02f - HubTitleBandF;
                    rt.anchorMin = new Vector2(0.02f, faceBottomF);
                    rt.anchorMax = new Vector2(0.98f, faceTopF);
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    face.fontSize = 36f;
                    face.alignment = TextAlignmentOptions.Center;
                    face.color = available ? ElarionUi.Gold : ElarionUi.ParchmentDim;
                    // ⛔ BOTH CALLS STAY LITERAL AND UNCONDITIONAL IN THEIR ARGS. HudLabelFitRegression
                    // reads the single-line fit call up to its first ')' and demands the deck's args
                    // match - a ternary inside the call would change what it parses. The BRANCH is
                    // outside the call, so the args it reads are still "30f, 40f".
                    if (faceIsLongCta) ElarionUiKit.FitBlock(face, 30f, 40f);
                    else ElarionUiKit.FitSingleLine(face, 30f, 40f);
                }
                // ⭐ THE FULL DESCRIPTION, WRAPPED OVER TWO LINES - FitBlock, not FitSingleLine.
                // ⛔ THE SINGLE-LINE FIT IS THE DEFECT. Measured on the owner's device
                // (Logs/device/screens/owner-screen-20260907-004724.png): "Construct and upgrade
                // yo...", "Train and manage your tr...", "Unlock powerful advance..." - all three
                // cards ellipsised, so the one sentence that says what the card DOES was unreadable
                // on every card. FitBlock wraps and truncates instead of ellipsising a single line,
                // the band below is two lines tall, and the floor is the kit's readable mobile
                // floor - so if it still cannot fit, the card is too small and something says so
                // rather than the sentence quietly disappearing.
                var description = ElarionUiKit.Label(card.transform, purpose,
                    0.02f, 0.02f + HubDescBandF,
                    available ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                    (int)ElarionUi.FontLabel, TextAlignmentOptions.Top, 0.06f, 0.94f);
                // Floor = the kit's readable MOBILE floor, ceiling a little above it so a short
                // description is not forced to the floor. FitBlock TRUNCATES rather than
                // ellipsising, so a sentence that genuinely will not fit goes missing visibly at
                // the end of a line instead of being replaced by three dots that look deliberate.
                ElarionUiKit.FitBlock(description, ElarionUi.FontFloorMobile, 34f);

                if (!available && captured == ManageTab.Troops)
                    BuildLockBadge(card.transform);

            }

            // ⛔ THE ART ASK, NAMED ONCE PER SESSION AND BY KEY. FlowTrace.Once, so it costs one
            // line and not one per rebuild. It is a WARN and not a Step because an unfilled well is
            // a real shortfall against the mockup that somebody has to close - it is simply not a
            // code defect, and saying which is the whole point of naming it.
            FlowTrace.Once("Manage", "hub-art-ask",
                "the three hub cards are painting STAND-IN portraits, not the illustrations mockup " +
                "panel 1 draws. OWED, by Resources key: " + string.Join(", ", ManageArt.HubArtKeys) +
                " (folder " + ManageArt.UiFolder + "). Standing in until they land, in card order: " +
                string.Join(", ", ManageArt.HubArtStandIns) + " - each 1024x1024, so nothing " +
                "letterboxes. The moment a hub key resolves it WINS with no code change " +
                "(ManageArt.LoadHubArt: painting first, stand-in second). The retired landscape " +
                "strips under UI/ElarionMedieval/cards/ are still NOT a substitute - preserveAspect " +
                "leaves two thirds of a tall card black, which reads as broken rather than pending.");

            // The Heart's door lives on the hub now, not in the chrome row - see BuildHeartFace.
            // ⛔ SUPERSEDED 2026-09-07 BY AN OWNER RULING (WO-1597). The note here read "AND THE
            // HEART CHIP STAYS ... removing it to match the mockup would ship the WO-1430 defect".
            // Owner, verbatim, on the device frame: "there is no reason to have heart on this set of
            // manage screens unless for an upgrade". The chip is now CONDITIONAL, not removed - it
            // is drawn as the UPGRADE VERB while an upgrade is due and stood down otherwise, so the
            // door exists in every state where there is something behind it. This CALL is
            // unconditional and must stay: HeartSurfaceRegression pins it, and the decision belongs
            // inside BuildHubHeartDoor where the band that complements it is already known.
            BuildHeartFace();
        }

        /// <summary>
        /// ⭐ THE HUB CARD'S FRAMED PLATE - the whole card rect, so the painting, the name AND the
        /// line of copy sit INSIDE one plate exactly as
        /// <c>docs/mockups/manage/MANAGE_MOCKUP_panel1_hub.png</c> draws them.
        /// <para>⛔ IT EXISTS BECAUSE THE KIT'S BUTTON ART CANNOT COVER A PORTRAIT CARD. MEASURED on
        /// the owner's frame (screen-20260907-1021-manage-hub.png): the card rect is y 299..667 and
        /// the ornate plate paints y 310..565 - a landscape sprite drawn preserveAspect can only
        /// ever fill a centred horizontal slice of a taller rect, so the description rendered on
        /// bare black beneath it. Nothing about the BUTTON is replaced: it keeps its click, its
        /// touch floor and its name. Only the visible plate is this method's.</para>
        /// <para>⚠ DRAWN, NOT A SPRITE, for the reason the art well below already records - the
        /// kit's frame PNGs declare a zero <c>spriteBorder</c>, so they cannot be 9-sliced to an
        /// arbitrary aspect, and preserveAspect is the defect itself.</para>
        /// </summary>
        private static void BuildHubCardPlate(Transform card, bool available)
        {
            if (card == null) return;
            var plateGo = new GameObject("HubCardPlate", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)plateGo.transform;
            rt.SetParent(card, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();          // behind the art well, the name and the copy
            var img = plateGo.GetComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            // The mockup's plate is a near-black field a shade lighter than the panel behind it, so
            // the card reads as a raised object rather than a hole. A locked card sits darker still.
            img.color = available
                ? new Color(0.075f, 0.068f, 0.060f, 1f)
                : new Color(0.045f, 0.042f, 0.040f, 1f);
            img.raycastTarget = false;       // the CARD is the tap target, not its own backing
            ElarionUiKit.GoldPerimeter(rt);
        }

        /// <summary>
        /// The hub card's ART WELL - the top block of mockup panel 1's card, with the painting in
        /// it.
        /// <para>⛔ THE EMPTINESS IS RETIRED (WO-1597, 2026-09-07). This note read "THE EMPTINESS IS
        /// THE FEATURE ... a drawn FRAME with an empty centre reads as 'a picture belongs here'".
        /// On the owner's device it did not: three dark rectangles with nothing in them read as a
        /// broken screen, which is the same verdict the landscape strips earned, and it is what her
        /// 10:21 frame shows. <see cref="ManageArt.LoadHubArt"/> now paints the OWED painting first
        /// and a resolving stand-in portrait second - never nothing. The art ask is unchanged and is
        /// still announced by key in RenderLauncherCards; what changed is what the player sees while
        /// it is open.</para>
        /// <para>⚠ THE FRAME IS DRAWN, NOT A SPRITE - corrected 2026-09-07 (WO-1567 round 25). This
        /// note used to say "the frame sprite is the tile frame, reused ... a border with a
        /// hollow-enough centre", and that was the defect: MEASURED,
        /// Assets/Resources/UI/ElarionMedieval/Manage/frame-tile.png.meta declares
        /// <c>spriteBorder: {x: 0, y: 0, z: 0, w: 0}</c>, so it is NOT 9-sliced - painted
        /// preserveAspect into a non-square well it collapsed to a centred square a fraction of the
        /// well's width, which is the "tiny square floating above the plate" on
        /// Builds/ui-capture/ManageFlow_BUILD_hub_2670x1200.png, and Image.Type.Sliced would have
        /// had no border to slice. A flat plate plus <c>ElarionUiKit.GoldPerimeter</c> fills the
        /// zone exactly at any card aspect and is the same border cue the grid tile and the list row
        /// already use for selection.</para>
        /// <para>⚠ THE MISS IS STILL CHEAP. <c>ManageArt.LoadSprite</c> caches MISSES as well as
        /// hits and announces each key exactly once per session, so loading the three owed hub keys
        /// on every rebuild costs three dictionary lookups, not three log lines a frame. That is
        /// what makes "painting first, stand-in second" affordable in a render path.</para>
        /// </summary>
        private static void BuildHubArtWell(Transform card, int cardIndex, bool available)
        {
            if (card == null) return;
            var wellGo = new GameObject("HubArtWell", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)wellGo.transform;
            rt.SetParent(card, false);
            // ⭐ THE WELL FILLS THE TOP OF THE CARD (WO-1567 round 25). It ran 0.54..0.94 of the
            // card at 8% side inset and painted frame-tile at preserveAspect TRUE - and that pair
            // is the "tiny square floating ABOVE the plate" on
            // Builds/ui-capture/ManageFlow_BUILD_hub_2670x1200.png. MEASURED, not inferred:
            // frame-tile.png.meta declares `spriteBorder: {x:0, y:0, z:0, w:0}`, so the sprite is
            // NOT 9-sliced; preserveAspect on a square sprite in a non-square zone therefore
            // collapses it to a centred square a fraction of the well's width, and Image.Type.Sliced
            // would not help either because there is no border to slice.
            // ⛔ SO THE FRAME IS DRAWN, NOT STRETCHED. A flat well plus ElarionUiKit.GoldPerimeter
            // fills the zone EXACTLY at any card aspect - the same border treatment the grid tile
            // and the list row already use for selection, so it reads as one system.
            rt.anchorMin = new Vector2(0.05f, 1f - HubArtWellF);
            rt.anchorMax = new Vector2(0.95f, 0.97f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = wellGo.GetComponent<Image>();
            img.type = Image.Type.Simple;
            img.raycastTarget = false;                   // the CARD is the tap target, not the well
            // ⛔ PAINTING FIRST, STAND-IN SECOND, NEVER NOTHING (WO-1597). The key order lives in
            // ManageArt.LoadHubArt so the View does not decide what art a card gets - it binds what
            // the art table resolves, and the day the three illustrations land they win here with
            // no edit and no layout change.
            string resolvedKey;
            var art = ManageArt.LoadHubArt(cardIndex, out resolvedKey);
            if (art != null)
            {
                img.sprite = art;
                // preserveAspect, because the stand-ins are 1024x1024 squares and the well is not
                // square. A square painting in a near-square well letterboxes by a few percent; the
                // alternative, stretching, distorts a building - and the retired landscape strips
                // are exactly why nothing here stretches to fill.
                img.preserveAspect = true;
                img.color = available ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
            else
            {
                // ⚠ BOTH KEYS MISSED. LoadSprite has already named each one; the well falls back to
                // the framed dark plate rather than a white box, and SAYS SO - a fallback that does
                // not log is the silent failure CLAUDE.md section 12 forbids.
                img.sprite = null;
                img.preserveAspect = false;
                img.color = new Color(0.04f, 0.035f, 0.03f, 0.85f);
                FlowTrace.Once("Manage", "hub-art-well-blank-" + cardIndex,
                    "hub card " + cardIndex + " has NO art: neither the owed painting nor its " +
                    "stand-in resolved, so the well is a framed empty plate. This is the state " +
                    "WO-1597 exists to end - check that Resources/" +
                    (cardIndex < ManageArt.HubArtStandIns.Length
                        ? ManageArt.HubArtStandIns[cardIndex] : "?") + " is still on disk");
            }
            ElarionUiKit.GoldPerimeter(rt);
            FlowTrace.Once("Manage", "hub-art-key-" + cardIndex,
                "hub card " + cardIndex + " paints '" + (resolvedKey ?? "<none>") + "'" +
                (resolvedKey != null && cardIndex < ManageArt.HubArtKeys.Length &&
                 resolvedKey == ManageArt.HubArtKeys[cardIndex]
                    ? " - the OWED PAINTING has landed" : " - STAND-IN (the painting is still owed)"));
        }

        private void BuildLauncherSummaries()
        {
            if (_launcherHost == null) return;
            const float gap = 0.018f;
            float w = (0.94f - gap * 2f) / 3f;
            for (int i = 0; i < _launcherSummaries.Length; i++)
            {
                float x0 = 0.03f + i * (w + gap);
                var panel = new GameObject("LauncherSummary_" + i, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)panel.transform;
                rt.SetParent(_launcherHost, false);
                rt.anchorMin = new Vector2(x0, 0.835f);
                rt.anchorMax = new Vector2(x0 + w, 0.985f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var panelImage = panel.GetComponent<Image>();
                panelImage.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/status-panel-icon-socket");
                panelImage.type = Image.Type.Simple;
                panelImage.preserveAspect = false;
                panelImage.color = Color.white;

                var iconGo = new GameObject("LineIcon", typeof(RectTransform), typeof(Image));
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.SetParent(rt, false);
                iconRt.anchorMin = new Vector2(0.015f, 0.04f);
                iconRt.anchorMax = new Vector2(0.28f, 0.96f);
                iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
                var icon = iconGo.GetComponent<Image>();
                icon.sprite = Resources.Load<Sprite>(LauncherSummaryIconPath(i));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                _launcherSummaries[i] = ElarionUiKit.Label(rt, "", 0.08f, 0.92f,
                    ElarionUi.Parchment, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Center, 0.28f, 0.97f, bold: true);
                ElarionUiKit.FitBlock(_launcherSummaries[i], 28f, 34f);
            }
        }

        private static string LauncherSummaryIconPath(int index)
        {
            switch (index)
            {
                case 0: return "UI/ElarionMedieval/icons/builder";
                case 1: return "UI/ElarionMedieval/icons/training";
                default: return "UI/ElarionMedieval/icons/research";
            }
        }

        private static string LauncherArtPath(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Defense: return "UI/ElarionMedieval/cards/defense";
                case ManageTab.Buildings: return "UI/ElarionMedieval/cards/buildings";
                case ManageTab.Troops: return "UI/ElarionMedieval/cards/troops-locked";
                case ManageTab.Research: return "UI/ElarionMedieval/cards/research";
                default: return "UI/ElarionMedieval/cards/buildings";
            }
        }

        private static void BuildLockBadge(Transform parent)
        {
            var plate = new GameObject("LockedPadlock", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)plate.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.345f, 0.20f);
            rt.anchorMax = new Vector2(0.50f, 0.76f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = plate.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/badges/lock-badge");
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>
        /// ⭐ SHOW THE HUB - mockup panel 1. Restored by WO-1443 after WO-2001 deleted it.
        /// <para>⚠ WO-2001's ShowLauncher was deleted because a REQUIRED four-tile chooser stood
        /// between the player and every destination while each destination was a narrow rail. Both
        /// halves of that have changed: the chooser is now THREE cards the owner drew herself, and
        /// what it leads to is a full-width grid. It is also what lets the BUILD/ARMY/RESEARCH row
        /// leave the workspace body and give the grid back its ~132px.</para>
        /// </summary>
        private void ShowLauncher()
        {
            _hubShowing = true;
            // ⭐ REMEMBER WHICH SCREEN THE MODEL WAS ON when the hub went up. RenderWorkspace drops
            // the hub the moment the model moves to a DIFFERENT nav entry, which is what makes
            // `vm.EnterTab(...)` - the path the capture harness and every non-card door use
            // (UICaptureLaunch.cs:7101-7111) - land on the grid instead of leaving the hub in front
            // of it. EnterTab always assigns a FRESH ManageNavEntry (ManageScreenVM.cs:2976), even
            // for a re-entry into the same tab, so reference identity is a reliable "did the player
            // ask for a screen" signal. A plain Rebuild (a queue tick, a timer) reuses the entry and
            // therefore does NOT dismiss the hub, which is the behaviour we want.
            _hubNav = _vm != null ? _vm.Nav : null;
            ApplyScreenVisibility();
            if (_operationalListBand != null) _operationalListBand.SetActive(false);
            if (_stripHost != null) _stripHost.gameObject.SetActive(false);
            _categoryNavigationCommitted = false;
            if (_workspaceTitle != null) ApplyWorkspaceTitle("MANAGE");
            FlowTrace.Step("Manage", "hub shown (BUILD / ARMY / RESEARCH)");
        }

        /// <summary>
        /// The model asked to leave the screen. On a root grid that now means RETURN TO THE HUB,
        /// not close: the mockup's back arrow walks BUILD -> hub, and only CLOSE leaves Manage.
        /// Closing from a root grid would skip panel 1 entirely and make the hub unreachable
        /// after the first tap - a door that exists but that no player can get back to.
        /// </summary>
        private void OnModelWantsOut()
        {
            if (!_hubShowing) { ShowLauncher(); return; }
            Close();
        }

        private void ActivateLauncherCard(ManageTab tab, bool commitLauncherNavigation = true)
        {
            if (commitLauncherNavigation && _categoryNavigationCommitted) return;
            if (tab == ManageTab.Troops && !BarracksUnlock.IsUnlocked)
            {
                Close();
                var controller = BuildModeController.Instance ?? BuildModeController.EnsureExists();
                controller?.EnterBuildMode(DeNelle.Core.Catalog.BuildType.Town);
                FlowTrace.Step("Manage", "troops locked door -> build mode barracks");
                return;
            }
            if (commitLauncherNavigation) _categoryNavigationCommitted = true;
            ShowOperational(tab);
        }

        private void RenderLauncherBadges()
        {
            if (_vm == null) return;
            for (int i = 0; i < _launcherBadges.Length; i++)
            {
                var badge = _launcherBadges[i];
                if (badge == null) continue;
                ChannelId channel = ManageScreenVM.ChannelOf((ManageTab)i);
                int depth = 0, cap = 5;
                for (int j = 0; j < _vm.Channels.Count; j++)
                    if (_vm.Channels[j].Channel == channel)
                    { depth = _vm.Channels[j].Depth; cap = _vm.Channels[j].DepthCap > 0 ? _vm.Channels[j].DepthCap : 5; break; }
                badge.text = depth + "/" + cap;
            }
        }

        private static string LockedPurposeFor(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Buildings: return "LOCKED - place a town building";
                case ManageTab.Troops: return "LOCKED - build a Barracks";
                case ManageTab.Research: return "LOCKED - build a research structure";
                default: return "LOCKED - place a defensive structure";
            }
        }

        private static string PurposeFor(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Defense: return "Towers, walls & gates";
                case ManageTab.Buildings: return "Construct and upgrade your town";
                case ManageTab.Troops: return "Train and manage your troops";
                case ManageTab.Research: return "Unlock powerful advancements";
                default: return "Open this management line";
            }
        }

        /// <summary>
        /// The hub card's PLAYER-FACING word, straight off mockup panel 1: BUILD / ARMY / RESEARCH.
        /// <para>⚠ Deliberately NOT <c>ManageScreenVM.TabLabels</c>. Those read "Buildings" and
        /// "Troops" - this host's internal vocabulary, which the player meets nowhere else on the
        /// Manage screens (the workspace already says BUILD and ARMY). Two words for one destination
        /// is the naming drift CLAUDE.md 7 keeps recording; the mockup settles which word wins.</para>
        /// </summary>
        private static string HubTitleFor(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Troops: return "ARMY";
                case ManageTab.Research: return "RESEARCH";
                default: return "BUILD";
            }
        }

        // ⛔ WO-2001 - ShowLauncher IS DELETED. It was the only path that made the four-tile chooser
        // visible, and BACK was its only caller ("Manage Back/root -> category cards"). The work
        // order retires the launcher and states that BACK must never route through it; leaving a
        // private method that can put it back on screen is how it would return. Verified before
        // deleting: no suite under Assets/Editor names ShowLauncher. The launcher CONSTRUCTION
        // (BuildLauncher / RenderLauncherCards) stays - it is still the source of record for the
        // approved 2026-08-31 art and copy, and two source oracles read it - but its host is never
        // activated. Those two pins are itemised for retirement in this work order's hand-back.

        /// <summary>
        /// The legacy destination entry point, kept as the ONE name every existing caller and the
        /// headless capture harness (Assets/Editor/UICaptureLaunch.cs:6877 invokes it by reflection)
        /// already uses. ⛔ WO-2001 RE-POINTS IT AT THE THREE-TAB WORKSPACE: the four legacy tabs
        /// collapse onto BUILD / ARMY / RESEARCH (Defense and Buildings merge, ruling 4, because
        /// they already share one Builder line), and the model decides the rest.
        /// </summary>
        private void ShowOperational(ManageTab tab)
        {
            ShowWorkspace();
            ManageTabId target = tab == ManageTab.Troops
                ? ManageTabId.Army
                : tab == ManageTab.Research ? ManageTabId.Research : ManageTabId.Build;
            _vm?.EnterTab(target);
            FlowTrace.Step("Navigation", "Manage -> " + tab + " (workspace tab " +
                ManageScreenVM.TabWordOf(target) + ")");
            // WO-1389: the TROOPS workspace is on screen - the post-raid beat's first route hop
            // (spotlight -> the Footman rail row). Raised HERE, after SelectTab has rebuilt and
            // Render has re-registered every "manage.troop_row.<id>" rect, so the hop can resolve
            // on the frame it fires; both the launcher card tap and the dialogue door land here.
            if (tab == ManageTab.Troops)
                Guard.Try("Manage", "raise troops-shown signal", () =>
                    DeNelle.Core.Tutorial.TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.ManageTroopsShown));
        }

        /// <summary>
        /// Post-scale canvas WIDTH in the same reference-px space as
        /// <see cref="ElarionUiKit.PostScaleCanvasHeight"/> — one scaleFactor drives both axes, so
        /// the post-scale canvas keeps the screen's aspect. DERIVED, never read off a live rect on
        /// the creation frame (that returns raw screen px).
        /// </summary>
        private static float CanvasWidthPx(float canvasH)
        {
            float sw = ElarionUiKit.SurfaceWidth, sh = ElarionUiKit.SurfaceHeight;
            if (sw < 1f || sh < 1f) return canvasH * (1080f / 1920f);   // headless: kit portrait reference
            return canvasH * (sw / sh);
        }

        /// <summary>
        /// Seat the next band under the previous one and advance the cursor by its height PLUS the
        /// guaranteed gutter. Top-anchored, top pivot, explicit <c>sizeDelta.y</c> — the height is
        /// REFERENCE PIXELS, never a fraction of the parent. Fractional bands are what shipped in
        /// build 1: a 112px MinTouch button inside a 23px fraction band overprinted its neighbours.
        /// </summary>
        private static RectTransform Band(RectTransform parent, string name, ref float cursorPx,
                                          float heightPx, float x0 = 0.01f, float x1 = 0.99f)
        {
            float h = Mathf.Max(0f, heightPx);
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, 1f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, -cursorPx);
            cursorPx += h + BandGapPx;
            return rt;
        }

        /// <summary>The notice seat in the reclaimed Close band, left of the centred Close box.</summary>
        private static RectTransform NoticeSeatBesideClose(Transform content, float x1)
        {
            var go = new GameObject("Band_Notice", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            rt.anchorMin = new Vector2(0.04f, CloseBandY0);
            rt.anchorMax = new Vector2(x1, CloseBandY0);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, NoticeCloseBandPx);
            rt.anchoredPosition = new Vector2(0f, 8f);
            return rt;
        }

        /// <summary>Band 1a — the three lines as three evenly spaced TEXT columns. One long
        /// run-on label is what wrapped and collided in build 1; a column per channel cannot.</summary>
        private void BuildStrip(RectTransform host)
        {
            if (host == null) return;
            _stripHost = host;
            const float gap = 0.014f;
            float w = (1f - gap * 2f) / 3f;
            for (int i = 0; i < _stripCells.Length; i++)
            {
                float x = i * (w + gap);
                var panel = new GameObject("ManageLineStatus_" + i, typeof(RectTransform), typeof(Image));
                var panelRt = (RectTransform)panel.transform;
                panelRt.SetParent(host, false);
                // 0.02-0.98 of the 120px band = 115px: the Training chip's tap target clears the
                // touch floor by construction, so ClampMinTouch never fires on it (WO-1382).
                panelRt.anchorMin = new Vector2(x, 0.02f);
                panelRt.anchorMax = new Vector2(x + w, 0.98f);
                panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
                var panelImage = panel.GetComponent<Image>();
                panelImage.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/status-panel-icon-socket");
                panelImage.type = Image.Type.Simple;
                panelImage.preserveAspect = false;
                panelImage.color = Color.white;
                panelImage.raycastTarget = false;

                var iconGo = new GameObject("LineIcon", typeof(RectTransform), typeof(Image));
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.SetParent(panelRt, false);
                iconRt.anchorMin = new Vector2(0.015f, 0.04f);
                iconRt.anchorMax = new Vector2(0.27f, 0.96f);
                iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
                var icon = iconGo.GetComponent<Image>();
                icon.sprite = Resources.Load<Sprite>(LauncherSummaryIconPath(i));
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                var t = ElarionUiKit.Label(panelRt, "", 0.08f, 0.92f, ElarionUi.Parchment,
                                           (int)ElarionUi.FontMicro, TextAlignmentOptions.Center,
                                           0.27f, 0.97f, bold: true);
                // WO-1406: all three status chips are destination doors. Training still carries
                // the longer queue-depth copy, so the shared fit floor remains 24.
                ElarionUiKit.FitSingleLine(t, 24f, 34f);
                _stripCells[i] = t;

                // WO-1406: these are destination chips, not alternate queue doors. QUEUE in the
                // title row remains the single drawer control.
                ManageTab destination = i == 0 ? ManageTab.Buildings : i == 1 ? ManageTab.Troops : ManageTab.Research;
                var tapGo = ElarionUiKit.AddImage(panelRt, "ManageLineStatus_TabTap_" + destination,
                    Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0f), rounded: false);
                var tapImage = tapGo.GetComponent<Image>();
                tapImage.raycastTarget = true;
                var tap = tapGo.AddComponent<Button>();
                tap.targetGraphic = tapImage;
                tap.transition = Selectable.Transition.None;
                // Reuse the launcher's guarded destination door. In particular, a locked Training
                // chip opens the BUILD BARRACKS route instead of bypassing it into Troops.
                tap.onClick.AddListener(() => ActivateLauncherCard(destination, commitLauncherNavigation: false));
                ElarionUiKit.ClampMinTouch(tap);
            }
        }

        /// <summary>Band 2 — the extra-slot sentence and the Buy-slot button, on their OWN row
        /// below the rails (WO-905 §2.7 #2: nothing floats over the rail text).</summary>
        private void BuildSlotRow(RectTransform band)
        {
            if (band == null) return;
            _slotLabel = ElarionUiKit.Label(band, "", 0f, 1f, ElarionUi.ParchmentDim,
                                            (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.62f);
            ElarionUiKit.FitSingleLine(_slotLabel);
            _slotButton = ElarionUiKit.BuildObsidianButton(band, ManageScreenVM.BuyBuilderButtonCopy,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.66f, 0.02f), new Vector2(0.99f, 0.98f),
                () => { _vm?.BuySlot(ManageScreenVM.ChannelOf(_vm.Tab)); FlushNotice(); });
            ElarionUiKit.ClampMinTouch(_slotButton);
        }

        /// <summary>
        /// The opt-in home for queue inspection AND queue ADMINISTRATION.
        ///
        /// <para>⛔ WO-1368 — THE MONEY PATH LIVED NOWHERE FOR THREE DAYS. Commit 486cd7b17
        /// (2026-09-01) removed the only call to <see cref="AddQueueRow"/> — the method that builds
        /// <c>Finish Now</c>, <c>Ad</c>, <c>Cancel</c> and <c>Move up</c> — and moved queue actions
        /// to "the explicit header Queue drawer". But this drawer held only the DISPLAY-ONLY rail
        /// and the Buy-Builder offer, and <see cref="MountRail"/>'s own comment says the rail's
        /// cards are raycast-off because "every action lives on the rows". The rows it deferred to
        /// were deleted in the same change, so the crystal sink and the rewarded-ad surface were
        /// both unreachable while <c>queueRows=2</c> was being logged correctly all morning.
        /// (Owner, on the production candidate: "i dont see the watch ad or pay crtystals to
        /// complete early stuff".)</para>
        ///
        /// <para>⭐ The 2026-08-31 ruling this drawer exists for — "tower browsing leads; queue
        /// administration is OPT-IN" — is UNCHANGED and is why the verbs are not simply put back
        /// inline: inline queue rows made the browse list overflow at landscape height. The verbs
        /// return HERE, behind the QUEUE affordance, which is where <see cref="MountRail"/> already
        /// said they lived. <c>ManageQueueDrawerRegression</c> is re-pointed to pin that shape —
        /// rows drawer-only AND present — rather than to pin their absence.</para>
        ///
        /// <para>LAYOUT: heading / scrolling queue list / Buy-Builder offer. The rail is the FIRST
        /// ROW of that list rather than a fixed band, reusing the proven demoted-rail pattern
        /// (<see cref="RenderList"/>): it keeps its full fixed <see cref="QueueRailView.Height"/>,
        /// scrolls with the rows, and cannot overprint the row beneath it at any well height.</para>
        /// </summary>
        private void BuildQueueDrawer(RectTransform well)
        {
            if (well == null) return;

            _queueDrawer = new GameObject("ManageQueueDrawer", typeof(RectTransform), typeof(Image));
            var drawer = (RectTransform)_queueDrawer.transform;
            drawer.SetParent(well, false);
            // Expanded is a genuine workspace state, not a translucent fly-over. It owns the
            // full body beneath the persistent channel strip so the queue cards have mobile-safe
            // width and the browse list cannot remain visually/actionably alive underneath it.
            // ⛔ THE OVERLAY TAKES THE WHOLE WELL. Do not shrink it back to 0.84.
            // Mockup panel 8 is a full modal, and the capture showed why the old rect could not
            // hold one: at 0.02-0.84 the drawer had ~82% of a ~583px well, and once a tab row was
            // added everything below row 1 fell off the bottom - "1. Militia x1" rendered, the next
            // line was sliced mid-glyph and CANCEL was cut in half. The container never grew.
            // Treating the overlay's TOTAL HEIGHT as the thing to solve is what the band table
            // below does; the seats are derived from it rather than each being nudged.
            // ⛔ THE OVERLAY EXTENDS BELOW THE WELL, OVER THE SHARED CLOSE BAND. That is deliberate.
            // MEASURED: the drawer was 475px and its chrome alone needs 372px (title 120 + tabs 120
            // + three 12px gaps), leaving 103px - less than ONE 132px row. Every band cannot clear
            // its floor inside a 475px overlay; the arithmetic simply does not close.
            // A modal that carries its own X does not need the panel's CLOSE visible underneath it,
            // and -0.25 of the well is ~121px against the ~202px of panel that sits below the well -
            // so it covers the Close band and still stops well inside the frame.
            // ⛔ WO-1488: THE ONE PAIR OF CONSTANTS, here and in ApplyDrawerPlacement. The literals
            // that used to sit on these two lines (-0.25 / 0.99) were overwritten by that method on
            // the next frame, so the estimate below described a 719px drawer that never rendered
            // while the real one measured 475px. See the DrawerOverlayY0/Y1 block.
            drawer.anchorMin = new Vector2(0.01f, DrawerOverlayY0);
            drawer.anchorMax = new Vector2(0.99f, DrawerOverlayY1);

            // A FIRST GUESS ONLY. ResolveDrawerBands re-runs this against the drawer's MEASURED
            // height once a layout pass has happened, and that pass is the authoritative one.
            // ⚠ THIS ESTIMATE WAS WRONG AND THE AUDIT CAUGHT IT: 1.24 * _wellPx came out at 719px
            // while the drawer actually renders 476px, so every band was scaled to a drawer 1.5x
            // too tall and the 120px tab row resolved to 79.4px - WORSE than the fraction it
            // replaced. The arithmetic is exact: 476 * (120/719) = 79.3.
            // The lesson is the QUEUE pill's, again: a size derived from a number I did not measure
            // is a guess wearing a px suffix.
            // ⭐ WO-1488: DERIVED FROM THE ANCHORS THAT ARE ACTUALLY USED, so the estimate and the
            // rendered rect can no longer be two different drawers. (It stays an estimate -
            // ResolveDrawerBands re-runs it against the measured height, which is authoritative.)
            SetDrawerBands((DrawerOverlayY1 - DrawerOverlayY0) * _wellPx);
            drawer.offsetMin = drawer.offsetMax = Vector2.zero;
            // ⭐ WO-1567 ROUND 25 - A FLAT PLATE WITH A DRAWN GOLD PERIMETER, NOT THE 9-SLICED
            // content-panel FRAME.
            // ⛔ THE FRAME WAS COSTING THE ROWS 192 REFERENCE PIXELS AND IT IS THE SINGLE BIGGEST
            // ITEM IN THE QUEUE'S BUDGET. MEASURED on Builds/cap-manage-wave4.log:
            //   MANAGE_QUEUE_PLATE sprite=content-panel inset=96px
            //   MANAGE_QUEUE_BANDS drawer=458px title=0 tabs=132 plateInset=96 list=206px
            // ResolveDrawerBands reads the inset off the LIVE sprite's 9-slice border and bounds the
            // rows inside it - correctly, because a row drawn over that border reads as a rendering
            // fault. So the border is the thing to change, not the bound: 96px at the top and 96 at
            // the bottom of a 458px overlay is 42% of it spent on frame art.
            // ⛔ AND IT IS ALSO WHAT THE MOCKUP DRAWS. Panel 8's queue overlay is a plain dark
            // rectangle with a THIN gold outline - not the ornate carved frame the other panels use.
            // GoldPerimeter is the kit's own thin border, already the selection cue on the grid
            // tile and the list row, so the overlay reads as part of one system.
            // ⚠ ResolveDrawerBands needs no change: it already answers `sprite == null` with an
            // inset of 0, and it MEASURES rather than assuming, so this is the one edit.
            var drawerImage = _queueDrawer.GetComponent<Image>();
            drawerImage.sprite = null;
            drawerImage.type = Image.Type.Simple;
            drawerImage.color = new Color(0.05f, 0.045f, 0.035f, 0.985f);
            ElarionUiKit.GoldPerimeter(drawer);

            // ⛔ THE OVERLAY'S HEADER IS A CENTRED "QUEUE" AND AN X. THERE IS NO 'HIDE' BUTTON.
            //
            // THE DEFECT IT REPLACES, measured by the capture auditor on EVERY tab:
            //   BUTTON OVER TEXT [ManageFlow_BUILD_queue] 'ManageQueueDrawer/ObsBtn_HIDE'
            //     (x 250.3..573.4, y 81.7..221.1) covers
            //     '...QueueRail_Builder/Cards/Card_wall_wood:13:9/Verb/Txt' ("REPAIR")
            //     (x 202.7..370.7, y 100.6..136.6) by 120.3x36 ref px
            // ...and the same over "TRAIN" on ARMY and "LEARN" on RESEARCH, plus each card's icon.
            // The arithmetic is plain once seen: HIDE was seated at y 0.70-0.99 while the list zone
            // below runs 0.02-0.86, so its lower 16% sat ON the first queue card. THE BUTTON THAT
            // PERFORMS THE JOB WAS UNDERNEATH THE BUTTON THAT CLOSES THE DRAWER - a player tapping
            // REPAIR / TRAIN / LEARN on the top row hit HIDE instead. That is the whole of the
            // outstanding geometry=20 / touch=20 count, on one control.
            //
            // The mockup's panel 8 settles the shape rather than the coordinate: title QUEUE centred,
            // an X at the top-right, and nothing else in the header. The X is seated ABOVE the list
            // zone's 0.86 ceiling with a gutter, so it cannot reach a card at any drawer height.
            // ⭐ THE BAND TABLE, top-down, as fractions of the drawer. Mockup panel 8's reading
            // order is TITLE -> TABS -> ROWS, and the capture had it title -> cards -> tabs -> rows
            // because the tab row was appended as a SCROLL ROW (MakeRowHost writes into the list),
            // so it landed after the rail instead of above the rows. The tabs now own a FIXED zone
            // in the drawer's chrome, which is the only way a header can be a header.
            //   title  0.90-0.99   (~53px at a 583px well)
            //   tabs   0.685-0.885 (0.20 => ~117px, clear of MinTouchPx 112)
            //   list   0.02-0.665
            // ⛔ THE TITLE AND THE X LIVE IN A REAL ZONE, not on fractions of the drawer.
            // Anchoring the X to a fraction of the drawer put it OUTSIDE the overlay twice - the
            // drawer's sliced frame art does not reach its own rect edge, so its (1,1) corner is not
            // where the overlay visibly ends. A child of a zone cannot leave that zone, which is a
            // guarantee no fraction of a parent gives.
            _drawerHeader = MakeZone(drawer, "Drawer_Header",
                new Vector2(0.03f, 1f), new Vector2(0.97f, 1f));
            SeatDrawerTitleOverlay();

            var heading = ElarionUiKit.Label(_drawerHeader, "QUEUE", 0.42f, 0.96f,
                ElarionUi.Gold, (int)ElarionUi.FontBody, TextAlignmentOptions.Center,
                0.20f, 0.80f, bold: true);
            ElarionUiKit.FitSingleLine(heading, 28f, 44f);
            _drawerHeading = heading != null ? heading.gameObject : null;

            // The overlay is a work timeline, not a second inventory screen. This quiet eyebrow
            // gives the large title band a job and explains the NOW / numbered markers below.
            var timelineCaption = ElarionUiKit.Label(_drawerHeader, "WORK TIMELINE", 0.08f, 0.40f,
                ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro, TextAlignmentOptions.Center,
                0.30f, 0.70f);
            ElarionUiKit.FitSingleLine(timelineCaption, 24f, 30f);

            // The tab row's own zone - built once, refilled by RenderQueueDrawer.
            _drawerTabs = MakeZone(drawer, "Drawer_QueueTabs",
                new Vector2(0.02f, _drawerTabsY0), new Vector2(0.98f, _drawerTabsY1));

            // A SQUARE X in the corner, as drawn. ASCII "X", never a glyph character - this
            // project's fonts render non-ASCII as tofu (the same rule that made BACK a "<-").
            //
            // ⛔ AUTHORED AT THE TOUCH FLOOR IN PIXELS, NOT AS A FRACTION OF THE DRAWER.
            // MEASURED last round: 'ManageQueueOverlayClose' resolved 71.8x57.7 ref px - the short
            // side 54.3px UNDER MinTouchPx(112), less than half the floor - because 0.875-0.995 of
            // a drawer whose height varies is not a promise about pixels. That is the identical
            // mistake the detail CTA made at 104.1px, and the identical cure: take the px, then
            // convert to the fraction this rect needs. A clamp that grows a control symmetrically
            // about its centre spills it into its neighbours, which on this header is the title.
            // ⚠ A FIXED PIXEL SIZE PINNED TO THE CORNER, not an anchor fraction. `drawer.rect` is
            // ZERO on the frame this is built - no layout has run yet - so any fraction derived
            // from it would be a guess, and a fraction of a drawer whose height varies cannot
            // promise a px floor anyway. Collapsing the anchors to the top-right corner and setting
            // sizeDelta gives exactly MinTouchPx + 8 on both axes at every drawer size, with no
            // measurement needed and nothing for ClampMinTouch to rescue.
            const float ClosePx = 112f;   // == ElarionUiKit.MinTouchPx, authored AT the floor
            // ⛔ WO-1488 — THE X IS TOP-RIGHT, IN THE TITLE OVERLAY, NEVER IN THE TAB STRIP.
            // MEASURED (Builds/ui-capture/ManageFlow_BUILD_queue_2670x1200.png, 18:39): seated in
            // _drawerTabs it renders as a FOURTH TAB - same row, same obsidian face, same height,
            // immediately right of "RESEARCH 2/2". A close control that looks like a channel is a
            // control the player taps to switch channels, and mockup panel 8 draws it at the
            // overlay's top-right corner, level with the word QUEUE.
            // The seat that made this possible is DrawerTitleOverlayPx (116px, up from 56): the
            // overlay row is now tall enough to hold a MinTouchPx square, which is precisely why
            // the X was in the tab band before - at 56px it could not be seated here AT ALL.
            // The zone bounds it on every side, so no fraction of a varying drawer is involved.
            var close = ElarionUiKit.BuildObsidianButton(_drawerHeader, "X",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.9f, 0.9f), new Vector2(0.99f, 0.99f), ToggleQueueDrawer);
            if (close != null)
            {
                var closeRt = (RectTransform)close.transform;
                // ⚠ PULLED INSIDE. The capture showed the X floating over the PANEL's border,
                // outside the overlay's own frame: pinned to the drawer's (1,1) with a -14/-10
                // nudge, it sat on the corner of a rect whose sliced frame art does not reach its
                // own edge. Anchoring to the TITLE BAND's right end instead puts it where panel 8
                // draws it - inside the overlay, level with the word QUEUE.
                // Pinned to the HEADER ZONE's right edge, vertically centred in it. A fixed px
                // square so it always clears MinTouchPx; the zone bounds it on every side.
                closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.99f, 0.5f);
                closeRt.pivot = new Vector2(1f, 0.5f);
                closeRt.sizeDelta = new Vector2(ClosePx, ClosePx);
                closeRt.anchoredPosition = Vector2.zero;
            }
            ElarionUiKit.ClampMinTouch(close);
            if (close != null) close.gameObject.name = "ManageQueueOverlayClose";
            _drawerHide = close != null ? close.gameObject : null;

            // WO-1368: the rail no longer owns a fixed band of its own — it is mounted as the
            // first row of the list below (see RenderQueueDrawer), so the 200px of card art can
            // never eat the space the ACTION rows need. _railBand stays null, which is what makes
            // the legacy pinned path (RenderRail) inert.
            _railBand = null;
            // WO-1393: the list zone runs from the drawer floor to the heading (0.02-0.86); the
            // slot offer is no longer a fixed zone under it but the list's LAST ROW (see
            // RenderQueueDrawer), which is what gives the rail + header + first row room at rest.
            var drawerList = MakeZone(drawer, "Drawer_QueueList",
                new Vector2(0.02f, _drawerListY0), new Vector2(0.98f, _drawerListY1));
            _drawerList = drawerList;
            var drawerScroll = ElarionUiKit.MakeScrollZone(drawerList, spacing: 8f, padding: 10);
            _drawerContent = drawerScroll != null ? drawerScroll.content : null;
            if (_drawerContent == null)
                FlowTrace.Fail("Manage",
                    "queue drawer MakeScrollZone returned no content - the queue ROWS have no build " +
                    "site, which is exactly the WO-1368 defect (Finish Now / Ad / Cancel / Move up " +
                    "unreachable). The rail alone carries no actions.");

            // Built ONCE (its label/button are fields RenderSlotOffer refreshes) and parked on the
            // drawer between renders; RenderQueueDrawer re-seats it as the last scroll row.
            _drawerSlotOffer = MakeZone(drawer, "Drawer_SlotOffer", Vector2.zero, Vector2.one);
            BuildSlotRow(_drawerSlotOffer);
            _drawerSlotOffer.gameObject.SetActive(false);

            _queueDrawer.SetActive(false);
        }

        /// <summary>WO-1393/1418/1422: the drawer is a BAND below the selected-card workspace, and
        /// since WO-1422 ALL FOUR destinations carry that workspace - so all four are band mode and
        /// the WO-1368 full-body drawer no longer has a tab that uses it (it stays for safety).
        /// ⛔ APPEND ONLY: ManageBuildingsCardRegression.cs:154 pins the verbatim substring
        /// "ManageTab.Troops || _vm.Tab == ManageTab.Buildings" - reordering these terms fails it.</summary>
        private bool DrawerInBandMode => _vm != null && (_vm.Tab == ManageTab.Troops || _vm.Tab == ManageTab.Buildings || _vm.Tab == ManageTab.Defense || _vm.Tab == ManageTab.Research);

        /// <summary>
        /// ⛔ WO-1393 - THE ONE PLACE THE DRAWER, THE LIST BAND AND THE TRAINING NOW BAND ARE
        /// SEATED RELATIVE TO EACH OTHER. Called on every toggle and after every RenderList (which
        /// rebuilds the TRAINING NOW band active), so the placement can never drift from the tab
        /// or the queue. Idempotent. See the DrawerModeListKeepPx block for the arithmetic.
        /// </summary>
        private void ApplyDrawerPlacement()
        {
            if (_queueDrawer == null) return;
            var drawer = (RectTransform)_queueDrawer.transform;
            // A modal must be the last-painted child of the operational well. BUILD can rebuild
            // its category workspace after the drawer is constructed; without reasserting order,
            // that later sibling masks the drawer's header and right-hand actions in the capture.
            if (_queueDrawerOpen) drawer.SetAsLastSibling();
            // ⛔ WO-2001 - QUEUE IS AN OVERLAY, so the band shape stands down while the workspace
            // owns the well. The owner's flow puts QUEUE on every screen's header and over the
            // screen it was opened from; a band seated inside the old browse-list rectangle has
            // nowhere to sit once that band is gone. DrawerInBandMode itself is UNTOUCHED (its
            // verbatim text is pinned by ManageBuildingsCardRegression:171) - only this one
            // condition, which is where the two shapes were always chosen between.
            // WO-2019: one queue, one shape. The legacy short-band branch still activated from
            // ARMY's root capture and squeezed a full three-channel drawer into the old list
            // width, clipping the RESEARCH segment and every right-hand action. The queue is now
            // the same full modal from every Manage destination.
            bool band = false;
            _drawerBandMode = band;

            // ⭐ WO-1567 ROUND 25 - THE SHARED CHROME ROW STANDS DOWN UNDER THE FULL OVERLAY.
            // ⛔ TWO REASONS, AND EITHER ALONE IS ENOUGH.
            //  1. THE MOCKUP. Panel 8 draws the queue as a full modal with its OWN title and X, and
            //     with NO back arrow and NO queue pill - the overlay IS the queue, so a pill that
            //     opens it and an arrow that leaves the screen under it are both wrong there.
            //  2. THE GEOMETRY. DrawerOverlayY1 is now 1.0, and the title row is drawn ABOVE the
            //     drawer's ceiling by SeatDrawerTitleOverlay (pivot 0, 116px). That band is the
            //     chrome row's (WorkspaceHeaderY0..Y1). Leaving both up would overprint the word
            //     QUEUE on the back arrow and the pill - the BUTTON OVER TEXT class of failure this
            //     screen has already paid for twice.
            // ⚠ SET FROM THE STATE, NEVER TOGGLED. A one-way hide is how a control never comes
            // back: this runs on open AND on close (ToggleQueueDrawer -> Render), so the row is
            // restored by the same line that hid it. The X and the scrim both close the overlay.
            bool chromeHidden = _queueDrawerOpen && !band;
            // ⛔ AND THIS IS EXACTLY WHY THE CONSTANT EXIT IS NOT A CHILD OF THIS ROW.
            // Owner ruling 2026-09-07 requires a top-right exit on EVERY Manage screen, the queue
            // overlay included - and this line takes the whole row down on that very screen. The X
            // is built onto chrome.content instead (BuildConstantExit), so it survives this hide
            // with the back arrow and the pill correctly going away underneath it. Do NOT "tidy"
            // it into _tabsHost.
            if (_tabsHost != null) _tabsHost.gameObject.SetActive(!chromeHidden);
            // ⛔ AND THE BREADCRUMB TITLE WITH IT, BECAUSE IT IS NOT IN THAT ROW.
            // MEASURED AT SOURCE: `_workspaceTitle = chrome.title` - it belongs to the KIT FRAME's
            // Zone_Header (ElarionUiKit authors that zone at y 0.900-0.972), not to
            // ManageHeaderActions, so hiding the row alone would leave "MANAGE - BUILD" behind. The
            // overlay's own title band is DrawerTitleOverlayPx (116px) drawn ABOVE the drawer's
            // ceiling, which at DrawerOverlayY1 = 1.0 lands at roughly y 0.838-0.963 of the panel -
            // straight through it. That is the BUTTON OVER TEXT class of failure this screen has
            // already paid for twice, and it would have been introduced by the same edit that
            // bought the rows their band.
            // ⚠ It is also what the mockup draws: panel 8 has ONE heading, and it is the word QUEUE.
            if (_workspaceTitle != null) _workspaceTitle.gameObject.SetActive(!chromeHidden);
            // The modal owns its own close X. Leaving the constant Manage exit visible produces
            // two adjacent X controls with different meanings (close Queue vs leave Manage).
            if (_manageExit != null) _manageExit.gameObject.SetActive(!chromeHidden);
            if (_workspaceHost != null) _workspaceHost.gameObject.SetActive(WorkspaceActive && !_queueDrawerOpen);

            // The TRAINING NOW band (and its extra rows) is the line's MIRROR; the drawer
            // supersedes it while open. Inactive rows drop out of the vertical layout, so the
            // list content shrinks to padding + workspace + padding.
            if (_listContent != null)
                for (int i = 0; i < _listContent.childCount; i++)
                {
                    var child = _listContent.GetChild(i);
                    if (child.name.StartsWith(TrainingNowPrefix, StringComparison.Ordinal) ||
                        child.name.StartsWith(BuildingNowPrefix, StringComparison.Ordinal) ||
                        child.name.StartsWith(ResearchNowPrefix, StringComparison.Ordinal))   // WO-1422
                        child.gameObject.SetActive(!band);
                }

            if (_operationalListBand != null)
            {
                // Full-body mode hides the browse list under the opaque drawer (WO-1368: a browse
                // list left actionable under a panel carrying paid verbs is a mis-tap surface).
                // Band mode keeps it, shrunk to the viewport ABOVE the card's CTA line.
                // WO-2001: the browse list band is retired while the workspace owns the well - an
                // empty scroll zone left active over the grid is an invisible raycast blocker.
                _operationalListBand.SetActive(!WorkspaceActive && (!_queueDrawerOpen || band));
                var listRt = (RectTransform)_operationalListBand.transform;
                float keep = band ? Mathf.Min(DrawerModeListKeepPx, _listBandPx) : _listBandPx;
                listRt.sizeDelta = new Vector2(listRt.sizeDelta.x, keep);
            }

            if (_drawerHeading != null) _drawerHeading.SetActive(!band);
            if (_drawerHide != null) _drawerHide.SetActive(!band);
            // ⛔ THE TAB ROW STANDS DOWN IN BAND MODE, with the title and the X.
            // Band mode is the SHORT drawer that sits under the Troops workspace card (~235px), and
            // a 114px tab row would eat half of it - the rows it exists to reach would be the thing
            // it displaced. The overlay keeps whichever channel it was on; only the full-body
            // overlay (mockup panel 8) offers the switch.
            if (_drawerTabs != null) _drawerTabs.gameObject.SetActive(!band);
            // ⭐ MEASURE, THEN DERIVE. This runs after a layout pass, so the band table finally
            // resolves against the drawer's real height rather than BuildQueueDrawer's estimate.
            // ⚠ ONLY the band resolve here. TraceQueueTabFit moved to the END of
            // RenderQueueDrawer: ApplyDrawerPlacement can run BEFORE the tabs are built, and the
            // capture proved it - the line printed face[NULL] and therefore proved nothing. Same
            // fault as the content trace two rounds ago. A measurement's timing is part of it.
            if (!band) ResolveDrawerBands();
            if (_drawerList != null)
            {
                // ⛔ THE BAND TABLE, NOT A LITERAL. This line used to read `band ? 1.0f : 0.86f`,
                // and because it runs AFTER RenderQueueDrawer it silently replaced the authored
                // 0.665 ceiling and zeroed the whole-row trim on every paint.
                _drawerList.anchorMin = new Vector2(0.02f, band ? 0.0f : _drawerListY0);
                _drawerList.anchorMax = new Vector2(0.98f, band ? 1.0f : _drawerListY1);
                _drawerList.offsetMin = _drawerList.offsetMax = Vector2.zero;
                // ...and the trim is RE-APPLIED here rather than only at render, so it survives
                // whichever of the two writers runs last. An invariant that depends on call order
                // is not an invariant.
                SeatQueueListToWholeRows();
            }

            var image = _queueDrawer.GetComponent<Image>();
            if (band)
            {
                float top = _listBandTopPx + Mathf.Min(DrawerModeListKeepPx, _listBandPx) + BandGapPx;
                float drawerPx = Mathf.Max(0f, _wellPx - top);
                drawer.anchorMin = new Vector2(0.01f, 1f);
                drawer.anchorMax = new Vector2(0.99f, 1f);
                drawer.pivot = new Vector2(0.5f, 1f);
                drawer.sizeDelta = new Vector2(0f, drawerPx);
                drawer.anchoredPosition = new Vector2(0f, -top);
                // A flat plate, not the framed sprite: frames/content-panel carries ~90px of
                // transparent margin above its gold line (the WO-1382 RCA), which on a band this
                // short would draw the first row outside the visible frame.
                if (image != null)
                {
                    image.sprite = null;
                    image.color = new Color(0.05f, 0.04f, 0.03f, 0.70f);
                }
                FlowTrace.Step("Manage", string.Format(
                    "drawer band(px): listTop={0:0} keep={1:0} gap={2:0} => drawer top={3:0} height={4:0} " +
                    "of well {5:0} (needs {6:0}: header {7:0} + row {8:0} + pad 20). TRAINING NOW " +
                    "collapsed; the card's CTAs stay in the list viewport above the drawer, never under it.",
                    _listBandTopPx, Mathf.Min(DrawerModeListKeepPx, _listBandPx), BandGapPx, top, drawerPx,
                    _wellPx, DrawerModeMinPx, SectionHeaderPx, RowHeightPx));
                if (drawerPx < DrawerModeMinPx)
                    FlowTrace.Warn("Manage", string.Format(
                        "drawer band is {0:0}px, under the {1:0}px it needs to seat the header and one " +
                        "row at rest - the rows still scroll, but the first verb is under the fold.",
                        drawerPx, DrawerModeMinPx));
            }
            else
            {
                // ⛔ WO-1488: the SAME pair BuildQueueDrawer authors. These were 0.02/0.84 typed
                // here and -0.25/0.99 typed there, and this writer runs last - so the estimate
                // handed to SetDrawerBands was of a rect that never existed (719px vs a measured
                // 475px). The ceiling is 0.79 now: the 0.05 it gives up is the room the X needs
                // above the drawer, and 0.84 could not seat a MinTouchPx control there.
                drawer.anchorMin = new Vector2(0.02f, DrawerOverlayY0);
                drawer.anchorMax = new Vector2(0.998f, DrawerOverlayY1);
                drawer.pivot = new Vector2(0.5f, 0.5f);
                drawer.offsetMin = drawer.offsetMax = Vector2.zero;
                // ⛔ DO NOT RE-POINT THIS AT content-panel. This branch used to restore the sliced
                // frame whenever the sprite was null - which is exactly the state BuildQueueDrawer
                // now authors deliberately - so it would have put the 96px 9-slice inset back on
                // the very next layout pass and silently taken 192px off the list band again. The
                // overlay's plate is FLAT with a drawn GoldPerimeter (see BuildQueueDrawer for the
                // measurement and the mockup reason); band mode paints its own flat plate above.
                if (image != null)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = new Color(0.05f, 0.045f, 0.035f, 0.985f);
                }
            }

            // ⭐ WO-1648 - THE SECOND HALF OF THE READ-BACK, and the reason it is a DELTA.
            // The device frames differ only in this state: the CLOSE plate reads 13/255 with the
            // drawer shut and 254/255 with it open, on ONE build and ONE session. Logging both
            // states from one helper means the next reader compares two lines instead of
            // theorising about one.
            if (_queueDrawerOpen) TraceHubCloseReadback("drawer-open");
        }

        /// <summary>WO-1393: the title-row QUEUE face stays on screen while the drawer is open and
        /// reads as the close ("HIDE QUEUE"); its onClick is ToggleQueueDrawer either way, so the
        /// top-right tap always toggles. Called from BuildTabs (rebuilds) and ToggleQueueDrawer.</summary>
        private void SyncQueueToggleFace()
        {
            if (_queueDrawerToggle == null) return;
            // ⛔ UNCONDITIONALLY ACTIVE. This used to read
            //     SetActive(_vm != null && _vm.Channels.Count > 0)
            // which was safe while the workspace painted its own queue face and this was a spare
            // chrome control. Since WO-1443 this pill IS the only door to the queue on every Manage
            // screen (measured: the operational OPEN QUEUE bands are in a list band held inactive,
            // the activity strip is retired, the HUD Builders chip's door went in WO-911), so a
            // condition on it is a condition on the door - the WO-1430 defect class exactly.
            // The mockup also draws the pill on all eight numbered panels with no empty state.
            _queueDrawerToggle.gameObject.SetActive(true);
            // ⛔ THE PILL ALWAYS READS "QUEUE". It must not become "HIDE QUEUE".
            // WO-1393 made this face read as the CLOSE because it was the only way to shut the
            // drawer. Panel 8 gives the overlay its OWN X (BuildQueueDrawer), so that reason is
            // gone - and the relabel actively broke the pill: SizeQueuePillToLabel measures the word
            // at BUILD time and sizes the button to it, so swapping in a longer word afterwards
            // truncated it. The capture read "HIDE QU..." in the top-right chrome slot.
            // The drawer is closed by its X, by BACK (OnBackPressed), or by tapping this pill again.
            var label = _queueDrawerToggle.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "QUEUE";
                ElarionUiKit.FitSingleLine(label);
            }
        }

        private void ToggleQueueDrawer()
        {
            _queueDrawerOpen = !_queueDrawerOpen;
            // ⭐ WO-1573 - THE ANCESTOR CHAIN IS RE-ASSERTED BEFORE THE DRAWER IS ACTIVATED.
            // ApplyScreenVisibility is the SINGLE writer of `_operationalWell` (this method must
            // never SetActive that well itself - two writers deciding one flag by last-write-wins
            // is the defect that method's header records), and the well now follows
            // `!_hubShowing || _queueDrawerOpen`. Calling it HERE, before the SetActive below,
            // means the drawer is switched on inside a LIVE hierarchy - which is the precondition
            // for the measurement further down: ResolveDrawerBands (reached from
            // ApplyDrawerPlacement) calls Canvas.ForceUpdateCanvases and then early-returns on
            // `drawerPx < 1f`, and a rect under an inactive ancestor resolves to ZERO however many
            // canvas updates are forced at it. Activate first and the band table means pixels.
            ApplyScreenVisibility();
            if (_queueDrawer != null) _queueDrawer.SetActive(_queueDrawerOpen);
            // WO-1389: the real OPEN QUEUE tap is the completion of the TRAINING NOW beat. Only
            // the OPENING edge raises (closing teaches nothing). Guarded: a bus subscriber must
            // never be able to leave the drawer half-toggled.
            if (_queueDrawerOpen)
                Guard.Try("Manage", "raise queue-opened signal", () =>
                    DeNelle.Core.Tutorial.TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.ManageQueueOpened));
            // WO-1393: the toggle STAYS visible while open - it was hidden here before, which is
            // why the top-right QUEUE tap in 10-troops-after-upgrade.png closed nothing.
            SyncQueueToggleFace();
            // WO-1368: hiding the browse band while the drawer is open STILL holds on the browse
            // tabs (the drawer is a full-body workspace carrying DESTRUCTIVE and PAID verbs; a
            // browse list left actionable underneath an opaque panel is a mis-tap surface). On the
            // Troops tab the drawer is a BAND under the kept card viewport instead (WO-1393), and
            // ApplyDrawerPlacement owns both shapes.
            ApplyDrawerPlacement();
            if (_queueDrawerOpen) RenderQueueDrawer();
            // WO-2001: closing the overlay hands the screen back to the workspace. Re-binding here
            // (rather than waiting for the next QueueChanged) is what re-hides the header toggle, so
            // exactly ONE queue affordance is on screen in each state: the workspace door while
            // browsing, and this toggle - reading "HIDE QUEUE" - while the overlay is up.
            else if (WorkspaceActive) RenderWorkspace();
            FlowTrace.Step("Manage", "queue drawer " + (_queueDrawerOpen ? "expanded" : "collapsed") +
                " (rows " + (_queueDrawerOpen ? (_vm != null ? _vm.QueueRows.Count : 0) : 0) + ")" +
                (_queueDrawerOpen ? (_drawerBandMode ? " as a band under the Troops workspace" : " full-body") : ""));

            // ⛔ WO-1573 - THE LINE THAT NAMES THE DEAD STEP, AND IT IS PERMANENT (CLAUDE.md 12).
            // The trace above says the drawer "expanded" from the flag ALONE, which is exactly what
            // it said while the player saw nothing: the flag flipped, SetActive(true) landed, and
            // the overlay was still invisible because an ANCESTOR was off. `activeSelf` was true and
            // `activeInHierarchy` was false, and no line printed the difference - so the door read
            // as working in every log.
            // ⚠ activeInHierarchy IS THE MEASUREMENT. Print the screen state beside it so the next
            // capture says WHICH ancestor, instead of leaving anyone to reason about a subtree they
            // cannot see. Same discipline as MANAGE_QUEUE_PILL_RECT, which ended nine rounds of
            // coordinate theories with one printed rectangle.
            FlowTrace.Step("Manage", "MANAGE_QUEUE_DOOR open=" + _queueDrawerOpen +
                " hub=" + _hubShowing +
                " drawer.activeSelf=" + (_queueDrawer != null && _queueDrawer.activeSelf) +
                " drawer.activeInHierarchy=" + (_queueDrawer != null && _queueDrawer.activeInHierarchy) +
                " well.activeSelf=" + (_operationalWell != null && _operationalWell.gameObject.activeSelf) +
                " launcher.activeSelf=" + (_launcherHost != null && _launcherHost.gameObject.activeSelf) +
                " pill.activeInHierarchy=" +
                    (_queueDrawerToggle != null && _queueDrawerToggle.gameObject.activeInHierarchy));
            if (_queueDrawerOpen && _queueDrawer != null && !_queueDrawer.activeInHierarchy)
                FlowTrace.Fail("Manage", "MANAGE_QUEUE_DOOR the queue overlay is open but an ANCESTOR " +
                    "is inactive - it renders nothing and accepts no input (hub=" + _hubShowing +
                    ", well.activeSelf=" +
                    (_operationalWell != null && _operationalWell.gameObject.activeSelf) + ")");
        }

        private void BuildNotice(RectTransform band)
        {
            if (band == null) return;
            _noticeLabel = ElarionUiKit.Label(band, "", 0f, 1f, ElarionUi.Gold,
                                              (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.99f);
            // A notice may run to two lines — FitBlock wraps and truncates INSIDE the band.
            ElarionUiKit.FitBlock(_noticeLabel);
        }

        private static RectTransform MakeZone(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void BuildTabs()
        {
            if (_tabsHost == null) return;
            for (int i = _tabsHost.childCount - 1; i >= 0; i--)
            {
                var child = _tabsHost.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            // ⭐ WO-2003 / WO-2017 - THE DIRECT ROUTE TO THE HEART, and it is built FIRST, before
            // the "nothing unlocked yet" early return below, precisely so it exists in EVERY state
            // of this screen. Owner 2026-09-06: "wire the heart" - she could not find how to raise
            // her realm tier, and MEASURED at source that day the ONLY control was the VillageGated
            // action band in BuildingUpgradePanelMvvm.cs:1322-1338, painted ONLY while she happened
            // to be looking at a building whose next tier was gated. A gate that gates nearly all
            // content had no door of its own. This face is that door.
            //
            // ⚠ SEAT RE-CUT 2026-09-06 (WO-1443, the mockup round). The chrome row is now
            //   <-  HEART Ln | BUILD  ARMY  RESEARCH |  QUEUE(n)
            // in ONE thin band (WorkspaceHeaderY0..Y1), because the mockup gives the GRID the screen
            // and leaves the chrome a single row. The Heart face keeps its door and its live level
            // but is deliberately small and unobtrusive: it appears NOWHERE in the mockup and
            // survives only because it is the sole unconditional route to PanelId.Heart and
            // HeartSurfaceRegression.cs:118-123 fails without it. When a door for the Heart exists
            // elsewhere, this face is the first thing that should go.
            // HEIGHT: the row is 0.845-0.975 (~115px at the measured reference), clear of
            // MinTouchPx(112) without ClampMinTouch having to rescue it.
            BuildBackArrow();

            if (_vm == null || _vm.VisibleTabs.Count == 0)
            {
                ElarionUiKit.Label(_tabsHost, "Place a structure to unlock Manage categories", 0f, 1f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.40f, 1f);
                return;
            }
            // ⚠ NO DESTINATION FACES IN THIS ROW YET, AND THAT IS A DELIBERATE HOLD.
            // The mockup's navigation is the HUB (screen 1) plus the back arrow - there is no tab
            // row on any panel. Moving BUILD/ARMY/RESEARCH up here as chrome would free the
            // workspace's 132px band, and the arithmetic says that is worth ~45px per ARMY tile.
            // It is NOT done in this round because this host's own tab vocabulary is the LEGACY
            // four (`ManageScreenVM.VisibleTabs` is a `List<ManageTab>` of Defense/Buildings/Troops/
            // Research) while the workspace speaks the WO-2001 three (`ManageTabId` Build/Army/
            // Research). Rendering the legacy four here would put a DEFENSE tab back on a screen
            // that merged it into BUILD - a visible regression traded for vertical space.
            // The hub (screen 1) retires both lists at once and is the honest place to do this.

            // ⭐ QUEUE IS A SMALL PILL AT TOP-RIGHT WITH A COUNT BADGE - the mockup draws it on all
            // eight numbered panels and CAPTURE_LOOP_GOAL.md 3.0b states it. Owner, 2026-09-06:
            // "the queuing doesn't deserve a place here... something small up with like the previous
            // next back kind of buttons - I don't think it deserves its own lane."
            // ⚠ THIS SUPERSEDES THE TAB-ROW SEAT of a few hours earlier: that was built from her
            // words before her picture was in the repo, and the picture had said "small pill,
            // top-right" since 09:26 that morning. The renderer no longer draws a queue face at all.
            // ⚠ SEAT PULLED IN 2026-09-06 after the capture. At 0.845-0.965 the pill ran into the
            // frame's ornate right border: ManageFlow_ARMY_gridtop_2670x1200.png shows "QUEUE" cut
            // mid-word. 0.775-0.925 clears the border and is wide enough for the word plus its badge.
            // MEASURED REFERENCE, not guessed: in that same frame the BUILD/ARMY/RESEARCH faces -
            // which are laid out across the workspace band, i.e. the panel's real usable content -
            // end at about x 0.895 of the frame, and the ornate border begins just outside that.
            // Anything seated past it is drawn over the frame art or clipped by it.
            _queueDrawerToggle = ElarionUiKit.BuildObsidianButton(_tabsHost, "QUEUE",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                // ⭐ WIDENED, AND THE INSTRUMENTATION IS WHY. Four rounds moved this pill's x
                // coordinate on the theory that the frame border was clipping it. The rect it
                // printed settled it:
                //   MANAGE_QUEUE_PILL_RECT world x 1791.3..2019.4  size 184x120px
                //   anchors 0.8..0.95  row size 1223x120px
                // The row spans world ~813..2036, so the pill ENDED 17px INSIDE the row's own right
                // edge - and the frame's border art (measured off frame_core.png: interior to
                // x 0.950 at this height) was never near it. THE FRAME WAS INNOCENT. The label was
                // overflowing its own 184px button, so the final E was cut off INSIDE the pill and
                // the corner badge had nowhere to sit.
                // 0.72-0.95 of a 1223px row = ~281px, which seats QUEUE at label size with room for
                // the badge. Widening rather than shrinking the type, per the same law the rail chip
                // records (HudKitController.cs:1866-1878): fewer characters or more room, never a
                // smaller font - a band under ~24px renders BLANK, not small.
                new Vector2(0.720f, 0f), new Vector2(0.950f, 1f), ToggleQueueDrawer);
            if (_queueDrawerToggle != null)
            {
                _queueDrawerToggle.gameObject.name = "ManageQueueDrawerToggle";
                // ⭐ RE-SEATED IMMEDIATELY, BEFORE ANYTHING ELSE TOUCHES IT (owner ruling
                // 2026-09-07). The authored 0.72-0.95 fractions above are now a FALLBACK ONLY: the
                // constant exit owns the right end of this row, so the pill is pinned by px to the
                // exit's left edge instead. Done HERE as well as in SizeQueuePillToLabel because
                // that method early-returns while `rowW < 1f` (no layout yet) - and on that path
                // the authored 0.95 would leave the pill sitting UNDER the X.
                SeatQueuePillLeftOfExit(QueuePillFallbackPx);
                // WO-1393: visible whether the drawer is open or closed - a second tap closes it.
                SyncQueueToggleFace();
                ElarionUiKit.ClampMinTouch(_queueDrawerToggle);
                BuildQueueCountBadge(_queueDrawerToggle.transform);

                // ⛔⛔ THE PILL IS SIZED TO ITS LABEL, MEASURED. DO NOT MOVE AN ANCHOR HERE AGAIN.
                //
                // Nine rounds and four coordinate fixes went into this one control, every one of
                // them a theory about what was clipping it from OUTSIDE. The instrumentation had
                // already answered it and we did not act on the answer:
                //   MANAGE_QUEUE_PILL_RECT ... size 281x120px | anchors 0.72..0.95 | row 1223x120px
                // Nothing clips the pill. THE LABEL DOES NOT FIT INSIDE IT. A wider anchor guesses
                // at how much room a word needs; measuring the word does not.
                //
                // ⚠ WHY A FRACTION COULD NEVER HAVE WORKED: BuildObsidianButton has two modes. The
                // PREFAB mode (ElarionUiKitObsidian.cs:622-651) takes the label's rect from the
                // authored prefab, whose ornate caps eat an inset this file cannot see; only the
                // constructed fallback (:680-682) uses the 0.04-0.96 the code states. So the usable
                // text width is not a knowable fraction of the button - it has to be read back.
                //
                // Canvas.ForceUpdateCanvases resolves the rects that are ZERO on the creation frame
                // (the same reason the overlay X takes a px size instead of a fraction), then the
                // pill is widened to whatever its own word needs, plus the badge's reserved room.
                // The word can change - a count, a translation - and this still holds.
                SizeQueuePillToLabel();

                // ⛔ INSTRUMENT, DO NOT INFER. THIS LINE EXISTS BECAUSE I GUESSED THREE TIMES.
                // The pill has been reported clipped mid-word with its badge outside the frame in
                // three consecutive captures, and each of my three fixes was a coordinate reasoned
                // from a RENDERED EDGE - the same class of error as reading a comment instead of the
                // code. MEASURED this round from the frame asset itself (System.Drawing alpha walk
                // over Assets/Resources/RpgUi/frame/frame_core.png, 1230x1833): at the chrome row's
                // own height the border art ends at x 0.048..0.950 of the frame, and the pill's
                // computed seat is nowhere near that - so my model of the failure is WRONG, and a
                // fourth guess would be worth nothing.
                // This prints where the pill ACTUALLY lands, in reference px, so the next capture
                // names the rect instead of me theorising about it. Read this line before touching
                // the pill's coordinates again (CLAUDE.md 12: the data pinpoints the dead step).
                var pillRt = _queueDrawerToggle.transform as RectTransform;
                if (pillRt != null)
                {
                    var corners = new Vector3[4];
                    pillRt.GetWorldCorners(corners);
                    FlowTrace.Step("Manage", "MANAGE_QUEUE_PILL_RECT world x " +
                        corners[0].x.ToString("0.#") + ".." + corners[2].x.ToString("0.#") +
                        " y " + corners[0].y.ToString("0.#") + ".." + corners[2].y.ToString("0.#") +
                        " | size " + pillRt.rect.width.ToString("0") + "x" +
                        pillRt.rect.height.ToString("0") + "px" +
                        " | anchors " + pillRt.anchorMin.x.ToString("0.###") + ".." +
                        pillRt.anchorMax.x.ToString("0.###") +
                        " | row size " + _tabsHost.rect.width.ToString("0") + "x" +
                        _tabsHost.rect.height.ToString("0") + "px");
                }
            }
        }

        /// <summary>
        /// ⭐ MEASURE THE WORD, THEN SIZE THE PILL TO IT. The end of nine rounds of anchor-nudging.
        ///
        /// <para>The pill keeps its RIGHT edge where the chrome row wants it and grows LEFTWARDS by
        /// whatever its own label needs, so the word can change - a longer count, a translation -
        /// without anyone re-deriving a fraction. Its right edge stays inside the frame's measured
        /// body zone, which is the one number that was ever correct
        /// (frame_core.png interior ends at x 0.950; see WorkspaceHeaderY0's note for the same
        /// treatment on the vertical axis).</para>
        ///
        /// <para>⚠ RECTS ARE ZERO ON THE CREATION FRAME, which is why every fraction this file
        /// computed before a layout pass was a guess. Canvas.ForceUpdateCanvases resolves them
        /// first; only then is <c>GetPreferredValues</c> meaningful.</para>
        ///
        /// <para>Both numbers are printed on MANAGE_QUEUE_PILL_RECT so a capture shows the word's
        /// requirement AND the pill's answer side by side. If they ever disagree again, the trace
        /// says so without anyone theorising.</para>
        /// </summary>
        private void SizeQueuePillToLabel()
        {
            if (_queueDrawerToggle == null || _tabsHost == null) return;
            var pillRt = _queueDrawerToggle.transform as RectTransform;
            if (pillRt == null) return;
            var label = _queueDrawerToggle.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            Canvas.ForceUpdateCanvases();

            float rowW = _tabsHost.rect.width;
            // No layout yet. The seat BuildTabs already applied stands - and since 2026-09-07 that
            // is SeatQueuePillLeftOfExit(QueuePillFallbackPx), an OFFSET from the constant exit
            // rather than the old 0.95-of-the-row fraction, so this path can no longer leave the
            // pill sitting under the X.
            if (rowW < 1f) return;

            // What the word actually needs at the size it is being rendered at.
            float wordPx = label.GetPreferredValues(label.text).x;
            // The chrome the label sits inside: whatever the pill is now, minus what its label rect
            // actually got. That difference IS the prefab's ornate inset, read rather than assumed.
            float labelW = label.rectTransform.rect.width;
            float chromePx = Mathf.Max(0f, pillRt.rect.width - labelW);

            // The badge lives inside the pill and must not eat the word's room.
            float badgePx = _queueDrawerToggle.transform.Find("ManageQueueCountBadge") != null
                ? QueueBadgePx + 12f : 0f;

            float wantPx = wordPx + chromePx + badgePx + 24f;   // +24 so the word never touches the art
            float maxPx = rowW * 0.60f;                          // never let it swallow the row
            float finalPx = Mathf.Clamp(wantPx, 160f, maxPx);

            // ⭐ THE RIGHT EDGE IS NO LONGER 0.95 OF THE ROW - IT IS THE CONSTANT EXIT'S LEFT EDGE.
            // Owner ruling 2026-09-07 puts a permanent X at the top right of every Manage screen,
            // and the pill sits immediately to its left. SeatQueuePillLeftOfExit is the ONE writer
            // of that seat; the offset is derived from ManageExitPx + ManageExitGapPx, never typed.
            SeatQueuePillLeftOfExit(finalPx);

            // The word yields the badge's corner rather than being centred under it. Done on the
            // LABEL's own rect so the button art is untouched - the kit centres the label across the
            // whole face, which is right when there is no badge and wrong when there is.
            if (badgePx > 0f && finalPx > 1f)
            {
                var lrt = label.rectTransform;
                float reserve = badgePx / finalPx;
                lrt.anchorMax = new Vector2(Mathf.Clamp01(lrt.anchorMax.x - reserve), lrt.anchorMax.y);
            }

            FlowTrace.Step("Manage", "MANAGE_QUEUE_PILL_EXITGAP right edge is the exit's left edge " +
                "minus " + ManageExitGapPx.ToString("0") + "px (exit " + ManageExitPx.ToString("0") +
                "px at panel x " + ManageChromeRightX.ToString("0.###") + ")");
            FlowTrace.Step("Manage", "MANAGE_QUEUE_PILL_FIT word='" + label.text + "' needs " +
                wordPx.ToString("0") + "px | chrome " + chromePx.ToString("0") +
                "px | badge " + badgePx.ToString("0") + "px | pill set to " +
                finalPx.ToString("0") + "px (was " + pillRt.rect.width.ToString("0") +
                ", row " + rowW.ToString("0") + "px)");
        }

        /// <summary>The pill's width before its own word has been measured. 281 ref px is the value
        /// MANAGE_QUEUE_PILL_RECT printed for "QUEUE" plus its badge, so the fallback seat is the
        /// measured one rather than a guess; SizeQueuePillToLabel replaces it the moment a layout
        /// pass exists.</summary>
        private const float QueuePillFallbackPx = 281f;

        /// <summary>
        /// ⛔ THE ONE WRITER OF THE QUEUE PILL'S SEAT, and it seats the pill RELATIVE TO THE
        /// CONSTANT EXIT rather than to a fraction of the row.
        ///
        /// <para>Owner ruling 2026-09-07 gives the top-right corner of every Manage screen to the
        /// exit X. The pill's right edge is therefore the exit's LEFT edge less
        /// <see cref="ManageExitGapPx"/>, i.e. <c>ManageExitPx + ManageExitGapPx</c> left of the
        /// chrome row's right edge - which is where the exit's right edge is pinned
        /// (<see cref="ManageChromeRightX"/>, shared by both). Two controls, one number, no
        /// possible disagreement.</para>
        ///
        /// <para>⛔ WHY AN OFFSET AND NOT A FRACTION. A fraction of the row would have to be
        /// recomputed from <c>rowW</c>, and <c>rowW</c> is ZERO on the creation frame - which is
        /// the early-return SizeQueuePillToLabel already carries. An anchored offset in px is the
        /// same number before and after layout, so the fallback seat and the measured seat agree
        /// by construction. This file has paid for the fraction-of-a-varying-rect mistake on the
        /// pill (nine rounds), on the overlay's X (71.8x57.7 against a 112 floor) and on the drawer
        /// band estimate (719px against a rendered 475px). Not a fourth time.</para>
        /// </summary>
        private void SeatQueuePillLeftOfExit(float widthPx)
        {
            if (_queueDrawerToggle == null) return;
            var pillRt = _queueDrawerToggle.transform as RectTransform;
            if (pillRt == null) return;

            pillRt.anchorMin = new Vector2(1f, 0f);
            pillRt.anchorMax = new Vector2(1f, 1f);
            pillRt.pivot = new Vector2(1f, 0.5f);
            pillRt.sizeDelta = new Vector2(Mathf.Max(160f, widthPx), 0f);
            pillRt.anchoredPosition = new Vector2(-(ManageExitPx + ManageExitGapPx), 0f);
        }

        /// <summary>
        /// The red count badge that rides the QUEUE pill's top-right corner, exactly as the mockup
        /// draws it on every panel.
        /// <para>⚠ COLOURBLIND LAW (CAPTURE_LOOP_GOAL 3c): the owner is red/green colourblind and
        /// meaning may never be carried by hue alone. The MEANING here is the DIGIT - how many jobs
        /// are in flight - and the digit is legible in greyscale. The red disc is decoration that
        /// matches her picture, not the channel. If the badge ever loses its number, it stops
        /// meaning anything and must be removed rather than kept as a coloured dot.</para>
        /// <para>Zero jobs paints NOTHING - the mockup shows a badge only where there is a count,
        /// and a "0" badge is a notification that nothing happened.</para>
        /// </summary>
        private void BuildQueueCountBadge(Transform pill)
        {
            if (pill == null) return;
            int jobs = 0;
            if (_vm != null)
                for (int i = 0; i < _vm.Channels.Count; i++)
                    jobs += Mathf.Max(0, _vm.Channels[i].Depth);
            if (jobs <= 0) return;

            // ⛔ THE BADGE LIVES INSIDE THE PILL. Do not push it past 1.0 on either axis again.
            // It was authored at 0.78-1.06 x / 0.52-1.18 y - i.e. deliberately overhanging the
            // corner, the way a notification badge usually does. MEASURED in
            // ManageFlow_ARMY_gridtop_2670x1200.png: the overhang put it OUTSIDE THE PANEL FRAME
            // ENTIRELY, a red square with "15" floating above the top-right corner of the modal,
            // detached from the pill it belongs to. A rect that leaves its parent leaves the panel;
            // this chrome row sits at the frame's edge and has no margin to overhang into.
            // Tucked on the pill's top-right corner and FULLY INSIDE it - the mockup's badge sits on
            // the corner within the panel, and this project has already proved that an overhanging
            // rect at this seat leaves the frame entirely (round 3: a red "15" floating above the
            // modal). On the widened pill (~281px) this is a ~59px disc, which is a badge rather
            // than a second button.
            var disc = ElarionUiKit.AddImage(pill, "ManageQueueCountBadge",
                new Vector2(0.74f, 0.50f), new Vector2(0.95f, 0.94f),
                new Color(0.72f, 0.10f, 0.10f, 1f), rounded: true);
            // ⚠ A FIXED PX SQUARE PINNED TO THE PILL'S TOP-RIGHT, not a fraction. The pill's width is
            // now MEASURED from its label (SizeQueuePillToLabel), so a fractional badge would change
            // size every time the word did - and QueueBadgePx, which that method subtracts to
            // reserve the badge's room, would stop being true. One number, honoured in both places.
            if (disc != null)
            {
                var discRt = (RectTransform)disc.transform;
                discRt.anchorMin = discRt.anchorMax = new Vector2(1f, 1f);
                discRt.pivot = new Vector2(1f, 1f);
                discRt.sizeDelta = new Vector2(QueueBadgePx, QueueBadgePx);
                discRt.anchoredPosition = new Vector2(-10f, -6f);
            }
            var discImage = disc != null ? disc.GetComponent<Image>() : null;
            if (discImage != null) discImage.raycastTarget = false;
            if (disc == null) return;

            var count = ElarionUiKit.Label(disc.transform, jobs.ToString(), 0.06f, 0.94f,
                ElarionUi.Parchment, (int)ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.02f, 0.98f,
                bold: true);
            ElarionUiKit.FitSingleLine(count, 20f, 30f);
        }

        /// <summary>
        /// ⭐ THE BACK ARROW - mockup 3.0b, on every numbered panel, top-LEFT.
        /// <para>Built HERE, inside the row's own rebuild, because <see cref="BuildTabs"/> clears
        /// every child of <c>_tabsHost</c> on entry. Round 5 built it once at chrome time and the
        /// first Render deleted it; the capture showed a Manage screen with no way back.</para>
        /// <para>⭐ WO-1491: the FACE is <see cref="ManageArt.IconBack"/>, bound by
        /// <see cref="ApplyBackGlyph"/>. ASCII "&lt;-" survives only as the fallback when that
        /// sprite does not resolve - a back door that renders blank is worse than an ugly one.
        /// The left-arrow CHARACTER is still banned (fonts render non-ASCII as tofu), and
        /// <c>RpgUi/button/arrow.png</c> is still rejected (a filled RIGHT-pointing play triangle;
        /// a mirrored play glyph reads as "rewind").</para>
        /// <para>Hidden on the hub (<see cref="ApplyScreenVisibility"/>): panel 1 is the root, and
        /// CLOSE is its way out - and it is the ONLY screen that still draws CLOSE.</para>
        /// </summary>
        private void BuildBackArrow()
        {
            if (_tabsHost == null) return;
            // 0-0.095 of the row. At the measured reference (~1218px row) that is ~116px, clear of
            // MinTouchPx(112) - authored to the floor, not left to ClampMinTouch.
            _workspaceBack = ElarionUiKit.BuildObsidianButton(_tabsHost, "<-",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0f, 0f), new Vector2(0.095f, 1f), OnBackPressed);
            if (_workspaceBack == null)
            {
                FlowTrace.Fail("Manage", "the BACK arrow failed to build - the screen has no visible " +
                    "way back, which is what ManageFlow_ARMY_gridtop showed on 2026-09-06.");
                return;
            }
            _workspaceBack.gameObject.name = "ManageWorkspaceBack";
            ElarionUiKit.ClampMinTouch(_workspaceBack);
            MedievalUiSkin.ApplyButton(_workspaceBack);
            ApplyBackGlyph(_workspaceBack);
            _workspaceBack.gameObject.SetActive(!_hubShowing);
        }

        /// <summary>
        /// ⭐ WO-1491 - THE BACK FACE IS THE KIT'S ARROW SPRITE, NOT THE LITERAL "&lt;-".
        /// <para>EVIDENCE: Logs/device/screens/owner-screen-20260907-010151.png reads
        /// "&lt; -" in the top-left - two ASCII glyphs kerned apart, which is what a text arrow
        /// looks like once FitSingleLine has sized it to a square plate. The mockup draws a plain
        /// arrow on every numbered panel.</para>
        /// <para>⛔ THE LABEL IS BLANKED, NOT DELETED, AND ONLY WHEN THE SPRITE RESOLVED. If
        /// <c>ManageArt.IconBack</c> is missing the button keeps its ASCII face and
        /// <see cref="ManageArt.LoadSprite"/> has already announced the miss - a back door that
        /// silently renders EMPTY is the WO-1443 defect (a Manage screen with no visible way
        /// back), and it is worse than an ugly one.</para>
        /// <para>The glyph is a CHILD image inset inside the plate so the button's own art, tint
        /// feedback and touch floor are untouched; nothing here re-implements a button.</para>
        /// </summary>
        private static void ApplyBackGlyph(Button back)
        {
            if (back == null) return;
            var arrow = ManageArt.LoadSprite(ManageArt.IconBack);
            if (arrow == null)
            {
                FlowTrace.Once("Manage", "back-glyph-miss",
                    "the BACK arrow sprite is unresolved at Resources/" + ManageArt.IconBack +
                    " - the button keeps its ASCII '<-' face rather than rendering blank. This is " +
                    "an ART ASK, not a layout fault.");
                return;
            }

            var label = back.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = string.Empty;

            var go = new GameObject("ManageBackGlyph", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(back.transform, false);
            var rt = (RectTransform)go.transform;
            // Inset inside the plate's own border art so the arrow never rides the frame edge.
            rt.anchorMin = new Vector2(0.26f, 0.24f);
            rt.anchorMax = new Vector2(0.74f, 0.76f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = arrow;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;      // the BUTTON takes the tap, never the decoration
        }

        /// <summary>
        /// The HEART face - the hub's door onto <see cref="PanelId.Heart"/>, drawn ONLY while an
        /// upgrade is due.
        /// <para>⛔ "ALWAYS-PRESENT" IS RETIRED, AND SO IS THE LEVEL BADGE (WO-1597, owner
        /// 2026-09-07): <i>"there is no reason to have heart on this set of manage screens unless
        /// for an upgrade"</i>. The face used to read "HEART L3" unconditionally - a level badge on
        /// a screen the mockup draws without one. It is now the UPGRADE VERB, gated on
        /// <c>ManageScreenVM.HeartUpgradeAvailable</c>, and absent otherwise.</para>
        /// <para>⚠ WHAT THAT COSTS, RECORDED RATHER THAN GLOSSED: at MAX Heart Level the hub has no
        /// Heart door. That is correct - there is nothing behind it to do - but it does mean this is
        /// no longer an UNCONDITIONAL route, which is the property HeartSurfaceRegression's
        /// [heart-has-a-door] case was written to defend. The remaining routes are the gated CTAs:
        /// <c>ManageScreenVM.OpenHeartPanel</c> opens PanelId.Heart from every Heart-gated card, and
        /// those exist precisely while something is still gated. If the owner wants the door back at
        /// max, the change is the predicate in the VM and nothing here moves.</para>
        /// <para>⚠ The level and the cost are read from the model at build time and never cached
        /// here - duplicated state is what produced the stale-copy family this program exists to
        /// kill.</para>
        /// </summary>
        private void BuildHeartFace()
        {
            // ⭐ THE HEART DOOR MOVED TO THE HUB. It is no longer in the chrome row on ANY screen.
            //
            // It sat there since WO-2003/WO-2017 for one reason - it was the only unconditional
            // route to PanelId.Heart, and this suite's own [heart-has-a-door] case exists to stop it
            // vanishing. That reason is unchanged and the door is unchanged; only its SEAT moved.
            //
            // WHY IT MOVED: it appears in none of the mockup's nine panels, and three rounds of
            // trying to make it small enough to be unobtrusive ended with "HEART ..." truncating at
            // ~177px. Shrinking a face below its own label does not make it quiet, it makes it
            // broken - and the honest fix, which the hub finally makes possible, is to put it where
            // a root-level destination belongs. Panel 1 does not draw it either; it is there because
            // the Heart must keep a door, and the hub is the least wrong place for one extra
            // affordance: it is the ROOT, it already lists destinations, and grid screens 2/4/6 now
            // match the mockup's chrome exactly - back arrow, centred title, queue pill, nothing else.
            //
            // ⛔ DO NOT re-seat it in the chrome row to "make it reachable faster". If the Heart
            // ever gets a door elsewhere (the town, the HUD, a quest), delete the hub face too and
            // move HeartSurfaceRegression's pin with it - do not end up with two.
            BuildHubHeartDoor();
        }

        /// <summary>
        /// The Heart's door, on the HUB - the UPGRADE VERB, in the header band, and ONLY while an
        /// upgrade is due.
        /// <para>⚠ The verb and its price are read from <c>ManageScreenVM</c> (which reads
        /// <c>HeartProgression</c>) at build time and never cached here - a second copy of a live
        /// number is the duplicated state this file keeps paying for. The face is rebuilt with the
        /// hub, so nothing can go stale.</para>
        /// </summary>
        private void BuildHubHeartDoor()
        {
            if (_launcherHost == null) return;
            // RenderLauncherCards clears the CARD GRID, not this host, so a re-render would stack a
            // second heart on the first. Drop any previous one before building.
            for (int i = _launcherHost.childCount - 1; i >= 0; i--)
            {
                var child = _launcherHost.GetChild(i);
                if (child == null || child.name != "ManageHeartFace") continue;
                if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
            }
            // ⭐ WO-1597 - NO UPGRADE DUE, NO CHIP. The destroy loop above has already run, so a
            // chip built on a previous render (when an upgrade WAS due) is gone rather than left
            // behind stale - the removal is a real removal, not a SetActive.
            // ⛔ READ THE FLAG, NOT THE PREDICATE. _hubHeartShown was written by BuildLauncher in
            // the same breath as the card band. Re-reading ManageScreenVM.HeartUpgradeAvailable here
            // would put the two decisions on two different frames, and a wallet spend between them
            // would leave a 136px hole above the cards or a chip overprinting them.
            if (!_hubHeartShown)
            {
                FlowTrace.Step("Manage", "MANAGE_HUB_HEART chip STOOD DOWN - " +
                    "HeartProgression.State is " +
                    DeNelle.Village.Buildings.Progression.HeartProgression.State +
                    " (Heart Level " + DeNelle.Village.Buildings.Progression.HeartProgression.Level +
                    " of " + DeNelle.Village.Buildings.Progression.HeartProgression.MaxLevel +
                    "), so no upgrade is due and mockup panel 1 draws no chip. The card band takes " +
                    "its " + (HubTitleBandPx + HubBandGapPx).ToString("0") + "px back");
                return;
            }
            // ⭐ WO-1567 ROUND 25 - THE CHIP IS IN THE HUB'S HEADER BAND, AT THE TOUCH FLOOR.
            // ⛔ THE SEAT IT REPLACES IS THE CAUSE OF SEVEN OF THE ELEVEN NON-QUEUE ORACLE FAILURES.
            // MEASURED on Builds/cap-manage-wave4.log, every line naming this object:
            //   SUB-TOUCH-FLOOR BAND 'ManageHeartFace' resolves 440.5x75.4 ref px -- 36.6 px UNDER
            //     ElarionUiKit.MinTouchPx (112)
            //   BUTTONS OVERLAP  ManageCard_BUILD / _ARMY / _RESEARCH each share 74.9x39.9 (ARMY
            //     242.7x39.9) ref px with it -- two tap targets in one place
            //   BUTTON OVER TEXT the same three cards cover its "HEART L4" label
            // The old comment reasoned the band was "under the card grid (which ends at 0.695)".
            // That number went stale the moment the band became derived: with a ~583px host the
            // cards ran 0.281..0.794, so 0.70..0.83 was INSIDE them. The cure is not a smaller chip
            // - it is the hub's own band arithmetic, exactly as that comment said, and this is it.
            // ⚠ THE BAND IS MEASURED, NOT TYPED: _hubHeartY0 is 1 - MinTouchPx/hostH, computed by
            // BuildLauncher off the SAME host height the card band's topF is computed from, so the
            // two rects are complements by construction and cannot drift apart again.
            // ⛔ AND IT STAYS ON THE HUB. HeartSurfaceRegression:118-139 pins that the door is
            // BuildHubHeartDoor's, named ManageHeartFace, and is NOT in the chrome row - the mockup
            // keeps that row for the back arrow, the centred title and the queue pill.
            // ⛔ THE FACE IS THE UPGRADE VERB, AND THE MODEL COMPOSES IT (canon section 9). The View
            // binds a string it did not write, so the chip and the Heart's own CTA cannot drift into
            // two vocabularies - which is the WO-1443 "HEART ..." family all over again.
            // ⚠ WIDENED FROM 0.24 TO 0.30 FOR A STATED REASON: the retired face was "HEART L3" (8
            // characters) and this one is 13. The recorded failure mode of this exact control is a
            // label truncating inside a plate authored for a shorter word (three rounds, ~177px), and
            // the cure that worked on the QUEUE pill was room, never a smaller font - a band under
            // ~24px renders BLANK rather than small.
            string verb = DeNelle.Village.UI.ManageScreenVM.HeartUpgradeFace;
            var heart = ElarionUiKit.BuildObsidianButton(_launcherHost,
                verb,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.02f, _hubHeartY0), new Vector2(0.30f, 1f), OpenHeartSurface);
            if (heart == null)
            {
                FlowTrace.Fail("Manage", "the HEART face failed to build - the direct route to " +
                    "PanelId.Heart is missing from this screen.");
                return;
            }
            heart.gameObject.name = "ManageHeartFace";
            MedievalUiSkin.ApplyButton(heart, true);
            ElarionUiKit.ClampMinTouch(heart);
            // ⭐ THE PRICE, ON ITS OWN LINE INSIDE THE PLATE. The band is MinTouchPx (112) tall, so
            // two lines at ElarionUi.FontFloorMobile (30) plus leading fit with room; a single line
            // carrying verb AND cost does not, at this width, and would ellipsise the verb.
            // The verb yields the top ~62% of the plate rather than staying centred across the whole
            // face - done on the LABEL's own rect, so the button art and its touch box are untouched.
            string price = DeNelle.Village.UI.ManageScreenVM.HeartUpgradeCost;
            var verbLabel = heart.GetComponentInChildren<TMP_Text>(true);
            if (verbLabel != null && !string.IsNullOrEmpty(price))
            {
                var vrt = verbLabel.rectTransform;
                vrt.anchorMin = new Vector2(vrt.anchorMin.x, 0.38f);
                vrt.anchorMax = new Vector2(vrt.anchorMax.x, 0.98f);
                vrt.offsetMin = vrt.offsetMax = Vector2.zero;
                var cost = ElarionUiKit.Label(heart.transform, price, 0.06f, 0.36f,
                    ElarionUi.Parchment, (int)ElarionUi.FontLabel,
                    TextAlignmentOptions.Center, 0.06f, 0.94f);
                if (cost != null)
                {
                    cost.raycastTarget = false;          // the BUTTON takes the tap, never the price
                    ElarionUiKit.FitSingleLine(cost, ElarionUi.FontFloorMobile, 32f);
                }
            }
            FlowTrace.Step("Manage", "MANAGE_HUB_HEART band " + _hubHeartY0.ToString("0.###") +
                "..1.0 of a " + _hubHeartHostH.ToString("0") + "px host = " +
                ((1f - _hubHeartY0) * _hubHeartHostH).ToString("0") + "px tall (floor " +
                ElarionUiKit.MinTouchPx.ToString("0") + ") - the header band, clear of every card; " +
                "face='" + verb + "' price='" + (price ?? "-") + "'");
        }

        /// <summary>Open the Heart surface. Closes Manage first (PanelManager holds one exclusive
        /// panel), and says so out loud if the route is dead rather than doing nothing.</summary>
        private void OpenHeartSurface()
        {
            Close();
            if (!PanelRouter.Open(PanelId.Heart))
                FlowTrace.Fail("Manage", "PanelRouter.Open(PanelId.Heart) returned FALSE - the Heart " +
                    "panel is not registered (HeartPanelBootstrap did not run) or it failed to become " +
                    "visible. The player just tapped a door that opened nothing.");
        }

        // =====================================================================
        //  RENDER
        // =====================================================================

        private void Render()
        {
            if (_vm == null || _ui == null) return;
            Guard.Try("Manage", "render manage rows", () =>
            {
                RenderStrip();
                RenderSlotOffer();
                RenderRail();
                BuildTabs();
                RenderList();
                // WO-1393: RenderList rebuilt the TRAINING NOW band active and the tab may have
                // changed - re-seat the drawer / list band / band before the drawer renders.
                ApplyDrawerPlacement();
                // WO-1368 — AFTER RenderList, which clears the tick/progress cells. The drawer's
                // rows register their own countdown cells and must survive that clear.
                if (_queueDrawerOpen) RenderQueueDrawer();
                Canvas.ForceUpdateCanvases();
                if (_listContent != null)
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
                if (_drawerContent != null)
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_drawerContent);
                ApplyOperationalMedievalSkin();
            });
            FlushNotice();
            // Capacity is already explicit in the three persistent channel chips. Do not add
            // a duplicate session-complete sentence beside Close; that footer seat is reserved
            // for actionable command feedback only.
        }

        private void ApplyOperationalMedievalSkin()
        {
            if (_ui == null) return;
            var buttons = _ui.GetComponentsInChildren<Button>(true);
            foreach (var button in buttons)
            {
                if (button == null) continue;
                // ⛔ WO-2001 - THE WORKSPACE SUBTREE IS EXEMPT, WHOLESALE. ManageWorkspacePanel
                // authors every face against a MEASURED pixel band and fits it there (its band
                // table states each height in px, floor 28 for text and 112 for touch). This
                // copy-keyed bulk pass re-promotes faces by their WORDS and re-fits labels with a
                // 30px floor - which is precisely the clipping the two WO-1422 polish notes below
                // record for the Defense and Research rails ("Archer Tower" -> "ARCHER T..."). An
                // ancestry test rather than a name prefix, because the renderer's object names are
                // its own business and a prefix list here would be a second copy of them.
                if (_workspaceHost != null && button.transform.IsChildOf(_workspaceHost)) continue;
                string objectName = button.gameObject.name ?? string.Empty;
                if (string.Equals(objectName, "Scrim", StringComparison.Ordinal) ||
                    string.Equals(objectName, "CloseButton", StringComparison.Ordinal) ||
                    objectName.StartsWith("ManageCard_", StringComparison.Ordinal) ||
                    objectName.StartsWith("TroopChoice_", StringComparison.Ordinal) ||
                    objectName.StartsWith("BuildingChoice_", StringComparison.Ordinal) ||
                    // WO-1422 POLISH (MEASURED 2026-09-06, ManageDefense/ManageResearch_2670x1200.png):
                    // the two NEW rails were MISSING from this skip-list, so the bulk pass below ran
                    // MedievalUiSkin.ApplyButton on every rail row - and that method rewrites the
                    // row's FIRST label (MedievalUiSkin.cs:83-95): ToUpperInvariant, characterSpacing
                    // 2, the wide TITLE face, and a re-fit whose floor is 30 instead of the rail's 26.
                    // "Archer Tower" became "ARCHER T..." and "Expanded Capacity" "EXPANDE..." while
                    // the Troops rail, already skipped here, fitted "Footman" at the same width.
                    // The rail row is a FLAT selectable face, never a gold button plate - same reason
                    // TroopChoice_ is on this list.
                    objectName.StartsWith("DefenseChoice_", StringComparison.Ordinal) ||
                    objectName.StartsWith("ResearchChoice_", StringComparison.Ordinal) ||
                    // WO-1382: the two card CTAs are skinned by their builder (TRAIN primary,
                    // UPGRADE secondary) - the copy-keyed pass below would promote "UPGRADE TO L2"
                    // to primary and erase the one-primary hierarchy the owner asked for. The
                    // Training-chip tap plate has no label and must never be painted as a CTA.
                    objectName.StartsWith("TroopCta_", StringComparison.Ordinal) ||
                    objectName.StartsWith("BuildingCta_", StringComparison.Ordinal) ||
                    // WO-1422 POLISH - same reason, INFERRED from the copy test below rather than
                    // measured in a frame: BuildDefenseCard / BuildResearchCard already call
                    // MedievalUiSkin.ApplyButton with the correct primary flag, and the copy-keyed
                    // pass would re-promote the GRAY dead faces ("RESEARCHING" contains "RESEARCH",
                    // "UPGRADE TO L2" contains "UPGRADE") to the primary face, erasing the
                    // one-primary hierarchy the owner asked for.
                    objectName.StartsWith("DefenseCta_", StringComparison.Ordinal) ||
                    objectName.StartsWith("ResearchCta_", StringComparison.Ordinal) ||
                    objectName.StartsWith("ManageLineStatus_", StringComparison.Ordinal)) continue;

                var label = button.GetComponentInChildren<TMP_Text>(true);
                string copy = label != null ? label.text ?? string.Empty : string.Empty;
                bool primary = copy.IndexOf("BUILD NEW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("BUILD DEFENSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("OPEN BUILD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("TRAIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("RESEARCH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("UPGRADE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("FINISH NOW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               copy.IndexOf("BUY BUILDER", StringComparison.OrdinalIgnoreCase) >= 0;
                MedievalUiSkin.ApplyButton(button, primary);
            }

            var trackSprite = Resources.Load<Sprite>("UI/ElarionMedieval/progress/progress-track-empty");
            if (trackSprite != null)
            {
                foreach (var image in _ui.GetComponentsInChildren<Image>(true))
                {
                    if (image == null || image.gameObject.name.IndexOf("Track", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    image.sprite = trackSprite;
                    image.type = Image.Type.Sliced;
                    image.color = Color.white;
                }
            }
        }

        // =====================================================================
        //  WO-1027 §3.3 — THE SESSION-COMPLETE SIGNAL (the quiet inverse of the ache)
        // =====================================================================
        // CoC never told a player she was DONE, and that is a genuine gap: a player who does not
        // know she is finished leaves hunting for a missed thing instead of leaving satisfied.
        //
        // ⚠ ITS PREDICATE IS STRICTER THAN THE BAR NUMERAL'S, on purpose. The Manage face goes
        // quiet at "no line is idle" (something is cooking everywhere); this line waits for
        // AllLinesLoaded() — every line at FULL crew, nothing left to start. Telling a player she
        // is set while a slot sits free would be a lie, and a wrong session-complete signal is
        // worse than none at all.
        //
        // It is A SENTENCE. Not a colour, not a checkmark glyph, not a toast (ruling (c) is
        // REJECTED and nothing here fires on entering town). It reuses the existing notice seat,
        // so no band is added and the panel's pixel budget is untouched — and it NEVER stomps a
        // real notice, which is the one message the player actually asked for.
        private const string SessionCompleteText = "Every line is loaded - you are set for now.";
        private bool _sessionCompleteShown;

        private void RenderSessionComplete()
        {
            if (_noticeLabel == null) return;
            bool set = ObsidianQueueGate.Status.AllLinesLoaded();
            if (set == _sessionCompleteShown) return;      // transition only

            if (set)
            {
                if (!string.IsNullOrEmpty(_noticeLabel.text)) return;   // a live notice wins
                _noticeLabel.text = SessionCompleteText;
                _sessionCompleteShown = true;
                FlowTrace.Step("Manage", "session complete: all 3 lines loaded, no free slots");
                return;
            }

            if (string.Equals(_noticeLabel.text, SessionCompleteText, StringComparison.Ordinal))
                _noticeLabel.text = "";
            _sessionCompleteShown = false;
        }

        private void RenderStrip()
        {
            for (int i = 0; i < _stripCells.Length; i++)
            {
                var cell = _stripCells[i];
                if (cell == null) continue;
                string text = i < _vm.Channels.Count
                    ? _vm.Channels[i].Describe()
                    : (i == 0 ? "Builders 0/0" : i == 1 ? "Training 0/0" : "Research 0/0");
                // WO-1382 ruling #1: the Training chip carries the line's DEPTH ("Training 1/2 .
                // 1/5 queued") - the VM composes it; this only paints it.
                if (i == 1 && _vm.TrainingChipText != null) text = _vm.TrainingChipText;
                cell.text = ManageScreenVM.Ascii(text);
                ElarionUiKit.FitSingleLine(cell, ElarionUiKit.FontHardFloor, 34f);
            }
            for (int i = 0; i < _launcherSummaries.Length; i++)
            {
                var cell = _launcherSummaries[i];
                if (cell == null) continue;
                if (i < _vm.Channels.Count)
                {
                    ChannelSummary s = _vm.Channels[i];
                    cell.text = s.Describe();
                }
                else
                {
                    string name = i == 0 ? "Builders" : i == 1 ? "Training" : "Research";
                    cell.text = name + " 0/0";
                }
                ElarionUiKit.FitSingleLine(cell, ElarionUiKit.FontHardFloor, 34f);
            }
        }

        private void RenderSlotOffer()
        {
            if (_slotLabel != null)
            {
                _slotLabel.text = ManageScreenVM.Ascii(_vm.SlotOfferText ?? "");
                ElarionUiKit.FitSingleLine(_slotLabel);
            }
            if (_slotButton != null)
            {
                _slotButton.gameObject.SetActive(_vm.BuilderUpsellVisible);
                var label = _slotButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = ManageScreenVM.Ascii(_vm.BuilderUpsellButtonText ?? "");
                    ElarionUiKit.FitSingleLine(label);
                }
                FlowTrace.Step("Manage", "builder upsell shown=" + _vm.BuilderUpsellVisible +
                    " price='" + (_vm.BuilderUpsellButtonText ?? "") + "'");
            }
        }

        private void RenderRail()
        {
            // PINNED path only. When the well could not afford a 200px pinned band (see the budget
            // in BuildChrome) the rail rides the scroll list instead and RenderList mounts it.
            // WO-1368: inside the drawer _railBand is null by construction, so this is inert there
            // and RenderQueueDrawer owns the rail. Kept because the demoted/pinned split is still
            // real for the browse list.
            if (!_railPinned || _railBand == null) return;
            MountRail(_railBand, forceRebuild: false);
        }

        /// <summary>
        /// ⛔ WO-1368 — THE BUILD SITE FOR THE QUEUE VERBS. This is the only caller of
        /// <see cref="AddQueueRow"/>, and it is deliberately DRAWER-ONLY: the 2026-08-31 ruling
        /// keeps the browse list free of queue rows, and this method keeps the verbs from having
        /// nowhere at all to be built (the three-day state in which <c>Finish Now</c> and
        /// <c>Ad</c> existed in code and rendered nowhere).
        ///
        /// <para>Called AFTER <see cref="RenderList"/> in <see cref="Render"/>, because RenderList
        /// clears <c>_tickCells</c> / <c>_progressCells</c> — rows built before it would keep their
        /// buttons but silently lose their countdowns.</para>
        /// </summary>
        private void RenderQueueDrawer()
        {
            if (_drawerContent == null || _vm == null) return;

            // WO-1393: the slot offer is built once and rides the list as its LAST row - park it
            // back on the drawer before the clear below so its label/button fields survive.
            if (_drawerSlotOffer != null && _queueDrawer != null)
            {
                _drawerSlotOffer.SetParent(_queueDrawer.transform, false);
                _drawerSlotOffer.gameObject.SetActive(false);
            }
            for (int i = _drawerContent.childCount - 1; i >= 0; i--)
            {
                var child = _drawerContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            // Redirect the shared row factory at the drawer for the length of this build. The
            // alternative — a second set of row builders — is the duplicated-state defect that
            // produced this ticket in the first place.
            _rowParent = _drawerContent;
            try
            {
                // ⭐ THE OVERLAY'S OWN CHANNEL, not the browse tab's. Until WO-1443 this read
                // ChannelOf(_vm.Tab), so the drawer rendered whichever line the browse tab happened
                // to be on and a player could not reach another channel's queue from inside the
                // overlay at all. The model owns the selection (ManageScreenVM.QueueOverlayChannel)
                // and it still DEFAULTS to the browse tab's line, so opening is unchanged.
                var channel = _vm.QueueOverlayChannel;

                // The rail leads as the FIRST ROW: a status glance (ruling §7, display-only) above
                // the rows that carry every action. WO-1393: NOT in band mode - the Troops
                // workspace rail and the strip's counts are already on screen, and a 200px rail
                // in a ~235px band is exactly what clipped the header under it.
                // ⛔ NO CARD RAIL IN THE OVERLAY. Do not mount it here again.
                // The capture showed five TRAIN cards - Militia / Archer / Militia / Spearman /
                // Archer - filling the top of panel 8, where the mockup has numbered rows and
                // nothing else. The rail is a STATUS GLANCE and it says what the rows below it
                // already say, twice over, in the space the rows need. It stays alive on the
                // workspace (MountRail's other callers); it is only the overlay that drops it.
                // ⚠ _railBandPx and MountRail are untouched - this is a caller removed, not a
                // capability deleted.

                // ⭐ THE TAB ROW REPLACES THE SINGLE-CHANNEL HEADER.
                // "IN QUEUE - BUILDERS" named the one line the drawer could show; panel 8 draws
                // THREE tabs - BUILDERS (2/2) / TRAINING (2/2) / RESEARCH (2/2) - and the active
                // one is the header. A header AND a tab row would tell the same fact twice, which is
                // the duplicated-state defect this screen keeps paying for.
                // ⛔ INTO ITS OWN FIXED ZONE, NEVER MakeRowHost. MakeRowHost appends to the
                // SCROLL LIST, so the first version of this line made the tabs a scrolling row that
                // rendered AFTER the rail - the capture's backwards reading order, title -> cards ->
                // tabs -> rows. A header that scrolls is not a header.
                BuildQueueTabs(_drawerTabs);
                SeatQueueListToWholeRows();
                if (_vm.QueueRows.Count == 0)
                    // ⭐ WO-1488 - THE MODEL'S SENTENCE, NAMING THIS CHANNEL'S OWN VERB.
                    // The literal that stood here said "Start an upgrade" on all three lines while
                    // the slot line beneath said "tap TRAIN" on all three, so the RESEARCH tab
                    // pointed the owner at the troop door. ManageScreenVM.QueueEmptyText composes
                    // it from the same table the slot offer reads (QueueChannelVerb).
                    // ⛔ AND IT IS SEATED IN THE WELL, not typed at it: AddNoteRow's row is
                    // SectionHeaderPx tall inside the list's own scroll padding, so the sentence
                    // cannot render below the plate's inner floor the way the capture showed
                    // "2 slots free - tap TRAIN to fill them" sliced by the frame.
                    AddNoteRow(_vm.QueueEmptyText);
                else
                    for (int i = 0; i < _vm.QueueRows.Count; i++) AddQueueRow(_vm.QueueRows[i]);

                // WO-1393: the Buy-Builder offer is the list's LAST row in both modes (it scrolls;
                // it no longer owns a fixed zone that starved the rows of height).
                if (_drawerSlotOffer != null)
                {
                    var offerRow = MakeRowHost("Drawer_SlotOfferRow", SlotBandPx);
                    _drawerSlotOffer.SetParent(offerRow, false);
                    _drawerSlotOffer.anchorMin = new Vector2(0.035f, 0f);
                    _drawerSlotOffer.anchorMax = new Vector2(0.965f, 1f);
                    _drawerSlotOffer.offsetMin = _drawerSlotOffer.offsetMax = Vector2.zero;
                    _drawerSlotOffer.gameObject.SetActive(true);
                }

                MakeRowHost("DrawerTailSpacer", ListTailPx);

                // ⛔ TRACED LAST, AFTER THE ROWS EXIST. The first version of this call sat up beside
                // BuildQueueTabs and measured the content BEFORE a single row had been added, which
                // is why the capture reported listContent as 1139x20 - "barely taller than a line of
                // text". That was not a layout failure; it was a measurement taken too early, and it
                // very nearly sent us hunting a row-height bug that does not exist.
                // ⚠ A measurement's TIMING is part of the measurement. Read this line only from the
                // end of a render.
                TraceQueueOverlayLayout();
                TraceQueueTabFit();
            }
            finally
            {
                // Restored in a finally so a throw inside a row build can never leave the BROWSE
                // list pointed at the drawer — that would silently move every later row.
                _rowParent = null;
            }

            // §12 — the acceptance evidence for this ticket. It names the BUILD SITE and the
            // controls, not just the VM's row count: queueRows tracked the real job count
            // perfectly all morning while no verb existed, so a count alone proves nothing.
            int finishable = 0, adOffers = 0, cancellable = 0;
            for (int i = 0; i < _vm.QueueRows.Count; i++)
            {
                var r = _vm.QueueRows[i];
                if (r == null || r.IsStackHeader) continue;
                if (r.FinishPrice > 0) finishable++;
                if (r.AdAvailable && DeNelle.Core.FeatureFlags.RewardedAdSkip) adOffers++;
                if (r.CanCancel) cancellable++;
            }
            FlowTrace.Step("Manage", string.Format(
                "queue drawer BUILT {0} row(s) into Drawer_QueueList: FinishNow={1} Ad={2} Cancel={3} " +
                "(rewardedAdSkip={4}). Zero rows with a non-empty queue, or zero FinishNow on a " +
                "priced job, is the WO-1368 defect returning.",
                _vm.QueueRows.Count, finishable, adOffers, cancellable,
                DeNelle.Core.FeatureFlags.RewardedAdSkip));
            if (_vm.QueueRows.Count > 0 && finishable == 0 && adOffers == 0 && cancellable == 0)
                FlowTrace.Warn("Manage",
                    "queue drawer built rows but NOT ONE carries a verb - Finish Now, Ad and Cancel " +
                    "are all withheld by the VM. The money path is unreachable from this screen.");
        }

        /// <summary>
        /// Build (or re-sync) the WO-864 rail into <paramref name="mount"/>. The rail pins itself to
        /// the TOP of its mount at a FIXED <see cref="QueueRailView.Height"/> — which is precisely
        /// why its host must be a pixel band: build 1 handed it a 0.2-of-body fraction (~82px) and
        /// 200px of rail painted straight over the tab row below it.
        /// </summary>
        private void MountRail(RectTransform mount, bool forceRebuild)
        {
            if (mount == null) return;
            var channel = ManageScreenVM.ChannelOf(_vm.Tab);

            // Rebuild the rail only when the TAB's channel actually changed (Defense -> Buildings
            // keeps the same Builders rail and must not thrash it). A rail living in the scroll list
            // is destroyed with the rows every render, so that path always rebuilds.
            if (!forceRebuild && _rail != null && _railChannel == channel) { _rail.Sync(); return; }

            for (int i = mount.childCount - 1; i >= 0; i--) Destroy(mount.GetChild(i).gameObject);
            _railChannel = channel;
            Guard.Try("Manage", "build queue rail", () =>
            {
                // Reuses the WO-864 rail component verbatim through its host-agnostic contract.
                // The rail is DECORATION here: its cards are raycast-off, so the collapsed xN card
                // physically cannot be a cancel target (ruling Q12). Every action lives on the rows.
                _rail = QueueRailView.Build(mount, channel, QueueRailView.Options.Default);
            });
        }

        /// <summary>
        /// ⛔ WO-2001 - THE BODY IS NOW THE THREE-TAB WORKSPACE. Everything below the delegation is
        /// the LEGACY rail + selected-card path (WO-1418 / WO-1422). It is retained deliberately and
        /// NOT deleted here for two stated reasons: (1) eight suites read these method bodies as
        /// SOURCE TEXT and a silent deletion would take them red inside a lane that cannot run the
        /// gate, and (2) the per-destination cards are the proven detail surfaces the redesign's
        /// DETAIL screens are modelled on. ⚠ It is nevertheless DEAD CODE UNDER GREEN PINS - the
        /// exact shape ManageQueueDrawerRegression:103-113 exists to catch - so its removal, and the
        /// pin moves that must precede it, are itemised in this work order's hand-back. Do not leave
        /// it here indefinitely.
        /// </summary>
        private void RenderList()
        {
            if (WorkspaceActive) { RenderWorkspace(); return; }
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                var child = _listContent.GetChild(i).gameObject;
                // Runtime keeps Unity's normal end-of-frame destruction semantics. The synchronous
                // edit-mode capture has no next frame before it renders, so deferred destruction
                // leaves the previous tab's rows painted above the requested destination and turns
                // screenshot evidence into a lie. Match BuildTabs' already-proven edit-mode rule.
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _tickCells.Clear();
            // The progress cells point at bars that were just destroyed with their rows. The tick
            // already skips a Unity-null fill, so this never crashed — but without the clear the
            // list grew by every rebuild for the life of the open panel.
            _progressCells.Clear();
            _trainingNowCells.Clear();   // WO-1382: the band's cells die with its rows too

            var channel = ManageScreenVM.ChannelOf(_vm.Tab);

            if (_vm.Tab == ManageTab.Buildings)
            {
                RenderBuildingsDestination(channel);
                return;
            }

            if (_vm.Tab == ManageTab.Troops)
            {
                RenderTroopsDestination(channel);
                MakeRowHost("ListTailSpacer", ListTailPx);
                return;
            }

            // WO-1422 (owner ruling 2026-09-06): Defence and Research take the SAME rail + card +
            // NOW band + footer shape as Buildings. The paged text list they used to share is
            // retired with its pager and its row painter - see ruling 3.4.
            if (_vm.Tab == ManageTab.Defense)
            {
                RenderDefenseDestination(channel);
                return;
            }

            if (_vm.Tab == ManageTab.Research)
            {
                RenderResearchDestination(channel);
                return;
            }

            // The DEMOTED rail (see the band budget): its own fixed-pixel row at the head of the
            // list, so it keeps its full 200px and simply scrolls away instead of overprinting.
            if (!_railPinned)
                MountRail(MakeRowHost("RailRow", _railBandPx), forceRebuild: true);

            var summary = FindSummary(channel);
            // The selected structure and its action lead the scroll content, keeping the primary
            // task above the queue history on a phone viewport.
            AddSectionHeader(BrowseHeading(_vm.Tab));
            // ⛔ WO-1422 - THE PAGED BROWSE PATH IS RETIRED AND IS NOT COMING BACK.
            // Every one of the four Manage destinations now branches above into its own rail +
            // card workspace, so the page-count sentence, its two page doors, AddBrowseRow and
            // BuildBrowseRowContent had no reachable call site left. They are DELETED rather than
            // parked: a private method with zero callers is dead code that LOOKS like a shipped
            // feature - the exact failure ManageQueueDrawerRegression's [rows-have-a-home] case was
            // written to catch for the drawer's own row builder. The VM's BrowseRows STAYS (three
            // suites drive it, and the Troops "Saved army compositions" row still reads it); only
            // the PANEL stopped painting it.
            // ⚠ Nothing in THIS method may name the drawer's row builder, not even in a comment:
            // [rows-not-inline] bans that token anywhere inside RenderList's body.
            if (_vm.BrowseRows.Count == 0)
                AddNoteRow(BrowseEmptyState(_vm.Tab));

            // ⛔ NO QUEUE ROWS HERE, AND THE VERBS ARE NOT MISSING — THEY ARE IN THE DRAWER.
            // Queue inspection and queue actions live in the explicit header Queue drawer
            // (RenderQueueDrawer). Repeating the same jobs inline beneath the upgrade catalogue
            // made the browse destination overflow at landscape height and contradicted the
            // approved Manage hierarchy: upgrades are the primary task; queue management is
            // opt-in. WO-1368: this sentence was true when it was written and the drawer it
            // pointed at contained NO ROWS for three days, so the money path (Finish Now / Ad)
            // could not be reached at all. The drawer now builds the rows; if you are here
            // because a verb is missing, read RenderQueueDrawer, do not re-add rows to this list
            // (ManageQueueDrawerRegression fails the build if you do).

            if (!string.IsNullOrEmpty(_vm.RepairOfferText))
                AddActionNoteRow(_vm.RepairOfferText, "Repair", () => { _vm.RepairAll(); FlushNotice(); });

            // WO-1058 — TAIL SPACER. The list is a scroller inside a RectMask2D whose floor sits
            // just above the shared Close, so at max scroll the last row used to end MID-GLYPH on
            // that mask edge and read as "the content runs under Close" (owner frame 2026-08-22).
            // An empty tail row lets the last real row clear the mask completely. It costs the
            // panel NO height — the fixed-band budget in BuildChrome is untouched.
            MakeRowHost("ListTailSpacer", ListTailPx);

            // §12 — the geometry is PROVEN by a capture, not by an eyeball. One line naming the
            // invariant this ticket exists to hold, so a screenshot can be checked against numbers.
            FlowTrace.Step("Manage", string.Format(
                "row bands: PRIMARY x{0:F3}-{1:F3} (never destructive: Upgrade / Finish Now / Expand / Repair) | " +
                "dead gap {2:F3}-{3:F3} | secondary cluster {4:F3}-{5:F3} (Ad, Cancel, Move up, even split) | " +
                "text column x<=0.44. queueRows={6} browseRows={7}",
                PrimaryX0, PrimaryX1, ClusterX1, PrimaryX0, ClusterX0, ClusterX1,
                _vm.QueueRows.Count, _vm.BrowseRows.Count));
        }

        private string FindSummary(ChannelId channel)
        {
            for (int i = 0; i < _vm.Channels.Count; i++)
                if (_vm.Channels[i].Channel == channel) return _vm.Channels[i].Describe();
            return BuildTimerService.ChannelWord(channel);
        }

        // =====================================================================
        //  WO-1422 - THE DEFENCE AND RESEARCH DESTINATIONS
        // ---------------------------------------------------------------------
        // ⛔ PLACEMENT IS LOAD-BEARING. Both live AFTER FindSummary and BEFORE
        // RenderBuildingsDestination. ManageQueueDrawerRegression.cs:90 and
        // ManageBuildingsCardRegression.cs:158 both scope their bans to
        // Body(panel, "private void RenderList()", "private string FindSummary"); anything defined
        // inside that window enters the ban. ManageBuildingsCardRegression.cs:141 scopes the
        // Buildings destination to Body("RenderBuildingsDestination(", "AddBuildingWorkspaceRow("),
        // so nothing may be inserted between those two either. This gap is the one seat that is
        // outside every window. Do not move these methods.
        // =====================================================================

        /// <summary>
        /// WO-1422 ruling 3.1/3.3: the Defence destination is the Buildings shape - one rail row
        /// per placed upgradable TYPE (never per instance: wall_wood alone would make the rail
        /// unbounded), one selected card, and the SHARED Builder band, named BUILDING NOW.
        /// Defence and Buildings ride ONE queue; a second name for it would be duplicated state.
        /// </summary>
        private void RenderDefenseDestination(ChannelId channel)
        {
            if (_vm == null) return;
            if (_vm.DefenseChoices.Count == 0)
            {
                // The no-fixture Defence capture (RunManageDefenseCaptureHeadless) renders exactly
                // this path, and two suites pin the "Build defense" door off it.
                AddNoteRow(BrowseEmptyState(ManageTab.Defense));
                AddActionNoteRow("Need another tower?", "Build defense", OpenDefenseBuilder);
                MakeRowHost("ListTailSpacer", ListTailPx);
                FlowTrace.Warn("Manage", "defense destination has no upgradable placed types - " +
                    "empty state + the Build defense door are the whole screen");
                return;
            }

            DefenseChoiceVM selected = null;
            for (int i = 0; i < _vm.DefenseChoices.Count; i++)
                if (string.Equals(_vm.DefenseChoices[i].Id, _selectedDefenseId, StringComparison.OrdinalIgnoreCase))
                { selected = _vm.DefenseChoices[i]; break; }
            if (selected == null)
            {
                // DefenseChoiceVM carries no Locked flag (lane A contract); "has something to do"
                // is Activate != null, which is null only at Max.
                for (int i = 0; i < _vm.DefenseChoices.Count; i++)
                    if (_vm.DefenseChoices[i] != null && _vm.DefenseChoices[i].Activate != null)
                    { selected = _vm.DefenseChoices[i]; break; }
                if (selected == null) selected = _vm.DefenseChoices[0];
                _selectedDefenseId = selected.Id;
            }

            AddDefenseWorkspaceRow(selected);
            AddBuildingNowBand();   // ruling 3.3 - the ONE Builder rail, verbatim
            AddActionNoteRow("Need another tower?", "Build defense", OpenDefenseBuilder);
            if (!string.IsNullOrEmpty(_vm.RepairOfferText))
                AddActionNoteRow(_vm.RepairOfferText, "Repair", () => { _vm.RepairAll(); FlushNotice(); });
            MakeRowHost("ListTailSpacer", ListTailPx);
            FlowTrace.Step("Manage", "defense destination: rail=" + _vm.DefenseChoices.Count +
                " selected=" + selected.Id + " placed=" + selected.PlacedCount +
                " level=" + selected.Level + " jobs=" + _vm.QueueRows.Count +
                " (channel=" + channel + ", shared with Buildings)");
        }

        /// <summary>
        /// WO-1422 ruling 3.6/3.7: the Research destination is the Buildings shape - one rail row
        /// per authored PERK (never per building: a per-building card would need three or four
        /// verbs in one CTA band and no card grammar here supports that), one selected card
        /// showing the whole tree including Researched and Researching, and its own RESEARCHING NOW
        /// band. Research has no LEVEL, so the card's level slot carries the tier requirement.
        /// </summary>
        private void RenderResearchDestination(ChannelId channel)
        {
            if (_vm == null) return;
            if (_vm.ResearchChoices.Count == 0)
            {
                AddNoteRow(BrowseEmptyState(ManageTab.Research));
                MakeRowHost("ListTailSpacer", ListTailPx);
                FlowTrace.Warn("Manage", "research destination has no perk choices - no owned " +
                    "building authors a tier ladder in this town");
                return;
            }

            ResearchChoiceVM selected = null;
            for (int i = 0; i < _vm.ResearchChoices.Count; i++)
                if (string.Equals(ResearchKeyOf(_vm.ResearchChoices[i]), _selectedResearchKey, StringComparison.OrdinalIgnoreCase))
                { selected = _vm.ResearchChoices[i]; break; }
            if (selected == null)
            {
                for (int i = 0; i < _vm.ResearchChoices.Count; i++)
                    if (_vm.ResearchChoices[i] != null && !_vm.ResearchChoices[i].Locked)
                    { selected = _vm.ResearchChoices[i]; break; }
                if (selected == null) selected = _vm.ResearchChoices[0];
                _selectedResearchKey = ResearchKeyOf(selected);
            }

            AddResearchWorkspaceRow(selected);
            AddResearchNowBand();
            MakeRowHost("ListTailSpacer", ListTailPx);
            FlowTrace.Step("Manage", "research destination: rail=" + _vm.ResearchChoices.Count +
                " selected=" + ResearchKeyOf(selected) + " state=" + (selected.StateWord ?? "<null>") +
                " jobs=" + _vm.QueueRows.Count + " (channel=" + channel + ")");
        }

        /// <summary>
        /// The Research selection key. ⛔ SHAPE IS A CROSS-LANE CONTRACT: "&lt;buildingId&gt;:&lt;perkId&gt;",
        /// which is BuildingPerkService.Key's shape (BuildingPerkService.cs:68) and the shape the
        /// capture fixture writes into _selectedResearchKey. Compose it in exactly one place.
        /// </summary>
        private static string ResearchKeyOf(ResearchChoiceVM choice) =>
            choice == null ? null : (choice.BuildingId ?? "") + ":" + (choice.PerkId ?? "");

        private void RenderBuildingsDestination(ChannelId channel)
        {
            if (_vm == null) return;
            if (_vm.BuildingChoices.Count == 0)
            {
                AddNoteRow("No placed buildings are available.");
                AddActionNoteRow("Need another town structure?", "Open build", OpenTownBuilder);
                MakeRowHost("ListTailSpacer", ListTailPx);
                FlowTrace.Warn("Manage", "buildings destination has no placed building choices");
                return;
            }

            BuildingChoiceVM selected = null;
            for (int i = 0; i < _vm.BuildingChoices.Count; i++)
                if (string.Equals(_vm.BuildingChoices[i].Id, _selectedBuildingId, StringComparison.OrdinalIgnoreCase))
                { selected = _vm.BuildingChoices[i]; break; }
            if (selected == null)
            {
                for (int i = 0; i < _vm.BuildingChoices.Count; i++)
                    if (!_vm.BuildingChoices[i].Locked) { selected = _vm.BuildingChoices[i]; break; }
                if (selected == null) selected = _vm.BuildingChoices[0];
                _selectedBuildingId = selected.Id;
            }

            AddBuildingWorkspaceRow(selected);
            AddBuildingNowBand();
            AddActionNoteRow("Need another town structure?", "Open build", OpenTownBuilder);
            MakeRowHost("ListTailSpacer", ListTailPx);
            FlowTrace.Step("Manage", "buildings destination: rail=" + _vm.BuildingChoices.Count +
                " selected=" + selected.Id + " jobs=" + _vm.QueueRows.Count);
        }

        private void AddBuildingWorkspaceRow(BuildingChoiceVM selected)
        {
            var workspace = MakeRowHost("BuildingSplitWorkspace", TroopWorkspacePx);
            var railZone = MakeZone(workspace, "BuildingSelectorRail", new Vector2(0f, 0f), new Vector2(0.26f, 1f));
            var railPlate = ElarionUiKit.AddImage(railZone, "RailPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            railPlate.GetComponent<Image>().raycastTarget = false;
            var railScroll = ElarionUiKit.MakeScrollZone(railZone, spacing: 6f, padding: 8);
            if (railScroll == null || railScroll.content == null)
                FlowTrace.Fail("Manage", "building rail MakeScrollZone returned no content - the rail has no build site.");
            else
            {
                int selectedIndex = 0;
                _rowParent = railScroll.content;
                try
                {
                    for (int i = 0; i < _vm.BuildingChoices.Count; i++)
                    {
                        var choice = _vm.BuildingChoices[i];
                        if (choice == null) continue;
                        bool isSelected = string.Equals(choice.Id, selected.Id, StringComparison.OrdinalIgnoreCase);
                        if (isSelected) selectedIndex = i;
                        Guard.Try("Manage", "building rail row " + choice.Id, () => BuildBuildingRailRow(choice, isSelected));
                    }
                    // The tail gives every selected row enough scroll range to align its TOP edge
                    // with the viewport. Without it the last row stops mid-pitch and the row above
                    // is left half-visible at the top of this short, two-row rail.
                    MakeRowHost("BuildingRailTailSpacer", TroopWorkspacePx - TroopRailRowPx);
                }
                finally { _rowParent = null; }

                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(railScroll.content);
                if (railScroll.scroll != null)
                {
                    var viewport = railScroll.scroll.viewport;
                    float viewportPx = viewport != null ? viewport.rect.height : TroopWorkspacePx;
                    float maxScrollPx = Mathf.Max(0f, railScroll.content.rect.height - viewportPx);
                    float selectedTopPx = Mathf.Min(maxScrollPx, selectedIndex * (TroopRailRowPx + 6f));
                    railScroll.scroll.StopMovement();
                    railScroll.scroll.verticalNormalizedPosition = maxScrollPx > 0.5f
                        ? 1f - selectedTopPx / maxScrollPx
                        : 1f;
                    FlowTrace.Step("Manage", "building rail aligned row=" + selectedIndex +
                        " topPx=" + selectedTopPx.ToString("0") + " maxPx=" + maxScrollPx.ToString("0"));
                }
            }

            var card = MakeZone(workspace, "BuildingSelectedCard", new Vector2(0.275f, 0f), new Vector2(1f, 1f));
            BuildBuildingCard(card, selected);
        }

        private void BuildBuildingRailRow(BuildingChoiceVM choice, bool isSelected)
        {
            var row = MakeRowHost("BuildingChoiceRow_" + choice.Id, TroopRailRowPx);
            var faceGo = ElarionUiKit.AddImage(row, "BuildingChoice_" + choice.Id, Vector2.zero, Vector2.one,
                isSelected ? new Color(0.24f, 0.18f, 0.08f, 0.90f) : new Color(0f, 0f, 0f, 0.28f), rounded: false);
            var face = faceGo.GetComponent<Image>();
            face.raycastTarget = true;
            var button = faceGo.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() =>
            {
                _selectedBuildingId = choice.Id;
                FlowTrace.Step("Manage", "building rail selected=" + choice.Id);
                Render();
            });
            if (isSelected)
            {
                var outline = faceGo.AddComponent<Outline>();
                outline.effectColor = ElarionUi.Gold;
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = false;
            }

            var medallion = MakeZone(faceGo.transform, "Medallion", new Vector2(0.03f, 0.08f), new Vector2(0.27f, 0.92f));
            var portrait = ElarionUiKit.Portrait(medallion, BuildingSprite(choice), active: isSelected);
            if (choice.Locked && portrait?.image != null) portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (choice.Locked) BuildLockBadge(medallion);

            var name = ElarionUiKit.Label(faceGo.transform, ManageScreenVM.Ascii(choice.Name ?? ""), 0.52f, 0.96f,
                choice.Locked ? ElarionUi.ParchmentDim : ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.30f, 0.84f, bold: true);
            ElarionUiKit.FitSingleLine(name, 26f, 38f);
            string railState = choice.Locked
                ? ManageScreenVM.Ascii(choice.LockText ?? "Locked")
                : "Level " + choice.Level +
                  (string.Equals(choice.StateWord, "Max", StringComparison.Ordinal) ? " . Max" :
                   string.Equals(choice.StateWord, "Building", StringComparison.Ordinal) ? " . Building" : "");
            var sub = ElarionUiKit.Label(faceGo.transform, railState,
                0.06f, 0.48f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.30f, 0.84f);
            ElarionUiKit.FitSingleLine(sub, 22f, 30f);
            var chevron = ElarionUiKit.Label(faceGo.transform, ">", 0.10f, 0.90f,
                isSelected ? ElarionUi.Gold : ElarionUi.ParchmentDim,
                (int)ElarionUi.FontBody, TextAlignmentOptions.Center, 0.84f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(chevron, 30f, 50f);
            ElarionUiKit.ClampMinTouch(button);
        }

        private static Sprite BuildingSprite(BuildingChoiceVM choice)
        {
            if (choice == null) return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");

            // Dedicated namespace: unlike legacy Portraits/<id>, every file here is guaranteed
            // to depict the STRUCTURE. Creative can drop a base sheet or optional tier sheet
            // without replacing an NPC portrait or requiring another code edit.
            Sprite art = LoadManageBuildingSprite(choice.Id, choice.Level);
            var entry = string.IsNullOrEmpty(choice.CatalogEntryId)
                ? null
                : DeNelle.Core.Catalog.CatalogRegistry.Get(choice.CatalogEntryId);

            // Keep the shared Build palette as the normal fallback, except for the six measured
            // ids whose current palette route resolves a person rather than a building.
            if (art == null && entry != null && !ManageBuildingPortraitGaps.Contains(choice.Id))
                art = DeNelle.Village.BuildPaletteUI.ResolveEntryArtPublic(entry);
            if (art == null && entry != null)
                art = DeNelle.Core.UI.ConceptIconResolver.ResolveAny(entry.id, entry.type.ToString());
            if (art == null)
                FlowTrace.Warn("Manage", "building art unresolved id=" + (choice.Id ?? "<null>") +
                    " catalogEntryId=" + (choice.CatalogEntryId ?? "<null>") +
                    " - add Portraits/Buildings/<id>; neutral hammer used");
            return art ?? RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");
        }

        private BuildingChoiceVM FindBuildingChoice(string buildingId)
        {
            if (_vm == null || string.IsNullOrEmpty(buildingId)) return null;
            for (int i = 0; i < _vm.BuildingChoices.Count; i++)
            {
                var choice = _vm.BuildingChoices[i];
                if (choice != null && string.Equals(choice.Id, buildingId, StringComparison.OrdinalIgnoreCase))
                    return choice;
            }
            return null;
        }

        private static Sprite LoadManageBuildingSprite(string buildingId, int level)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;
            string root = "Portraits/Buildings/" + buildingId;
            string tierKey = level > 1 ? root + "-" + level : null;
            Sprite art = LoadManageBuildingSpriteAt(tierKey);
            return art ?? LoadManageBuildingSpriteAt(root);
        }

        /// <summary>
        /// WO-2017 - INTERNAL, not private: <see cref="HeartPanel"/> loads the Heart's own portrait
        /// through this exact path so the Heart cannot become the one art route with its own loader,
        /// its own Texture2D fallback and its own cache-miss behaviour.
        ///
        /// <para>⛔ WO-2001 - THIS IS NOW A ONE-LINE FORWARDER, NOT AN IMPLEMENTATION. The body it
        /// used to hold was duplicated verbatim in <see cref="DeNelle.Core.Manage.ManageArt.LoadSprite"/>
        /// (see that file's header: it could not call this one, because `internal` does not cross
        /// the DeNelle.Core / DeNelle.Village assembly line). Two copies of one behaviour is the
        /// failure CLAUDE.md 2 / 5 / 16 records three times over, so the Core copy WINS - it is
        /// reachable from both assemblies, it caches MISSES as well as hits, and it announces a
        /// miss once per key through FlowTrace instead of silently returning null.</para>
        ///
        /// <para>Kept as a method rather than deleted so the Village callers (and
        /// <see cref="HeartPanel"/>) keep one name for the route; the sprite name suffix
        /// "_manage_building" moved to ManageArt's "_manage" with it.</para>
        /// </summary>
        internal static Sprite LoadManageBuildingSpriteAt(string resourceKey)
            => DeNelle.Core.Manage.ManageArt.LoadSprite(resourceKey);

        private void BuildBuildingCard(RectTransform card, BuildingChoiceVM selected)
        {
            var plate = ElarionUiKit.AddImage(card, "CardPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            plate.GetComponent<Image>().raycastTarget = false;
            ElarionUiKit.GoldPerimeter(card);

            var medallion = MakeZone(card, "BuildingPortrait", new Vector2(0.02f, 0.59f), new Vector2(0.16f, 0.99f));
            var portrait = ElarionUiKit.Portrait(medallion, BuildingSprite(selected), active: true);
            if (selected.Locked && portrait?.image != null) portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (selected.Locked) BuildLockBadge(medallion);

            var name = ElarionUiKit.Label(card, ManageScreenVM.Ascii((selected.Name ?? "").ToUpperInvariant()),
                0.835f, 1f, ElarionUi.Gold, (int)ElarionUi.FontTitle,
                TextAlignmentOptions.Left, 0.19f, 0.74f, bold: true);
            ElarionUiKit.FitSingleLine(name, 30f, 48f);
            var level = ElarionUiKit.Label(card, "LEVEL " + selected.Level, 0.835f, 1f, ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Right, 0.75f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(level, 26f, 36f);

            var desc = ElarionUiKit.Label(card, ManageScreenVM.Ascii(selected.Description ?? ""), 0.70f, 0.83f,
                ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.19f, 0.72f);
            ElarionUiKit.FitSingleLine(desc, ElarionUiKit.FontHardFloor, 30f);
            var badge = ElarionUiKit.AddImage(card, "BuildingStateBadge", new Vector2(0.74f, 0.70f),
                new Vector2(0.98f, 0.83f), new Color(0.12f, 0.25f, 0.08f, 0.82f), rounded: false);
            badge.GetComponent<Image>().raycastTarget = false;
            var state = ElarionUiKit.Label(badge.transform, ManageScreenVM.Ascii(selected.StateWord ?? ""), 0f, 1f,
                ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.02f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(state, 20f, 28f);

            if (string.Equals(selected.StateWord, "Max", StringComparison.Ordinal)) return;

            // WO-1423 - a LOCKED card lifts its cost/fact row to 0.565-0.70 (35.1px at
            // TroopWorkspacePx = 260) to free 0.45-0.56 (28.6px) for the lock SENTENCE, which is the
            // exact band arithmetic BuildResearchCard already proved on device. Both clear the ~24px
            // floor below which TMP culls a whole line.
            float factY0 = selected.Locked ? 0.565f : 0.54f;
            float factY1 = selected.Locked ? 0.70f : 0.695f;
            ElarionUiKit.CostRow(card, selected.UpgradeCostParts, new Vector2(0.02f, factY0),
                new Vector2(0.72f, factY1), ElarionUi.Parchment, prefix: "Upgrade:",
                fontPx: (int)ElarionUi.FontMicro);
            string readiness = selected.UpgradeReady ? "Ready" : "Short";
            string factText = string.IsNullOrEmpty(selected.UpgradeTimeText)
                ? readiness : selected.UpgradeTimeText + " . " + readiness;
            var fact = ElarionUiKit.Label(card, factText, factY0, factY1, ElarionUi.Parchment,
                (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, 0.73f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(fact, 20f, 28f);

            // The "After upgrade" band (0.445-0.535) is the band the lock sentence takes over, so a
            // locked card paints one or the other, never both stacked on the same pixels.
            if (!selected.Locked)
            {
                var benefit = ElarionUiKit.Label(card,
                    string.IsNullOrEmpty(selected.AfterUpgradeText) ? "" : "After upgrade: " + ManageScreenVM.Ascii(selected.AfterUpgradeText),
                    0.445f, 0.535f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Left, 0.02f, 0.98f);
                ElarionUiKit.FitSingleLine(benefit, ElarionUiKit.FontHardFloor, 26f);
            }

            // Locked and in-progress choices still explain what the next tier costs and buys.
            // Only their CTA face changes; Max is the sole state without a next-tier fact row.
            if (selected.Locked)
            {
                // ⛔ WO-1423 - THE DEAD END THE OWNER HIT ("some items are locked till village level 1,
                // which there is no way to trigger"). This branch used to paint the DISABLED-face
                // helper (the same one the "Building" state still uses below) wearing
                // "UNLOCKS AT VILLAGE LEVEL"+n, and RETURN before the door below was ever built - so
                // the ONE card that names the gate was the ONE card with no route to the control that
                // opens it. (The helper's NAME is deliberately not written in this branch: the
                // WO-1423 oracle fails the branch if that call ever comes back.) A named lock with
                // no door is worse than no lock at all: it teaches the player the game is stuck.
                //
                // Same treatment the locked RESEARCH card got (BuildResearchCard, this file): the
                // requirement is a BODY TEXT LINE - prose belongs in the body, a sentence never fits a
                // button face - and the card carries exactly ONE FULL-WIDTH LIVE door via
                // selected.ViewDetails. The door is full width REGARDLESS of DoorLabel: a ladder that
                // authors no perks (the Farm) can still be Heart-gated, and this door is the GATE
                // door, not the PERKS door.
                //
                // ⚠ CORRECTED WO-2003 (2026-09-06): this said the door led to "OpenUpgradePanel, whose
                // action band renders 'Raise Village Tier N'". Both halves have moved. ViewDetails is
                // authored BY STATE in ManageScreenVM.BuildBuildingChoices - a LOCKED card's
                // ViewDetails now opens PanelId.Heart (the Heart surface itself), an unlocked card's
                // still opens the building's upgrade page - and that band's face is now
                // "Raise Heart Level to N" (BuildingUpgradePanelMvvm.cs:425). The View is unchanged
                // and still just invokes selected.ViewDetails; only the destination the VM authors
                // moved, which is the point of the dumb-UI rule.
                var lockLine = ElarionUiKit.Label(card,
                    ManageScreenVM.Ascii(string.IsNullOrEmpty(selected.LockReason) ? "Locked." : selected.LockReason),
                    0.45f, 0.56f, ElarionUi.Parchment, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Left, 0.02f, 0.98f, bold: true);
                if (lockLine != null)
                {
                    lockLine.gameObject.name = "BuildingLockReason";
                    ElarionUiKit.FitSingleLine(lockLine, ElarionUiKit.FontHardFloor, 28f);
                }
                var lockedDoor = ElarionUiKit.BuildObsidianButton(card,
                    ManageScreenVM.Ascii(string.IsNullOrEmpty(selected.LockCtaLabel) ? "OPEN" : selected.LockCtaLabel),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.02f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1),
                    () => Guard.Try("Manage", "open building village gate", () => selected.ViewDetails?.Invoke()));
                if (lockedDoor != null)
                {
                    lockedDoor.gameObject.name = "BuildingCta_Locked";
                    lockedDoor.interactable = selected.ViewDetails != null;
                    MedievalUiSkin.ApplyButton(lockedDoor, false);
                }
                ElarionUiKit.ClampMinTouch(lockedDoor);
                return;
            }
            if (string.Equals(selected.StateWord, "Building", StringComparison.Ordinal))
            {
                BuildDisabledBuildingFace(card, "BuildingCta_Building", "BUILDING");
                return;
            }

            // WO-1422 ruling 3.5 (owner: "Keep one door, but name what's behind it"): the second
            // door is no longer the developer word VIEW DETAILS. It carries the VM's DoorLabel -
            // "PERKS" on a ladder that authors perks - and is HIDDEN when DoorLabel is null (the
            // Farm authors zero perks, so the Farm card is ONE full-width CTA; that is the
            // feature). ⛔ The GameObject NAME stays BuildingCta_Details - ManageBuildingsCardRegression
            // pins it, and renaming it breaks the pin for no player-visible gain.
            bool hasDoor = !string.IsNullOrEmpty(selected.DoorLabel);
            var upgrade = ElarionUiKit.BuildObsidianButton(card, "UPGRADE TO L" + selected.NextTier,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                selected.UpgradeReady ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, TroopCtaY0), new Vector2(hasDoor ? 0.48f : 0.98f, TroopCtaY1),
                () => Guard.Try("Manage", "upgrade building", () => selected.Activate?.Invoke()));
            if (upgrade != null)
            {
                upgrade.gameObject.name = "BuildingCta_Upgrade";
                upgrade.interactable = selected.UpgradeReady;
                MedievalUiSkin.ApplyButton(upgrade, true);
            }
            ElarionUiKit.ClampMinTouch(upgrade);

            if (!hasDoor)
            {
                FlowTrace.Step("Manage", "building card " + selected.Id +
                    " has no second door (DoorLabel null) - UPGRADE is full width");
                return;
            }

            var details = ElarionUiKit.BuildObsidianButton(card,
                ManageScreenVM.Ascii(selected.DoorLabel.ToUpperInvariant()),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.52f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1),
                () => Guard.Try("Manage", "open building door " + selected.DoorLabel, () => selected.ViewDetails?.Invoke()));
            if (details != null) details.gameObject.name = "BuildingCta_Details";
            ElarionUiKit.ClampMinTouch(details);
        }

        private static void BuildDisabledBuildingFace(RectTransform card, string objectName, string text)
        {
            var face = ElarionUiKit.BuildObsidianButton(card, text,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1), null);
            if (face == null) return;
            face.gameObject.name = objectName;
            face.interactable = false;
            MedievalUiSkin.ApplyButton(face, false);
            ElarionUiKit.ClampMinTouch(face);
        }

        private void AddBuildingNowBand()
        {
            var band = MakeRowHost("BuildingNowBand", TrainingNowBandPx);
            var bandPlate = ElarionUiKit.AddImage(band, "BandPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            bandPlate.GetComponent<Image>().raycastTarget = false;
            int hiddenJobs = Mathf.Max(0, _vm.QueueRows.Count - 1);
            var title = ElarionUiKit.Label(band, "BUILDING NOW", hiddenJobs > 0 ? 0.53f : 0.15f, 0.85f, ElarionUi.Gold,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.165f, bold: true);
            ElarionUiKit.FitSingleLine(title, 22f, 32f);
            if (hiddenJobs > 0)
            {
                var more = ElarionUiKit.Label(band, "+" + hiddenJobs + " more", 0.12f, 0.48f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Left, 0.01f, 0.165f, bold: true);
                ElarionUiKit.FitSingleLine(more, ElarionUiKit.FontHardFloor, 28f);
            }
            var open = ElarionUiKit.BuildObsidianButton(band, "OPEN QUEUE",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(PrimaryX0, BandCtrlY0), new Vector2(PrimaryX1, BandCtrlY1), ToggleQueueDrawer);
            if (open != null) open.gameObject.name = "BuildingOpenQueue";
            ElarionUiKit.ClampMinTouch(open);

            if (_vm.QueueRows.Count == 0)
            {
                var empty = ElarionUiKit.Label(band, "No builder at work", 0.15f, 0.85f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    0.18f, ClusterX1 + 0.01f);
                ElarionUiKit.FitSingleLine(empty, 24f, 34f);
                return;
            }

            var first = _vm.QueueRows[0];
            if (first != null)
            {
                // WO-1422 POLISH (MEASURED 2026-09-06, ManageDefense_2670x1200.png): this ONE band
                // serves Buildings AND Defence (ruling 3.3), and on Defence it read "Tower Ground
                // Archer..." beside an art-less medallion. A placed-structure upgrade is not a
                // BuildingTierCatalog row, so QueueRowVM.BuildingId is empty and FindBuildingChoice
                // can never match it. The Defence tab therefore resolves the job against
                // DefenseChoices - exactly the way ResearchJobSprite matches ResearchChoices, which
                // is the band that already reads "Warding Runes" with its real perk icon.
                // ⛔ The Buildings expression below stays VERBATIM (ManageBuildingsCardRegression
                // pins `BuildingSprite(FindBuildingChoice(first.BuildingId))` as a literal).
                DefenseChoiceVM defenseJob = _vm.Tab == ManageTab.Defense ? FindDefenseChoiceForJob(first) : null;
                Sprite jobArt = defenseJob != null
                    ? DefenseSprite(defenseJob)
                    : BuildingSprite(FindBuildingChoice(first.BuildingId));
                string jobLabel = defenseJob != null ? ManageScreenVM.Ascii(defenseJob.Name ?? "") : null;
                // §12 — the next capture PROVES which arm fired instead of leaving us to infer it
                // from pixels. A resolve used to be silent, so "resolved and painted" was
                // indistinguishable from "resolved but Name was blank and the raw key came back".
                //   tab=Defense resolved=<id> art=yes label='Archer Tower'  -> fixed.
                //   tab=Defense resolved=<none> art=no label=<fallback:...> -> still broken, and
                //     FindDefenseChoiceForJob's Warn above names the id it could not place.
                //   tab=Defense resolved=<id> label=<fallback:...>          -> resolved but the
                //     choice's Name was blank; the resolver is fine, the projection is not.
                //   tab=Buildings ...                                       -> the Defence branch
                //     was NOT reached, i.e. the tab check is the suspect, not the resolver.
                // Printed UNCONDITIONALLY so that last case is observable rather than merely
                // described - a tab-gated trace can never prove the tab gate.
                FlowTrace.Step("Manage", "BUILDING NOW band: tab=" + _vm.Tab + " jobId='" +
                        (first.JobId ?? "<null>") + "' buildingId='" + (first.BuildingId ?? "") +
                        "' resolved=" + (defenseJob != null ? defenseJob.Id : "<none>") +
                        " art=" + (jobArt != null ? "yes" : "no") +
                        " label=" + (!string.IsNullOrEmpty(jobLabel)
                            ? "'" + jobLabel + "'"
                            : "<fallback:" + ManageScreenVM.Ascii(first.Label ?? "") + ">"));
                Guard.Try("Manage", "building now job 1", () => BuildTroopTrainingNowJob(band, 1, first,
                    0.175f, 0.205f, 0.21f, 0.27f, 0.28f, 0.45f, 0.46f, 0.60f, 0.61f, ClusterX1 + 0.01f,
                    jobArt, jobLabel));
            }
            if (hiddenJobs > 0)
                FlowTrace.Step("Manage", "building now capped inside band: painted=1 hidden=" + hiddenJobs);
        }

        private void RenderTroopsDestination(ChannelId channel)
        {
            if (_vm == null) return;
            if (_vm.TroopChoices.Count == 0)
            {
                AddSectionHeader("TRAIN & UPGRADE TROOPS");
                AddNoteRow("No troop definitions are available.");
                return;
            }

            TroopChoiceVM selected = null;
            for (int i = 0; i < _vm.TroopChoices.Count; i++)
                if (string.Equals(_vm.TroopChoices[i].Id, _selectedTroopId, StringComparison.OrdinalIgnoreCase))
                { selected = _vm.TroopChoices[i]; break; }
            if (selected == null)
            {
                for (int i = 0; i < _vm.TroopChoices.Count; i++)
                    if (_vm.TroopChoices[i].Unlocked) { selected = _vm.TroopChoices[i]; break; }
                if (selected == null) selected = _vm.TroopChoices[0];
                _selectedTroopId = selected.Id;
            }

            // WO-1382 (owner ruling 2026-09-04 22:50): rail + card in ONE reserved row, then the
            // TRAINING NOW band (its own rows, built by AddTroopTrainingNowBand - never by
            // AddQueueRow, which is drawer-only by ManageQueueDrawerRegression's pin), then the
            // one Saved-armies row. Four verbs on the whole screen: BACK, TRAIN 1 <NAME>,
            // UPGRADE TO L<n>, OPEN QUEUE / OPEN ARMIES. Nothing here is a mode switch.
            AddTroopWorkspaceRow(selected);
            AddTroopTrainingNowBand();

            for (int i = 0; i < _vm.BrowseRows.Count; i++)
            {
                var row = _vm.BrowseRows[i];
                if (row == null || !string.Equals(row.ActionText, "Open", StringComparison.OrdinalIgnoreCase)) continue;
                AddActionNoteRow("Saved army compositions", "Open armies", row.Activate);
                break;
            }

            // §12 — the geometry and the verb count, PROVEN off a capture rather than eyeballed.
            FlowTrace.Step("Manage", string.Format(
                "troops workspace: {0} troop(s) in the rail, selected={1} (unlocked={2} trainReady={3} " +
                "upgradeReady={4} hasNext={5}), TRAINING NOW rows={6}. Bands(px): workspace={7:0} " +
                "railRow={8:0} band={9:0} extraRow={10:0}; above-the-fold = 10 + {7:0} + 8 + {9:0} = {11:0}. " +
                "Verbs on screen: TRAIN 1 / UPGRADE TO L / OPEN QUEUE.",
                _vm.TroopChoices.Count, selected.Id, selected.Unlocked, selected.TrainReady,
                selected.UpgradeReady, selected.HasNextLevel, _vm.QueueRows.Count,
                // WO-1541: the ARMY workspace is TroopCardPx(346) now, not TroopWorkspacePx(260) -
                // the card grew to seat the 112px raid door row. The fold arithmetic is traced off
                // the height that actually ships, or the measurement describes a card nobody paints.
                TroopCardPx, TroopRailRowPx, TrainingNowBandPx, TrainingNowRowPx,
                10f + TroopCardPx + 8f + TrainingNowBandPx));
        }

        // =====================================================================
        //  WO-1382 — THE TROOPS WORKSPACE: rail (left, scrolls) + selected-troop card (right)
        // ---------------------------------------------------------------------
        // ⚠ WHY THE ROW CARRIES NO ApplyRowSurface. The RCA on WO-1382 proved the owner's
        // "box around train": frames/content-panel (1672x941, spriteBorder 96) carries ~90px of
        // transparent margin above its gold line and ~140px below, so on any TALL row the 9-slice
        // draws its frame ~100px INSIDE the row's top and bottom edges and every child outside
        // that band looks like it is floating over a card. The sprite's .meta is shared by every
        // other consumer and is not re-authored here; the rail and the card sit on kit
        // AddImage plates instead, which draw edge-to-edge by construction.
        // =====================================================================

        private void AddTroopWorkspaceRow(TroopChoiceVM selected)
        {
            // WO-1541 owner ruling 2026-09-06 ("raise the card, tappable row"): the ARMY workspace
            // is TroopCardPx(346), not TroopWorkspacePx(260) - the card GROWS to seat the 112px
            // raid door row. Buildings / Defense / Research keep TroopWorkspacePx untouched.
            var workspace = MakeRowHost("TroopSplitWorkspace", TroopCardPx);

            // ── RAIL: one row per troop def, vertical scroll, NO pager arrows (ruling #2) ──
            var railZone = MakeZone(workspace, "TroopSelectorRail", new Vector2(0f, 0f), new Vector2(0.26f, 1f));
            var railPlate = ElarionUiKit.AddImage(railZone, "RailPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            railPlate.GetComponent<Image>().raycastTarget = false;
            var railScroll = ElarionUiKit.MakeScrollZone(railZone, spacing: 6f, padding: 8);
            if (railScroll == null || railScroll.content == null)
            {
                FlowTrace.Fail("Manage", "troop rail MakeScrollZone returned no content - the rail has no build site.");
            }
            else
            {
                int selectedIndex = 0;
                // Redirect the shared row factory at the rail for the length of this build (the
                // drawer's proven idiom) so every rail row is a fixed-pixel MakeRowHost band.
                _rowParent = railScroll.content;
                try
                {
                    for (int i = 0; i < _vm.TroopChoices.Count; i++)
                    {
                        var choice = _vm.TroopChoices[i];
                        if (choice == null) continue;
                        bool isSelected = string.Equals(choice.Id, selected.Id, StringComparison.OrdinalIgnoreCase);
                        if (isSelected) selectedIndex = i;
                        Guard.Try("Manage", "troop rail row " + choice.Id, () => BuildTroopRailRow(choice, isSelected));
                    }
                }
                finally
                {
                    _rowParent = null;
                }

                // Keep the selected troop in view when the rail is longer than the row: a fresh
                // Render rebuilds the column at the top, and a selection on row 7 of 9 would
                // otherwise open scrolled away from itself.
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(railScroll.content);
                int count = _vm.TroopChoices.Count;
                if (railScroll.scroll != null)
                    railScroll.scroll.verticalNormalizedPosition = count > 1 ? 1f - selectedIndex / (float)(count - 1) : 1f;
            }

            // ── CARD: the selected troop, everything readable without a tap ──
            var card = MakeZone(workspace, "TroopSelectedCard", new Vector2(0.275f, 0f), new Vector2(1f, 1f));
            BuildTroopCard(card, selected);
        }

        /// <summary>
        /// One rail entry: portrait medallion + NAME + "Level n" (or "Locked . T2" + padlock, dimmed).
        /// Selected = gold outline AND a ">" chevron - state by shape and words, never hue alone
        /// (owner colourblind). The row is the tap target (>= MinTouchPx by its 120px band).
        /// </summary>
        private void BuildTroopRailRow(TroopChoiceVM choice, bool isSelected)
        {
            var row = MakeRowHost("TroopChoiceRow_" + choice.Id, TroopRailRowPx);
            // ⚠ The BUTTON's own object carries the TroopChoice_ name. The first capture (2026-09-04)
            // showed a gold plate slicing through every "Level 1": ApplyOperationalMedievalSkin
            // keys its skip-list off button.gameObject.name, the Button lived on a child called
            // "Face", so the bulk pass painted button-normal-empty (Simple, stretched) over the
            // whole row. A FLAT face (rounded: false) is the design; the name fixes the skip.
            var faceGo = ElarionUiKit.AddImage(row, "TroopChoice_" + choice.Id, Vector2.zero, Vector2.one,
                isSelected ? new Color(0.24f, 0.18f, 0.08f, 0.90f) : new Color(0f, 0f, 0f, 0.28f), rounded: false);
            var face = faceGo.GetComponent<Image>();
            face.raycastTarget = true;
            var button = faceGo.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() =>
            {
                _selectedTroopId = choice.Id;
                // WO-1389: the REAL rail tap is a route hop of the post-raid beat ("Pick a troop" ->
                // the UPGRADE face). Raised BEFORE Render so the spotlight's next target (the CTA
                // face, registered by BuildTroopCard) is rebuilt on the same frame it is asked for.
                Guard.Try("Manage", "raise troop-selected signal", () =>
                    DeNelle.Core.Tutorial.TutorialSignals.Raise(
                        DeNelle.Core.Tutorial.TutorialSignals.ManageTroopSelectedPrefix + choice.Id));
                Render();
            });
            // WO-1389: spotlightable by id ("manage.troop_row.<troopId>"; the footman row is the
            // KnownIds contract). Idempotent; every Render re-registers the fresh rect.
            DeNelle.Core.UI.TutorialHighlightRegistry.Register("manage.troop_row." + choice.Id, (RectTransform)faceGo.transform);
            if (isSelected)
            {
                // Frames the WHOLE row (the face fills it). useGraphicAlpha off so the outline is
                // full gold and not dimmed by the face's own alpha.
                var outline = faceGo.AddComponent<Outline>();
                outline.effectColor = ElarionUi.Gold;
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = false;
            }

            // Two clear TEXT bands beside the medallion: name on the upper band (0.52-0.96), the
            // level / lock word on its own lower band (0.06-0.48). Nothing is drawn between them.
            var medallion = MakeZone(faceGo.transform, "Medallion", new Vector2(0.03f, 0.08f), new Vector2(0.27f, 0.92f));
            var portrait = ElarionUiKit.Portrait(medallion, TroopSprite(choice.IconId), active: isSelected);
            if (!choice.Unlocked && portrait?.image != null)
                portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);   // dim + padlock + tier WORD below
            if (!choice.Unlocked) BuildLockBadge(medallion);

            var name = ElarionUiKit.Label(faceGo.transform, ManageScreenVM.Ascii(choice.Name ?? ""), 0.52f, 0.96f,
                choice.Unlocked ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.30f, 0.84f, bold: true);
            ElarionUiKit.FitSingleLine(name, 26f, 38f);
            var sub = ElarionUiKit.Label(faceGo.transform,
                choice.Unlocked ? "Level " + choice.Level : "Locked . T" + choice.LockTier,
                0.06f, 0.48f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.30f, 0.84f);
            ElarionUiKit.FitSingleLine(sub, 22f, 30f);

            if (isSelected)
            {
                var chevron = ElarionUiKit.Label(faceGo.transform, ">", 0.10f, 0.90f, ElarionUi.Gold,
                    (int)ElarionUi.FontBody, TextAlignmentOptions.Center, 0.84f, 0.98f, bold: true);
                ElarionUiKit.FitSingleLine(chevron, 30f, 50f);
            }
            ElarionUiKit.ClampMinTouch(button);
        }

        /// <summary>Troop portrait art by icon id, with the kit's sword icon as the last resort.</summary>
        private static Sprite TroopSprite(string iconId)
        {
            return RpgUiCatalog.Get(RpgUiCatalog.RoleTroop, iconId)
                ?? RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconSword);
        }

        /// <summary>
        /// The SELECTED TROOP card (ruling #3/#4/#8): portrait medallion, NAME at title size with
        /// LEVEL n right-aligned in the same band, the status word, the description, the fact
        /// sentence "Train one: cost . time . state", TWO verb buttons on one line, and the
        /// upgrade fact sentence under them. A locked troop is selectable and shows ONE Gray
        /// non-interactable LOCKED . TIER n face instead ("Don't hide future content").
        /// </summary>
        private void BuildTroopCard(RectTransform card, TroopChoiceVM selected)
        {
            var plate = ElarionUiKit.AddImage(card, "CardPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            plate.GetComponent<Image>().raycastTarget = false;

            // Card bands at TroopWorkspacePx = 260 (see the fold arithmetic on the constants):
            //   name + LEVEL   0.845-1.000 -> 40px   (a 30px title line needs ~35px)
            //   army + badge   0.740-0.840 -> 26px   (WO-1422 polish; 18px CULLED both labels)
            //   desc + status  0.585-0.735 -> 39px   (one line, 24-30)
            //   train fact     0.455-0.575 -> 31px   (one line, 22-26)
            //   CTAs           0.010-0.445 -> 113px  >= MinTouchPx
            // Portrait medallion, top-left, spanning the name and description bands.
            var medallion = MakeZone(card, "TroopPortrait", new Vector2(0.02f, 0.59f), new Vector2(0.16f, 0.99f));
            var portrait = ElarionUiKit.Portrait(medallion, TroopSprite(selected.IconId), active: true);
            if (!selected.Unlocked && portrait?.image != null)
                portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (!selected.Unlocked) BuildLockBadge(medallion);

            // NAME band at title size + LEVEL n right-aligned, always on screen (it is the first
            // band of a row that starts at scroll 0 - the name can no longer scroll off the top).
            // ⚠ WO-1541: the fractions are ArmyNameY0..1 now, DERIVED off TroopCardPx. In pixels the
            // band is unchanged at 40.3px - the card grew, so the same pixels are a smaller fraction.
            var name = ElarionUiKit.Label(card, ManageScreenVM.Ascii((selected.Name ?? "").ToUpperInvariant()),
                ArmyNameY0, 1.0f, ElarionUi.Gold, (int)ElarionUi.FontTitle,
                TextAlignmentOptions.Left, 0.19f, 0.74f, bold: true);
            ElarionUiKit.FitSingleLine(name, 30f, 48f);
            var level = ElarionUiKit.Label(card, "LEVEL " + selected.Level, ArmyNameY0, 1.0f, ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Right, 0.75f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(level, 26f, 36f);
            // WO-1422 ruling 3.10.1: the army summary gives up its right edge (x1 0.98 -> 0.72) so
            // the state-word badge has a seat. ⚠ DELIBERATE DEVIATION from "the same zone Buildings
            // uses" (0.74,0.70)-(0.98,0.83): on the Troops card that rect overprints BOTH the army
            // line (y 0.745-0.815) and the top of the status label (y 0.585-0.735, x 0.71-0.98),
            // because the Buildings card has no army line. The badge takes the army band's right
            // half instead - same x, y clipped to the army band - which overlaps nothing.
            //
            // ⚠ WO-1422 POLISH (MEASURED 2026-09-06, ManageTroops_1920x1080.png): the badge PLATE
            // painted and its WORD did not, and the army summary beside it was missing too. Neither
            // was a text bug - the band was 0.745-0.815 = 0.07 x TroopWorkspacePx(260) = 18.2px, and
            // TMP's Ellipsis overflow CULLS THE WHOLE LINE when the line at fontSizeMin cannot seat
            // in the rect (ElarionUiKitObsidian.cs:3110-3116 states this as the proven cause of the
            // "bare plate" class). FitSingleLine's floor is FontHardFloor=20, whose line is ~23-24px,
            // so nothing in an 18px band can ever render. The band is now 0.74-0.84 = 26px and the
            // NAME band gives up the 0.025 it can spare (it keeps 40.3px, well over the ~35px a
            // 30px title line needs). ⛔ Never re-shrink a text band below ~24px on this card.
            // ⭐ WO-1541 - THE MOST MOTIVATING SENTENCE ON THE CARD STOPS BEING RANKED LAST.
            // It read "Army 8 / 10 - The Forsaken Camp fields 12" at ElarionUi.FontMicro (32) in
            // ParchmentDim - and ElarionUi.cs:115 reserves FontMicro for "hotkey badge, rune
            // strip", the SMALLEST authored role in the kit. The game named the player's enemy and
            // counted their garrison in the type size it uses for a hotkey badge.
            // It is now FontLabel, Parchment, bold - a role/weight/colour rank-up.
            //
            // ⭐⭐ WO-1541 RULING 2, OWNER 2026-09-06 (question tool), VERBATIM:
            //     "RAISE THE CARD, TAPPABLE ROW."
            // The band is no longer 0.74-0.84 (26px). It is ArmyDoorY0..ArmyDoorY1 = a full
            // TroopArmyDoorRowPx(112) = ElarionUiKit.MinTouchPx row, and the whole row is the DOOR
            // to the raid grid. ⛔ NOTHING SHRANK TO PAY FOR IT: the ARMY card grew from 260px to
            // TroopCardPx(346), past its old 256px floor, and every other band keeps the exact
            // pixels it had (see the band ladder on the constants). The ~24px band rule this
            // comment block records holds trivially - the band QUADRUPLED.
            //
            // ⛔ THIS HONOURS WO-1422 RULING 3.10, IT DOES NOT OVERRULE IT. That ruling bans
            // SQUEEZING a door in beside TRAIN + UPGRADE and says a third face "needs a taller
            // card". This is the taller card, and the door is NOT a third CTA face - it is its own
            // row above the CTA band, so both faces keep 113.1px untouched.
            //
            // ⛔ THE ROW IS AUTHORED TO THE TOUCH FLOOR, NOT LEFT TO ClampMinTouch. That was the
            // whole reason the 26px seat was refused: the clamp would have grown the hit rect ~43px
            // each way into the NAME and description bands - an invisible mis-tap surface.
            // ClampMinTouch is still called, as every other face on this card does, but it has
            // nothing left to rescue.
            var armyRow = _vm.TroopArmyDoor != null
                ? ElarionUiKit.BuildObsidianButton(card,
                    ManageScreenVM.Ascii(_vm.TroopArmySummaryText ?? ""),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.02f, ArmyDoorY0), new Vector2(0.98f, ArmyDoorY1),
                    () => { Guard.Try("Manage", "army line raid door", () => _vm.TroopArmyDoor?.Invoke()); })
                : null;
            if (armyRow != null)
            {
                armyRow.gameObject.name = "TroopCta_RaidDoor";
                MedievalUiSkin.ApplyButton(armyRow, true);
                ElarionUiKit.ClampMinTouch(armyRow);
                // ⛔ THE CHEVRON IS A SHAPE, NOT A HUE. The owner is red/green colourblind, so
                // "this row is tappable" can never be carried by colour. ASCII ">" and not a
                // unicode chevron: this project's fonts render non-ASCII as tofu (CLAUDE.md 7).
                var chevron = ElarionUiKit.Label(armyRow.transform, ">", 0f, 1f, ElarionUi.Gold,
                    (int)ElarionUi.FontTitle, TextAlignmentOptions.Right, 0.90f, 0.97f, bold: true);
                ElarionUiKit.FitSingleLine(chevron, 26f, 40f);
                if (chevron != null) chevron.raycastTarget = false;
                FlowTrace.Step("Manage", "army line is a raid door: '" + _vm.TroopArmyDoorLabel +
                    "' seated at " + (TroopArmyDoorRowPx).ToString("0") + "px (MinTouchPx " +
                    ElarionUiKit.MinTouchPx.ToString("0") + ") on a " + TroopCardPx.ToString("0") +
                    "px card - WO-1541 ruling 2, owner 2026-09-06");
            }
            else
            {
                // No published camp = no destination, so the row stays a LABEL. A live button that
                // opens nothing is the defect the VM's null door exists to prevent.
                var army = ElarionUiKit.Label(card, ManageScreenVM.Ascii(_vm.TroopArmySummaryText ?? ""),
                    ArmyDoorY0, ArmyDoorY1, ElarionUi.Parchment, (int)ElarionUi.FontLabel,
                    TextAlignmentOptions.Left, 0.19f, 0.72f, bold: true);
                ElarionUiKit.FitSingleLine(army, ElarionUiKit.FontHardFloor, 40f);
            }
            if (!string.IsNullOrEmpty(selected.StateWord))
            {
                // ⚠ WO-1541: the state badge rides the DOOR ROW's own band now, and it is built
                // AFTER the row so it paints above it. Its raycastTarget is already false, so the
                // whole row - badge included - stays one tap target.
                // ⛔ x PULLED IN 0.98 -> 0.90 so it CANNOT overlap the chevron at 0.90-0.97. The
                // badge is not shrunk in height: it gained the row's full 112px band.
                var troopBadge = ElarionUiKit.AddImage(card, "TroopStateBadge", new Vector2(0.72f, ArmyDoorY0),
                    new Vector2(0.90f, ArmyDoorY1), new Color(0.12f, 0.25f, 0.08f, 0.82f), rounded: false);
                troopBadge.GetComponent<Image>().raycastTarget = false;
                var troopState = ElarionUiKit.Label(troopBadge.transform, ManageScreenVM.Ascii(selected.StateWord), 0f, 1f,
                    ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.02f, 0.98f, bold: true);
                ElarionUiKit.FitSingleLine(troopState, 18f, 26f);
            }

            // Description left, status WORD ("Available" / "Requires Barracks Tier 2") right, one
            // band - words carry the state; the old green/red tint pair was the same colour to a
            // red/green colourblind owner.
            var desc = ElarionUiKit.Label(card, ManageScreenVM.Ascii(selected.Description ?? ""), ArmyDescY0, ArmyDescY1,
                ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.19f, 0.70f);
            ElarionUiKit.FitSingleLine(desc, 22f, 30f);
            var status = ElarionUiKit.Label(card, ManageScreenVM.Ascii(selected.Requirement ?? ""), ArmyDescY0, ArmyDescY1,
                ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, 0.71f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(status, 22f, 30f);

            if (!selected.Unlocked)
            {
                // Ruling #8: selectable, dim, the requirement in words, ONE Gray non-interactable
                // face, no Train / Upgrade buttons at all.
                var fact = ElarionUiKit.Label(card, ManageScreenVM.Ascii(selected.Requirement ?? ""), ArmyFactY0, ArmyFactY1,
                    ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.02f, 0.98f, bold: true);
                ElarionUiKit.FitSingleLine(fact, 22f, 26f);
                var lockedFace = ElarionUiKit.BuildObsidianButton(card, "LOCKED . TIER " + selected.LockTier,
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.02f, ArmyCtaY0), new Vector2(0.48f, ArmyCtaY1), null);
                if (lockedFace != null)
                {
                    lockedFace.gameObject.name = "TroopCta_Locked";
                    lockedFace.interactable = false;
                    MedievalUiSkin.ApplyButton(lockedFace, false);
                }
                return;
            }

            // The fact SENTENCE (ruling #4) - composed by the VM, painted here, directly ABOVE the
            // TRAIN button it explains.
            var trainFact = ElarionUiKit.Label(card, ManageScreenVM.Ascii(selected.TrainFactText ?? ""), ArmyFactY0, ArmyFactY1,
                ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.02f, 0.49f, bold: true);
            ElarionUiKit.FitSingleLine(trainFact, 22f, 26f);

            // WO-1422 ruling 3.10.2 - THE MEASURED TRUNCATION FIX. On the device the destination
            // sentence rode the UPGRADE face's sub-line as the THIRD clause and was cut mid-word:
            // "4m 30s . Ready . L3 unlocks Sweepi...". It now has its own row, at Buildings'
            // wording ("After upgrade: <benefit>"), and the sub-line keeps only cost + state.
            // ⚠ DELIBERATE DEVIATION from the WO's literal y 0.445-0.535: that band is already the
            // train-fact row on this card (0.455-0.575) and Buildings has no such row. The split is
            // HORIZONTAL instead - each sentence sits directly above the button it explains, which
            // is WO-1382 ruling #4's actual requirement: train fact over TRAIN (x 0.02-0.49),
            // upgrade benefit over UPGRADE (x 0.51-0.98). No font was shrunk to make it fit.
            var troopBenefit = ElarionUiKit.Label(card,
                string.IsNullOrEmpty(selected.NextUnlockText) ? "" : "After upgrade: " + ManageScreenVM.Ascii(selected.NextUnlockText),
                ArmyFactY0, ArmyFactY1, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.51f, 0.98f);
            ElarionUiKit.FitSingleLine(troopBenefit, ElarionUiKit.FontHardFloor, 26f);

            // WO-1422 ruling 3.10.3 / 3.5: the Troops card's CTA band carries TWO faces at the
            // touch floor (113.1px tall, 0.02-0.48 and 0.52-0.98) and has NO third slot. Ruling
            // 3.10's own escape hatch says drop the SECOND DOOR before the touch height, so a
            // DoorLabel authored here is REPORTED, never squeezed in.
            if (!string.IsNullOrEmpty(selected.DoorLabel))
                FlowTrace.Warn("Manage", "troop " + selected.Id + " authors DoorLabel='" + selected.DoorLabel +
                    "' but the Troops CTA band already holds TRAIN + UPGRADE at the 112px floor - " +
                    "the door is NOT painted (WO-1422 ruling 3.10 escape hatch). A third face needs a " +
                    "taller card, which is the Phase 2 unification WO.");

            // THE DOOR is unchanged: the VM's verb-led "Train <name>" row -> TrainTroop ->
            // BarracksService.EnqueueTraining -> the Train line. One job per tap, no count picker
            // (owner: "No count picker. At least for now."). The button face is the owner's
            // wording; the row's Activate is the same delegate the old browse row invoked.
            BrowseRowVM trainRow = FindTroopRow(selected.Id, "Train");
            BrowseRowVM upgradeRow = FindTroopRow(selected.Id, "Upgrade");

            bool trainOn = trainRow != null && selected.TrainReady;
            var train = ElarionUiKit.BuildObsidianButton(card,
                "TRAIN 1 " + ManageScreenVM.Ascii((selected.Name ?? "").ToUpperInvariant()),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                trainOn ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, ArmyCtaY0), new Vector2(0.48f, ArmyCtaY1),
                () => { Guard.Try("Manage", "train one", () => trainRow?.Activate?.Invoke()); });
            if (train != null)
            {
                train.gameObject.name = "TroopCta_Train";
                // Disabled + the sentence above says why (ruling #4). Never colour alone.
                train.interactable = trainOn;
                MedievalUiSkin.ApplyButton(train, true);
                DeNelle.Core.UI.TutorialHighlightRegistry.Register("manage.troop_cta_train", (RectTransform)train.transform);   // WO-1389
            }

            // The upgrade fact ("300 wood, 120 iron . Ready" / "Short 40 iron" / "At max level")
            // rides the UPGRADE face as its SUB-LINE through the panel's existing two-line CTA -
            // the 260px card has no spare band under the buttons, and the sentence stays with the
            // button it explains (ruling #4: "directly above or beneath the button").
            Button upgrade;
            if (selected.HasNextLevel)
            {
                bool upgradeOn = upgradeRow != null && selected.UpgradeReady;
                string upgradeSub = string.IsNullOrEmpty(selected.UpgradeCostText)
                    ? selected.UpgradeStateText
                    : selected.UpgradeCostText + " . " + selected.UpgradeStateText;
                // ⛔ WO-1422 ruling 3.10.2: NextUnlockText NO LONGER rides this sub-line. WO-1389
                // appended it as a third clause ("1m 30s . Ready . L3 unlocks Sweeping Cut") and
                // the device frame proved it truncates. It is painted as its own row above this
                // face (troopBenefit) - the panel still READS the field, which is what
                // ManageTroopsTrainDoorRegression case 7 asserts.
                upgrade = BuildTwoLineCta(card, "UPGRADE TO L" + (selected.Level + 1), upgradeSub,
                    ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.52f, ArmyCtaY0), new Vector2(0.98f, ArmyCtaY1),
                    () => { Guard.Try("Manage", "upgrade troop", () => upgradeRow?.Activate?.Invoke()); });
                if (upgrade != null) upgrade.interactable = upgradeOn;
            }
            else
            {
                upgrade = BuildTwoLineCta(card, "MAX LEVEL", selected.UpgradeStateText,
                    ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.52f, ArmyCtaY0), new Vector2(0.98f, ArmyCtaY1), null);
                if (upgrade != null) upgrade.interactable = false;
            }
            if (upgrade != null)
            {
                upgrade.gameObject.name = "TroopCta_Upgrade";
                MedievalUiSkin.ApplyButton(upgrade, false);   // the SECONDARY verb, by construction
                DeNelle.Core.UI.TutorialHighlightRegistry.Register("manage.troop_cta_upgrade", (RectTransform)upgrade.transform);   // WO-1389
            }
        }

        /// <summary>The VM's verb-led browse row for a troop ("Train"/"Upgrade"), or null.</summary>
        private BrowseRowVM FindTroopRow(string troopId, string actionText)
        {
            for (int i = 0; i < _vm.BrowseRows.Count; i++)
            {
                var candidate = _vm.BrowseRows[i];
                if (candidate == null) continue;
                if (!string.Equals(candidate.SubjectId, troopId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(candidate.ActionText, actionText, StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }
            return null;
        }

        // =====================================================================
        //  WO-1382 — THE TRAINING NOW BAND (ruling #5/#6): an informational MIRROR of the line.
        // ---------------------------------------------------------------------
        // ⛔ Built by THIS method from RenderTroopsDestination, NEVER by AddQueueRow and never
        // from RenderList: AddQueueRow is the drawer's build site for Finish Now / Ad / Cancel /
        // Move up (ManageQueueDrawerRegression pins both halves). Those verbs stay OUT of this
        // screen ("Keep advanced queue actions OUT of this screen"); the ONE door here is OPEN
        // QUEUE -> ToggleQueueDrawer. The band re-renders on QueueChanged (Rebuild -> Changed ->
        // Render), so the tap's consequence is visible without opening the drawer.
        // =====================================================================

        private void AddTroopTrainingNowBand()
        {
            // ONE 120px row carries the label, the FIRST job and OPEN QUEUE, so the band and at
            // least one job are above the fold at 2670x1200 (see the constants' arithmetic). The
            // first capture (2026-09-04) had a separate 128px header and the band fell below the
            // viewport - ruling #5 ("the screen visibly reacts") failed at scroll 0.
            var band = MakeRowHost("TroopTrainingNowBand", TrainingNowBandPx);
            var bandPlate = ElarionUiKit.AddImage(band, "BandPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            bandPlate.GetComponent<Image>().raycastTarget = false;
            var title = ElarionUiKit.Label(band, "TRAINING NOW", 0.15f, 0.85f, ElarionUi.Gold,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.165f, bold: true);
            ElarionUiKit.FitSingleLine(title, 22f, 32f);
            var open = ElarionUiKit.BuildObsidianButton(band, "OPEN QUEUE",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(PrimaryX0, BandCtrlY0), new Vector2(PrimaryX1, BandCtrlY1), ToggleQueueDrawer);
            if (open != null)
            {
                open.gameObject.name = "TroopOpenQueue";
                DeNelle.Core.UI.TutorialHighlightRegistry.Register("manage.open_queue", (RectTransform)open.transform);   // WO-1389
            }
            ElarionUiKit.ClampMinTouch(open);

            if (_vm.QueueRows.Count == 0)
            {
                var t = ElarionUiKit.Label(band, "Nothing training. Tap TRAIN to start.", 0.15f, 0.85f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.18f, ClusterX1 + 0.01f);
                ElarionUiKit.FitSingleLine(t, 24f, 34f);
                return;
            }

            // First job shares the band row, right of the label and left of the primary slot.
            var first = _vm.QueueRows[0];
            if (first != null)
                Guard.Try("Manage", "training now job 1", () => BuildTroopTrainingNowJob(band, 1, first,
                    0.175f, 0.205f, 0.21f, 0.27f, 0.28f, 0.45f, 0.46f, 0.60f, 0.61f, ClusterX1 + 0.01f));

            // Every further job is its own 88px informational row under the fold.
            for (int i = 1; i < _vm.QueueRows.Count; i++)
            {
                var r = _vm.QueueRows[i];
                if (r == null) continue;
                int ordinal = i + 1;
                Guard.Try("Manage", "training now row " + ordinal, () =>
                {
                    var row = MakeRowHost("TroopTrainingNowRow_" + ordinal, TrainingNowRowPx);
                    // WO-1422 POLISH (MEASURED 2026-09-06, ManageTroops_1920x1080.png): at alpha
                    // 0.28 over the dark screen the extra rows had no visible plate at all, so
                    // "2. Archer x1" read as SPILLING BELOW the TRAINING NOW band rather than as
                    // its continuation. Same plate colour as the band above it - one band, two rows.
                    var plate = ElarionUiKit.AddImage(row, "RowPlate", Vector2.zero, Vector2.one,
                        new Color(0.05f, 0.04f, 0.03f, 0.70f));
                    plate.GetComponent<Image>().raycastTarget = false;
                    BuildTroopTrainingNowJob(row, ordinal, r,
                        0.005f, 0.05f, 0.055f, 0.115f, 0.13f, 0.46f, 0.48f, 0.78f, 0.80f, 0.99f);
                });
            }
        }

        /// <summary>
        /// One numbered, read-only job: "<n>." + portrait + name, then for the ACTIVE job the kit
        /// <see cref="ElarionUiKit.Bar"/> + "<n>s left" (ticked at 1 Hz), and for a pending job
        /// "Queued <ordinal>". The x-bands are passed in because the first job shares the band
        /// row with the label and OPEN QUEUE while later jobs own a full row. No control here.
        /// </summary>
        private void BuildTroopTrainingNowJob(RectTransform row, int ordinal, QueueRowVM r,
            float numX0, float numX1, float medX0, float medX1, float nameX0, float nameX1,
            float barX0, float barX1, float timeX0, float timeX1, Sprite artOverride = null,
            string labelOverride = null)
        {
            var number = ElarionUiKit.Label(row, ordinal + ".", 0.15f, 0.85f, ElarionUi.Gold,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Center, numX0, numX1, bold: true);
            ElarionUiKit.FitSingleLine(number, 24f, 36f);

            var medallion = MakeZone(row, "Medallion", new Vector2(medX0, 0.12f), new Vector2(medX1, 0.88f));
            Sprite art = artOverride ?? (!string.IsNullOrEmpty(r.IconRole) ? RpgUiCatalog.Get(r.IconRole, r.IconKey) : null);
            ElarionUiKit.Portrait(medallion, art, active: !r.Queued);

            // WO-1422 POLISH: labelOverride wins when the CALLER can name the job better than the
            // queue row can. A placed-structure upgrade carries no BuildingTierCatalog identity
            // (ManageScreenVM.cs:765-772 leaves BuildingId empty and falls back to
            // ObsidianQueueHud.FormatJobTarget), so the Builder band printed the raw job key
            // title-cased - "Tower Ground Archer..." - measured in ManageDefense_2670x1200.png.
            var name = ElarionUiKit.Label(row,
                string.IsNullOrEmpty(labelOverride) ? ManageScreenVM.Ascii(r.Label ?? "") : labelOverride,
                0.15f, 0.85f, ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, nameX0, nameX1, bold: true);
            ElarionUiKit.FitSingleLine(name, 24f, 36f);

            bool running = !r.Queued && !r.IsStackHeader && r.Progress01 >= 0f && r.JobId != null;
            if (running)
            {
                var bar = ElarionUiKit.Bar(row, ElarionUiKit.BarKind.Castle,
                    new Vector2(barX0, 0.32f), new Vector2(barX1, 0.68f));
                if (bar?.fill != null)
                {
                    bar.fill.fillAmount = Mathf.Clamp01(r.Progress01);
                    bar.fill.raycastTarget = false;
                }
                if (bar?.track != null) _progressCells.Add(new ProgressCell
                {
                    Handle = bar,
                    Channel = r.Channel,
                    JobId = r.JobId,
                    Queued = false,
                });

                var svc = BuildTimerService.Instance;
                double rem = svc != null ? svc.RemainingSeconds(r.Channel, r.JobId) : 0d;
                var left = ElarionUiKit.Label(row, ManageScreenVM.FormatTime(rem) + " left", 0.15f, 0.85f,
                    ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, timeX0, timeX1, bold: true);
                ElarionUiKit.FitSingleLine(left, 20f, 30f);
                _trainingNowCells.Add(new TrainingNowCell { Text = left, Channel = r.Channel, JobId = r.JobId });
            }
            else
            {
                string state = r.IsStackHeader
                    ? "Queued x" + r.StackCount
                    : "Queued " + ManageScreenVM.Ordinal(r.PendingIndex + 1);
                var queued = ElarionUiKit.Label(row, state, 0.15f, 0.85f, ElarionUi.ParchmentDim,
                    (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, barX0, timeX1, bold: true);
                ElarionUiKit.FitSingleLine(queued, 22f, 30f);
            }
        }

        // =====================================================================
        //  WO-1422 — THE DEFENCE WORKSPACE: rail (one row per TYPE) + selected-defence card
        // ---------------------------------------------------------------------
        // ⛔ ONE ROW PER TYPE, NEVER PER PLACED INSTANCE (ruling 3.1). wall_wood is upgradable and
        // a town carries many segments, so a per-instance rail is UNBOUNDED. The card names the
        // type, says how many are placed and at what level, and its CTA upgrades the FIRST placed
        // instance at the LOWEST level - which is exactly what the JobKey already targeted before
        // this ticket, so this is presentation, not behaviour.
        // =====================================================================

        private void AddDefenseWorkspaceRow(DefenseChoiceVM selected)
        {
            var workspace = MakeRowHost("DefenseSplitWorkspace", TroopWorkspacePx);
            var railZone = MakeZone(workspace, "DefenseSelectorRail", new Vector2(0f, 0f), new Vector2(0.26f, 1f));
            var railPlate = ElarionUiKit.AddImage(railZone, "RailPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            railPlate.GetComponent<Image>().raycastTarget = false;
            var railScroll = ElarionUiKit.MakeScrollZone(railZone, spacing: 6f, padding: 8);
            if (railScroll == null || railScroll.content == null)
                FlowTrace.Fail("Manage", "defense rail MakeScrollZone returned no content - the rail has no build site.");
            else
            {
                int selectedIndex = 0;
                _rowParent = railScroll.content;
                try
                {
                    for (int i = 0; i < _vm.DefenseChoices.Count; i++)
                    {
                        var choice = _vm.DefenseChoices[i];
                        if (choice == null) continue;
                        bool isSelected = string.Equals(choice.Id, selected.Id, StringComparison.OrdinalIgnoreCase);
                        if (isSelected) selectedIndex = i;
                        Guard.Try("Manage", "defense rail row " + choice.Id, () => BuildDefenseRailRow(choice, isSelected));
                    }
                    MakeRowHost("DefenseRailTailSpacer", TroopWorkspacePx - TroopRailRowPx);
                }
                finally { _rowParent = null; }

                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(railScroll.content);
                if (railScroll.scroll != null)
                {
                    var viewport = railScroll.scroll.viewport;
                    float viewportPx = viewport != null ? viewport.rect.height : TroopWorkspacePx;
                    float maxScrollPx = Mathf.Max(0f, railScroll.content.rect.height - viewportPx);
                    float selectedTopPx = Mathf.Min(maxScrollPx, selectedIndex * (TroopRailRowPx + 6f));
                    railScroll.scroll.StopMovement();
                    railScroll.scroll.verticalNormalizedPosition = maxScrollPx > 0.5f
                        ? 1f - selectedTopPx / maxScrollPx
                        : 1f;
                    FlowTrace.Step("Manage", "defense rail aligned row=" + selectedIndex +
                        " topPx=" + selectedTopPx.ToString("0") + " maxPx=" + maxScrollPx.ToString("0"));
                }
            }

            var card = MakeZone(workspace, "DefenseSelectedCard", new Vector2(0.275f, 0f), new Vector2(1f, 1f));
            BuildDefenseCard(card, selected);
        }

        private void BuildDefenseRailRow(DefenseChoiceVM choice, bool isSelected)
        {
            var row = MakeRowHost("DefenseChoiceRow_" + choice.Id, TroopRailRowPx);
            var faceGo = ElarionUiKit.AddImage(row, "DefenseChoice_" + choice.Id, Vector2.zero, Vector2.one,
                isSelected ? new Color(0.24f, 0.18f, 0.08f, 0.90f) : new Color(0f, 0f, 0f, 0.28f), rounded: false);
            var face = faceGo.GetComponent<Image>();
            face.raycastTarget = true;
            var button = faceGo.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() =>
            {
                _selectedDefenseId = choice.Id;
                FlowTrace.Step("Manage", "defense rail selected=" + choice.Id);
                Render();
            });
            if (isSelected)
            {
                var outline = faceGo.AddComponent<Outline>();
                outline.effectColor = ElarionUi.Gold;
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = false;
            }

            var medallion = MakeZone(faceGo.transform, "Medallion", new Vector2(0.03f, 0.08f), new Vector2(0.27f, 0.92f));
            ElarionUiKit.Portrait(medallion, DefenseSprite(choice), active: isSelected);

            var name = ElarionUiKit.Label(faceGo.transform, ManageScreenVM.Ascii(choice.Name ?? ""), 0.52f, 0.96f,
                ElarionUi.Parchment, (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.30f, 0.84f, bold: true);
            ElarionUiKit.FitSingleLine(name, 26f, 38f);
            // The rail sub-line states the LEVEL the card acts on (the lowest placed) and, when the
            // type has more than one instance, how many - so "one row" never reads as "one tower".
            string railState = "Level " + choice.Level +
                (string.Equals(choice.StateWord, "Max", StringComparison.Ordinal) ? " . Max" :
                 string.Equals(choice.StateWord, "Building", StringComparison.Ordinal) ? " . Building" : "") +
                (choice.PlacedCount > 1 ? " . x" + choice.PlacedCount : "");
            var sub = ElarionUiKit.Label(faceGo.transform, railState,
                0.06f, 0.48f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.30f, 0.84f);
            ElarionUiKit.FitSingleLine(sub, 22f, 30f);
            var chevron = ElarionUiKit.Label(faceGo.transform, ">", 0.10f, 0.90f,
                isSelected ? ElarionUi.Gold : ElarionUi.ParchmentDim,
                (int)ElarionUi.FontBody, TextAlignmentOptions.Center, 0.84f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(chevron, 30f, 50f);
            ElarionUiKit.ClampMinTouch(button);
        }

        /// <summary>
        /// WO-1422 ruling 3.8 — DEFENCE TIER ART. Assets/Resources/Portraits/ holds three tier
        /// sheets each for archer-tower / ballista / catapult / arcane-spire / wizard-tower plus
        /// the wall, mine, caravan and storage sheets, and NO code path could reach the tier
        /// suffix: ResolveEntryArtPublic never appends one, and LoadManageBuildingSprite probes
        /// only Portraits/Buildings/. This probes, in order: the LEVEL-SUFFIXED key, the base key,
        /// the shared Build palette (which owns the alias table, e.g. wall_wood -> Wooden_Wall),
        /// the concept resolver, then warns and falls back to the neutral hammer.
        /// ⚠ This is NOT BuildingSprite and is deliberately not bound by its [building-art-palette-first]
        /// ban - and it does not consult ManageBuildingPortraitGaps, which is a Buildings-only list.
        /// </summary>
        private static Sprite DefenseSprite(DefenseChoiceVM choice)
        {
            if (choice == null) return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");

            Sprite art = Guard.Try<Sprite>("Manage", "defense art " + (choice.Id ?? "<null>"), () =>
            {
                // ResolveBuildingPortraitKey has ALREADY appended the level suffix, so the base key
                // is recovered by stripping it - never by re-slugging the id a second way.
                string key = choice.PortraitKey ?? "";
                string levelSuffix = "-" + choice.Level;
                string root = choice.Level > 1 && key.EndsWith(levelSuffix, StringComparison.Ordinal)
                    ? key.Substring(0, key.Length - levelSuffix.Length)
                    : key;
                string tierKey = choice.Level > 1 ? root + "-" + choice.Level : null;
                Sprite found = LoadManageBuildingSpriteAt(tierKey) ?? LoadManageBuildingSpriteAt(root);
                if (found != null) return found;

                // CatalogRegistry is NOT populated under -executeMethod unless the caller hydrates
                // it, so Get can legitimately return null here - guard it, never assume.
                var entry = string.IsNullOrEmpty(choice.CatalogEntryId)
                    ? null
                    : DeNelle.Core.Catalog.CatalogRegistry.Get(choice.CatalogEntryId);
                if (entry == null) return null;
                return DeNelle.Village.BuildPaletteUI.ResolveEntryArtPublic(entry)
                       ?? DeNelle.Core.UI.ConceptIconResolver.ResolveAny(entry.id, entry.type.ToString());
            }, null);

            if (art == null)
                FlowTrace.Warn("Manage", "defense art unresolved id=" + (choice.Id ?? "<null>") +
                    " portraitKey=" + (choice.PortraitKey ?? "<null>") +
                    " level=" + choice.Level + " catalogEntryId=" + (choice.CatalogEntryId ?? "<null>") +
                    " - add Portraits/<key>[-level]; neutral hammer used");
            return art ?? RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");
        }

        /// <summary>
        /// The SELECTED DEFENCE card. Same grammar as the Buildings card, plus the "n placed"
        /// sub-line ruling 3.1 requires so one rail row never reads as one structure. Defence has
        /// no second door (ruling 3.5: DoorLabel is null), so the CTA is FULL WIDTH.
        /// </summary>
        private void BuildDefenseCard(RectTransform card, DefenseChoiceVM selected)
        {
            var plate = ElarionUiKit.AddImage(card, "CardPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            plate.GetComponent<Image>().raycastTarget = false;
            ElarionUiKit.GoldPerimeter(card);

            var medallion = MakeZone(card, "DefensePortrait", new Vector2(0.02f, 0.59f), new Vector2(0.16f, 0.99f));
            ElarionUiKit.Portrait(medallion, DefenseSprite(selected), active: true);

            var name = ElarionUiKit.Label(card, ManageScreenVM.Ascii((selected.Name ?? "").ToUpperInvariant()),
                0.855f, 1f, ElarionUi.Gold, (int)ElarionUi.FontTitle,
                TextAlignmentOptions.Left, 0.19f, 0.72f, bold: true);
            ElarionUiKit.FitSingleLine(name, 30f, 48f);
            var level = ElarionUiKit.Label(card, "LEVEL " + selected.Level, 0.855f, 1f, ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Right, 0.74f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(level, 26f, 36f);

            // ⚠ WO-1422 POLISH (MEASURED 2026-09-06, ManageDefense_2670x1200.png): the card jumped
            // from the name straight to "Upgrade: 540 240" - NEITHER this line NOR the description
            // painted. The VM was innocent (ManageScreenVM.cs:1383-1384 substitutes "A village
            // structure." for a blank, :1413-1415 always composes PlacedText); both labels were
            // authored into 0.07 x TroopWorkspacePx(260) = 18.2px bands, and TMP's Ellipsis overflow
            // CULLS THE WHOLE LINE when the fontSizeMin line (FontHardFloor 20 -> ~23-24px) cannot
            // seat in the rect - the cause ElarionUiKitObsidian.cs:3110-3116 records for the "bare
            // plate" class. Buildings' own description band (0.70-0.83 = 33.8px) is the only height
            // proven to render on this card, so BOTH sentences now share it: ruling 3.1 asks for a
            // card SUB-LINE, not a second band, and the placed tally leads so an ellipsis can only
            // ever eat the flavour half. ⛔ Never re-author a text band on this card below ~24px.
            string defenseSubLine = ManageScreenVM.Ascii(selected.PlacedText ?? "");
            string defenseDesc = ManageScreenVM.Ascii(selected.Description ?? "");
            if (defenseSubLine.Length > 0 && defenseDesc.Length > 0) defenseSubLine += " - " + defenseDesc;
            else if (defenseSubLine.Length == 0) defenseSubLine = defenseDesc;
            var desc = ElarionUiKit.Label(card, defenseSubLine, 0.70f, 0.83f,
                ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.19f, 0.72f);
            ElarionUiKit.FitSingleLine(desc, ElarionUiKit.FontHardFloor, 30f);

            // The state WORD is the only carrier of state - the owner is red/green colourblind.
            var badge = ElarionUiKit.AddImage(card, "DefenseStateBadge", new Vector2(0.74f, 0.70f),
                new Vector2(0.98f, 0.83f), new Color(0.12f, 0.25f, 0.08f, 0.82f), rounded: false);
            badge.GetComponent<Image>().raycastTarget = false;
            var state = ElarionUiKit.Label(badge.transform, ManageScreenVM.Ascii(selected.StateWord ?? ""), 0f, 1f,
                ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.02f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(state, 20f, 28f);

            if (string.Equals(selected.StateWord, "Max", StringComparison.Ordinal)) return;

            ElarionUiKit.CostRow(card, selected.UpgradeCostParts, new Vector2(0.02f, 0.54f),
                new Vector2(0.72f, 0.695f), ElarionUi.Parchment, prefix: "Upgrade:",
                fontPx: (int)ElarionUi.FontMicro);
            string readiness = selected.UpgradeReady ? "Ready" : "Short";
            string factText = string.IsNullOrEmpty(selected.UpgradeTimeText)
                ? readiness : selected.UpgradeTimeText + " . " + readiness;
            var fact = ElarionUiKit.Label(card, factText, 0.54f, 0.695f, ElarionUi.Parchment,
                (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, 0.73f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(fact, 20f, 28f);

            var benefit = ElarionUiKit.Label(card,
                string.IsNullOrEmpty(selected.AfterUpgradeText) ? "" : "After upgrade: " + ManageScreenVM.Ascii(selected.AfterUpgradeText),
                0.445f, 0.535f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.02f, 0.98f);
            ElarionUiKit.FitSingleLine(benefit, ElarionUiKit.FontHardFloor, 26f);

            if (string.Equals(selected.StateWord, "Building", StringComparison.Ordinal))
            {
                BuildCardFace(card, "DefenseCta_Building", "BUILDING", 0.02f, 0.98f);
                return;
            }

            var upgrade = ElarionUiKit.BuildObsidianButton(card, "UPGRADE TO L" + selected.NextLevel,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                selected.UpgradeReady ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1),
                () => Guard.Try("Manage", "upgrade defense", () => selected.Activate?.Invoke()));
            if (upgrade != null)
            {
                upgrade.gameObject.name = "DefenseCta_Upgrade";
                upgrade.interactable = selected.UpgradeReady && selected.Activate != null;
                MedievalUiSkin.ApplyButton(upgrade, true);
            }
            ElarionUiKit.ClampMinTouch(upgrade);
        }

        /// <summary>
        /// WO-1422 POLISH — the BUILDING NOW medallion + name on the DEFENCE tab. A placed-structure
        /// upgrade job is keyed by <c>PlacedUpgradeKey.Compose(itemId, cellX, cellZ)</c>
        /// ("tower_ground_archer@3_7"), and <c>QueueRowVM.BuildingId</c> is populated only when the
        /// job resolves to a <c>BuildingTierCatalog</c> row — which a tower never does — so the band
        /// fell back to the title-cased job key and neutral art.
        /// ⛔ THE KEY SHAPE IS NOT TRUSTED, AND THE FIRST PASS OF THIS HELPER TRUSTED IT TOO MUCH.
        /// It derived the item id from <c>PlacedUpgradeKey.TryParse</c> ALONE, which accepts only
        /// the live <c>itemId@cellX_cellZ</c> grammar (PlacedUpgradeKey.cs:42 — no '@', no parse).
        /// The Manage capture fixture seeds the Builder job as <c>"tower_ground_archer:7:0"</c>
        /// (Assets/Editor/UICaptureLaunch.cs:7252), a COLON key, so TryParse returned false, every
        /// remaining arm missed, and ManageDefense_2670x1200.png printed the title-cased raw key
        /// beside empty art — the exact defect this helper was written to close.
        /// FOUR derivations are now tried, all OrdinalIgnoreCase:
        ///   1. <c>choice.JobKey</c> vs <c>r.JobId</c> — the literal string the card's own CTA
        ///      composed for this instance; an exact hit needs no id grammar at all.
        ///   2. the parsed placed key (live '@' shape).
        ///   3. the PREFIX CUT at the first ':' or '@' — the SAME cut
        ///      <c>ManageScreenVM.NormalizeBuildingJobId</c> already applies (ManageScreenVM.cs:1653),
        ///      with underscores KEPT because <c>DefenseChoiceVM.Id</c> is the raw BaseLayout itemId
        ///      while that method hyphenates for the tier catalog.
        ///   4. <c>r.BuildingId</c>, for the fixture that seeds a bare catalog id.
        /// On a miss this returns null, the caller's existing fallback art runs, and a Warn names
        /// the id. It never throws.
        /// </summary>
        private DefenseChoiceVM FindDefenseChoiceForJob(QueueRowVM r)
        {
            if (r == null || _vm == null) return null;

            string itemId;
            if (string.IsNullOrEmpty(r.JobId) ||
                !DeNelle.Village.Buildings.Progression.PlacedUpgradeKey.TryParse(r.JobId, out itemId, out _, out _))
                itemId = null;

            // The shape-agnostic cut. Never rendered — only matched against authored ids.
            string cutId = null;
            if (!string.IsNullOrEmpty(r.JobId))
            {
                int suffix = r.JobId.IndexOfAny(new[] { ':', '@' });
                cutId = (suffix > 0 ? r.JobId.Substring(0, suffix) : r.JobId).Trim();
                if (cutId.Length == 0) cutId = null;
            }

            for (int i = 0; i < _vm.DefenseChoices.Count; i++)
            {
                var choice = _vm.DefenseChoices[i];
                if (choice == null || string.IsNullOrEmpty(choice.Id)) continue;
                if ((!string.IsNullOrEmpty(choice.JobKey) && !string.IsNullOrEmpty(r.JobId) &&
                        string.Equals(choice.JobKey, r.JobId, StringComparison.OrdinalIgnoreCase)) ||
                    (itemId != null && string.Equals(choice.Id, itemId, StringComparison.OrdinalIgnoreCase)) ||
                    (cutId != null && string.Equals(choice.Id, cutId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(r.BuildingId) && string.Equals(choice.Id, r.BuildingId, StringComparison.OrdinalIgnoreCase)))
                    return choice;
            }

            // ⚠ A MISS IS NOT ALWAYS A DEFECT. The Builder line is SHARED, so the first job on the
            // Defence tab can legitimately be a town building (Farm -> L2); the tier catalog already
            // named it, BuildingId is populated, and the caller's Buildings expression resolves it
            // correctly. Warning on that normal state would spam a §12 Warn on every Render, which
            // is how a real signal gets tuned out. Warn ONLY for the genuinely unnameable job.
            if (string.IsNullOrEmpty(r.BuildingId))
                FlowTrace.Warn("Manage", "defence job id '" + (r.JobId ?? "<null>") + "' (parsed item '" +
                    (itemId ?? "<none>") + "', cut item '" + (cutId ?? "<none>") +
                    "') does not resolve to an authored type in DefenseChoices (" +
                    _vm.DefenseChoices.Count + " known) AND carries no BuildingId - the BUILDING NOW row " +
                    "keeps the band's fallback name and art");
            else
                FlowTrace.Step("Manage", "defence tab BUILDING NOW job '" + r.JobId + "' is a catalog " +
                    "building (buildingId=" + r.BuildingId + "), not a placed defence - Buildings art path used");
            return null;
        }

        // =====================================================================
        //  WO-1422 — THE RESEARCH WORKSPACE: rail (one row per PERK) + selected-perk card
        // ---------------------------------------------------------------------
        // ⛔ ONE ROW PER PERK (ruling 3.6), not per building: a per-building card would need three
        // or four verbs inside the single CTA band and no card grammar here supports that. The
        // owning building becomes the row's SUB-LINE, which is what kills the developer-shaped
        // "Lumber Mill - Improved Logging" label. Research has NO LEVEL: the card's level slot
        // carries "TIER n" (ruling 3.7) and never paints "LEVEL 0".
        // =====================================================================

        private void AddResearchWorkspaceRow(ResearchChoiceVM selected)
        {
            var workspace = MakeRowHost("ResearchSplitWorkspace", TroopWorkspacePx);
            var railZone = MakeZone(workspace, "ResearchSelectorRail", new Vector2(0f, 0f), new Vector2(0.26f, 1f));
            var railPlate = ElarionUiKit.AddImage(railZone, "RailPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            railPlate.GetComponent<Image>().raycastTarget = false;
            var railScroll = ElarionUiKit.MakeScrollZone(railZone, spacing: 6f, padding: 8);
            if (railScroll == null || railScroll.content == null)
                FlowTrace.Fail("Manage", "research rail MakeScrollZone returned no content - the rail has no build site.");
            else
            {
                int selectedIndex = 0;
                string selectedKey = ResearchKeyOf(selected);
                _rowParent = railScroll.content;
                try
                {
                    for (int i = 0; i < _vm.ResearchChoices.Count; i++)
                    {
                        var choice = _vm.ResearchChoices[i];
                        if (choice == null) continue;
                        bool isSelected = string.Equals(ResearchKeyOf(choice), selectedKey, StringComparison.OrdinalIgnoreCase);
                        if (isSelected) selectedIndex = i;
                        Guard.Try("Manage", "research rail row " + ResearchKeyOf(choice), () => BuildResearchRailRow(choice, isSelected));
                    }
                    MakeRowHost("ResearchRailTailSpacer", TroopWorkspacePx - TroopRailRowPx);
                }
                finally { _rowParent = null; }

                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(railScroll.content);
                if (railScroll.scroll != null)
                {
                    var viewport = railScroll.scroll.viewport;
                    float viewportPx = viewport != null ? viewport.rect.height : TroopWorkspacePx;
                    float maxScrollPx = Mathf.Max(0f, railScroll.content.rect.height - viewportPx);
                    float selectedTopPx = Mathf.Min(maxScrollPx, selectedIndex * (TroopRailRowPx + 6f));
                    railScroll.scroll.StopMovement();
                    railScroll.scroll.verticalNormalizedPosition = maxScrollPx > 0.5f
                        ? 1f - selectedTopPx / maxScrollPx
                        : 1f;
                    FlowTrace.Step("Manage", "research rail aligned row=" + selectedIndex +
                        " topPx=" + selectedTopPx.ToString("0") + " maxPx=" + maxScrollPx.ToString("0"));
                }
            }

            var card = MakeZone(workspace, "ResearchSelectedCard", new Vector2(0.275f, 0f), new Vector2(1f, 1f));
            BuildResearchCard(card, selected);
        }

        private void BuildResearchRailRow(ResearchChoiceVM choice, bool isSelected)
        {
            string key = ResearchKeyOf(choice);
            var row = MakeRowHost("ResearchChoiceRow_" + key, TroopRailRowPx);
            var faceGo = ElarionUiKit.AddImage(row, "ResearchChoice_" + key, Vector2.zero, Vector2.one,
                isSelected ? new Color(0.24f, 0.18f, 0.08f, 0.90f) : new Color(0f, 0f, 0f, 0.28f), rounded: false);
            var face = faceGo.GetComponent<Image>();
            face.raycastTarget = true;
            var button = faceGo.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() =>
            {
                _selectedResearchKey = key;
                FlowTrace.Step("Manage", "research rail selected=" + key);
                Render();
            });
            if (isSelected)
            {
                var outline = faceGo.AddComponent<Outline>();
                outline.effectColor = ElarionUi.Gold;
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = false;
            }

            var medallion = MakeZone(faceGo.transform, "Medallion", new Vector2(0.03f, 0.08f), new Vector2(0.27f, 0.92f));
            var portrait = ElarionUiKit.Portrait(medallion, ResearchSprite(choice), active: isSelected);
            if (choice.Locked && portrait?.image != null) portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (choice.Locked) BuildLockBadge(medallion);

            // NAME over SUB-LINE - the perk's own name, then the building that owns it. This is
            // what retires the "Lumber Mill - Improved Logging" developer label (ruling 3.6).
            var name = ElarionUiKit.Label(faceGo.transform, ManageScreenVM.Ascii(choice.Name ?? ""), 0.52f, 0.96f,
                choice.Locked ? ElarionUi.ParchmentDim : ElarionUi.Parchment,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.30f, 0.84f, bold: true);
            ElarionUiKit.FitSingleLine(name, 26f, 38f);
            // ⚠ ORDER IS LOAD-BEARING (measured 2026-09-06, seeker-357569-research.png): with the
            // building name leading, EVERY row of a building read "Cathedral of Magic ...." and the
            // ellipsis ate the only term that differs between rows. The STATE leads now, so the
            // discriminator always survives and the ellipsis eats the shared half - the same rule
            // the description band below already documents. The rail zone is 0..0.26 by design and
            // is NOT widened. Both terms are kept; only their order changed.
            string railBuilding = ManageScreenVM.Ascii(choice.BuildingName ?? "");
            string railSub = choice.StateWord ?? "";
            if (!string.IsNullOrEmpty(railBuilding))
                railSub = string.IsNullOrEmpty(railSub) ? railBuilding : railSub + " . " + railBuilding;
            var sub = ElarionUiKit.Label(faceGo.transform, railSub,
                0.06f, 0.48f, ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                TextAlignmentOptions.Left, 0.30f, 0.84f);
            ElarionUiKit.FitSingleLine(sub, 22f, 30f);
            var chevron = ElarionUiKit.Label(faceGo.transform, ">", 0.10f, 0.90f,
                isSelected ? ElarionUi.Gold : ElarionUi.ParchmentDim,
                (int)ElarionUi.FontBody, TextAlignmentOptions.Center, 0.84f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(chevron, 30f, 50f);
            ElarionUiKit.ClampMinTouch(button);
        }

        /// <summary>
        /// WO-1422 ruling 3.6 — PERK ART. Assets/Resources/HudIcons/BuildingUpgrades/ holds 15 .jpg
        /// plus Upgrade.png covering all 17 authored perks, and it is the folder
        /// BuildingUpgradePanelMvvm.cs:2025 already loads from.
        /// ⚠ BuildingPerkDef.IconId's doc comment (Assets/_Modules/Core/State/BuildingTierCatalog.cs)
        /// names Resources/HudItems/BuildingUpgrades/ - THAT FOLDER DOES NOT EXIST. The comment is
        /// wrong; the loader below is the truth. (The comment lives in another file and is flagged
        /// in this lane's hand-back rather than edited here.)
        /// </summary>
        private static Sprite ResearchSprite(ResearchChoiceVM choice)
        {
            if (choice == null || string.IsNullOrEmpty(choice.IconName))
                return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");

            // LoadManageBuildingSpriteAt is reused for its Texture2D->Sprite fallback and its cache:
            // these are .jpg files whose import type is not guaranteed to be Sprite.
            Sprite art = Guard.Try<Sprite>("Manage", "research art " + choice.IconName,
                () => LoadManageBuildingSpriteAt("HudIcons/BuildingUpgrades/" + choice.IconName), null);
            if (art == null)
                FlowTrace.Warn("Manage", "research perk art unresolved perk=" + ResearchKeyOf(choice) +
                    " iconName=" + choice.IconName +
                    " - expected Resources/HudIcons/BuildingUpgrades/<IconName>; neutral hammer used");
            return art ?? RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, "hammer");
        }

        /// <summary>
        /// WO-1422 ruling 3.9 — the RESEARCHING NOW medallion. A Research job carries no BuildingId
        /// (QueueRowVM.BuildingId is populated only on the Builder channel), so the perk is read
        /// back off the job id, whose shape is "building-research:&lt;buildingId&gt;:&lt;perkId&gt;".
        /// ⛔ THE THIRD SEGMENT IS NOT TRUSTED: it is matched against the CATALOG-DERIVED
        /// ResearchChoices, never used as art key on its own. A shipped fixture carried the perk id
        /// `warding`, which is authored nowhere - so a segment that looks like a perk id can be one
        /// that does not exist. On a miss this returns null (the band's existing fallback art runs)
        /// and warns naming the id. It never throws.
        /// </summary>
        private Sprite ResearchJobSprite(QueueRowVM r)
        {
            if (r == null || string.IsNullOrEmpty(r.JobId)) return null;
            string[] parts = r.JobId.Split(':');
            if (parts.Length >= 3 && _vm != null)
            {
                for (int i = 0; i < _vm.ResearchChoices.Count; i++)
                {
                    var choice = _vm.ResearchChoices[i];
                    if (choice == null) continue;
                    if (string.Equals(choice.BuildingId, parts[1], StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(choice.PerkId, parts[2], StringComparison.OrdinalIgnoreCase))
                        return ResearchSprite(choice);
                }
            }
            FlowTrace.Warn("Manage", "research job id '" + r.JobId + "' does not resolve to an authored " +
                "perk in ResearchChoices (" + (_vm != null ? _vm.ResearchChoices.Count : 0) + " known) - " +
                "the RESEARCHING NOW medallion falls back to the band's default art");
            return null;
        }

        /// <summary>
        /// The SELECTED PERK card (ruling 3.7). The WHOLE tree is shown, including the two states
        /// the retired list HID: an owned perk (Researched) and an in-flight one (Researching).
        /// Researched -> no CTA. Researching -> one non-interactable RESEARCHING face. Locked -> the
        /// CanResearch reason VERBATIM as a BODY TEXT LINE, above ONE full-width live door to the
        /// prerequisite (the two-half-faces shape is retired - see the block comment on the locked
        /// branch). Available -> RESEARCH. The parameter is named `choice` because
        /// ManageProgressiveDisclosureRegression's migrated [research-locked-visible] case reads
        /// this body for `choice.Locked` and `BuildLockBadge(`.
        /// </summary>
        private void BuildResearchCard(RectTransform card, ResearchChoiceVM choice)
        {
            var plate = ElarionUiKit.AddImage(card, "CardPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            plate.GetComponent<Image>().raycastTarget = false;
            ElarionUiKit.GoldPerimeter(card);

            var medallion = MakeZone(card, "ResearchPortrait", new Vector2(0.02f, 0.59f), new Vector2(0.16f, 0.99f));
            var portrait = ElarionUiKit.Portrait(medallion, ResearchSprite(choice), active: true);
            if (choice.Locked && portrait?.image != null) portrait.image.color = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (choice.Locked) BuildLockBadge(medallion);

            var name = ElarionUiKit.Label(card, ManageScreenVM.Ascii((choice.Name ?? "").ToUpperInvariant()),
                0.855f, 1f, ElarionUi.Gold, (int)ElarionUi.FontTitle,
                TextAlignmentOptions.Left, 0.19f, 0.72f, bold: true);
            ElarionUiKit.FitSingleLine(name, 30f, 48f);
            // ⛔ TierText, never the Buildings level line: a perk has no level, so reusing that
            // line would print a zero here (ruling 3.7). The literal is deliberately absent from
            // this whole method - [no-level-zero] bans it from every Research card path.
            var tier = ElarionUiKit.Label(card, ManageScreenVM.Ascii(choice.TierText ?? ""), 0.855f, 1f,
                ElarionUi.Parchment, (int)ElarionUi.FontLabel, TextAlignmentOptions.Right, 0.74f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(tier, 26f, 36f);

            // ⚠ WO-1422 POLISH (MEASURED 2026-09-06, ManageResearch_2670x1200.png): the card jumped
            // from the name straight to "Research: 6400" - neither the OWNING BUILDING line nor the
            // description painted, though the rail row beside it read "Barracks . Available". Not a
            // VM defect: both labels sat in 0.07 x TroopWorkspacePx(260) = 18.2px bands, and TMP's
            // Ellipsis overflow CULLS THE WHOLE LINE when the fontSizeMin line (FontHardFloor 20 ->
            // ~23-24px) cannot seat in the rect (ElarionUiKitObsidian.cs:3110-3116). They now share
            // Buildings' description band (0.70-0.83 = 33.8px), the only height on this card proven
            // to render, with the owning building leading so an ellipsis eats only the flavour half.
            // ⛔ Never re-author a text band on this card below ~24px.
            string researchLine = ManageScreenVM.Ascii(choice.BuildingName ?? "");
            string researchDesc = ManageScreenVM.Ascii(choice.Description ?? "");
            if (researchLine.Length > 0 && researchDesc.Length > 0) researchLine += " - " + researchDesc;
            else if (researchLine.Length == 0) researchLine = researchDesc;
            var desc = ElarionUiKit.Label(card, researchLine, 0.70f, 0.83f,
                ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.19f, 0.72f);
            ElarionUiKit.FitSingleLine(desc, ElarionUiKit.FontHardFloor, 30f);

            var badge = ElarionUiKit.AddImage(card, "ResearchStateBadge", new Vector2(0.74f, 0.70f),
                new Vector2(0.98f, 0.83f), new Color(0.12f, 0.25f, 0.08f, 0.82f), rounded: false);
            badge.GetComponent<Image>().raycastTarget = false;
            var state = ElarionUiKit.Label(badge.transform, ManageScreenVM.Ascii(choice.StateWord ?? ""), 0f, 1f,
                ElarionUi.Parchment, (int)ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.02f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(state, 20f, 28f);

            // An owned perk is DONE: the word in the badge is the whole story, and a card with no
            // verb is the honest shape (the same delta WO-1418 made when it stopped hiding maxed
            // buildings). No cost row either - there is nothing left to pay.
            if (string.Equals(choice.StateWord, "Researched", StringComparison.Ordinal))
                return;

            // A LOCKED card needs one extra body line for the lock sentence, and the only slack on
            // this card is between the CTA band top (TroopCtaY1 = 0.445) and the cost row. The fact
            // row lifts by 0.025 ONLY when locked, which frees 0.45-0.56 below it. Arithmetic at
            // TroopWorkspacePx = 260: locked cost band 0.565-0.70 = 35.1px, which is 0.9px UNDER
            // AddCostText's preferredHeight of FontMicro+4 = 36 (CostFormat.cs:145). Safe: the
            // HorizontalLayoutGroup (childControlHeight true, childForceExpandHeight false) clamps
            // the child to 35.1, and CostText never sets overflowMode, so TMP's default Overflow
            // RENDERS it - a different path from the Ellipsis cull that blanks a short band.
            // Locked reason band 0.45-0.56 = 28.6px.
            // Available / Researching cards keep the 0.54-0.695 that was MEASURED to render.
            // ⛔ Never re-author a text band on this card below ~24px - TMP culls the whole line.
            float factY0 = choice.Locked ? 0.565f : 0.54f;
            float factY1 = choice.Locked ? 0.70f : 0.695f;
            ElarionUiKit.CostRow(card, choice.CostParts, new Vector2(0.02f, factY0),
                new Vector2(0.72f, factY1), ElarionUi.Parchment, prefix: "Research:",
                fontPx: (int)ElarionUi.FontMicro);
            string readiness = choice.Ready ? "Ready" : "Short";
            string factText = string.IsNullOrEmpty(choice.TimeText) ? readiness : choice.TimeText + " . " + readiness;
            var fact = ElarionUiKit.Label(card, factText, factY0, factY1, ElarionUi.Parchment,
                (int)ElarionUi.FontMicro, TextAlignmentOptions.Right, 0.73f, 0.98f, bold: true);
            ElarionUiKit.FitSingleLine(fact, 20f, 28f);

            if (string.Equals(choice.StateWord, "Researching", StringComparison.Ordinal))
            {
                BuildCardFace(card, "ResearchCta_Researching",
                    string.IsNullOrEmpty(choice.CtaLabel) ? "RESEARCHING" : choice.CtaLabel, 0.02f, 0.98f);
                return;
            }

            if (choice.Locked)
            {
                // ⚠ DEVICE FIX (measured 2026-09-06, owner's Seeker build 2026.09.06.357569,
                // seeker-357569-research.png). This branch used to paint TWO HALF-WIDTH faces and
                // BOTH ellipsized: "UPGRADE THE BUILDING T..." beside "UPGRADE CATHEDRAL OF M...".
                // The player could read neither WHY the perk was locked nor where the door went, and
                // the dead face was indistinguishable from the live one. Root cause is the CONTAINER,
                // not the width: CanResearch's reason is an authored SENTENCE ("Upgrade the building
                // to Tier 3 first."), and a sentence never fits a button face.
                // The reason is now a BODY TEXT LINE - prose in the body, where the description band
                // right above it already proved text renders - and the card carries exactly ONE
                // FULL-WIDTH live door wearing the SHORT CtaLabel, which is the single-CTA shape
                // BuildDefenseCard and BuildBuildingCard already use. Both halves of ruling 3.7 are
                // intact and more legible: the reason is verbatim and complete, the prerequisite is
                // still one tap away. [research-locked-visible] is satisfied by the LockReason paint
                // plus the door - it pins the reason reaching the screen, never a dead button.
                var lockLine = ElarionUiKit.Label(card,
                    ManageScreenVM.Ascii(string.IsNullOrEmpty(choice.LockReason) ? "Locked." : choice.LockReason),
                    0.45f, 0.56f, ElarionUi.Parchment, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Left, 0.02f, 0.98f, bold: true);
                if (lockLine != null)
                {
                    lockLine.gameObject.name = "ResearchLockReason";
                    ElarionUiKit.FitSingleLine(lockLine, ElarionUiKit.FontHardFloor, 28f);
                }
                var door = ElarionUiKit.BuildObsidianButton(card,
                    ManageScreenVM.Ascii(string.IsNullOrEmpty(choice.CtaLabel) ? "OPEN" : choice.CtaLabel),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(0.02f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1),
                    () => Guard.Try("Manage", "open research prerequisite", () => choice.Activate?.Invoke()));
                if (door != null)
                {
                    door.gameObject.name = "ResearchCta_Door";
                    door.interactable = choice.Activate != null;
                    MedievalUiSkin.ApplyButton(door, false);
                }
                ElarionUiKit.ClampMinTouch(door);
                return;
            }

            var research = ElarionUiKit.BuildObsidianButton(card,
                ManageScreenVM.Ascii(string.IsNullOrEmpty(choice.CtaLabel) ? "RESEARCH" : choice.CtaLabel),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                choice.Ready ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.02f, TroopCtaY0), new Vector2(0.98f, TroopCtaY1),
                () => Guard.Try("Manage", "start research", () => choice.Activate?.Invoke()));
            if (research != null)
            {
                research.gameObject.name = "ResearchCta_Start";
                research.interactable = choice.Ready && choice.Activate != null;
                MedievalUiSkin.ApplyButton(research, true);
            }
            ElarionUiKit.ClampMinTouch(research);
        }

        /// <summary>
        /// A non-interactable CTA face at an ARBITRARY x-span. ⚠ Deliberately NOT a change to
        /// BuildDisabledBuildingFace, which is hardcoded full-width AND is a Body() boundary marker
        /// for ManageBuildingsCardRegression's card window - widening its signature would move that
        /// boundary. The y-span is always the shared CTA band, so the touch floor is the same
        /// 113.1px every other face on these cards clears.
        /// ⚠ The x-span is arbitrary but the ONLY live caller is the Researching state, at FULL
        /// width. The locked-Research caller that needed a HALF-width dead face is RETIRED
        /// (2026-09-06): its sentence ellipsized on a half face, so it became a body text line.
        /// Do not reintroduce a half-width face to hold a sentence.
        /// </summary>
        private static void BuildCardFace(RectTransform card, string objectName, string text, float x0, float x1)
        {
            var face = ElarionUiKit.BuildObsidianButton(card, ManageScreenVM.Ascii(text ?? ""),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(x0, TroopCtaY0), new Vector2(x1, TroopCtaY1), null);
            if (face == null) return;
            face.gameObject.name = objectName;
            face.interactable = false;
            MedievalUiSkin.ApplyButton(face, false);
            ElarionUiKit.ClampMinTouch(face);
        }

        /// <summary>
        /// WO-1422 — the RESEARCHING NOW band: the Builder band's exact grammar on the Research
        /// channel. One painted job plus "+N more"; the extra jobs are NOT given their own rows
        /// (that is the Buildings shape, and [building-now-stays-in-band] is the pin that keeps it).
        /// The host name starts with ResearchNowPrefix so ApplyDrawerPlacement collapses it when
        /// the queue drawer opens over the card.
        /// </summary>
        private void AddResearchNowBand()
        {
            var band = MakeRowHost(ResearchNowPrefix + "Band", TrainingNowBandPx);
            var bandPlate = ElarionUiKit.AddImage(band, "BandPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.70f));
            bandPlate.GetComponent<Image>().raycastTarget = false;
            int hiddenJobs = Mathf.Max(0, _vm.QueueRows.Count - 1);
            var title = ElarionUiKit.Label(band, "RESEARCHING NOW", hiddenJobs > 0 ? 0.53f : 0.15f, 0.85f, ElarionUi.Gold,
                (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.165f, bold: true);
            ElarionUiKit.FitSingleLine(title, 22f, 32f);
            if (hiddenJobs > 0)
            {
                var more = ElarionUiKit.Label(band, "+" + hiddenJobs + " more", 0.12f, 0.48f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontMicro,
                    TextAlignmentOptions.Left, 0.01f, 0.165f, bold: true);
                ElarionUiKit.FitSingleLine(more, ElarionUiKit.FontHardFloor, 28f);
            }
            var open = ElarionUiKit.BuildObsidianButton(band, "OPEN QUEUE",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(PrimaryX0, BandCtrlY0), new Vector2(PrimaryX1, BandCtrlY1), ToggleQueueDrawer);
            if (open != null) open.gameObject.name = "ResearchOpenQueue";
            ElarionUiKit.ClampMinTouch(open);

            if (_vm.QueueRows.Count == 0)
            {
                var empty = ElarionUiKit.Label(band, "No research under way", 0.15f, 0.85f,
                    ElarionUi.ParchmentDim, (int)ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    0.18f, ClusterX1 + 0.01f);
                ElarionUiKit.FitSingleLine(empty, 24f, 34f);
                return;
            }

            var first = _vm.QueueRows[0];
            if (first != null)
                Guard.Try("Manage", "research now job 1", () => BuildTroopTrainingNowJob(band, 1, first,
                    0.175f, 0.205f, 0.21f, 0.27f, 0.28f, 0.45f, 0.46f, 0.60f, 0.61f, ClusterX1 + 0.01f,
                    ResearchJobSprite(first)));
            if (hiddenJobs > 0)
                FlowTrace.Step("Manage", "research now capped inside band: painted=1 hidden=" + hiddenJobs);
        }

        private static string BrowseHeading(ManageTab tab)
        {
            switch (tab)
            {
                // WO-1422 ruling 3.2: the Defence arm is GONE with the paged path. The heading it
                // returned promised upgradable TOWERS, and that was a measured LIE - the tab also
                // lists walls, the crystal mine, the healing caravan and the three storage
                // containers. The lie dies with the surface that printed it, not by rewording it.
                // The literal itself is deliberately not repeated here: a re-pointed suite asserts
                // it is absent from this FILE, not merely unreachable.
                case ManageTab.Buildings: return "BUILDING UPGRADES - affordable first";
                case ManageTab.Troops: return "TRAIN & UPGRADE TROOPS";
                case ManageTab.Research: return "RESEARCH PROJECTS";
                default: return "AVAILABLE ACTIONS";
            }
        }

        private static string BrowseEmptyState(ManageTab tab)
        {
            switch (tab)
            {
                case ManageTab.Defense: return "No defenses are ready to upgrade. Build your first tower or wall here.";
                case ManageTab.Buildings: return "No placed buildings are ready to upgrade.";
                case ManageTab.Troops: return "No trainable troops are available yet.";
                case ManageTab.Research: return "No research projects currently meet their requirements.";
                default: return "Nothing is available on this line yet.";
            }
        }

        // ── Row factories (fixed-pixel bands) ─────────────────────────────────

        private RectTransform MakeRowHost(string name, float heightPx)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            // WO-1368: ONE row factory, two destinations. _rowParent is non-null only for the
            // duration of RenderQueueDrawer, so the browse list is the default and cannot be
            // reached by accident; the drawer reuses every row builder verbatim rather than
            // forking a second copy that would drift.
            rt.SetParent(_rowParent != null ? _rowParent : _listContent, false);
            var le = go.GetComponent<LayoutElement>();
            // BOTH the LayoutElement AND sizeDelta — the scroll column has childControlHeight off,
            // so a row that only sets one of them collapses to zero.
            le.preferredHeight = heightPx;
            le.minHeight = heightPx;
            le.flexibleWidth = 1f;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, heightPx);
            return rt;
        }

        /// <summary>
        /// WO-1058 — resolve the <paramref name="index"/>'th of <paramref name="count"/> SECONDARY
        /// controls inside <see cref="ClusterX0"/>..<see cref="ClusterX1"/>, evenly split with a
        /// fixed gutter. Even split is deliberate: it is the only division under which three
        /// controls all clear MinTouchPx at the narrowest supported aspect, so ClampMinTouch never
        /// fires and never inflates one control into its neighbour.
        /// </summary>
        private static void ClusterSlot(int index, int count, out Vector2 aMin, out Vector2 aMax)
        {
            if (count < 1) count = 1;
            float w = ((ClusterX1 - ClusterX0) - ClusterGapX * (count - 1)) / count;
            float x = ClusterX0 + index * (w + ClusterGapX);
            aMin = new Vector2(x, RowCtrlY0);
            aMax = new Vector2(x + w, RowCtrlY1);
        }

        /// <summary>
        /// ⭐ WO-1488 - AN EVEN SPLIT OF WHAT THE COMPACT AD CHIP LEFT BEHIND, for the WORD
        /// controls only (CANCEL, Move up).
        /// <para><see cref="ClusterSlot"/> splits the whole cluster evenly and is KEPT, because the
        /// other row types on this screen still lay their controls that way; this is the queue
        /// row's variant, where one member ("Ad") is two characters and the others are six and
        /// seven. Giving all three the same width is what ellipsised CANCEL to "CANC..." on the
        /// owner's device.</para>
        /// <para>⚠ The ORDER is unchanged - Ad, Cancel, Move up - so the destructive control is
        /// still never adjacent to the primary slot (the WO-1058 rule).</para>
        /// </summary>
        /// <param name="y0">The row's control band FLOOR. Passed in rather than read off
        /// <see cref="RowCtrlY0"/> because the QUEUE row's height is a MEASUREMENT, not
        /// <see cref="RowHeightPx"/> - see <see cref="QueueCtrlY0"/> for the 98.6px failure that
        /// the fixed fraction produced on forty controls.</param>
        /// <param name="y1">The band's ceiling, the mirror of <paramref name="y0"/>.</param>
        private static void WordSlot(int index, int count, float x0, float span,
                                     float y0, float y1,
                                     out Vector2 aMin, out Vector2 aMax)
        {
            if (count < 1) count = 1;
            float w = (span - ClusterGapX * (count - 1)) / count;
            float x = x0 + index * (w + ClusterGapX);
            aMin = new Vector2(x, y0);
            aMax = new Vector2(x + w, y1);
        }

        private void AddSectionHeader(string text)
        {
            var row = MakeRowHost("SectionHeader", SectionHeaderPx);
            var t = ElarionUiKit.Label(row, ManageScreenVM.Ascii(text), 0f, 1f, ElarionUi.Gold,
                                       (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.01f, 0.99f, bold: true);
            ElarionUiKit.FitSingleLine(t);
        }

        private void AddNoteRow(string text)
        {
            var row = MakeRowHost("Note", SectionHeaderPx);
            var t = ElarionUiKit.Label(row, ManageScreenVM.Ascii(text), 0f, 1f, ElarionUi.ParchmentDim,
                                       (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.02f, 0.99f);
            ElarionUiKit.FitSingleLine(t);
        }

        private void OpenDefenseBuilder()
        {
            Close();
            var controller = BuildModeController.Instance ?? BuildModeController.EnsureExists();
            controller?.EnterBuildMode(DeNelle.Core.Catalog.BuildType.Defense);
        }

        private void OpenTownBuilder()
        {
            Close();
            var controller = BuildModeController.Instance ?? BuildModeController.EnsureExists();
            controller?.EnterBuildMode(DeNelle.Core.Catalog.BuildType.Town);
        }

        /// <summary>
        /// WO-1571 - the not-built card's BUILD door. Opens placement for THAT id (the ghost,
        /// armed) instead of the Build Collections root, which offers no ECONOMY / CRAFT / STORAGE
        /// collection and is therefore a dead end for every non-defence row. The controller owns
        /// every gate; this method only carries the id across the panel boundary.
        /// </summary>
        private void OpenPlacementFor(string structureId)
        {
            Close();
            var controller = BuildModeController.Instance ?? BuildModeController.EnsureExists();
            controller?.EnterBuildModeForStructure(structureId);
        }

        private void AddActionNoteRow(string text, string action, Action onTap)
        {
            var row = MakeRowHost("ActionNote", RowHeightPx);
            ApplyRowSurface(row);
            var t = ElarionUiKit.Label(row, ManageScreenVM.Ascii(text), 0f, 1f, ElarionUi.Parchment,
                                       (int)ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.02f, 0.74f);
            ElarionUiKit.FitSingleLine(t);
            var b = ElarionUiKit.BuildObsidianButton(row, action,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                // WO-1058: the Repair offer's CTA already sat on the primary band; it now names it
                // by constant so a future move of the slot moves every row type at once.
                new Vector2(PrimaryX0, RowCtrlY0), new Vector2(PrimaryX1, RowCtrlY1), () => onTap?.Invoke());
            ElarionUiKit.ClampMinTouch(b);
        }

        /// <summary>
        /// ⭐ Panel 8's tab row: BUILDERS (2/2) / TRAINING (2/2) / RESEARCH (2/2), the active one
        /// gold. It replaces the single "IN QUEUE - BUILDERS" header, which named the ONE line the
        /// drawer could show - a player could not reach another channel's queue from inside the
        /// overlay at all.
        /// <para>⛔ THE LABEL AND THE COUNT ARE BOTH THE MODEL'S. This paints
        /// <c>tab.Label</c> and <c>tab.CountText</c> and joins them; it never counts jobs, never
        /// spells a channel name, and never decides which tab is active
        /// (<c>ManageScreenVM.ComposeQueueTabs</c> reads ChannelSummary.Busy / .Slots - the same
        /// source the three-line status strip reads, so the two can never disagree).</para>
        /// <para>Active reads as GOLD **and** as the only underlined face - shape as well as hue,
        /// because the owner is red/green colourblind.</para>
        /// </summary>
        private void BuildQueueTabs(RectTransform host)
        {
            if (host == null || _vm == null) return;
            // ⚠ THE ROW STOPS SHORT OF THE X'S COLUMN. The audit found
            //   'ManageQueueOverlayClose' (x 453..573) covers 'ManageQueueTab_Research/Label'
            //   ("RESEARCH 2/2") (x 224..563) by 110x31px
            // The two zones are disjoint in Y once the title band is tall enough to hold the X
            // (see SetDrawerBands), but the X is a 120px square in the top-RIGHT and the tab row
            // spans the full width - so the last tab sat under its corner. The band table fixes the
            // vertical half; this fixes the horizontal one, and it costs the tabs almost nothing.
            // ⚠ IT ONLY FIRED ON BUILD_queue: the ARMY drawer runs in BAND mode, where the title,
            // the X and now the tabs all stand down (ApplyDrawerPlacement). A fault that appears on
            // one tab of three is worth understanding before fixing - here it confirmed the X was
            // the collider rather than the tabs being mis-seated.
            // ⭐ WO-1488: THE RESERVED COLUMN IS GONE BECAUSE THE X IS GONE. The stop was 0.86 to
            // hold a 120px X off the last tab; the X now lives in the title overlay at the drawer's
            // top-right (BuildQueueDrawer), so 14% of the band was about to become dead space -
            // and dead space beside three faces that must each clear MinTouchPx is exactly what
            // this file keeps paying for. The collision the 0.86 fixed cannot recur: the two
            // controls are no longer in the same zone.
            const float TabsRightStop = 1.0f;
            var tabs = _vm.QueueTabs;
            if (tabs == null || tabs.Count == 0)
            {
                FlowTrace.Warn("Manage", "the queue overlay has no tabs - ComposeQueueTabs returned " +
                    "nothing, so the player can reach only the line the drawer opened on");
                return;
            }
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (t == null) continue;
                float w = TabsRightStop / tabs.Count;
                float tx0 = i * w;
                var captured = t;
                // The whole segment remains a full-height compliant touch target. Its visible
                // treatment is deliberately flat: these are a status rail, not three competing
                // primary CTAs.
                var btn = ElarionUiKit.BuildObsidianButton(host, t.Label ?? string.Empty,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    t.IsActive ? ElarionUiKit.ObsidianButtonColor.Yellow
                               : ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(tx0 + 0.006f, 0f), new Vector2(tx0 + w - 0.006f, 1f),
                    () => captured.Activate?.Invoke());
                if (btn == null) continue;
                btn.gameObject.name = "ManageQueueTab_" + t.Channel;
                MedievalUiSkin.ApplyButton(btn, t.IsActive);
                ElarionUiKit.ClampMinTouch(btn);
                var tabImage = btn.GetComponent<Image>();
                if (tabImage != null)
                {
                    tabImage.sprite = null;
                    tabImage.type = Image.Type.Simple;
                    tabImage.color = t.IsActive
                        ? new Color(0.18f, 0.145f, 0.07f, 0.98f)
                        : new Color(0.075f, 0.068f, 0.055f, 0.96f);
                }
                // ⭐ WO-1488 - THE ACTIVE LINE PLATE READS AS ACTIVE, BY FILL AND WEIGHT.
                // MEASURED on the owner's device (owner-screen-20260907-010356.png and -010257.png):
                // all three plates render identically, on both the BUILD tab and the RESEARCH tab,
                // so the overlay never says which line the rows below belong to. The colour lever
                // was already in place and could not carry it: MedievalUiSkin.ApplyButton's
                // `primary` arm tints the plate (1.08, 1.03, 0.88) - about 8% of luminance, which
                // is under the threshold on a dark plate and is invisible to a red/green
                // colourblind reader either way.
                // ⛔ SO THE CUE IS SHAPE + WEIGHT + INK, NEVER HUE ALONE: the active face is BOLD
                // gold with an underline; the inactive faces are regular and dim. All three
                // channels are readable in greyscale, which is the law on this project.
                // ⚠ `tabLabel`, NOT `face`. The composed BUTTON TEXT is already a `string face` in
                // this same scope (the `t.Label + " " + t.CountText` line above), so a second
                // `face` here is CS0128 and every later `.color` / `.fontStyle` binds to the
                // string instead (CS1061). Caught at the gate, Builds/cg-wave4a.log.
                var tabLabel = btn.GetComponentInChildren<TMP_Text>(true);
                if (tabLabel != null)
                {
                    tabLabel.color = t.IsActive ? ElarionUi.Gold : ElarionUi.ParchmentDim;
                    if (t.IsActive) tabLabel.fontStyle |= FontStyles.Bold;
                    else tabLabel.fontStyle &= ~FontStyles.Bold;
                    tabLabel.alignment = TextAlignmentOptions.Left;
                    var labelRt = tabLabel.rectTransform;
                    labelRt.anchorMin = new Vector2(0.08f, 0.16f);
                    labelRt.anchorMax = new Vector2(0.69f, 0.84f);
                    labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
                    ElarionUiKit.FitSingleLine(tabLabel, 26f, 36f);
                }
                if (!string.IsNullOrEmpty(t.CountText))
                {
                    var countChip = ElarionUiKit.AddImage(btn.transform, "CapacityChip",
                        new Vector2(0.72f, 0.24f), new Vector2(0.94f, 0.76f),
                        t.IsActive ? new Color(0.42f, 0.30f, 0.08f, 1f)
                                   : new Color(0.13f, 0.12f, 0.10f, 1f), rounded: true);
                    var countChipImage = countChip != null ? countChip.GetComponent<Image>() : null;
                    if (countChipImage != null) countChipImage.raycastTarget = false;
                    if (countChip != null)
                    {
                        var count = ElarionUiKit.Label(countChip.transform, t.CountText, 0.05f, 0.95f,
                            t.IsActive ? ElarionUi.Gold : ElarionUi.ParchmentDim,
                            30, TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
                        count.raycastTarget = false;
                        ElarionUiKit.FitSingleLine(count, 24f, 30f);
                    }
                }
                if (t.IsActive)
                {
                    var bar = ElarionUiKit.AddImage(btn.transform, "ActiveLineUnderline",
                        new Vector2(0.02f, 0.06f), new Vector2(0.035f, 0.94f), ElarionUi.Gold);
                    var barImg = bar != null ? bar.GetComponent<Image>() : null;
                    if (barImg != null) barImg.raycastTarget = false;
                }
            }
        }

        /// <summary>
        /// ⭐ PRINT THE OVERLAY'S RESOLVED RECTS. The tab row was reported drawn ON TOP of row 1
        /// while its authored zone (0.685-0.885) and the list's (0.02-0.665) do not overlap at all -
        /// so one of them is not landing where this file thinks. That is not a fraction to adjust;
        /// it is a measurement to take, and this project has now twice ended a multi-round
        /// coordinate hunt by printing a rectangle instead of theorising about one
        /// (MANAGE_QUEUE_PILL_RECT, MANAGE_TITLE_RECT).
        /// <para>Every rect is printed in the SAME space - world corners - so they can be compared
        /// directly. Read this line before touching any drawer fraction.</para>
        /// </summary>
        private void TraceQueueOverlayLayout()
        {
            if (_queueDrawer == null) return;
            Canvas.ForceUpdateCanvases();
            FlowTrace.Step("Manage", "MANAGE_QUEUE_LAYOUT " +
                RectLine("drawer", _queueDrawer.transform as RectTransform) +
                RectLine("header", _drawerHeader) +
                RectLine("tabs", _drawerTabs) +
                RectLine("listView", _drawerList) +
                RectLine("listContent", _drawerContent));
        }

        /// <summary>
        /// One rect as "name[x0..x1 y0..y1 | local WxH]" in WORLD space, with the LOCAL size beside
        /// it. Null-safe and loud.
        /// <para>⚠ THE TWO ARE DIFFERENT UNITS AND MUST NOT BE SUBTRACTED FROM EACH OTHER. The
        /// world corners carry the canvas scale; <c>rect.width/height</c> do not. In the 2026-09-06
        /// capture listView printed world y 299..791 (a 492-unit span) beside a local height of
        /// 396px, and reading those as one number makes a healthy rect look 96px wrong. Compare
        /// WORLD to WORLD when you are looking for an overlap - which is what the tabs-over-rows
        /// finding correctly did - and LOCAL to LOCAL when you are checking a size against a px
        /// constant like RowHeightPx.</para>
        /// </summary>
        private static string RectLine(string name, RectTransform rt)
        {
            if (rt == null) return " " + name + "[NULL]";
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return " " + name + "[x " + c[0].x.ToString("0") + ".." + c[2].x.ToString("0") +
                   " y " + c[0].y.ToString("0") + ".." + c[2].y.ToString("0") +
                   " | local " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") + "]";
        }

        /// <summary>
        /// ⭐ THE LIST SEATS WHOLE ROWS. Its viewport is trimmed DOWN to a whole multiple of
        /// RowHeightPx so its bottom edge lands BETWEEN rows, never through one.
        /// <para>The capture showed "Refund: nothing" sliced mid-glyph at 2.8 rows. A part-row is
        /// not a scroll hint - it is a row whose text has been cut, the same defect the grid had
        /// before whole-row seating, and the same reason a text band under the cull floor is worse
        /// than an omitted one: a sliced line looks like a rendering fault, not like more content.</para>
        /// </summary>
        private void SeatQueueListToWholeRows()
        {
            if (_drawerList == null) return;
            Canvas.ForceUpdateCanvases();
            float have = _drawerList.rect.height;

            // ⛔ A ROW IS NOT RowHeightPx TALL ON SCREEN. IT IS RowHeightPx PLUS THE LAYOUT'S OWN
            // SPACING, INSIDE THE LAYOUT'S OWN PADDING - AND BOTH ARE READ BACK, NEVER ASSUMED.
            //
            // The first version trimmed to a multiple of RowHeightPx and the capture still showed
            // row 2 sliced mid-glyph with its progress bar gone. The trim was seating a whole
            // number of THE WRONG UNIT: MakeScrollZone gives the content a VerticalLayoutGroup with
            // spacing 8 and padding 10 on every side (ElarionUiKitObsidian.cs:3327-3331), so two
            // 132px rows actually need 10 + 132 + 8 + 132 + 10 = 292px, not 264px. The 28px
            // difference is exactly the refund line and the bar that were cut.
            //
            // ⚠ READ FROM THE LIVE COMPONENT, not from those numbers. They are MakeScrollZone's
            // defaults and this caller passes its own (spacing: 8f, padding: 10); copying either
            // into this file would be the duplicated state that has now cost this screen three
            // separate faults - the drawer's 0.86 literal, the band table locals, and this.
            float spacing = 0f, padTop = 0f, padBottom = 0f;
            var vlg = _drawerContent != null
                ? _drawerContent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() : null;
            if (vlg != null)
            {
                spacing = vlg.spacing;
                padTop = vlg.padding.top;
                padBottom = vlg.padding.bottom;
            }
            else
            {
                FlowTrace.Warn("Manage", "the queue list has no VerticalLayoutGroup to read its " +
                    "spacing and padding from - the whole-row trim falls back to bare row heights " +
                    "and the last row may clip");
            }

            // ⭐ WO-1488 (2026-09-07) — THE ROW HEIGHT IS DERIVED FROM THE MEASURED BAND, so the
            // mockup's FIVE visible rows are arithmetic instead of an aspiration.
            //
            // ⛔ AND IT IS DERIVED FROM `have`, WHICH IS THE LIVE VIEWPORT - never from a constant
            // describing a card that no longer exists. That is the ticketed cause: the drawer's
            // kept viewport was `DrawerModeListKeepPx = 10 + TroopWorkspacePx * (1 - TroopCtaY1)`,
            // i.e. a measurement of the RETIRED 260px troop card, pinned verbatim by
            // ManageQueueDrawerRegression. A number that measures a control the screen no longer
            // builds is duplicated state with nothing left to duplicate.
            //
            // THE FLOOR IS THE TOUCH FLOOR AND IT WINS. Five rows in a short well would resolve to
            // sub-60px rows carrying three text lines and three controls - every one of them under
            // MinTouchPx and under the TMP cull floor, i.e. five rows nobody can read or press.
            // So the derived height is clamped into [MinTouchPx, RowHeightPx], the whole-row trim
            // below then seats however many of THOSE fit, and the shortfall is named in px.
            float ideal = (have - padTop - padBottom - (QueueRowsVisibleTarget - 1) * spacing)
                          / QueueRowsVisibleTarget;
            _queueRowPx = Mathf.Clamp(ideal, ElarionUiKit.MinTouchPx, RowHeightPx);
            if (ideal < ElarionUiKit.MinTouchPx)
                FlowTrace.Warn("Manage", "the queue overlay seats " + have.ToString("0") +
                    "px of list, so " + QueueRowsVisibleTarget + " rows would be " +
                    ideal.ToString("0") + "px each - under ElarionUiKit.MinTouchPx (" +
                    ElarionUiKit.MinTouchPx.ToString("0") + "). Building at the floor and seating " +
                    "whole rows only: the mockup's five-row capacity needs a list band of at least " +
                    ((QueueRowsVisibleTarget * ElarionUiKit.MinTouchPx) +
                     ((QueueRowsVisibleTarget - 1) * spacing) + padTop + padBottom).ToString("0") +
                    "px. The WELL has to grow; nothing here will shrink a row under the touch floor " +
                    "to hide that.");
            else
                FlowTrace.Step("Manage", "MANAGE_QUEUE_ROWPX derived " + _queueRowPx.ToString("0") +
                    "px per row for " + QueueRowsVisibleTarget + " visible rows in a " +
                    have.ToString("0") + "px list band (spacing " + spacing.ToString("0") +
                    ", padding " + (padTop + padBottom).ToString("0") + ")");

            float pitch = _queueRowPx + spacing;           // what each row after the first costs
            float chrome = padTop + padBottom;
            if (have < chrome + _queueRowPx) return;       // less than one row: nothing to trim to

            // ⚠ THE EPSILON IS LOAD-BEARING. `_queueRowPx` is derived from `have` by the very
            // division this floor inverts, so when the band is sized for exactly N rows the ratio
            // lands ON N - and float returns 4.9999997 as readily as 5.0000002. Without it a band
            // authored to seat five seats four, and the shortfall WARN below fires on a screen that
            // had the room. One ULP, not padding: `have` is a measured rect, so a real shortfall is
            // always larger than this by orders of magnitude.
            const float QueueRowFitEpsilon = 0.001f;
            int whole = Mathf.FloorToInt((have - chrome + spacing) / pitch + QueueRowFitEpsilon);
            if (whole < 1) return;
            float want = chrome + whole * _queueRowPx + (whole - 1) * spacing;
            if (whole < QueueRowsVisibleTarget)
                FlowTrace.Warn("Manage", "the queue overlay seats " + whole + " whole row(s) where " +
                    "mockup panel 8 draws " + QueueRowsVisibleTarget + " - " + have.ToString("0") +
                    "px of list at " + _queueRowPx.ToString("0") + "px a row. The rows still " +
                    "scroll and NONE is clipped; the missing ones are under the fold, which is a " +
                    "well shortfall and not a layout preference.");
            if (have - want < 1f) return;                 // already whole

            // ⛔ TRIM THE BOTTOM EDGE UP. DO NOT TOUCH THE ANCHORS.
            // The first version collapsed anchorMin.y onto anchorMax.y and set a sizeDelta, which
            // re-hung the rect from its TOP and grew it UPWARD into the tab band. MEASURED by
            // MANAGE_QUEUE_LAYOUT: tabs [y 688..806] against listView [y 299..791] - a 103px
            // overlap between two authored zones (tabs 0.685-0.885, list 0.02-0.665) that CANNOT
            // overlap. The zones were right; the trim moved the wrong edge.
            // offsetMin.y is the bottom edge's offset from the BOTTOM anchor, so a positive value
            // raises the floor and leaves the ceiling exactly where the band table put it. No anchor
            // is written, so the authored fractions stay the single source of the seat.
            _drawerList.offsetMin = new Vector2(_drawerList.offsetMin.x,
                                                _drawerList.offsetMin.y + (have - want));
            FlowTrace.Step("Manage", "MANAGE_QUEUE_LIST seats " + whole + " whole rows: " +
                want.ToString("0") + "px of " + have.ToString("0") + "px (row " +
                _queueRowPx.ToString("0") + " + spacing " + spacing.ToString("0") +
                ", padding " + chrome.ToString("0") + ") - the bottom edge lands between rows");
        }

        private void AddQueueRow(QueueRowVM r)
        {
            // ⭐ WO-1488: the MEASURED row height, not the authored constant. See _queueRowPx.
            var row = MakeRowHost("QueueRow", _queueRowPx);
            ApplyRowSurface(row);

            // ⭐ WO-1567 ROUND 25 - THE CONTROL BAND IS DERIVED FROM THIS ROW'S MEASURED HEIGHT.
            // Read ONCE per row so every verb on it shares one band; see QueueCtrlY0 for the
            // captured 98.6px failure the fixed 0.88 fraction produced on forty controls.
            float ctrlY0 = QueueCtrlY0, ctrlY1 = QueueCtrlY1;
            FlowTrace.Once("Manage", "queue-row-ctrl-band",
                "MANAGE_QUEUE_CTRL row=" + _queueRowPx.ToString("0") + "px -> control band " +
                ctrlY0.ToString("0.###") + ".." + ctrlY1.ToString("0.###") + " = " +
                ((ctrlY1 - ctrlY0) * _queueRowPx).ToString("0") + "px (floor " +
                ElarionUiKit.MinTouchPx.ToString("0") + "). The fraction follows the MEASURED row; " +
                "the row is never grown to suit the fraction, because that costs a visible row.");

            // A stack CHILD is indented so the parent/child relationship reads structurally, not
            // by colour — the expanded items visibly belong to the xN header above them.
            float x0 = r.IsStackChild ? 0.06f : 0.02f;
            string label = (r.IsStackChild ? "- " : "") + ManageScreenVM.Ascii(r.Label ?? "");

            // ⭐ THE ROW NUMBER, mockup panel 8's "1. 2. 3." - painted in its own gutter so it can
            // never push the label. MODEL-SUPPLIED (QueueRowVM.OrdinalText): the view must not count
            // its own children, because expanding a stack changes how many ROWS exist without
            // changing the queue, and a view-side count would disagree with the engine on the spot.
            // A stack CHILD carries no number by design - the header holds the position.
            if (!string.IsNullOrEmpty(r.PositionText))
            {
                // Treat the queue as a timeline: NOW is the active node, numbered nodes follow.
                // The words are model-owned; the rail and marker are only their visual treatment.
                const float markerWidth = 0.058f;
                var track = ElarionUiKit.AddImage(row, "QueueTimelineTrack",
                    new Vector2(x0 + markerWidth * 0.48f, 0f),
                    new Vector2(x0 + markerWidth * 0.52f, 1f),
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.38f));
                var trackImage = track != null ? track.GetComponent<Image>() : null;
                if (trackImage != null) trackImage.raycastTarget = false;

                var marker = ElarionUiKit.AddImage(row, "QueueTimelineMarker",
                    new Vector2(x0, 0.28f), new Vector2(x0 + markerWidth, 0.72f),
                    r.Queued ? new Color(0.12f, 0.11f, 0.09f, 1f)
                             : new Color(0.34f, 0.24f, 0.06f, 1f), rounded: true);
                var markerImage = marker != null ? marker.GetComponent<Image>() : null;
                if (markerImage != null) markerImage.raycastTarget = false;
                if (marker != null)
                {
                    var markerText = ElarionUiKit.Label(marker.transform, r.PositionText, 0.06f, 0.94f,
                        r.Queued ? ElarionUi.ParchmentDim : ElarionUi.Gold,
                        28, TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
                    markerText.raycastTarget = false;
                    ElarionUiKit.FitSingleLine(markerText, 22f, 28f);
                }
                x0 += markerWidth + 0.010f;
            }

            // ⭐ WO-1488 SECTION 2 — THE ROW THUMBNAIL, the last open item on that ticket.
            // Mockup panel 8 draws a small picture of the thing being built between the number and
            // its name; the owner's capture has bare text where every row should carry one.
            // ⛔ ONE LOADER, THE ONE THE BUILD GRID USES. ManageArt.LoadSprite against the key the
            // MODEL supplies (QueueRowVM.PortraitKey, composed by ObsidianQueueVM from
            // ManageArt.BuildingPortraitKey) - never a second key producer and never a second
            // loader. The slug composer that used to make these keys is deleted (WO-1567 s5 i3).
            string thumbKey = r.PortraitKey;
            if (!string.IsNullOrEmpty(thumbKey))
            {
                var thumb = ManageArt.LoadSprite(thumbKey);
                if (thumb != null)
                {
                    var zone = MakeZone(row, "QueueRowThumb",
                        new Vector2(x0, 0.12f), new Vector2(x0 + 0.05f, 0.88f));
                    var img = ElarionUiKit.AddImage(zone, "Art", Vector2.zero, Vector2.one,
                        Color.white, rounded: false).GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = thumb;
                        img.preserveAspect = true;
                        img.raycastTarget = false;      // the ROW owns the tap
                    }
                    x0 += 0.06f;
                }
            }

            // THE ROW IS TWO COLUMNS, and they do not share pixels: TEXT left of x=0.44, CONTROLS
            // right of x=0.455. The left column is three stacked text lines (name / state / refund)
            // — build 1 put the refund line UNDER the button block at x 0.46-0.98 and the two
            // overprinted on every cancellable row.
            // WO-898 item 1 re-band. The three text lines shift UP inside the same row height to
            // free a strip at the bottom for the progress bar. Re-banding beats growing the row:
            // the list well is measured and clamps to 0px when the bands no longer fit, which
            // degrades to "headers and no rows" with only a trace line to explain it.
            //
            // WO-1058 CLIPPING PASS: the three bands below were authored at FontLabel(40) — a
            // ~46px line box — inside 34-37px bands, so every line bled over its band edge and the
            // owner's 2026-08-22 frame shows the title sheared. The bands are re-seated (see the
            // QRow* block) and each label is now capped at a size whose line box FITS, which is a
            // TEXT change, never a control one: MinTouchPx and the CTA boxes are untouched.
            var name = ElarionUiKit.Label(row, label, QRowNameY0, QRowNameY1, ElarionUi.Parchment,
                                          (int)QueueNameFontPx, TextAlignmentOptions.Left, x0, QueueTextX1, bold: true);
            ElarionUiKit.FitSingleLine(name, 0f, QueueNameFontPx);
            var state = ElarionUiKit.Label(row, ManageScreenVM.Ascii(r.StateText ?? ""), QRowStateY0, QRowStateY1,
                                           ElarionUi.ParchmentDim, (int)QueueLineFontPx,
                                           TextAlignmentOptions.Left, x0, QueueTextX1);
            // WO-1488: the TIMER line, fitted to its own floor. `0f` here resolved to FontFloor(30)
            // against a 32px max - two points of headroom - and the capture ellipsised at "(0% do...".
            ElarionUiKit.FitSingleLine(state, QueueStateFontFloorPx, QueueLineFontPx);

            // The bar itself. Drawn only for a job with a known duration (Progress01 >= 0), and
            // deliberately NOT for a collapsed stack header, which stands for several jobs at
            // different points and would have to lie about one number.
            //
            // COLOURBLIND LAW: the fill is never the only signal - StateText already carries the
            // percentage in words ("Building - 2m 10s left (63% done)"), so the row reads correctly
            // with the bar ignored entirely.
            if (r.Progress01 >= 0f && !r.IsStackHeader)
            {
                var bar = ElarionUiKit.Bar(row, ElarionUiKit.BarKind.Castle,
                                           new Vector2(x0, QRowBarY0), new Vector2(QueueTextX1, QRowBarY1));
                if (bar?.fill != null)
                {
                    bar.fill.fillAmount = Mathf.Clamp01(r.Progress01);
                    bar.fill.raycastTarget = false;
                }
                if (bar?.track != null) _progressCells.Add(new ProgressCell
                {
                    Handle = bar,
                    Channel = r.Channel,
                    JobId = r.JobId,
                    Queued = r.Queued,
                });
            }

            if (r.JobId != null && state != null)
                _tickCells.Add(new TickCell
                {
                    Text = state,
                    Channel = r.Channel,
                    JobId = r.JobId,
                    Queued = r.Queued,
                    PendingIndex = r.PendingIndex,
                });

            if (r.IsStackHeader)
            {
                // ⚠ RULING Q12 — A COLLAPSED xN CARD HAS NO CANCEL AND NO PAID FINISH.
                // Owner, verbatim: "can not cancel on a collapsed card, must expand then select
                // item to cancel and others automatically move up." A destructive or paid verb must
                // never act on an ambiguous aggregate (the same principle as Q11). The ONLY control
                // here is the expander; cancel appears on the individual children it reveals, and
                // the remaining items close the gap by themselves.
                //
                // WO-1058: the expander IS this row's primary, so it takes the PRIMARY SLOT like
                // every other row's primary. It used to start at 0.62 and straddle the slot — a
                // second tap landing on a stack header then hit an ambiguous strip. Now the whole
                // slot is one harmless, non-spending verb.
                string key = r.StackKey;
                var expand = ElarionUiKit.BuildObsidianButton(row, r.Expanded ? "Collapse" : "Expand x" + r.StackCount,
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(PrimaryX0, ctrlY0), new Vector2(PrimaryX1, ctrlY1),
                    () => _vm?.ToggleStack(key));
                ElarionUiKit.ClampMinTouch(expand);
                StyleQueueAction(expand, new Color(0.11f, 0.10f, 0.085f, 1f), ElarionUi.Parchment, false);
                return;
            }

            string jobId = r.JobId;
            var channel = r.Channel;

            // FINISH NOW — offered on Builder, Train AND Research, on RUNNING and QUEUED jobs
            // alike (rulings Q5 + the "all channels" rule), and ALWAYS SHOWN while the job exists,
            // including when the player cannot afford it. The price is on the face as TEXT.
            if (r.FinishPrice > 0)
            {
                // TWO-LINE CTA (owner felt-test 2026-08-08): verb on top, cost UNDERNEATH in a
                // smaller font. The old face was "Finish 5c" / "Finish 5c (short)" — "5c" assumed
                // the player already knew that c meant crystals AND that the price scales with the
                // time remaining, and "(short)" silently meant "you cannot afford this" while
                // reading like part of the price. Both strings are the VM's (FinishCostText); this
                // only renders them.
                //
                // WO-1058: this is THE PRIMARY SLOT — the same strip of glass the browse row's
                // `Upgrade` occupies, so the owner's "tap, tap again" gesture lands on the verb she
                // wants without moving her finger. The verb reads "Finish Now" (not "Finish")
                // because in the primary slot it is answering the question the previous tap asked;
                // the cost line under it is unchanged and is what makes the second tap non-blind.
                // WO-1372: the VERB is the VM's (r.FinishVerbText) - Finish Now on every channel
                // that pays crystals, and the canon HIRE REINFORCEMENTS on a gold-priced training
                // job (creative canon §6). The View still only renders; it does not decide currency.
                // The literal below is a REAL fallback, not decoration: a row built by older code
                // (or a future one that forgets the field) must not render a BLANK primary face.
                string finishVerb = string.IsNullOrEmpty(r.FinishVerbText) ? "Finish Now" : r.FinishVerbText;
                var fin = BuildTwoLineCta(row, finishVerb, r.FinishCostText,
                    r.CanAffordFinish ? ElarionUiKit.ObsidianButtonColor.Yellow : ElarionUiKit.ObsidianButtonColor.Gray,
                    new Vector2(PrimaryX0, ctrlY0), new Vector2(PrimaryX1, ctrlY1),
                    () => { _vm?.FinishNow(channel, jobId); FlushNotice(); });
                ElarionUiKit.ClampMinTouch(fin);
                StyleQueueAction(fin,
                    r.CanAffordFinish ? new Color(0.25f, 0.18f, 0.045f, 1f)
                                      : new Color(0.105f, 0.095f, 0.08f, 1f),
                    r.CanAffordFinish ? ElarionUi.Gold : ElarionUi.ParchmentDim, true);
            }

            // ── THE SECONDARY CLUSTER (WO-1058) ──────────────────────────────────────────
            // Everything that is NOT the primary lives LEFT of the dead gap, evenly split so no
            // control is authored under MinTouchPx. Order is fixed — Ad, Cancel, Move up — which
            // puts `Move up` between `Cancel` and the primary slot: the destructive control is
            // never adjacent to the one the player is double-tapping.
            bool wantAd = r.AdAvailable && DeNelle.Core.FeatureFlags.RewardedAdSkip;
            Vector2 slotMin, slotMax;
            // ⭐ WO-1488: the AD CHIP IS COMPACT AND IT PAYS FOR THE FULL WORD "CANCEL".
            // An even split gave a two-letter face the same ~131px as a six-letter one, and the
            // owner's capture reads "CANC...". The chip takes exactly MinTouchPx (AdChipWidthX)
            // and the word controls share what is left. The AD OFFER ITSELF IS UNCHANGED and is
            // still the MODEL's per-channel answer (WO-911's CanWatchAdToSkip(ChannelId, ...);
            // see AdChipWidthX for the citation) - this is a WIDTH, not a gate.
            float wordSpan = ClusterX1 - ClusterX0 - (wantAd ? AdChipWidthX + ClusterGapX : 0f);
            int wordCount = (r.CanCancel ? 1 : 0) + (r.CanBumpUp ? 1 : 0);
            float wordX0 = ClusterX0 + (wantAd ? AdChipWidthX + ClusterGapX : 0f);
            int wordIdx = 0;

            // THE "Ad" CONTROL IS NEVER CONSTRUCTED while FeatureFlags.RewardedAdSkip is OFF —
            // absent, not present-and-disabled. The VM and BuildTimerService gate on the same
            // flag; this is the build site, so it is the one that guarantees absence. Its slot is
            // RESERVED by the even split (it simply is not counted while the flag is off).
            //
            // ⚠ CORRECTED 2026-09-04 (WO-1368 §15). The 2026-08-07 version of this comment called
            // the flag OFF and claimed the project contained no ad SDK at all. BOTH HALVES ARE
            // FALSE: FeatureFlags.RewardedAdSkip is declared defaultOn:true, and LevelPlay /
            // ironSource is integrated (canon records real, if tiny, ad revenue). A seat trusting
            // it would go hunting for a flag that is already on. If `Ad` is absent while a job is
            // queued, the flag is NOT the suspect — BuildTimerService.CanWatchAdToSkip ALSO
            // requires AdGateService.IsOffered(BuildSkipPlacementId) and a non-null
            // RewardedAdManager.Instance with IsAdReady, and either can withhold r.AdAvailable
            // while Finish Now renders perfectly. That gap is REPORTED, not widened here.
            if (wantAd)
            {
                // FIRST in the cluster, as the WO-1058 order requires (Ad, Cancel, Move up), and
                // exactly AdChipWidthX wide - see that constant for the width and the ad ruling.
                slotMin = new Vector2(ClusterX0, ctrlY0);
                slotMax = new Vector2(ClusterX0 + AdChipWidthX, ctrlY1);
                var ad = ElarionUiKit.BuildObsidianButton(row, "Ad",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Green,
                    slotMin, slotMax,
                    () => { _vm?.WatchAd(channel, jobId); FlushNotice(); });
                ElarionUiKit.ClampMinTouch(ad);
                StyleQueueAction(ad, new Color(0.075f, 0.18f, 0.11f, 1f), ElarionUi.Parchment, false);
            }

            if (r.CanCancel)
            {
                // Refund is 100% flat (ruling Q1) and the face SAYS what comes back, so the player
                // never has to infer it from a colour or a number that appears after the fact.
                // WO-1058 moved the BOX, not the promise: same Red face, same refund line, and it
                // is now the FURTHEST control from the primary slot instead of sitting inside it.
                WordSlot(wordIdx++, wordCount, wordX0, wordSpan, ctrlY0, ctrlY1, out slotMin, out slotMax);
                // ⭐ THE FULL WORD, IN CAPS, like every other verb the mockup draws on this
                // overlay. It read "CANC..." on the owner's device because six letters were being
                // asked to fit a slot sized for two; the slot is the thing that changed.
                var cancel = ElarionUiKit.BuildObsidianButton(row, "CANCEL",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Red,
                    slotMin, slotMax,
                    () => { _vm?.Cancel(channel, jobId); FlushNotice(); });
                ElarionUiKit.ClampMinTouch(cancel);
                StyleQueueAction(cancel, new Color(0.20f, 0.065f, 0.055f, 1f), ElarionUi.Parchment, false);

                // Third line of the TEXT column (never under the buttons — see the two-column note).
                //
                // !! THE VIEW NO LONGER DECIDES WHETHER THE PLAYER IS TOLD (WO-1479). It used to
                // build the sentence itself - prefix "Refund: " onto the model's bare basket, then
                // string-match that text against "nothing" and suppress the line when it matched.
                // Two model rules living in a skin, and the second one hid the case that hurts: a
                // job with no paid basket showed a BARE CANCEL, so the one player who gets nothing
                // back was the one player told nothing. The reasoning behind the old suppression
                // still stands and is where it belongs - ObsidianQueueVM.QuoteRefund composes the
                // real line AND the zero wording, so the row states the consequence either way and
                // "Refund: nothing" (a sentence that says neither) never appears.
                // !! Still the TEXT COLUMN and not the button face: the cluster gives ~122-192 ref px
                // per control and "Refund: 120 wood, 40 iron" needs several times that at
                // ElarionUiKit.FontFloor - it would ellipsise, which is the "HIRE REIN..." failure
                // MakeJobRow's own comment records. It is beside the Cancel that causes it, and the
                // player reads it before the finger arrives.
                string refundText = ManageScreenVM.Ascii(r.RefundText ?? "");
                if (!string.IsNullOrEmpty(refundText))
                {
                    var refund = ElarionUiKit.Label(row, refundText,
                                                    QRowRefundY0, QRowRefundY1, ElarionUi.ParchmentDim,
                                                    (int)QueueLineFontPx, TextAlignmentOptions.Left, x0, QueueTextX1);
                    // ⛔ WO-1651: THE REFUND NOTE IS FITTED TO THE ROW'S OWN FLOOR, EXACTLY LIKE THE
                    // TIMER LINE TWO BLOCKS UP. It read `0f` until 2026-09-10, and `0f` resolves to
                    // the kit's FontFloor (30) against a 32px max - the SAME two-points-of-headroom
                    // bug WO-1488 already fixed on `state`, left standing on the sibling that shares
                    // its band height.
                    // MEASURED, Builds/wave5-manageflow1 (2026-09-10), the same finding four times on
                    // each of ManageFlow_{BUILD,ARMY,RESEARCH}_queue_2670x1200 and CLEAN at 1920 and
                    // 2340: "No refund - nothing was paid for this job" drew ZERO of 33 printable
                    // glyphs - a WHOLE-LINE CULL, not an ellipsis - at rect y -54.3..-22.4, i.e. a
                    // 31.9 px band, font 30 [autosize 30..32].
                    // ⭐ WHY ONLY 2670, ARITHMETIC THAT CLOSES ON THE MEASUREMENT: the row height is
                    // _queueRowPx = Clamp(ideal, ElarionUiKit.MinTouchPx, RowHeightPx), and at this
                    // aspect it is pinned to the TOUCH FLOOR, 112. This band is
                    // QRowRefundY1 - QRowRefundY0 = 0.285 of the row, and 0.285 x 112 = 31.92 px -
                    // the captured 31.9 to a tenth of a pixel. At 1920/2340 `ideal` clears the floor,
                    // the row is taller, and the same fraction seats the line.
                    // ⛔ SO IT IS THE **ONE**-LINE NEED THAT IS UNMET, NOT A TWO-LINE NEED, AND A
                    // WRAP WOULD BE THE WRONG SHAPE - two lines want ~2x this band inside 31.9 px.
                    // The line factor is bounded by this run's OWN two labels: the `name` label sits
                    // in a 0.317 x 112 = 35.5 px band at the same 30 floor and renders (so 30f <=
                    // 35.5, f <= 1.183), while this one culls (so 30f > 31.9, f > 1.063). At the
                    // row's own floor of 24 the line box is at most 24 x 1.183 = 28.4 px against
                    // 31.9 px of band - it clears by >= 3.5 px (+11%).
                    // ⛔ NO FLOOR IS LOWERED BELOW THE KIT'S: QueueStateFontFloorPx is 24, above
                    // ElarionUiKit.FontHardFloor (20), and it is the floor this very row already
                    // uses one label away. No player copy is shortened.
                    // ⚠ WIDTH IS A SEPARATE AXIS AND IS **NOT** PROVEN HERE. The oracle logs a rect
                    // only for labels it FAILS, so no run has measured this sentence's width at 24;
                    // if the next capture ellipsises it horizontally in the 426.6 px lane that is a
                    // NEW finding with its own lever (x0 / QueueTextX1), not this one coming back.
                    ElarionUiKit.FitSingleLine(refund, QueueStateFontFloorPx, QueueLineFontPx);
                }
            }

            if (r.CanBumpUp)
            {
                int idx = r.PendingIndex;
                WordSlot(wordIdx++, wordCount, wordX0, wordSpan, ctrlY0, ctrlY1, out slotMin, out slotMax);
                var up = ElarionUiKit.BuildObsidianButton(row, "Move up",
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    slotMin, slotMax,
                    () => { _vm?.BumpUp(channel, jobId, idx); FlushNotice(); });
                ElarionUiKit.ClampMinTouch(up);
                StyleQueueAction(up, new Color(0.11f, 0.10f, 0.085f, 1f), ElarionUi.Parchment, false);
            }
        }

        /// <summary>
        /// Queue verbs share the row's full-height touch floor, but they no longer need the visual
        /// weight of four ornate plates. A flat surface keeps every hit box unchanged and keeps the
        /// two-line finish price visibly inside the same card as its verb.
        /// </summary>
        private static void StyleQueueAction(Button button, Color fill, Color ink, bool primary)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = fill;
            }
            var labels = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].color = ink;
                if (!primary)
                {
                    labels[i].fontSize = 34f;
                    ElarionUiKit.FitSingleLine(labels[i], 28f, 34f);
                }
            }
            if (primary) ElarionUiKit.GoldPerimeter((RectTransform)button.transform);
        }

        // =====================================================================
        //  Two-line CTA (verb over cost)
        // =====================================================================

        /// <summary>Verb line size. Below FontBody(50) because it shares the box with a second
        /// line, and comfortably over the kit's mobile floor (<c>ElarionUiKit.FontFloor</c> = 30).</summary>
        private const float CtaVerbPx = 42f;

        /// <summary>Cost line size — SMALLER than the verb, as the owner asked, but still 2px over
        /// the floor. "Smaller" means smaller than the verb, never small enough to fail the floor.</summary>
        private const float CtaSubPx = 32f;

        // Band split inside the button box. At RowHeightPx 132 the control band (RowCtrlY0..Y1 =
        // 0.88) resolves to 116 reference px, so:
        //   verb 0.50-0.96 -> 0.46 * 116 = 53.4px, holding a 42px line box (~48px)   OK
        //   cost 0.06-0.46 -> 0.40 * 116 = 46.4px, holding a 32px line box (~37px)   OK
        // 99.8px of the 116 is spent, leaving ~16px of air top and bottom. The button's own touch
        // floor is unaffected: 116 >= MinTouchPx (112), so ClampMinTouch never grows it.
        // ⚠ RE-CHECKED AT THE OTHER END OF THE RANGE (WO-1567 round 25). A QUEUE row sits at the
        // MinTouchPx floor, where QueueCtrlY0 gives the control the WHOLE row - so the box is 112px,
        // not 116, and these two bands resolve 4px tighter:
        //   verb 0.46 * 112 = 51.5px, holding a 42px line box   OK
        //   cost 0.40 * 112 = 44.8px, holding a 32px line box   OK
        // Both stay far above the ~24px band under which TMP culls a line outright, so the touch
        // fix costs the two-line CTA nothing it needed. Stated rather than assumed, because the
        // paragraph above reasons from 116 and 116 is no longer the only box this method is given.
        private const float CtaVerbY0 = 0.50f, CtaVerbY1 = 0.96f;
        private const float CtaSubY0  = 0.06f, CtaSubY1  = 0.46f;

        /// <summary>
        /// An Obsidian CTA carrying a VERB over a smaller SUB-LINE, e.g. "Finish" / "5 crystals".
        ///
        /// Built here rather than in the kit because no kit button has a sub-label affordance — its
        /// <c>BuildObsidianButton</c> stamps ONE label across the whole face and FitSingleLine's it.
        /// This reuses that button whole (art, tint feedback, contrast law, touch floor) and only
        /// RESEATS the label it already made into the upper band, then adds the second line beneath
        /// in the SAME ink — so the sub-line inherits the kit's face-vs-label contrast rule instead
        /// of re-deriving it. If a two-line CTA is ever wanted elsewhere, THIS is the thing to lift
        /// into the kit; until then a second caller is the trigger, not a guess.
        ///
        /// COLOURBLIND LAW: the affordable/unaffordable difference is carried by the sub-line's TEXT
        /// ("5 crystals" vs "Short 3 crystals"). The Yellow/Gray face is a redundant second signal,
        /// never the only one — the owner is red/green colourblind.
        ///
        /// Both lines are floored at <c>ElarionUiKit.FontFloor</c> (30): FitSingleLine may shrink
        /// each toward the floor to fit the width, but can never take either below it — it
        /// ellipsizes instead of going sub-legible.
        /// </summary>
        private Button BuildTwoLineCta(Transform parent, string verb, string subLine,
            ElarionUiKit.ObsidianButtonColor color, Vector2 anchorMin, Vector2 anchorMax, Action onClick)
        {
            var btn = ElarionUiKit.BuildObsidianButton(parent, ManageScreenVM.Ascii(verb ?? ""),
                ElarionUiKit.ObsidianButtonStyle.Style1, color, anchorMin, anchorMax, onClick);
            if (btn == null) return null;

            // No sub-line to add: leave the kit's single centred label exactly as built.
            string sub = ManageScreenVM.Ascii(subLine ?? "");
            if (string.IsNullOrEmpty(sub)) return btn;

            var primary = btn.GetComponentInChildren<TMP_Text>();
            if (primary == null)
            {
                // The button exists but carries no label — the verb would be invisible and the cost
                // would have nothing to sit under. Say so rather than silently shipping a blank face.
                FlowTrace.Warn("Manage",
                    "two-line CTA '" + verb + "': the kit button has no TMP label, so the cost line '" +
                    sub + "' was not drawn. The face shows art only.");
                return btn;
            }

            var prt = primary.rectTransform;
            prt.anchorMin = new Vector2(prt.anchorMin.x, CtaVerbY0);
            prt.anchorMax = new Vector2(prt.anchorMax.x, CtaVerbY1);
            prt.offsetMin = new Vector2(prt.offsetMin.x, 0f);
            prt.offsetMax = new Vector2(prt.offsetMax.x, 0f);
            primary.fontSize = CtaVerbPx;
            // ⭐ GIVE THE VERB THE BUTTON'S FULL WIDTH BEFORE FITTING IT. Same finding as the QUEUE
            // pill: BuildObsidianButton's PREFAB path seats the label from the authored prefab, and
            // its ornate caps eat an inset this file cannot see, so a long verb hits the font floor
            // and ellipsises while the face still looks half empty. The capture read "HIRE REIN...".
            // Widening the LABEL inside the button costs its neighbours nothing - CANCEL sits in its
            // own box - and recovers the caps' inset for the word.
            prt.anchorMin = new Vector2(0.02f, prt.anchorMin.y);
            prt.anchorMax = new Vector2(0.98f, prt.anchorMax.y);
            prt.offsetMin = new Vector2(0f, prt.offsetMin.y);
            prt.offsetMax = new Vector2(0f, prt.offsetMax.y);
            ElarionUiKit.FitSingleLine(primary, ElarionUiKit.FontFloor, CtaVerbPx);

            // ⚠ AND IF IT STILL DOES NOT FIT, SAY SO IN PX RATHER THAN ELLIPSISING IN SILENCE.
            // "HIRE REINFORCEMENTS" is CANON (creative canon 6, BuildTimerService.HireReinforcementsVerb)
            // and the font floor is a floor, so if the word cannot seat at ElarionUiKit.FontFloor in
            // this box the answer is a ruling - a shorter canon verb, or a wider primary slot at
            // CANCEL's expense - not a quieter failure. Neither is this file's call to make.
            Canvas.ForceUpdateCanvases();
            float haveW = prt.rect.width;
            float needW = primary.GetPreferredValues(primary.text).x;
            if (haveW > 1f && needW > haveW)
                FlowTrace.Warn("Manage", "CTA verb '" + primary.text + "' needs " + needW.ToString("0") +
                    "px and its box gives " + haveW.ToString("0") + "px at the " +
                    ElarionUiKit.FontFloor + "px font floor - it will ELLIPSISE. The word is canon; " +
                    "this needs a ruling (shorter verb, or a wider primary slot), not a smaller font");

            var cost = ElarionUiKit.Label(btn.transform, sub, CtaSubY0, CtaSubY1,
                                          primary.color, (int)CtaSubPx,
                                          TextAlignmentOptions.Center, 0.04f, 0.96f);
            cost.raycastTarget = false;                 // the whole face stays one tap target
            ElarionUiKit.FitSingleLine(cost, ElarionUiKit.FontFloor, CtaSubPx);
            return btn;
        }

        // ⛔ WO-1422 - AddBrowseRow AND BuildBrowseRowContent WERE DELETED HERE, DELIBERATELY.
        // They painted the paged text list ("Lumber Mill - Improved Logging  Ready - takes 11m 0s
        // [RESEARCH]") that Defence and Research used to share. All four destinations now build a
        // rail + selected card, so AddBrowseRow's ONE call site - the pager inside RenderList -
        // went away with it and both methods had zero callers. Dead code that looks like a shipped
        // feature is the exact failure ManageQueueDrawerRegression:103-113 was written to catch,
        // so they are gone rather than parked. The LOCK treatment they carried (r.Locked + the
        // BuildLockBadge padlock, WO-1390) MOVED to BuildResearchCard, which is the only surface
        // that ever showed a locked browse row. _vm.BrowseRows itself is UNCHANGED and still built
        // by the VM: three suites drive it, and the Troops "Saved army compositions" row reads it.
        private static void ApplyRowSurface(RectTransform row)
        {
            if (row == null) return;
            var image = row.GetComponent<Image>() ?? row.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = new Color(0.92f, 0.88f, 0.76f, 0.96f);
            image.raycastTarget = false;
        }

        // =====================================================================
        //  NOTICES + THE CHEAP TICK
        // =====================================================================

        private void FlushNotice()
        {
            if (_vm == null || string.IsNullOrEmpty(_vm.Notice)) return;
            string msg = ManageScreenVM.Ascii(_vm.Notice);
            bool broke = _vm.NoticeIsBrokeCase;
            _vm.ClearNotice();

            // In-panel first (the toast sorts below this modal), and traced either way so a headless
            // capture proves the outcome the player was shown.
            if (_noticeLabel != null) _noticeLabel.text = msg;
            FlowTrace.Step("Manage", "notice: " + msg);

            if (broke)
            {
                // The owner's broke-case rule: never a silent no-op — offer the route to crystals.
                // The store panel takes the screen, so the notice above is already on record.
                FlowTrace.Step("Manage", "broke case -> routing to the crystal store.");
                _vm.OpenCrystalStore();
            }
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Time.unscaledTime < _tickAt) return;
            _tickAt = Time.unscaledTime + 1f;

            // CHEAP TICK: strings only. No row is destroyed, no layout is rebuilt, the rail
            // self-syncs. Rows come back only on QueueChanged.
            var svc = BuildTimerService.Instance;
            if (svc == null) return;
            for (int i = 0; i < _tickCells.Count; i++)
            {
                var cell = _tickCells[i];
                if (cell.Text == null) continue;
                double rem = svc.RemainingSeconds(cell.Channel, cell.JobId);
                // Ordinal("3rd"), matching the VM's build-time string. The tick used to write a raw
                // int here, so every row silently lost its ordinal one second after being built.
                cell.Text.text = ManageScreenVM.QueueTimingText(
                    svc, cell.Channel, cell.JobId, cell.Queued, rem);
            }

            // WO-1382: the TRAINING NOW band's short countdown ("32s left"), same tick, strings only.
            for (int i = 0; i < _trainingNowCells.Count; i++)
            {
                var cell = _trainingNowCells[i];
                if (cell.Text == null) continue;
                cell.Text.text = ManageScreenVM.FormatTime(svc.RemainingSeconds(cell.Channel, cell.JobId)) + " left";
            }

            // WO-898 item 1: advance the fills on the same tick as the timers.
            for (int i = 0; i < _progressCells.Count; i++)
            {
                var pc = _progressCells[i];
                if (pc.Handle?.fill == null) continue;
                if (pc.Queued) continue;   // a queued job is 0% until it starts
                pc.Handle.fill.fillAmount = ManageScreenVM.ProgressOfLive(svc, pc.Channel, pc.JobId);
            }
            // Unity's null operator, NOT ?. — a rail destroyed by a list rebuild is C#-non-null but
            // Unity-null, and ?. would call Sync() straight into a MissingReferenceException.
            if (_rail != null) _rail.Sync();
        }
    }
}

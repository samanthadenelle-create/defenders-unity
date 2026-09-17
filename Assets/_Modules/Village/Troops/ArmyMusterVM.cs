// =============================================================================
// ArmyMusterVM — the Armies loadout-bank / training-order panel's PURE ViewModel
// (WO-1512). Extracted from ArmyMusterPanel, which had been holding the MODEL
// itself: `private static readonly ArmyComposition s_composition` lived on the
// View, and every command (train, save slot, select slot, quick-fill, rename,
// step a troop count) mutated it in place and called ArmyMusterService /
// ArmyLoadoutService directly.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS FILE EXISTS (ARCHITECTURE_PRINCIPLES.md §1/§2 — presentation is a
// separate layer that NEVER touches the objects): a View that OWNS the staged
// army owns game state. Two consequences, both real rather than theoretical:
//   1. The composition's lifetime was the View's static field, so it survived
//      panel close, scene load and Reset with no owner — nothing could observe,
//      test or reset it except the panel itself.
//   2. Nothing could unit-test the muster transaction without building uGUI.
// The VM now owns the composition and every verb; the View paints and routes
// taps. Pattern copied from ManageScreenPanel / TroopTrainingVM — commands on
// the VM, `Changed` back to the View, no new idiom invented.
//
// PURE C#: implements DeNelle.Core.UI.Mvvm.IPanelViewModel and carries NO
// UnityEngine UI types. Toast TONE is a VM-side enum (MusterTone) that the View
// maps to ElarionUiKit.ToastTone — the VM must not know what a toast looks like.
//
// ⚠ THE COMPOSITION IS STILL PROCESS-WIDE (Shared) ON PURPOSE. The staged plan
// deliberately survives closing the panel — the owner's loop is "stage, go play,
// come back and train". Moving it here does not change that behaviour; it moves
// the OWNERSHIP off the presentation layer so a future save-backed home has one
// obvious place to land.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village
{
    /// <summary>
    /// WO-1811 - THE PLAYER-FACING COPY FOR THE ARMY SCREEN, as pure functions of numbers.
    /// -------------------------------------------------------------------------------
    /// Owner, 2026-09-16: "the armies training screen is where im confused, i have no way to
    /// understand it". The screen had three number systems on it at once (a saved PRESET, the
    /// TRAINING QUEUE, and ARMY CAPACITY) and spoke engine vocabulary for all three - "staged",
    /// "SHORT OF: Army room", "Fits now: 5 of 10", "2 train slots".
    ///
    /// Every sentence the player reads is produced HERE, from plain integers, so:
    ///   * the View owns no copy and no arithmetic (ARCHITECTURE_PRINCIPLES - presentation is a
    ///     separate layer), and
    ///   * a regression can measure the sentences for all four army states WITHOUT a GameState,
    ///     a canvas or a queue service (ArmyScreenCopyRegression).
    ///
    /// ⛔ BANNED WORDS, and why each one was a defect rather than a style note:
    ///   "staged"    - internal word; the player has no model for a plan that is neither owned nor training.
    ///   "SHORT OF"  - reads as an error while the button underneath still said Train.
    ///   "slot N"    - the loadout SAVE slot, which has nothing to do with army slots or train slots.
    /// The regression fails on any of them appearing in any string this class returns.
    ///
    /// English lives in the table, not at the call site (localization policy). The fallback overload
    /// is used deliberately so a headless capture - and a device whose locale file is a build behind -
    /// renders real copy instead of "[[missing:key]]".
    /// </summary>
    public static class ArmyBoardCopy
    {
        public const string KeyArmyLine = "armyScreen.armyLine";
        public const string KeyRecovering = "armyScreen.recovering";
        public const string KeyRoomFor = "armyScreen.roomFor";
        public const string KeyArmyFull = "armyScreen.armyFull";
        public const string KeyArmyFullRecovering = "armyScreen.armyFullRecovering";
        public const string KeyNothingTraining = "armyScreen.nothingTraining";
        public const string KeyTraining = "armyScreen.training";
        public const string KeyQueueFull = "armyScreen.queueFull";
        public const string KeyRowMeta = "armyScreen.rowMeta";
        public const string KeyTrainButton = "armyScreen.trainButton";
        public const string KeyInTraining = "armyScreen.inTrainingTitle";
        public const string KeyReady = "armyScreen.ready";
        public const string KeyNotReady = "armyScreen.notReady";
        public const string KeyLoadouts = "armyScreen.loadouts";
        public const string KeyTitle = "armyScreen.title";
        public const string KeyTip = "armyScreen.tip";
        public const string KeyTrainedOne = "armyScreen.trainedOne";
        public const string KeyNoTroops = "armyScreen.noTroops";

        // WO-1811 owner ruling 2026-09-16 - trained vs deployed, and the two exits from a full army.
        public const string KeyRowTraining = "armyScreen.rowTraining";
        public const string KeyReserveLine = "armyScreen.reserveLine";
        public const string KeyManage = "armyScreen.manage";
        public const string KeyManageBody = "armyScreen.manageBody";
        public const string KeyMoveToReserve = "armyScreen.moveToReserve";
        public const string KeyDismissFor = "armyScreen.dismissFor";
        public const string KeyDismissFree = "armyScreen.dismissFree";
        public const string KeyRecall = "armyScreen.recall";
        public const string KeyCancel = "armyScreen.cancel";
        public const string KeyMovedToReserve = "armyScreen.movedToReserve";
        public const string KeyRecalled = "armyScreen.recalled";
        public const string KeyDismissed = "armyScreen.dismissed";
        public const string KeyNoRoomToRecall = "armyScreen.noRoomToRecall";
        public const string KeyReadyWhenRecovered = "armyScreen.readyWhenRecovered";
        public const string KeyNotReadyRecovering = "armyScreen.notReadyRecovering";
        public const string KeyDismissedFree = "armyScreen.dismissedFree";

        /// <summary>"Army 7 of 10" - the ONE headline. Used counts the roster (wounded included,
        /// because a wounded troop really does hold its slot - BarracksService.cs:385) plus the
        /// units already training, which is exactly the sum the train gate subtracts from the cap,
        /// so this line and <see cref="RoomLine"/> can never disagree.</summary>
        public static string ArmyLine(int usedSlots, int capSlots)
        {
            return LocalText.FormatWithFallback(KeyArmyLine, "Army {0} of {1}",
                Clamp0(usedSlots), Clamp0(capSlots));
        }

        /// <summary>"3 recovering - ready in 19m", or "" at zero wounded. WO-1810 removed the raid
        /// path that CREATED wounded troops, so the zero case is now the normal one and this line
        /// must disappear rather than print a zero.</summary>
        public static string RecoveringLine(int wounded, double secondsLeft)
        {
            if (wounded <= 0) return "";
            return LocalText.FormatWithFallback(KeyRecovering, "{0} recovering - ready in {1}",
                wounded, ArmyMusterPlanner.FormatDuration(secondsLeft < 0d ? 0d : secondsLeft));
        }

        /// <summary>The plain sentence that REPLACED "SHORT OF: Army room" - it names the cause
        /// (the wounded holding slots) instead of a blocker the screen never showed.</summary>
        public static string RoomLine(int roomSlots, int wounded)
        {
            if (roomSlots > 0)
                return LocalText.FormatWithFallback(KeyRoomFor, "Room for {0} more", roomSlots);
            if (wounded > 0)
                return LocalText.FormatWithFallback(KeyArmyFullRecovering, "Army full - {0} recovering", wounded);
            return LocalText.Get(KeyArmyFull, "Army full");
        }

        /// <summary>The training well's one line: what is training and how long is left.</summary>
        public static string QueueLine(int training, double secondsLeft)
        {
            if (training <= 0) return LocalText.Get(KeyNothingTraining, "Nothing training");
            return LocalText.FormatWithFallback(KeyTraining, "Training {0} - {1} left",
                training, ArmyMusterPlanner.FormatDuration(secondsLeft < 0d ? 0d : secondsLeft));
        }

        /// <summary>"Training queue full - 5 of 5" (queue DEPTH, named as the queue, never mixed
        /// into the army line).</summary>
        public static string QueueFullLine(int depth, int cap)
        {
            return LocalText.FormatWithFallback(KeyQueueFull, "Training queue full - {0} of {1}",
                Clamp0(depth), Clamp0(cap));
        }

        /// <summary>
        /// One troop row's sub-line: "45s each - 4 active, 1 reserve", with " +2 training" appended
        /// while any are in the queue. WO-1811 owner ruling: "we need to know how many troops are
        /// trained or how many are deployed" - so ACTIVE (holds a cap slot, goes on the raid) and
        /// RESERVE (trained, kept, holds no slot) are separate numbers in the same breath, and the
        /// queue is a third term that appears only while it is true.
        /// </summary>
        public static string RowMeta(string timeEach, int active, int reserve, int training)
        {
            string line = LocalText.FormatWithFallback(KeyRowMeta, "{0} each - {1} active, {2} reserve",
                timeEach ?? "", Clamp0(active), Clamp0(reserve));
            if (training > 0)
                line += " " + LocalText.FormatWithFallback(KeyRowTraining, "+{0} training", training);
            return line;
        }

        /// <summary>"Reserve 2", or "" when nothing is set aside.</summary>
        public static string ReserveLine(int reserve)
        {
            if (reserve <= 0) return "";
            return LocalText.FormatWithFallback(KeyReserveLine, "Reserve {0}", reserve);
        }

        /// <summary>The manage sheet's body: what the two exits mean, in the player's words.</summary>
        public static string ManageBody(int active, int reserve)
        {
            return LocalText.FormatWithFallback(KeyManageBody,
                "{0} active, {1} in reserve. Reserve troops stay trained but do not join a raid.",
                Clamp0(active), Clamp0(reserve));
        }

        /// <summary>The dismiss face: with a price when it pays, plain when it does not.</summary>
        public static string DismissFace(int gold)
        {
            if (gold > 0)
                return LocalText.FormatWithFallback(KeyDismissFor, "Dismiss for {0} gold", gold);
            return LocalText.Get(KeyDismissFree, "Dismiss");
        }

        /// <summary>
        /// The raid door's face: ready, or what is still missing - never a blank refusal, and
        /// NEVER a number the player cannot act on.
        ///
        /// ⛔ THE WOUNDED SPLIT IS THE POINT. The gate counts DEPLOYABLE slots, so a 7/10 army with
        /// 3 recovering is 6 short - but only 3 of those can be trained today, because the wounded
        /// are already holding the other 3 slots. "Train 6 more" beside "Room for 3 more" is the
        /// same two-axis contradiction this whole ticket exists to remove, so when anyone is
        /// recovering the sentence names BOTH halves, or - when recovery alone closes the gap -
        /// says to wait rather than to train into slots that do not exist.
        /// </summary>
        public static string ReadyLine(bool ready, int missingSlots, int wounded)
        {
            if (ready) return LocalText.Get(KeyReady, "Army ready - go raid");

            if (wounded > 0)
            {
                int trainable = missingSlots - wounded;
                if (trainable <= 0)
                    return LocalText.FormatWithFallback(KeyReadyWhenRecovered,
                        "Ready when {0} recover", wounded);
                return LocalText.FormatWithFallback(KeyNotReadyRecovering,
                    "Train {0} more, {1} recovering", trainable, wounded);
            }

            return LocalText.FormatWithFallback(KeyNotReady, "Train {0} more to raid",
                missingSlots < 1 ? 1 : missingSlots);
        }

        /// <summary>ONE-LINE time grammar for a troop row ("45s", "1m", "1m30", "2h"). It lived on
        /// the PANEL (a MonoBehaviour); the VM may not reach into presentation for a string, so it
        /// lives here and the panel's method now forwards to it - one implementation, not two.</summary>
        public static string CompactDuration(double seconds)
        {
            if (seconds <= 0d) return "0s";
            int total = (int)Math.Round(seconds);
            if (total < 60) return total + "s";
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return m > 0 ? h + "h" + m : h + "h";
            return s > 0 ? m + "m" + s : m + "m";
        }

        private static int Clamp0(int v) { return v < 0 ? 0 : v; }
    }

    /// <summary>One troop the player can train, projected for the View (WO-1811). The View paints
    /// these fields and routes the tap; it decides nothing.</summary>
    public struct ArmyTrainRow
    {
        /// <summary>TroopDef id - the only thing the View hands back on a tap.</summary>
        public string TroopId;
        /// <summary>Player-facing troop name.</summary>
        public string Name;
        /// <summary>"45s each - 4 active, 1 reserve" (+ " +2 training" when any are in the queue).</summary>
        public string Meta;
        /// <summary>False when the army has no room for this unit; the View dims the Train button.</summary>
        public bool CanTrain;

        // ── WO-1811, owner ruling 2026-09-16 ──────────────────────────────────
        // "we need to know how many troops are trained or how many are deployed" - so the two
        // counts are separate FIELDS, not one number the player has to interpret.

        /// <summary>In the ACTIVE army: holds a cap slot and goes on the raid.</summary>
        public int Active;
        /// <summary>In the RESERVE: trained, kept, holds NO cap slot and never deploys.</summary>
        public int Reserve;
        /// <summary>In the train queue right now (committed slots, not yet troops).</summary>
        public int Training;
        /// <summary>True when there is an active troop of this type to move out.</summary>
        public bool CanManage;
        /// <summary>True when a reserved troop of this type can come back (room for its slots).</summary>
        public bool CanRecall;
        /// <summary>Gold a dismissal of this type pays right now (0 = it pays nothing, and says so).</summary>
        public int DismissGold;
    }

    /// <summary>Neutral outcome tone for a VM command. The View maps this to its toast palette;
    /// the VM never names a colour or a UI type (§2).</summary>
    public enum MusterTone { Info, Good, Warn, Bad }

    /// <summary>What a VM command wants said on screen, and how loudly. Empty Message = say nothing.</summary>
    public readonly struct MusterCommandResult
    {
        public readonly string Message;
        public readonly MusterTone Tone;
        public readonly bool Changed;

        public MusterCommandResult(string message, MusterTone tone, bool changed = true)
        {
            Message = message;
            Tone = tone;
            Changed = changed;
        }

        public static readonly MusterCommandResult None = new MusterCommandResult(null, MusterTone.Info, false);
    }

    /// <summary>
    /// ViewModel for the Armies panel: owns the staged <see cref="ArmyComposition"/>, the active
    /// loadout slot, the last training-order receipt, and every verb the panel offers.
    /// </summary>
    public sealed class ArmyMusterVM : IPanelViewModel
    {
        private const string Sys = "Muster";

        // The staged plan. Process-wide so the panel can be closed and reopened mid-plan (see the
        // header) — but owned HERE, by the model layer, not by a View's static field.
        private static readonly ArmyComposition s_shared = new ArmyComposition { Name = "Raid Push" };

        private readonly ArmyComposition _composition;
        private int _activeSlot;
        private string _lastResultHeadline = "";
        private string _lastResultDetail = "";

        public event Action Changed;

        /// <summary>WO-1811: the screen is called ARMY, not "Armies - Loadouts". The old title named
        /// the preset bank, which is precisely the thing the owner met first and could not place
        /// ("i have no way to understand it"); the preset bank is now a drawer.</summary>
        public string Title => LocalText.Get(ArmyBoardCopy.KeyTitle, "Army");

        /// <summary>Live view of the staged plan. The View READS it to paint; it never mutates it
        /// (every mutation is a command below).</summary>
        public ArmyComposition Composition => _composition;

        public int ActiveSlot => _activeSlot;
        public string LastResultHeadline => _lastResultHeadline;
        public string LastResultDetail => _lastResultDetail;
        public string ArmyName => _composition.Name;
        public int SlotCount => ArmyLoadoutService.SlotCount;

        /// <summary>Wallet readout for the panel's currency chip.</summary>
        public int GoldBalance => ArmyMusterService.GoldBalance();

        /// <summary>The staged plan's cost/time/queue-fit projection, computed by the service.</summary>
        public MusterPreview Preview => ArmyMusterService.Preview(_composition);

        // =====================================================================
        //  WO-1811 - THE PRIMARY SURFACE: what you have, what to train, go.
        //  Every number below is READ from the same authorities the train gate
        //  uses (ArmyReadiness / BarracksService), never re-derived, so the copy
        //  cannot promise what BarracksService.EnqueueTraining then refuses.
        // =====================================================================

        /// <summary>True while the player has the LOADOUTS drawer open (presets / steppers / the
        /// bulk training order). Off by default - the owner opened this screen to TRAIN troops,
        /// and the preset bank was the first thing she met.</summary>
        public bool LoadoutsOpen { get; private set; }

        /// <summary>Army slots the roster and the in-flight training already hold.</summary>
        public int UsedSlots { get { return Board().Used; } }
        /// <summary>The army cap in slots.</summary>
        public int CapSlots { get { return Board().Cap; } }
        /// <summary>Army slots still free (cap - used), never negative.</summary>
        public int RoomSlots { get { return Board().Room; } }
        /// <summary>Wounded troops on the roster. WO-1810: a raid no longer creates these, but
        /// the backstop still can, and while one exists it holds its slot.</summary>
        public int WoundedCount { get { return Board().Wounded; } }

        /// <summary>"Army 7 of 10".</summary>
        public string ArmyLine { get { var b = Board(); return ArmyBoardCopy.ArmyLine(b.Used, b.Cap); } }
        /// <summary>"3 recovering - ready in 19m", or "" when nothing is recovering.</summary>
        public string RecoveringLine { get { var b = Board(); return ArmyBoardCopy.RecoveringLine(b.Wounded, b.RecoverySeconds); } }
        /// <summary>"Reserve 2" (trained, not deployed), or "" when nothing is set aside.</summary>
        public string ReserveLine { get { return ArmyBoardCopy.ReserveLine(Board().Reserved); } }
        /// <summary>"Room for 3 more" / "Army full - 3 recovering" / "Army full".</summary>
        public string RoomLine { get { var b = Board(); return ArmyBoardCopy.RoomLine(b.Room, b.Wounded); } }
        /// <summary>"Training 2 - 4m 10s left" or "Nothing training".</summary>
        public string QueueLine { get { var b = Board(); return ArmyBoardCopy.QueueLine(b.Training, b.TrainingSeconds); } }
        /// <summary>"Training queue full - 5 of 5", or "" while the line has room.</summary>
        public string QueueFullLine
        {
            get
            {
                var b = Board();
                return b.QueueRoom > 0 ? "" : ArmyBoardCopy.QueueFullLine(b.QueueDepth, ArmyMusterPlanner.TrainQueueDepthCap);
            }
        }
        /// <summary>The raid door's face.</summary>
        public string ReadyLine { get { var b = Board(); return ArmyBoardCopy.ReadyLine(b.Ready, b.MissingSlots, b.Wounded); } }
        /// <summary>True when the raid door may be taken (the ONE readiness opinion, ArmyReadiness).</summary>
        public bool ArmyReady { get { return Board().Ready; } }

        /// <summary>One projected row per UNLOCKED troop: name, "45s each - you have 4", and whether
        /// the army has room for one more of it right now.</summary>
        public List<ArmyTrainRow> TrainRows()
        {
            var rows = new List<ArmyTrainRow>();
            var board = Board();
            foreach (var def in OfferedTroops())
            {
                if (def == null) continue;
                int active = OwnedOf(def.Id);
                int training = BarracksService.CountInFlightTrainOf(def.Id);
                int reserve = ReserveOf(def.Id);
                int slots = TroopDialogueCommands.SlotOf(def.Id);
                if (slots < 1) slots = 1;
                bool fitsArmy = board.Room >= slots;
                bool fitsQueue = board.QueueRoom > 0;
                // maxOwned counts the ACTIVE army plus what is in flight. A reserved troop is not
                // in the army, so it does not hold the per-type cap either - that is the same
                // "true by construction" the separate list buys everywhere else.
                bool underOwnedCap = def.MaxOwned <= 0 || active + training < def.MaxOwned;
                rows.Add(new ArmyTrainRow
                {
                    TroopId = def.Id,
                    Name = DisplayNameOf(def.Id),
                    Meta = ArmyBoardCopy.RowMeta(ArmyBoardCopy.CompactDuration(def.BuildSeconds),
                                                 active, reserve, training),
                    CanTrain = fitsArmy && fitsQueue && underOwnedCap,
                    Active = active,
                    Reserve = reserve,
                    Training = training,
                    CanManage = active > 0 || reserve > 0,
                    CanRecall = reserve > 0 && fitsArmy,
                    DismissGold = DismissGoldFor(def.Id),
                });
            }
            return rows;
        }

        /// <summary>
        /// THE ONE-TAP TRAIN (CoC pattern): queue exactly ONE of this troop through the SANCTIONED
        /// single-troop path - <see cref="BarracksService.EnqueueTraining(string,int,out string)"/>,
        /// the same call the bulk order funnels through. No second enqueue path is invented, and the
        /// refusal REASON is the service's own, so the screen can never promise what the gate refuses
        /// (WO-1811 §1b: the old CTA read only the queue axis and stayed enabled on a full army).
        /// </summary>
        public MusterCommandResult TrainOne(string troopId)
        {
            if (string.IsNullOrEmpty(troopId)) return MusterCommandResult.None;

            string stopReason;
            int queued = BarracksService.EnqueueTraining(troopId, 1, out stopReason);
            string name = DisplayNameOf(troopId);

            if (queued > 0)
            {
                FlowTrace.Step("ArmyUI", "TrainOne queued 1x '" + troopId + "'.");
                Raise();
                return new MusterCommandResult(
                    LocalText.FormatWithFallback(ArmyBoardCopy.KeyTrainedOne, "{0} added to training", name),
                    MusterTone.Good);
            }

            string why = string.IsNullOrEmpty(stopReason) ? ArmyBoardCopy.RoomLine(RoomSlots, WoundedCount) : stopReason;
            FlowTrace.Warn("ArmyUI", "TrainOne REFUSED '" + troopId + "' - " + why);
            Raise();
            return new MusterCommandResult(why, MusterTone.Warn);
        }

        // ── WO-1811, owner ruling 2026-09-16: THE TWO EXITS FROM A FULL ARMY ──
        //
        // Verbatim: "If you change the you want 3 archers and 2 healers and rest footman, you need
        // to remove ones from active army, and either return them to gold or to a staged ready
        // troop". So a removal is never destruction-by-accident: it is a CHOICE, made per action,
        // between RESERVE (kept, out of the raid cap, swappable back) and DISMISS (gone, paid for).
        // The raid gate is untouched - it still asks for a full army (ArmyReadiness).

        /// <summary>
        /// Gold a dismissal of <paramref name="troopId"/> pays right now: a PERCENT of the troop's
        /// CATALOG value, behind the `army.dismissReturnPercent` knob (ships 50).
        /// ⚠ IT IS NOT A REFUND OF ANYTHING PAID. Training has charged nothing since WO-1387
        /// (BarracksService.EnqueueTraining spends nothing; MusterPreview.Cost is a hardcoded zero
        /// that prints "Free"), so "give the gold back" has no amount to give back. The owner ruled
        /// the VERB and not the number, so the number is a knob, not a literal in this method.
        /// </summary>
        public static int DismissGoldFor(string troopId)
        {
            var def = TroopCatalog.Find(troopId);
            if (def == null || def.CostGold <= 0) return 0;
            int pct = DeNelle.Core.Ops.RemoteTunables.Int(DeNelle.Core.Ops.RemoteTunables.KeyArmyDismissReturnPercent);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            return def.CostGold * pct / 100;
        }

        /// <summary>Move ONE active troop of this type into the Reserve (frees its cap slot).</summary>
        public MusterCommandResult MoveToReserveOne(string troopId)
        {
            var army = ArmyOrNull();
            string name = DisplayNameOf(troopId);
            if (army == null || !army.MoveToReserve(troopId))
            {
                FlowTrace.Warn("ArmyUI", "move-to-reserve REFUSED '" + troopId + "' - none in the active army.");
                return new MusterCommandResult(ArmyBoardCopy.RoomLine(RoomSlots, WoundedCount), MusterTone.Warn);
            }
            Persist();
            FlowTrace.Step("ArmyUI", "moved 1x '" + troopId + "' to the reserve.");
            Raise();
            return new MusterCommandResult(
                LocalText.FormatWithFallback(ArmyBoardCopy.KeyMovedToReserve, "{0} moved to Reserve", name),
                MusterTone.Good);
        }

        /// <summary>Bring ONE reserved troop of this type back into the active army.</summary>
        public MusterCommandResult RecallOne(string troopId)
        {
            var army = ArmyOrNull();
            string name = DisplayNameOf(troopId);
            int slots = TroopDialogueCommands.SlotOf(troopId);
            if (slots < 1) slots = 1;
            if (army == null || RoomSlots < slots)
            {
                FlowTrace.Warn("ArmyUI", "recall REFUSED '" + troopId + "' - no army room (" + RoomSlots + " free).");
                return new MusterCommandResult(
                    LocalText.Get(ArmyBoardCopy.KeyNoRoomToRecall, "No room - move or dismiss one first"),
                    MusterTone.Warn);
            }
            if (!army.RecallFromReserve(troopId))
                return new MusterCommandResult(
                    LocalText.Get(ArmyBoardCopy.KeyNoRoomToRecall, "No room - move or dismiss one first"),
                    MusterTone.Warn);

            Persist();
            FlowTrace.Step("ArmyUI", "recalled 1x '" + troopId + "' from the reserve.");
            Raise();
            return new MusterCommandResult(
                LocalText.FormatWithFallback(ArmyBoardCopy.KeyRecalled, "{0} returned to the army", name),
                MusterTone.Good);
        }

        /// <summary>
        /// Dismiss ONE troop of this type - the ACTIVE army first, the reserve only when the army
        /// has none - and pay the knob's share of its catalog value. Removal routes through
        /// <c>ArmyStorage.RemoveOwned</c>, the WO-1810 deletion seam; nothing here forks it.
        /// </summary>
        public MusterCommandResult DismissOne(string troopId)
        {
            var army = ArmyOrNull();
            string name = DisplayNameOf(troopId);
            if (army == null) return MusterCommandResult.None;

            bool removed = false;
            bool fromReserve = false;
            if (army.Owned != null)
            {
                PlayerTroopPick(army, troopId, out string id);
                if (!string.IsNullOrEmpty(id)) removed = army.RemoveOwned(new[] { id }) > 0;
            }
            if (!removed && army.Reserve != null)
            {
                for (int i = 0; i < army.Reserve.Count; i++)
                {
                    var t = army.Reserve[i];
                    if (t == null || t.TroopDefId != troopId) continue;
                    army.Reserve.RemoveAt(i);
                    removed = true;
                    fromReserve = true;
                    break;
                }
            }
            if (!removed)
            {
                FlowTrace.Warn("ArmyUI", "dismiss REFUSED '" + troopId + "' - none owned or reserved.");
                return MusterCommandResult.None;
            }

            int gold = DismissGoldFor(troopId);
            if (gold > 0) DeNelle.Village.EconomyService.Instance?.AddCoins(gold);   // the ONE persisted coin seam
            Persist();
            FlowTrace.Step("ArmyUI", "dismissed 1x '" + troopId + "' from " +
                                     (fromReserve ? "the RESERVE" : "the ACTIVE army") +
                                     " for " + gold + " gold.");
            Raise();
            // A dismissal that pays nothing must SAY nothing about gold - quoting "0 gold" is the
            // same false price the screen was cleaned of (the regression forbids that shape).
            return new MusterCommandResult(
                gold > 0
                    ? LocalText.FormatWithFallback(ArmyBoardCopy.KeyDismissed, "{0} dismissed - {1} gold", name, gold)
                    : LocalText.FormatWithFallback(ArmyBoardCopy.KeyDismissedFree, "{0} dismissed", name),
                MusterTone.Info);
        }

        /// <summary>
        /// Pick the troop a dismissal takes: a HEALTHY one first, the wounded only as a last resort.
        ///
        /// ⛔ THE PREFERENCE USED TO BE THE OTHER WAY ROUND, AND IT WAS AN EXPLOIT. Paying the same
        /// percent for a wounded troop as for a healthy one turns the recovery timer into a price:
        /// dismiss the three recovering bodies for half their value, retrain healthy replacements
        /// for nothing (training charges nothing, WO-1387) and the wait has been bought off with
        /// gold the game just handed over. Healthy-first keeps the wounded where the recovery clock
        /// can still reach them (AdvanceRecovery iterates Owned only) and keeps the timer real.
        /// </summary>
        private static void PlayerTroopPick(DeNelle.Core.State.ArmyStorage army, string troopId, out string id)
        {
            id = null;
            foreach (var t in army.Owned)
            {
                if (t == null || t.TroopDefId != troopId || string.IsNullOrEmpty(t.Id)) continue;
                if (id == null) id = t.Id;          // a wounded one, only if nothing else turns up
                if (!t.Wounded) { id = t.Id; break; }
            }
        }

        private static DeNelle.Core.State.ArmyStorage ArmyOrNull()
        {
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            return state != null ? state.Army : null;
        }

        private static void Persist()
        {
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc == null) return;
            svc.Save();
            svc.CombatChanged?.Invoke();   // the roster listeners (deployable count) refresh
        }

        private static int ReserveOf(string troopId)
        {
            var army = ArmyOrNull();
            return army != null ? army.ReserveCountOf(troopId) : 0;
        }

        /// <summary>Open / close the LOADOUTS drawer (the preset bank the primary surface no longer
        /// leads with). The preset data model is untouched - only where it lives changed.</summary>
        public MusterCommandResult ToggleLoadouts()
        {
            LoadoutsOpen = !LoadoutsOpen;
            FlowTrace.Step("ArmyUI", "loadouts drawer " + (LoadoutsOpen ? "OPEN" : "CLOSED") + ".");
            Raise();
            return MusterCommandResult.None;
        }

        /// <summary>The army/queue numbers, read once per paint from the live authorities.</summary>
        public struct ArmyBoard
        {
            public int Used, Cap, Room, Wounded, Training, QueueDepth, QueueRoom, MissingSlots, Reserved;
            public double RecoverySeconds, TrainingSeconds;
            public bool Ready;
        }

        /// <summary>
        /// ONE read of the army + queue state, shared by every sentence above so two lines on the
        /// same screen can never disagree. Null-safe end to end: with no GameState (headless) it
        /// reports an empty army rather than throwing or false-blocking.
        /// </summary>
        public ArmyBoard Board()
        {
            var b = new ArmyBoard();
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            var army = state != null ? state.Army : null;

            var snap = ArmyReadiness.Compute(state);
            b.Cap = snap.CapSlots;
            b.Used = snap.RosterSlots + snap.QueuedSlots;
            b.Room = b.Cap - b.Used;
            if (b.Room < 0) b.Room = 0;
            b.Ready = snap.Ready;
            b.MissingSlots = snap.RequiredSlots - (snap.DeployableSlots + snap.QueuedSlots);
            if (b.MissingSlots < 0) b.MissingSlots = 0;

            if (army != null && army.Owned != null)
            {
                foreach (var t in army.Owned)
                {
                    if (t == null || !t.Wounded) continue;
                    b.Wounded++;
                    if (t.RecoveryRemaining > b.RecoverySeconds) b.RecoverySeconds = t.RecoveryRemaining;
                }
            }
            // WO-1811: the reserve is TRAINED but not DEPLOYED. It is counted separately here and
            // said separately on screen, because conflating the two is the confusion the owner hit.
            b.Reserved = army != null && army.Reserve != null ? army.Reserve.Count : 0;

            b.QueueDepth = ArmyMusterService.TrainLineDepth();
            b.QueueRoom = ArmyMusterService.TrainLineRoom();
            b.Training = b.QueueDepth;
            b.TrainingSeconds = TrainingSecondsLeft();

            FlowTrace.Throttle("ArmyUI", "board", 5f,
                "army board used=" + b.Used + "/" + b.Cap + " room=" + b.Room +
                " wounded=" + b.Wounded + " training=" + b.Training + " queueRoom=" + b.QueueRoom +
                " ready=" + b.Ready + ".");
            return b;
        }

        /// <summary>
        /// Wall-clock seconds until the Train line is empty, through the SAME parallel-aware planner
        /// the staged-plan estimate uses (<see cref="ArmyMusterPlanner.BatchSeconds"/>) - a second
        /// time model here is how two numbers on one screen start disagreeing.
        /// </summary>
        private static double TrainingSecondsLeft()
        {
            var queue = BuildTimerService.Instance;
            if (queue == null) return 0d;

            var durations = new List<double>();
            var active = queue.ActiveJobsOf(DeNelle.Core.Jobs.ChannelId.Train);
            if (active != null)
                foreach (var j in active)
                    durations.Add(queue.RemainingSeconds(DeNelle.Core.Jobs.ChannelId.Train, j.StructureId));
            var pending = queue.PendingJobsOf(DeNelle.Core.Jobs.ChannelId.Train);
            if (pending != null)
                foreach (var j in pending)
                    durations.Add(j.DurationMs / 1000.0);

            return ArmyMusterPlanner.BatchSeconds(durations, ArmyMusterService.TrainSlots());
        }

        private static int OwnedOf(string troopId)
        {
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            if (state == null || state.Army == null) return 0;
            return state.Army.CountOfDef(troopId);
        }

        /// <summary>Live production build: the process-wide staged plan.</summary>
        public static ArmyMusterVM CreateDefault() => new ArmyMusterVM(s_shared);

        /// <summary>Test seam — inject an isolated composition so a suite never touches the shared plan.</summary>
        public ArmyMusterVM(ArmyComposition composition)
        {
            _composition = composition ?? new ArmyComposition { Name = "Raid Push" };
        }

        // ── lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hydrate the working set from the saved ACTIVE slot on panel open, seeding a quick-fill
        /// when the slot is empty and no receipt is on screen (so the panel is never blank).
        /// </summary>
        public void HydrateFromActiveSlot()
        {
            var army = ArmyLoadoutService.Ensure();
            _activeSlot = army != null ? army.ActiveLoadoutIndex : 0;
            ArmyLoadoutService.LoadInto(_activeSlot, _composition);
            if (_composition.TotalUnits <= 0 && string.IsNullOrEmpty(_lastResultHeadline))
                ArmyLoadoutService.ApplyRecipe(_composition, 0);
            FlowTrace.Step(Sys, "ArmyMusterVM hydrated slot " + _activeSlot +
                                " units=" + _composition.TotalUnits);
            Raise();
        }

        public void Close() { /* the View owns its own teardown; nothing to detach here. */ }

        public void Dispose() { Changed = null; }

        // ── queries the View would otherwise have made against the catalog ────

        /// <summary>Troops the Barracks has unlocked, in catalog order. The UNLOCK GATE is a rule,
        /// so it is decided here — the View used to call BarracksService.IsTroopUnlocked itself.</summary>
        public List<TroopDef> OfferedTroops()
        {
            var offered = new List<TroopDef>();
            var all = TroopCatalog.All;
            if (all == null) return offered;
            foreach (var def in all)
                if (def != null && !string.IsNullOrEmpty(def.Id) && BarracksService.IsTroopUnlocked(def.Id))
                    offered.Add(def);
            return offered;
        }

        public int CountOf(string troopId) => _composition.CountOf(troopId);

        public string SlotName(int index) => ArmyLoadoutService.SlotName(index);

        /// <summary>Display name for a staged row's troop id (catalog lookup lives in the VM).</summary>
        public string DisplayNameOf(string troopId)
        {
            var def = TroopCatalog.Find(troopId);
            return def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : troopId;
        }

        // ── commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// THE TRAINING ORDER. Auto-saves the active slot first (a staged plan is never lost to a
        /// train tap), musters through the service, then drops what actually queued from the working
        /// set — the saved slot keeps the FULL plan.
        /// </summary>
        public MusterCommandResult Muster()
        {
            ArmyLoadoutService.SaveFrom(_activeSlot, _composition);

            var report = ArmyMusterService.Muster(_composition);
            _lastResultHeadline = PlayerWords(report.Headline);
            _lastResultDetail = PlayerWords(report.Detail);

            var tone = report.Complete ? MusterTone.Good
                     : report.AnyQueued ? MusterTone.Warn
                     : MusterTone.Bad;

            foreach (var r in report.Rows)
                if (r.Queued > 0) _composition.Add(r.TroopId, -r.Queued);

            FlowTrace.Step(Sys, "ArmyMusterVM.Muster complete=" + report.Complete +
                                " anyQueued=" + report.AnyQueued);
            Raise();
            return new MusterCommandResult(PlayerWords(report.Summary), tone);
        }

        public MusterCommandResult SaveSlot()
        {
            ArmyLoadoutService.SaveFrom(_activeSlot, _composition);
            Raise();
            return new MusterCommandResult(
                "Saved '" + _composition.Name + "' to slot " + (_activeSlot + 1) + ".", MusterTone.Good);
        }

        /// <summary>Re-tap the ACTIVE slot to reload it (discard unsaved edits); tap another slot to
        /// auto-save the one you leave and edit the new one. Never lose work.</summary>
        public MusterCommandResult SelectSlot(int index)
        {
            if (index < 0 || index >= ArmyLoadoutService.SlotCount) return MusterCommandResult.None;

            string message;
            if (index == _activeSlot)
            {
                ArmyLoadoutService.LoadInto(index, _composition);
                message = "Reloaded " + ArmyLoadoutService.SlotName(index) + ".";
            }
            else
            {
                ArmyLoadoutService.SaveFrom(_activeSlot, _composition);
                _activeSlot = index;
                ArmyLoadoutService.LoadInto(index, _composition);
                message = "Editing " + ArmyLoadoutService.SlotName(index) + ".";
            }
            Raise();
            return new MusterCommandResult(message, MusterTone.Info);
        }

        public MusterCommandResult ApplyRecipe(int recipe)
        {
            string msg = ArmyLoadoutService.ApplyRecipe(_composition, recipe);
            Raise();
            return new MusterCommandResult(msg, MusterTone.Warn);
        }

        /// <summary>Cycle the default army names so the player can feel ownership without a soft
        /// keyboard. The name list is copy, so it lives with the model that carries the name.</summary>
        public MusterCommandResult CycleName()
        {
            string[] names =
            {
                "Raid Push", "Wall Hold", "Siege Prep",
                "Night Watch", "Quick Strike", "Last Stand", "New Army",
            };
            string cur = _composition.Name ?? "";
            int idx = 0;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], cur, StringComparison.OrdinalIgnoreCase))
                { idx = (i + 1) % names.Length; break; }
            _composition.Name = names[idx];
            Raise();
            return MusterCommandResult.None;
        }

        /// <summary>
        /// Step one troop's staged count. The maxOwned ceiling is a RULE and is enforced here —
        /// the View used to read TroopCatalog itself to decide whether the tap was legal.
        /// </summary>
        public MusterCommandResult Step(string troopId, int delta)
        {
            if (string.IsNullOrEmpty(troopId)) return MusterCommandResult.None;

            var def = TroopCatalog.Find(troopId);
            if (def != null && def.MaxOwned > 0 && delta > 0)
            {
                int want = _composition.CountOf(troopId) + delta;
                if (want > def.MaxOwned)
                {
                    string name = string.IsNullOrEmpty(def.DisplayName) ? troopId : def.DisplayName;
                    return new MusterCommandResult(
                        "Only " + def.MaxOwned + "x " + name + " in a loadout.", MusterTone.Info, changed: false);
                }
            }

            _composition.Add(troopId, delta);
            Raise();
            return MusterCommandResult.None;
        }

        // ── copy ──────────────────────────────────────────────────────────────

        /// <summary>
        /// OWNER RULING 2026-08-26 ("what dos muster army mean? Thats where im lost"): "muster" is
        /// archaic jargon for a TRAINING ORDER and must not reach the player. The rewrite stays a
        /// PLAYER-FACING STRING map — ArmyMusterService's identifiers are live and renaming them is a
        /// wide mechanical diff with zero player benefit. It moved from the panel to the VM with the
        /// rest of the transaction because the VM is what now produces the message. ASCII in, out.
        /// ⚠ ArmyMusterService.cs still AUTHORS the archaic word in its literals; this maps them.
        /// </summary>
        public static string PlayerWords(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("Nothing to muster", "Nothing to train");
            s = s.Replace("Mustered", "Training ordered");
            s = s.Replace("mustered", "training ordered");
            s = s.Replace("Muster", "Train");
            s = s.Replace("muster", "training");
            return s;
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

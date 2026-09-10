// =============================================================================
// StoreReturnToManageRegression [store-return-to-manage] (WO-1412) - closing the
// store returns the player to the SAME Manage tab that sent them, never to the HUD.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression
// Markers:  STORE_RETURN_TO_MANAGE_OK / STORE_RETURN_TO_MANAGE_FAIL
//
// WHAT WAS FOUND (WO-1412, device walk build 355952,
// docs/qa/UI_REVIEW_2026-09-05/11-research-upgrade-door.png -> 12-hud-after-store-close.png):
// mid-management the player followed the builder upsell into the store, tapped CLOSE,
// and was standing in the town with Manage gone - the loop they were in had vanished.
//
// THE FIX SHAPE THIS SUITE PINS - ONE mechanism, the WO-1400 arbiter, with a different
// opener. ManageScreenVM.OpenRealmStoreFromManage records the SENDING TAB as the return
// door ("Manage tab=<tab>") BEFORE it opens the store, and PackStore.CloseStore closes
// through its ordinary lifecycle so the SAME arbiter arms and fires. There is deliberately
// no second, store-owned return route.
//
//   A  [returns-to-the-sending-tab] Manage(tab) OPEN -> BuySlot -> the store SWAPS Manage out
//      (arbiter exclusivity), door SET "Manage tab=<tab>" and KEPT across that swap; close -> ARMED;
//      a pump ON the WO-1393 close-grace frame does NOT fire; the pump after it re-opens
//      Manage AND hands the opener the SAME tab string. Traces: "[Flow:Manage] store
//      handoff source=builder offer returnTab=<tab>", "return door SET/FIRED 'Manage tab=<tab>'".
//   B  [failed-open-clears-the-door] when the RealmStore opener does not actually open a
//      panel, the handoff CLEARS the door instead of leaving a live one that would later
//      teleport the player into Manage from somewhere else entirely.
//   C  [hud-opened-store-does-not-return] a store opened with no Manage handoff closes to
//      nothing - the door is not a global "always go to Manage".
//   D  [source-manage-handoff] OpenRealmStoreFromManage sets the door BEFORE the open, the
//      door callback carries the tab, the failure branch clears, and both traces exist.
//   E  [source-store-close] PackStore.CloseStore READS PanelManager.ReturnDoorName, traces
//      "close -> return opener=", and contains NO SetReturnDoor / PanelRouter.Open - the
//      "no second return path" half of the ruling.
//   F  [busy-only-upsell] the builder upsell is not offered while a slot is free, and the
//      offered label carries a PRICE (see the pending-ruling note in that case).
//
// RED-FIRST - ONE-LINE MUTATIONS that turn this suite RED on the fixed tree:
//   * delete the `PanelManager.SetReturnDoor("Manage tab=" + tab, ...)` line from
//     ManageScreenVM.OpenRealmStoreFromManage -> A and D fail: no door is ever set, the
//     store's close arms nothing and lands on the HUD. THIS IS THE WO-1412 DEFECT ITSELF.
//   * change the door callback to `PanelRouter.Open(PanelId.Manage, null)` -> A fails: the
//     player returns to Manage's default tab instead of the one that sent them.
//   * add a `PanelRouter.Open(PanelId.Manage, ...)` into PackStore.CloseStore -> E fails
//     (a second return route racing the arbiter).
//   * delete `PanelManager.ClearReturnDoor("manage-store-open-failed")` -> B fails.
//   * change `busy >= slots` to `busy >= 0` in BuildSlotOffer -> F fails.
//
// THE HONEST LIMIT (section 11B). Cases A-C drive the REAL ManageScreenVM through the REAL
// PanelRouter/PanelManager arbiter with fake registered panels - that half is behavioural.
// PackStore is a MonoBehaviour that needs the store canvas and the marketplace interactor,
// and BuildTimerService is a scene singleton that cannot be driven to an all-slots-busy
// state in editor batchmode, so E and the all-busy half of F are SOURCE sweeps and are
// labelled as such. The runtime half of those two is the device walk the WO already asks
// for (11 -> 12 reproduced, CLOSE returns to Manage).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Jobs;
using DeNelle.Core.UI;
using DeNelle.Village.UI;

namespace DeNelle.Editor.Regression
{
    public static class StoreReturnToManageRegression
    {
        private const string Tag = "[store-return-to-manage]";
        private const string VmSrc = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";
        private const string StoreSrc = "Assets/_Modules/Wallet/PackStore.cs";

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STORE_RETURN_TO_MANAGE_OK - " + reason);
            else Debug.LogError("STORE_RETURN_TO_MANAGE_FAIL: " + reason);
        }

        /// <summary>A registered panel shaped like the real ones: Open records itself with the
        /// arbiter, Close notifies it. Opens counts every accepted open.</summary>
        private sealed class FakePanel
        {
            public readonly string Name;
            public bool IsOpen;
            public int Opens;
            public readonly PanelHandle Handle;

            public FakePanel(string name)
            {
                Name = name;
                Handle = PanelManager.Register(name, Close, () => IsOpen);
            }

            public bool Open()
            {
                IsOpen = true;
                if (!PanelManager.NotifyOpened(Handle)) { IsOpen = false; return false; }
                Opens++;
                return true;
            }

            public void Close()
            {
                if (!IsOpen) return;
                IsOpen = false;
                PanelManager.NotifyClosed(Handle);
            }
        }

        /// <summary>
        /// Captures every FlowTrace line so the suite can assert the traces the WO names.
        /// <para>Info/Warn forward to the previous sink so the run log keeps them. ERROR is
        /// captured but NOT forwarded, deliberately: case B exercises a REFUSAL path whose
        /// FlowTrace.Fail would otherwise print as a Unity LogError inside a PASSING gate run
        /// and read to a human as a failure. The line is still asserted from the capture.</para>
        /// </summary>
        private sealed class CapturingSink : ITraceSink
        {
            public readonly List<string> Lines = new List<string>();
            private readonly ITraceSink _inner;
            public CapturingSink(ITraceSink inner) { _inner = inner; }
            public void Info(string line) { Lines.Add(line); _inner?.Info(line); }
            public void Warn(string line) { Lines.Add(line); _inner?.Warn(line); }
            public void Error(string line) { Lines.Add(line); }
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- STORE RETURN TO MANAGE (WO-1412): CLOSE returns to the sending tab ---");

            bool enabledBefore = FlowTrace.Enabled;
            var sinkBefore = FlowTrace.Sink;
            var sink = new CapturingSink(sinkBefore);
            FlowTrace.Enabled = true;
            FlowTrace.AllOn();
            FlowTrace.Sink = sink;
            try
            {
                CaseA_ReturnsToTheSendingTab(failures, log, sink);
                CaseB_FailedOpenClearsTheDoor(failures, log, sink);
                CaseC_HudOpenedStoreDoesNotReturn(failures, log, sink);
                CaseD_SourceManageHandoff(failures, log);
                CaseE_SourceStoreClose(failures, log);
                CaseF_BusyOnlyUpsell(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                Guard.Try("Regression", "store-return-to-manage teardown", PanelManager.CloseAll);
                Guard.Try("Regression", "store-return-to-manage door teardown",
                    () => PanelManager.ClearReturnDoor("regression-teardown"));
                FlowTrace.Sink = sinkBefore;
                FlowTrace.Enabled = enabledBefore;
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "STORE_RETURN_TO_MANAGE_OK");
                reason = "STORE RETURN TO MANAGE OK - the Manage handoff records the sending tab, the store's " +
                         "close returns through the one arbiter to that same tab after the close grace, a failed " +
                         "open clears the door, a HUD-opened store returns nowhere, and the builder upsell is " +
                         "offered only when every slot is busy";
                return true;
            }
            reason = "store-return-to-manage: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "STORE_RETURN_TO_MANAGE_FAIL: " + reason);
            return false;
        }

        // -- shared fixture helpers -------------------------------------------

        private static int Reset(CapturingSink sink)
        {
            PanelManager.CloseAll();
            PanelManager.ClearReturnDoor("regression-reset");
            return sink.Lines.Count;
        }

        private static bool TraceSince(CapturingSink sink, int from, string system, string needle)
        {
            for (int i = from; i < sink.Lines.Count; i++)
                if (sink.Lines[i].Contains("[Flow:" + system + "]") && sink.Lines[i].Contains(needle)) return true;
            return false;
        }

        // -- A ----------------------------------------------------------------
        private static void CaseA_ReturnsToTheSendingTab(List<string> failures, StringBuilder log, CapturingSink sink)
        {
            int mark = Reset(sink);
            var manage = new FakePanel("Manage (oracle)");
            var store = new FakePanel("Realm Store (oracle)");
            var reopenedWith = new List<string>();

            Action<string> manageOpener = ctx => { reopenedWith.Add(ctx ?? "<null>"); manage.Open(); };
            Action storeOpener = () => store.Open();
            NoteOpenerCollision(log, "A");
            PanelRouter.Register(PanelId.Manage, manageOpener);
            PanelRouter.Register(PanelId.RealmStore, storeOpener);
            try
            {
                // The WO's walk starts with MANAGE OPEN. That matters: opening the store then runs the
                // arbiter's EXCLUSIVE SWAP (NotifyOpened(store) closes Manage -> its NotifyClosed arms
                // the door -> NotifyOpened must KEEP it), which is precisely where a door gets eaten.
                // A case that opens the store into an empty arbiter proves only that the arbiter CAN
                // return to Manage, not that Manage -> store -> close does.
                if (!manage.Open())
                {
                    failures.Add(Tag + " A: the Manage fixture did not open (arbiter refused a plain handle) - " +
                                 "nothing below can be trusted");
                    return;
                }

                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Troops);
                // !! NOT hardcoded to "Troops": a bare editor fixture may not offer every tab, and
                // Rebuild re-seats Tab onto a visible one. The acceptance is that the SENDING tab
                // rides through, whichever it is - so the expectation is read off the VM.
                string sendingTab = vm.Tab.ToString();
                log.AppendLine("  A: sending tab resolved to '" + sendingTab + "'" +
                               (vm.Tab == ManageTab.Troops ? " (the WO's Troops walk)" : " (fixture re-seated the tab)"));

                vm.BuySlot(ChannelId.Builder);

                if (!store.IsOpen || PanelManager.OpenPanelName != store.Name)
                    failures.Add(Tag + " A: the builder offer did not open the store (open='" +
                                 (PanelManager.OpenPanelName ?? "<none>") + "')");
                if (manage.IsOpen)
                    failures.Add(Tag + " A: the arbiter did not close Manage when the store opened - the store " +
                                 "stacked on top instead of swapping, so the close is not the case under test");
                string expectedDoor = "Manage tab=" + sendingTab;
                if (PanelManager.ReturnDoorName != expectedDoor)
                    failures.Add(Tag + " A: the store handoff did not record the sending tab as the return door " +
                                 "(ReturnDoorName='" + (PanelManager.ReturnDoorName ?? "<null>") + "', expected '" +
                                 expectedDoor + "') - this is the WO-1412 defect: CLOSE would land on the HUD");
                if (PanelManager.ReturnDoorPending)
                    failures.Add(Tag + " A: the door is already PENDING while the store is open - it must be KEPT, not armed");
                if (!TraceSince(sink, mark, "Manage", "store handoff source=builder offer returnTab=" + sendingTab))
                    failures.Add(Tag + " A: no '[Flow:Manage] store handoff source=builder offer returnTab=" + sendingTab + "' line");
                if (!TraceSince(sink, mark, "Navigation", "return door SET '" + expectedDoor + "'"))
                    failures.Add(Tag + " A: no '[Flow:Navigation] return door SET' line for the Manage opener");

                store.Close();
                int grace = PanelManager.CloseGraceUntilFrame;
                if (!PanelManager.ReturnDoorPending)
                    failures.Add(Tag + " A: closing the store to nothing did not ARM the door");
                if (PanelManager.PumpReturnDoor(grace) || manage.IsOpen)
                    failures.Add(Tag + " A: the door fired INSIDE the WO-1393 close grace (frame " + grace +
                                 ") - the re-open would eat the dismissing tap");
                if (!PanelManager.PumpReturnDoor(grace + 1))
                    failures.Add(Tag + " A: the pump after the grace (frame " + (grace + 1) + ") did not fire the door");
                if (manage.Opens != 2)
                    failures.Add(Tag + " A: Manage opened " + manage.Opens + " time(s) across the whole walk, " +
                                 "expected 2 (the player's own open, then the return)");
                if (!manage.IsOpen || PanelManager.OpenPanelName != manage.Name)
                    failures.Add(Tag + " A: closing the store did NOT return to Manage (open='" +
                                 (PanelManager.OpenPanelName ?? "<none>") + "') - the player was ejected to the HUD, " +
                                 "which is exactly the 11 -> 12 device walk");
                if (reopenedWith.Count != 1 || reopenedWith[0] != sendingTab)
                    failures.Add(Tag + " A: Manage re-opened on '" +
                                 (reopenedWith.Count > 0 ? reopenedWith[0] : "<never>") + "' instead of the sending tab '" +
                                 sendingTab + "' - the loop resumes on the wrong screen");
                if (PanelManager.ReturnDoorName != null)
                    failures.Add(Tag + " A: the door was not consumed by firing (ReturnDoorName='" +
                                 PanelManager.ReturnDoorName + "')");
                if (!TraceSince(sink, mark, "Navigation", "return door FIRED '" + expectedDoor + "'"))
                    failures.Add(Tag + " A: no '[Flow:Navigation] return door FIRED '" + expectedDoor + "'' line");

                log.AppendLine("  A: Manage[" + sendingTab + "] -> store -> close -> pump(grace)=hold, pump(grace+1)=Manage[" +
                               sendingTab + "] re-opened, door consumed");
            }
            finally
            {
                PanelRouter.Unregister(PanelId.Manage, manageOpener);
                PanelRouter.Unregister(PanelId.RealmStore, storeOpener);
            }
        }

        // -- B ----------------------------------------------------------------
        private static void CaseB_FailedOpenClearsTheDoor(List<string> failures, StringBuilder log, CapturingSink sink)
        {
            int mark = Reset(sink);
            var manage = new FakePanel("Manage (oracle B)");
            int reopens = 0;

            // An opener that runs but opens NOTHING: PanelRouter's WO-465 visibility verify sees no
            // panel recorded open and reports the open as failed - the branch under test.
            Action<string> manageOpener = _ => { reopens++; manage.Open(); };
            Action deadStoreOpener = () => { };
            NoteOpenerCollision(log, "B");
            PanelRouter.Register(PanelId.Manage, manageOpener);
            PanelRouter.Register(PanelId.RealmStore, deadStoreOpener);
            try
            {
                var vm = new ManageScreenVM();
                vm.BuySlot(ChannelId.Builder);

                if (PanelManager.ReturnDoorName != null || PanelManager.ReturnDoorPending)
                    failures.Add(Tag + " B: a store open that did not open anything left the door live " +
                                 "(ReturnDoorName='" + (PanelManager.ReturnDoorName ?? "<null>") + "') - the next close of " +
                                 "ANY panel would teleport the player into Manage");
                bool fired = PanelManager.PumpReturnDoor(PanelManager.CloseGraceUntilFrame + 1);
                if (fired || reopens != 0)
                    failures.Add(Tag + " B: the pump re-opened Manage after a failed store open (reopens=" + reopens + ")");
                if (!TraceSince(sink, mark, "Manage", "store handoff failed source=builder offer"))
                    failures.Add(Tag + " B: no '[Flow:Manage] store handoff failed' warning - a dead-ended route must " +
                                 "never be silent (section 12)");
                log.AppendLine("  B: store opener opens nothing -> door cleared, nothing re-opens, the failure is worded");
            }
            finally
            {
                PanelRouter.Unregister(PanelId.Manage, manageOpener);
                PanelRouter.Unregister(PanelId.RealmStore, deadStoreOpener);
            }
        }

        // -- C ----------------------------------------------------------------
        private static void CaseC_HudOpenedStoreDoesNotReturn(List<string> failures, StringBuilder log, CapturingSink sink)
        {
            int mark = Reset(sink);
            var manage = new FakePanel("Manage (oracle C)");
            var store = new FakePanel("Realm Store (oracle C)");

            if (!store.Open()) { failures.Add(Tag + " C: the HUD-opened store did not open"); return; }
            store.Close();
            bool fired = PanelManager.PumpReturnDoor(PanelManager.CloseGraceUntilFrame + 1);
            if (fired || manage.Opens != 0 || manage.IsOpen || PanelManager.AnyOpen)
                failures.Add(Tag + " C: a store opened from the HUD re-opened Manage on close (fired=" + fired +
                             " manage.Opens=" + manage.Opens + ") - the return door is the SENDER's, never a global rule");
            if (TraceSince(sink, mark, "Navigation", "return door FIRED"))
                failures.Add(Tag + " C: a FIRED trace appeared with no door set");
            log.AppendLine("  C: HUD -> store -> close -> nothing re-opens");
        }

        // -- D ----------------------------------------------------------------
        private static void CaseD_SourceManageHandoff(List<string> failures, StringBuilder log)
        {
            string vm = ReadOrNull(VmSrc);
            if (vm == null) { failures.Add(Tag + " D: could not read " + VmSrc); return; }

            string handoff = Between(vm, "private bool OpenRealmStoreFromManage(string source)",
                                         "private static void OpenUpgradePanel(string id)");
            if (handoff == null)
            {
                failures.Add(Tag + " D: ManageScreenVM.OpenRealmStoreFromManage not found - the ONE Manage-to-store " +
                             "handoff is gone, so every door below is unproven");
                return;
            }

            int set = handoff.IndexOf("PanelManager.SetReturnDoor(\"Manage tab=\" + tab", StringComparison.Ordinal);
            int open = handoff.IndexOf("PanelRouter.Open(PanelId.RealmStore", StringComparison.Ordinal);
            if (set < 0)
                failures.Add(Tag + " D: the handoff does not record 'Manage tab=<tab>' as the return door - closing the " +
                             "store drops to the HUD (the WO-1412 defect)");
            else if (open < 0 || set > open)
                failures.Add(Tag + " D: the handoff sets the return door AFTER opening the store - the store's own open " +
                             "would run against a door that does not exist yet");
            if (!handoff.Contains("PanelRouter.Open(PanelId.Manage, tab)"))
                failures.Add(Tag + " D: the return-door callback does not carry the sending TAB - the player would come " +
                             "back to Manage's default tab, not the one they left");
            if (!handoff.Contains("PanelManager.ClearReturnDoor(\"manage-store-open-failed\")"))
                failures.Add(Tag + " D: a failed store open does not clear the door - a live door with no store would " +
                             "fire on some unrelated close");
            if (!handoff.Contains("FlowTrace.Step(\"Manage\", \"store handoff source=\""))
                failures.Add(Tag + " D: the handoff is not traced ('[Flow:Manage] store handoff source=...')");
            if (!handoff.Contains("FlowTrace.Warn(\"Manage\", \"store handoff failed source=\""))
                failures.Add(Tag + " D: the failure branch is silent - section 12 forbids a swallowed dead end");

            // ONE mechanism: the store handoff must not grow a second door of its own.
            int handoffDoors = Count(handoff, "PanelManager.SetReturnDoor(");
            if (handoffDoors > 1)
                failures.Add(Tag + " D: the handoff sets " + handoffDoors + " return doors - WO-1400's arbiter is ONE door");

            log.AppendLine("  D: source - OpenRealmStoreFromManage sets 'Manage tab=<tab>' before the open, the callback " +
                           "carries the tab, the failure branch clears and warns");
        }

        // -- E ----------------------------------------------------------------
        private static void CaseE_SourceStoreClose(List<string> failures, StringBuilder log)
        {
            string store = ReadOrNull(StoreSrc);
            if (store == null) { failures.Add(Tag + " E: could not read " + StoreSrc); return; }

            string close = Between(store, "private void CloseStore()", "private void TraceShelfStateOnOpen()");
            if (close == null)
            {
                failures.Add(Tag + " E: PackStore.CloseStore not found - the close path cannot be evaluated, so this is " +
                             "reported as a FAILURE rather than passing vacuously");
                return;
            }

            if (!close.Contains("PanelManager.ReturnDoorName"))
                failures.Add(Tag + " E: CloseStore does not read the arbiter's return door - the close cannot report " +
                             "where the player is about to land");
            if (!close.Contains("FlowTrace.Step(\"Store\", \"close -> return opener=\""))
                failures.Add(Tag + " E: CloseStore does not trace '[Flow:Store] close -> return opener=<door>' - the WO's " +
                             "own proving line");
            if (close.Contains("PanelManager.SetReturnDoor(") || close.Contains("PanelRouter.Open("))
                failures.Add(Tag + " E: CloseStore opens a return route of its OWN. The ruling is ONE arbiter: a second " +
                             "path races the armed door and re-opens Manage twice, or fights the close grace");
            if (!close.Contains("CloseViaInteractor()"))
                failures.Add(Tag + " E: CloseStore no longer closes through the interactor - that path is the soft-lock " +
                             "guard (it re-enables HeroLocomotion) AND the lifecycle that arms the arbiter");

            log.AppendLine("  E: source - CloseStore reads the door, traces the opener, owns no second return route " +
                           "(SOURCE sweep: PackStore needs the store canvas, see the honest-limit note)");
        }

        // -- F ----------------------------------------------------------------
        private static void CaseF_BusyOnlyUpsell(List<string> failures, StringBuilder log)
        {
            // Behavioural half: with no all-busy Builder line in an editor fixture, the upsell must
            // be OFF and no surface may read "Buy builder".
            var vm = new ManageScreenVM();
            vm.Rebuild();
            if (vm.BuilderUpsellVisible)
                failures.Add(Tag + " F: the builder upsell is visible with no all-busy Builder line - the WO's ruling is " +
                             "busy-only, and an upsell beside a free slot reads as noise");
            if (Mentions(vm.SlotOfferText, "buy builder") || Mentions(vm.BuilderUpsellButtonText, "buy builder"))
                failures.Add(Tag + " F: a slot-offer surface says 'Buy builder' while the upsell is hidden " +
                             "(SlotOfferText='" + (vm.SlotOfferText ?? "<null>") + "', button='" +
                             (vm.BuilderUpsellButtonText ?? "<null>") + "')");

            // Source half: the predicate and the price. BuildTimerService is a scene singleton and
            // cannot be driven to all-busy in editor batchmode (honest limit above).
            string src = ReadOrNull(VmSrc);
            if (src == null) { failures.Add(Tag + " F: could not read " + VmSrc); return; }
            string offer = Between(src, "private void BuildSlotOffer(ChannelId channel)", "private void BuildRepairOffer()");
            if (offer == null)
            {
                failures.Add(Tag + " F: ManageScreenVM.BuildSlotOffer not found - the busy-only rule cannot be evaluated");
                return;
            }
            if (!offer.Contains("busy >= slots"))
                failures.Add(Tag + " F: the upsell is not gated on 'busy >= slots' - it would show while a builder is " +
                             "free (REVIEW_B A9)");
            if (!offer.Contains("pack.UsdReference"))
                failures.Add(Tag + " F: the offered label carries no price at all - an unpriced BUY BUILDER is the other " +
                             "half of the WO-1412 report");
            if (!offer.Contains("slot free - tap") || !offer.Contains("slots free - tap"))
                failures.Add(Tag + " F: the free-slot branch does not tell the player to use the queue verb instead");
            // STOP NO SKR ASSERTION HERE, IN EITHER DIRECTION, AND THAT IS DELIBERATE (2026-09-09).
            // WO-1412 asks for "BUY BUILDER - 511 SKR (~$9.99)". The honest SKR figure is SERVER-
            // QUOTED (PurchaseQuoteService.SkrAmountFor, assembly DeNelle.Wallet); the authored
            // packs.json `pricing.skr` is named IN SOURCE as "a stale hand-typed figure ... nobody
            // will honour" (Assets/_Modules/Wallet/SolanaPackPricing.cs, WO-1158). Village does not
            // reference DeNelle.Wallet and WO-1282's pointer block in PackCatalog.cs forbids adding
            // it (a Google Play artifact excludes that assembly whole). So: pinning "contains SKR"
            // would fail forever, and pinning "contains no SKR" would enforce the OPPOSITE of the
            // ticket - the ManageQueueDrawerRegression mistake, where a suite guaranteed the defect.
            // The label stays USD-only until the owner rules. See WORK_ORDER_1412 item 2.

            log.AppendLine("  F: upsell hidden with no all-busy line, no 'Buy builder' copy on any surface; source - " +
                           "gated on busy >= slots, priced from UsdReference (SKR half awaits a ruling)");
        }

        // -- helpers ----------------------------------------------------------

        /// <summary>
        /// PanelRouter.Register REPLACES, and Unregister only removes an IDENTICAL delegate - so a
        /// fake opener registered here DESTROYS any opener that was already installed for that id
        /// rather than restoring it, and the router exposes no way to read the previous one back.
        /// Nothing in Assets/Editor/ registers PanelId.Manage or PanelId.RealmStore today (grepped
        /// 2026-09-09; RealmStoreSingleRegistrarRegression only names the call as a source string),
        /// and DataRegression runs with no live panels. This LOGS the state anyway, so the day a
        /// suite ordering hazard appears it is visible in the run log instead of being a mystery.
        /// </summary>
        private static void NoteOpenerCollision(StringBuilder log, string caseName)
        {
            bool manage = PanelRouter.IsRegistered(PanelId.Manage);
            bool store = PanelRouter.IsRegistered(PanelId.RealmStore);
            if (manage || store)
                log.AppendLine("  " + caseName + ": NOTE - an opener was already registered before this case " +
                               "(Manage=" + manage + " RealmStore=" + store + "); this suite's fake openers replace " +
                               "it and cannot restore it. Check suite ordering if a later case misbehaves.");
        }

        private static bool Mentions(string text, string needle) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static string Between(string src, string from, string until)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(until, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }

        private static int Count(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}

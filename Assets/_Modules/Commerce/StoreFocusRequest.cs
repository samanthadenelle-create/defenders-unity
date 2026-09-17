// =============================================================================
// StoreFocusRequest - the rail-neutral "open the store on THIS sku" latch (WO-1282).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Commerce   Namespace: DeNelle.Commerce   (STATIC)
//
// WHY IT EXISTS. DeNelle.Village used to call PackStore.RequestFocusSku directly
// (ManageScreenVM.BuySlot, WO-1253). PackStore is 3546 lines, constructs a
// WalletService, and CANNOT leave DeNelle.Wallet - so that one call was the last
// thing keeping Village bound to the Solana rail on this path. The LATCH moves; the
// 3546-line store does not.
//
// ⛔ THIS IS A LATCH, NOT AN EVENT, AND THE DIFFERENCE IS THE WHOLE DESIGN.
//    PackStore.RequestFocusSku was already static precisely because "the host may
//    not exist yet" - the request is made BEFORE the panel is built and is consumed
//    on its next Render. An event would fire into an empty room and be lost. A latch
//    survives the gap, which is the behaviour WO-1253 shipped and depends on.
//
// ⛔ AND THAT IS WHY THIS SEAM CANNOT FAIL SILENTLY THE WAY A REGISTERED HOOK CAN
//    (WO-1282's own correction block warns about exactly that class of seam). There
//    is no registration and no ordering: the caller writes a string here, and
//    whichever storefront is compiled into this build reads it back. In a Google Play
//    artifact NOTHING reads it, and that is correct and intended - there is no Solana
//    storefront to focus. The latch simply sits, and Consume() never runs.
// =============================================================================

using DeNelle.Core.Diagnostics;

namespace DeNelle.Commerce
{
    /// <summary>
    /// A pending "open the storefront focused on this SKU" request. Written by any assembly that
    /// can name Commerce; read by whichever storefront implementation this build carries.
    /// </summary>
    public static class StoreFocusRequest
    {
        /// <summary>FlowTrace system tag for every line this seam emits. Matches PackStore's.</summary>
        private const string TraceSystem = "Store";

        private static string _pendingSku;

        /// <summary>
        /// WO-1253 - ask the storefront to open pre-focused on a named SKU (the Manage
        /// "Buy builder" route). Static because the host may not exist yet; the storefront
        /// consumes it on its next render.
        /// </summary>
        public static void RequestFocusSku(string sku)
        {
            _pendingSku = sku;
            FlowTrace.Step(TraceSystem, "RequestFocusSku '" + (sku ?? "<null>") + "' latched.");
        }

        /// <summary>
        /// Takes the pending SKU and CLEARS it, so a focus request is honoured exactly once.
        /// Returns null when nothing is pending - the normal case on almost every render.
        /// </summary>
        public static string Consume()
        {
            var sku = _pendingSku;
            _pendingSku = null;
            return sku;
        }

        /// <summary>True when a focus request is waiting. Does NOT consume it.</summary>
        public static bool HasPending => !string.IsNullOrEmpty(_pendingSku);

        // ---------------------------------------------------------------------
        //  WO-1388 - THE DOOR. Which entry point opened the store, for the funnel's
        //  store_opened {door}. Same latch shape as the SKU above, for the same reason:
        //  the host may not exist when the door is walked through, and the read happens
        //  on the next OnEnable. Written by PackStoreBootstrap from the PanelRouter
        //  context string; callers that open with no context leave it unset and the
        //  store infers (shortfall / manage / hud-card) from the other latches.
        // ---------------------------------------------------------------------
        private static string _pendingDoor;

        /// <summary>Latch the door name (hud-card | shortfall | settings | vendor ...) for the next open.</summary>
        public static void RequestDoor(string door)
        {
            _pendingDoor = string.IsNullOrWhiteSpace(door) ? null : door.Trim();
            FlowTrace.Step(TraceSystem, "RequestDoor '" + (_pendingDoor ?? "<null>") + "' latched.");
        }

        /// <summary>Takes the pending door and CLEARS it. Null when no caller named one.</summary>
        public static string ConsumeDoor()
        {
            var door = _pendingDoor;
            _pendingDoor = null;
            return door;
        }

        // ---------------------------------------------------------------------
        //  WO-1801 - THE SHORTFALL, handed over by whoever HAS the gap.
        // ---------------------------------------------------------------------
        //  PackStore.FocusShortfall(label, missing) already existed and is the right
        //  seam - but it is an INSTANCE method on a DeNelle.Wallet type, so no caller
        //  outside that assembly could ever reach it, and on 2026-09-16 it had ZERO
        //  callers anywhere in the tree. The shortfall context lives with the build /
        //  upgrade surface that is blocked, which is DeNelle.Village.
        //
        //  So the shortfall takes the same LATCH shape as the SKU and the door above,
        //  for the same stated reason: the storefront host may not exist when the door
        //  is walked through, and the read happens on its next render. In an artifact
        //  with no Solana storefront nothing reads it and that is correct.
        //
        //  ⛔ THE STORE STILL CANNOT COMPUTE A SHORTFALL BY ITSELF and this does not
        //     let it: a gap only exists relative to a thing the player is blocked ON.
        //     This carries the caller's OWN numbers across the assembly line; it
        //     derives nothing.
        // ---------------------------------------------------------------------
        private static string _pendingShortfallLabel;
        private static int _pendingShortfallMissing;

        /// <summary>
        /// Latch the gap the next store open is a remedy for: the caller's own resource word
        /// ("Wood"/"Iron"/"Stone"/"Crystals") and how many units short they are. A non-positive
        /// <paramref name="missing"/> or a blank label CLEARS the latch - there is no such thing as
        /// a zero shortfall, and half a latch would make the store claim a context it does not have.
        /// </summary>
        public static void RequestShortfall(string resourceLabel, int missing)
        {
            if (string.IsNullOrWhiteSpace(resourceLabel) || missing <= 0)
            {
                _pendingShortfallLabel = null;
                _pendingShortfallMissing = 0;
                FlowTrace.Step(TraceSystem, "RequestShortfall CLEARED (label='" +
                    (resourceLabel ?? "<null>") + "' missing=" + missing + ").");
                return;
            }
            _pendingShortfallLabel = resourceLabel.Trim();
            _pendingShortfallMissing = missing;
            FlowTrace.Step(TraceSystem, "RequestShortfall '" + _pendingShortfallLabel + "' x" +
                missing + " latched.");
        }

        /// <summary>
        /// Takes the pending shortfall and CLEARS it, so it is honoured exactly once. Returns false
        /// when nothing is pending - the normal case on almost every open.
        /// </summary>
        public static bool ConsumeShortfall(out string resourceLabel, out int missing)
        {
            resourceLabel = _pendingShortfallLabel;
            missing = _pendingShortfallMissing;
            _pendingShortfallLabel = null;
            _pendingShortfallMissing = 0;
            return !string.IsNullOrEmpty(resourceLabel) && missing > 0;
        }

        /// <summary>True when a shortfall is waiting. Does NOT consume it.</summary>
        public static bool HasPendingShortfall =>
            !string.IsNullOrEmpty(_pendingShortfallLabel) && _pendingShortfallMissing > 0;
    }
}

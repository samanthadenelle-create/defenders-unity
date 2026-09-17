// =============================================================================
// FirstBuyDoorModel — WO-1801: the ONE door that offers the $1.99 first buy at the
// moment of need, and the gates that keep it from becoming a nag.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village   (STATIC MODEL)
//
// Owner, 2026-09-16, verbatim: "yeah cause i just want a first sale you know".
//
// ⛔ WHAT THE PRODUCTION NUMBERS SAID, AND WHY THIS FILE IS THE ANSWER (30 days,
//    analytics_events, read by the lead 2026-09-16): store_opened 59 events by THREE
//    ids; pack_tapped 54 by two ids, last on 09-10; checkout_started 9; purchase_completed
//    3 by ONE id (the owner's own, Aug 25) - against ~25 distinct players a day. The
//    funnel does not leak at the shelf. Almost nobody OPENS the store at all, because
//    nothing in the game ever OFFERS it at a moment the player wants something.
//
// ⛔ AND THE STORE DOOR AT THE HIGHEST-INTENT MOMENT DID NOT EXIST. Measured 2026-09-16:
//    * PackStore.FocusShortfall(label, missing) - the "open the store on the remedy"
//      seam - had ZERO callers anywhere in the tree;
//    * the upgrade panel's WO-1037 shortfall plate is INERT by construction and says
//      "Coming soon - tap to dismiss" (BuildingUpgradePanelMvvm.BuildShortfallBand),
//      even though FeatureFlags.RealmStorePurchase now returns TRUE
//      (FeatureFlags.cs:751) and three real purchases have settled.
//    So the player who decides they want the Barracks, cannot pay for it, and is
//    therefore at their most willing, is handed a dead end.
//
// THIS MODEL IS THE DOOR'S BRAIN, NOT ITS SKIN. It decides whether to offer, words the
// line, and performs the open. BuildStructureInfoPanel paints one row and calls Tap().
// Model-side deliberately: the UI-MVVM conformance oracle failed BuildPreviewModal by
// name for reading game state in a View, and derived text is model work (canon 9).
//
// ⛔ ONE DOOR, ONE PACK, NO NAG - each rule is a line of code here:
//    * ONE PACK: FirstBuyOffer.Resolve() reads the authored FIRST BUY badge. Never a
//      SKU literal here, never an upsell ladder, never a second pack.
//    * IT MUST ACTUALLY CLOSE THE GAP, in EVERY lane (FirstBuyOffer.Covers). A door
//      that closes three of four gaps sends the player to a till and leaves them
//      blocked, which is worse than no door.
//    * ONCE PER SESSION PER STRUCTURE (_offeredThisSession). Not persisted: a
//      dismissal is a mood, not a setting - the same reasoning, and the same shape, as
//      BuildingUpgradePanelMvvm._dismissedOffers.
//    * NEVER DURING A WAVE - the same combat authority the shops already close on
//      (AmbientNPC.IsCombatActive, via DialogueCommandSink.ShopsClosedForCombat). No
//      second wave-state read is introduced and WaveManager is not touched.
//    * ONLY BEFORE THE FIRST SALE - FirstBuyOffer.HasPaidEntitlement() fails CLOSED.
//    * FAIL CLOSED ON EVERY UNKNOWN: no purchase rail, no storefront registered in this
//      artifact, no economy service, no pack, gap not covered => NO DOOR. A door that
//      opens onto something the player cannot complete is the WO-931 mistake.
//
// ⛔ AND IT NEVER PRINTS A DISCOUNT. The 20%-off shortfall grant is issued SERVER-SIDE at
//    the till, once per wallet per 7 days, and the public price list deliberately excludes
//    it (api/purchases/quote.js:308). The caption therefore prints the AUTHORED USD only;
//    the confirm line renders the server's own discountLabel once the quote returns. A
//    client-side "20% off today" would be a claim this build cannot prove, and would be
//    flatly wrong for any player who already spent their window (CLAUDE.md §11B).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Commerce;
using DeNelle.Core.Analytics;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village
{
    /// <summary>
    /// The projection one build surface paints: whether to show the first-buy line, what it says,
    /// and the gap it is a remedy for. <see cref="Show"/> false is the normal outcome.
    /// </summary>
    public readonly struct FirstBuyDoor
    {
        /// <summary>True when the surface should paint the line.</summary>
        public readonly bool Show;
        /// <summary>The player-facing line, ASCII, words-carry-the-state (never colour alone).</summary>
        public readonly string Caption;
        /// <summary>The pack the door opens on ("" when not showing).</summary>
        public readonly string Sku;
        /// <summary>The worst-short resource word ("Iron"), or null.</summary>
        public readonly string ResourceLabel;
        /// <summary>Units short of <see cref="ResourceLabel"/>.</summary>
        public readonly int Missing;
        /// <summary>Why the door is or is not shown - traced, never rendered.</summary>
        public readonly string Reason;
        /// <summary>The structure this offer belongs to (the once-per-session key).</summary>
        public readonly string StructureId;

        public FirstBuyDoor(bool show, string caption, string sku, string label, int missing,
                            string reason, string structureId)
        {
            Show = show; Caption = caption; Sku = sku; ResourceLabel = label;
            Missing = missing; Reason = reason; StructureId = structureId;
        }

        public static FirstBuyDoor None(string reason, string structureId) =>
            new FirstBuyDoor(false, string.Empty, string.Empty, null, 0, reason, structureId);
    }

    /// <summary>
    /// Resolves and performs the WO-1801 first-buy door. Static: it holds one session latch and no
    /// scene state, so a regression can drive every rule without a scene.
    /// </summary>
    public static class FirstBuyDoorModel
    {
        private const string TraceSystem = "Store";

        /// <summary>The funnel events this door adds. TWO, one emit site each, same discipline as
        /// the WO-1388 store funnel. api/events/track.js applies NO event-name allowlist (read at
        /// source 2026-09-16: it skips only a blank name), so these land with no server change.</summary>
        public const string EventShown = "shortfall_offer_shown";
        public const string EventTapped = "shortfall_offer_tapped";

        /// <summary>The funnel door name this open is recorded under. Already a documented door
        /// (StoreFocusRequest.RequestDoor) - not a new vocabulary word.</summary>
        public const string DoorName = "shortfall";

        // ── The no-nag latch ─────────────────────────────────────────────────
        // Keyed by structure id, session-scoped, NOT persisted. Static so it survives the panel
        // being closed and reopened, which is what "once this session" means to a player; cleared
        // only by a domain reload or a new run. Persisting it would silently hide the door forever
        // after one glance, which is the failure mode WO-1037 §5 recorded for its own dismissal.
        private static readonly HashSet<string> _offeredThisSession = new HashSet<string>();

        /// <summary>Has this structure already been offered the door this session?</summary>
        public static bool WasOfferedThisSession(string structureId) =>
            !string.IsNullOrEmpty(structureId) && _offeredThisSession.Contains(structureId);

        /// <summary>Records that the door was SHOWN for this structure. Idempotent.</summary>
        public static void MarkOfferedThisSession(string structureId)
        {
            if (!string.IsNullOrEmpty(structureId)) _offeredThisSession.Add(structureId);
        }

        /// <summary>Clears the session latch. For regressions and a new run only.</summary>
        public static void ResetSessionLatch() => _offeredThisSession.Clear();

        // ── The decision ─────────────────────────────────────────────────────

        /// <summary>
        /// Should this structure's blocked build offer the first-buy door, and what does it say?
        /// Pure apart from the reads it names: economy balances, combat state, entitlement, catalog.
        /// Never throws; every refusal carries a traced reason.
        /// </summary>
        /// <param name="entry">The structure the player is looking at.</param>
        /// <param name="cost">The cost that surface is showing (the freebie-aware effective cost).</param>
        public static FirstBuyDoor Resolve(CatalogEntry entry, CoreCost cost)
        {
            string id = entry != null ? entry.id : null;
            if (entry == null || string.IsNullOrEmpty(id))
                return FirstBuyDoor.None("no entry", id);

            var door = FirstBuyDoor.None("unresolved", id);
            Guard.Try(TraceSystem, "resolve the first-buy door for " + id, () =>
            {
                // (1) THE RAIL. A door onto a closed rail is the WO-931 defect.
                if (!DeNelle.Core.FeatureFlags.RealmStorePurchase)
                { door = FirstBuyDoor.None("purchase rail CLOSED (FeatureFlags.RealmStorePurchase)", id); return; }

                // (2) A STOREFRONT MUST EXIST IN THIS ARTIFACT. PanelRouter is last-writer-wins and
                //     unregistered ids simply refuse to open; asking first means the door is never
                //     painted over a dead route.
                if (!PanelRouter.IsRegistered(PanelId.RealmStore))
                { door = FirstBuyDoor.None("no RealmStore registrar in this artifact", id); return; }

                // (3) NEVER DURING A WAVE - the shops are shut, and an offer mid-assault is a nag.
                if (AmbientNPC.IsCombatActive)
                { door = FirstBuyDoor.None("combat active - shops closed during the assault", id); return; }

                // (4) ONCE PER SESSION PER STRUCTURE.
                if (WasOfferedThisSession(id))
                { door = FirstBuyDoor.None("already offered for this structure this session", id); return; }

                // (5) A REAL, MEASURED GAP. No economy service => no gap can be measured => no door.
                var econ = EconomyService.Instance;
                if (econ == null)
                { door = FirstBuyDoor.None("no EconomyService - a gap cannot be measured", id); return; }

                int missWood = Missing(cost.wood, econ.Wood);
                int missIron = Missing(cost.iron, econ.Iron);
                int missStone = Missing(cost.stone, econ.Stone);
                int missCrystals = Missing(cost.crystals, econ.Crystals);
                if (missWood <= 0 && missIron <= 0 && missStone <= 0 && missCrystals <= 0)
                { door = FirstBuyDoor.None("affordable - no shortfall", id); return; }

                // (6) THE PACK, AND IT MUST CLOSE EVERY LANE.
                var pack = FirstBuyOffer.Resolve();
                if (pack == null)
                { door = FirstBuyDoor.None("no first-buy pack in the catalog", id); return; }
                if (!FirstBuyOffer.Covers(pack, missWood, missIron, missStone, missCrystals))
                {
                    door = FirstBuyDoor.None("the first-buy grant does not cover the gap (" +
                        "short wood " + missWood + " / iron " + missIron + " / stone " + missStone +
                        " / crystals " + missCrystals + ")", id);
                    return;
                }

                // (7) ONLY BEFORE THE FIRST SALE (fails closed on "cannot tell").
                //
                // ⚠ DELIBERATELY THE LAST GATE, AND THAT IS A COST DECISION. The probe walks every
                // priced row in the catalogue and each probe builds a PackStoreVM over the live save;
                // running it before the cheap arithmetic above would pay that walk on EVERY tap of
                // EVERY unaffordable card, including the majority of taps the gap check rejects in a
                // handful of integer comparisons.
                if (FirstBuyOffer.HasPaidEntitlement())
                { door = FirstBuyDoor.None("this save already carries a paid entitlement (or it cannot be proven)", id); return; }

                // The WORST lane names the gap, because that is the number the player is staring at.
                string label; int missing;
                Worst(missWood, missIron, missStone, missCrystals, out label, out missing);

                // ASCII only, and the WORDS carry the state - the owner is red/green colourblind, so
                // nothing here may depend on a tint. The price is the AUTHORED figure; no discount is
                // claimed (see the class header).
                string caption = "Short " + missing + " " + label + " - raise it now for " +
                                 pack.UsdReference;

                door = new FirstBuyDoor(true, caption, pack.Sku, label, missing,
                    "offering '" + pack.Sku + "' " + pack.UsdReference + " (covers wood " + missWood +
                    " / iron " + missIron + " / stone " + missStone + " / crystals " + missCrystals + ")", id);
            });

            FlowTrace.Step(TraceSystem, "first-buy door on '" + id + "': " +
                (door.Show ? "SHOW - " : "hidden - ") + door.Reason);
            return door;
        }

        /// <summary>
        /// The surface reports that it PAINTED the line: latch the no-nag rule and emit
        /// <see cref="EventShown"/>. Called by the View immediately after it builds the row, so the
        /// event counts impressions the player could actually see - never a resolve that was thrown
        /// away (the mistake WO-1798 §0 named in bundle_viewed, which fires per card BUILT).
        /// </summary>
        public static void NotifyShown(FirstBuyDoor door)
        {
            if (!door.Show) return;
            MarkOfferedThisSession(door.StructureId);
            Guard.Try(TraceSystem, "track " + EventShown, () =>
                EventTracker.Track(EventShown, new
                {
                    sku = door.Sku,
                    structure = door.StructureId,
                    resource = door.ResourceLabel,
                    missing = door.Missing,
                    door = DoorName,
                }));
        }

        /// <summary>
        /// The tap. Latches the pack AND the gap on the rail-neutral
        /// <see cref="StoreFocusRequest"/> seam, emits <see cref="EventTapped"/>, and opens the
        /// store on that pack through the door the funnel already knows.
        ///
        /// <para>⛔ IT DOES NOT CHARGE, QUOTE OR GRANT ANYTHING. The till owns the price, the
        /// discount and the refusal; this only routes the player to it with the context. A false
        /// return means the route refused and the caller must say so rather than appear to work.</para>
        /// </summary>
        public static bool Tap(FirstBuyDoor door)
        {
            if (!door.Show || string.IsNullOrEmpty(door.Sku))
            {
                FlowTrace.Warn(TraceSystem, "first-buy door tapped with nothing to open - ignored.");
                return false;
            }

            // ⛔ RE-CHECK COMBAT AT THE TAP, NOT ONLY AT THE PAINT. A wave can start in the seconds
            // between the row being drawn and the finger landing, and the shops shut the moment it
            // does - DialogueCommandSink re-checks on EVERY shop verb for exactly this reason. The
            // refusal uses the SAME sentence that path already shows, so the player never meets two
            // wordings for one rule.
            if (AmbientNPC.IsCombatActive)
            {
                FlowTrace.Warn(TraceSystem, "first-buy door tap BLOCKED - combat started after the row " +
                    "was painted (shops closed during the assault).");
                BuildFeedbackToast.Show("Shops closed during the assault!");
                return false;
            }

            Guard.Try(TraceSystem, "track " + EventTapped, () =>
                EventTracker.Track(EventTapped, new
                {
                    sku = door.Sku,
                    structure = door.StructureId,
                    resource = door.ResourceLabel,
                    missing = door.Missing,
                    door = DoorName,
                }));

            StoreFocusRequest.RequestFocusSku(door.Sku);
            StoreFocusRequest.RequestShortfall(door.ResourceLabel, door.Missing);

            bool opened = PanelRouter.Open(PanelId.RealmStore, DoorName);
            if (!opened)
            {
                // Leaving the latches set would focus a later, unrelated open on this pack.
                StoreFocusRequest.Consume();
                StoreFocusRequest.RequestShortfall(null, 0);
                FlowTrace.Fail(TraceSystem, "first-buy door: PanelId.RealmStore refused to open - " +
                    "latches cleared so a later store open is not silently hijacked. The line said " +
                    "'" + door.Caption + "' and led nowhere.");
                return false;
            }

            FlowTrace.Step(TraceSystem, "first-buy door TAPPED on '" + door.StructureId + "' -> store " +
                "sku=" + door.Sku + " shortfall=" + door.Missing + " " + door.ResourceLabel +
                " door=" + DoorName + ".");
            return true;
        }

        // ── Pure helpers ─────────────────────────────────────────────────────

        private static int Missing(int cost, int held) => cost > held ? cost - held : 0;

        /// <summary>The biggest gap, and its player-facing word. Ties resolve wood > iron > stone >
        /// crystals - a fixed order, so the sentence is deterministic between two renders.</summary>
        private static void Worst(int wood, int iron, int stone, int crystals,
                                  out string label, out int missing)
        {
            label = "Wood"; missing = wood;
            if (iron > missing) { label = "Iron"; missing = iron; }
            if (stone > missing) { label = "Stone"; missing = stone; }
            if (crystals > missing) { label = "Crystals"; missing = crystals; }
        }
    }
}

// =============================================================================
// HeartboundEchoEvent - the DESCRIPTOR of one Echo Event, as the backend decided
// it. Dumb data: it judges nothing, applies nothing and derives nothing.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Heartbound
// WO-1678 (HEART-005). Owner rulings 2026-09-10 13:12 / 13:20 / 13:36.
//
// ⛔ THE SERVER DECIDES, THE CLIENT APPLIES, AND THAT ASYMMETRY IS THE DESIGN.
// api/_lib/heartbound-events.js rolls the event from
// SHA256(globalPulseId + playerId + tableVersion) and hands the ANSWER across the
// wire. This struct is that answer. It must never grow a method that PICKS an
// event, scales a reward or re-derives a tier - a client that could compute its
// own event is a client that can choose one.
//
// ⛔ AND IT NEVER CARRIES THE SEED. The payload shape at
// heartbound-events.js `toClientPayload` deliberately omits `seedHex`: a client
// holding the seed could enumerate the table offline and know its next event.
// There is no field for it below, so a future edit has to add one on purpose.
//
// ⚠ WHY IT IS SEEKER-ONLY AT COMPILE TIME. Owner ruling 2026-09-10 13:10 -
// "Seeker only; Play never shows it." DAPP_STORE is the DISTRIBUTION define
// (AndroidBuild.cs stamps exactly one of DAPP_STORE / GOOGLE_PLAY per artifact),
// so #if DAPP_STORE says WHERE the binary is going, which is the only thing that
// answers the compliance question. A feature flag would not: FeatureFlags.cs
// records that a stored PlayerPrefs value BEATS the default, so a flag-gated
// Heartbound is flippable in a shipped Play build. Absent code is absent.
//
// ⛔ NO SAVE SCHEMA CHANGE. Nothing here is serialised into the save blob. The
// once-only ledger is the backend's (WO-1677) with a local guard in
// HeartboundEventInbox; SaveSchema.CurrentVersion does not move for this feature,
// and a bump would be the defect (the save is CLIENT-AUTHORED, so a replayed blob
// could rewrite a claim).
//
// ASCII only in every string that can reach a font atlas.
// =============================================================================

#if DAPP_STORE

using System;

namespace DeNelle.Core.Heartbound
{
    /// <summary>
    /// The reward classes an Echo Event may carry. This is an ALLOW-LIST, and the
    /// backend refuses any row outside it.
    ///
    /// <para>⛔ NO MEMBER OF THIS ENUM MAY EVER GRANT COMBAT POWER. Owner ruling
    /// Q-P2W (2026-09-10 13:10): economic acceleration is allowed, combat power is
    /// not. Adding a "Damage" or "Armour" member is the defect this list exists to
    /// make visible; HeartboundEventRegression fails on one.</para>
    ///
    /// <para>Ordinals are stable. Nothing indexes an array by them today, but the
    /// analytics and trace strings quote the NAME, so a rename is a doc change and
    /// a renumber is free - keep it that way by never depending on the number.</para>
    /// </summary>
    public enum HeartboundRewardKind
    {
        /// <summary>Unparseable or absent. Applies nothing, and says so.</summary>
        None = 0,

        /// <summary>A time-boxed economic rate/speed modifier. See HeartboundModifiers.</summary>
        Modifier = 1,

        /// <summary>
        /// Earlier access to the EXISTING scout report. Owner ruling 2026-09-10 13:36:
        /// "existing report, earlier ... no new intel." Never a new field of intel.
        /// </summary>
        Scout = 2,

        /// <summary>
        /// The ONE ruled second source of Heartfire (owner, 2026-09-10 13:12). Capped,
        /// never purchasable, clamped at the pool ceiling.
        /// </summary>
        Heartfire = 3,

        /// <summary>
        /// A game-native item, granted through the reward authority.
        /// <para>⚠ NOT REACHABLE IN TABLE V1, ON PURPOSE. Every item row was removed by
        /// ruling (the dungeon ingredient) or moved to its own ticket (the visiting
        /// vendor), so no V1 row carries this kind. It is declared because the payload
        /// contract names it; the applier refuses it rather than pretending to grant
        /// something, and says which ticket owns the wiring.</para>
        /// </summary>
        Item = 4,
    }

    /// <summary>
    /// One Echo Event, exactly as the backend rolled it. Immutable by construction.
    /// </summary>
    [Serializable]
    public struct HeartboundEchoEvent
    {
        /// <summary>Row id from the authored table (e.g. "resource_surge"). Never localised.</summary>
        public string EventId;

        /// <summary>Which reward class this row pays.</summary>
        public HeartboundRewardKind Kind;

        /// <summary>
        /// The table version the server rolled against. Part of the seed, so an event
        /// from an older table stays valid rather than being re-rolled.
        /// </summary>
        public int TableVersion;

        /// <summary>
        /// The once-only key: "&lt;pulseId&gt;:&lt;tableVersion&gt;". The client's local
        /// guard keys on this, and the server's ledger keys on the same pulse - so both
        /// halves agree on what "the same event" means without either deriving it.
        /// </summary>
        public string ClaimId;

        /// <summary>Which modifier lane a <see cref="HeartboundRewardKind.Modifier"/> moves.</summary>
        public string Modifier;

        /// <summary>Modifier size as a FRACTION (0.15 = +15%). Authored in config, never here.</summary>
        public double Magnitude;

        /// <summary>How long the reward lasts, in seconds. 0 means instantaneous.</summary>
        public double DurationSeconds;

        /// <summary>Heartfire charges to light. Authored; the applier still clamps at the ceiling.</summary>
        public int Charges;

        /// <summary>Item id for <see cref="HeartboundRewardKind.Item"/>. Empty in table V1.</summary>
        public string ItemId;

        /// <summary>Item count for <see cref="HeartboundRewardKind.Item"/>.</summary>
        public int Amount;

        /// <summary>True when this descriptor names a real event (the server said hasEvent).</summary>
        public bool HasEvent;

        /// <summary>The "no event this pulse" answer - a normal outcome, not a failure.</summary>
        public static HeartboundEchoEvent Empty => new HeartboundEchoEvent
        {
            EventId = string.Empty,
            Kind = HeartboundRewardKind.None,
            TableVersion = 0,
            ClaimId = string.Empty,
            Modifier = string.Empty,
            ItemId = string.Empty,
            HasEvent = false,
        };

        /// <summary>
        /// Parse a `kind` string from the payload. Unknown text is <see cref="HeartboundRewardKind.None"/>,
        /// never a guess - a build that met a kind it does not understand must apply
        /// NOTHING rather than the nearest thing it recognises.
        /// </summary>
        public static HeartboundRewardKind ParseKind(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return HeartboundRewardKind.None;
            switch (text.Trim().ToLowerInvariant())
            {
                case "modifier": return HeartboundRewardKind.Modifier;
                case "scout": return HeartboundRewardKind.Scout;
                case "heartfire": return HeartboundRewardKind.Heartfire;
                case "item": return HeartboundRewardKind.Item;
                default: return HeartboundRewardKind.None;
            }
        }

        /// <summary>A one-line diagnostic form. Never shown to a player.</summary>
        public override string ToString()
        {
            return "EchoEvent[" + (EventId ?? "?") + " kind=" + Kind + " v" + TableVersion +
                   " claim=" + (ClaimId ?? "?") + "]";
        }
    }
}

#endif

// =============================================================================
// HeartboundEventApplier - the ONE thing that turns a decided Echo Event into
// something the settlement felt. Registers itself into HeartboundEventInbox and
// is the only implementation of IHeartboundEventApplier.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Heartbound
// WO-1678 (HEART-005). Owner rulings 2026-09-10 13:10 / 13:12 / 13:20 / 13:36.
//
// ⛔ WHY THE APPLIER LIVES HERE AND THE PARSER LIVES IN DeNelle.Wallet.
// DeNelle.Wallet references Core / Commerce / Data and NOT DeNelle.Village, so the
// file that reads the server's answer structurally cannot touch Heartfire, the
// inventory or the raid screen. The event crosses as DATA through Core, and the
// module that owns the game objects registers as its applier. That is the
// CoreServices.Hud shape, and it is the reason this is two files instead of one.
//
// ⛔ WHAT THIS FILE MAY NEVER DO, each one an owner ruling with a date:
//
//   * GRANT THE DUNGEON INGREDIENT (2026-09-10 13:12 - the event was DROPPED, not
//     deferred). Its supply is capped on both existing faucets and its only use is
//     a weighted craft roll; a staked position must not become a third faucet.
//     There is no branch below that could grant it, the shipped table carries no
//     such row, and HeartboundEventRegression fails on the token in this file.
//
//   * SPAWN AN ECHO (2026-09-10 13:20 - "modifiers only ... EchoWorldPresence stays
//     the one owner"). Echo Labor moves a NUMBER. No Instantiate, no prefab, no
//     second appearance owner. The single-appearance-owner rule is pinned
//     independently by EchoWorldPresenceRegression; this file must not be the thing
//     that breaks it.
//
//   * CREATE SCOUT INTELLIGENCE (2026-09-10 13:36 - "existing report, earlier ... no
//     new intel"). The scout branch sets a FLAG. It builds no lines, reads no
//     garrison and never touches RaidDeployVM.BuildScoutReport, whose last line must
//     remain the spoils estimate (RaidDeployZeroArmyRegression [zero-army-spoils]).
//
//   * TOUCH COMBAT (2026-09-10 13:10 Q-P2W - "economic acceleration is allowed;
//     combat power stays off the table"). Every lane it can move is economic.
//
// ⛔ AND IT NEVER MARKS ITS OWN CLAIM. The inbox does that, and only after this
// returns true. A branch that applied half a reward and returned true would burn
// the claim; every branch below returns the truth about whether it paid.
//
// ⚠ HONEST LIMITS, NAMED RATHER THAN IMPLIED:
//   * The modifier lanes are WRITTEN into HeartboundModifiers and read by nothing
//     in the economy yet. The consumers are WO-1679 (benefit table) and WO-1682
//     (the 10% acceleration meter), and wiring them before the meter exists would
//     be shipping an uncapped acceleration and calling it capped.
//   * The scout flag is written and read by nobody yet - the deploy screen already
//     paints the report unconditionally, so "earlier" means the raid SELECTION
//     surface, which belongs to the raid lane's files.
//   * The Item branch REFUSES. No V1 row carries it (see the applier's own trace).
//
// #if DAPP_STORE: Seeker-only by owner ruling 2026-09-10 13:10. A plain Windows or
// WebGL build carries neither distribution define, so the guard is not redundant
// with DeNelle.Wallet's !GOOGLE_PLAY constraint - and DeNelle.Village has no such
// constraint at all, which is exactly why this file needs its own.
// =============================================================================

#if DAPP_STORE

using DeNelle.Core.Diagnostics;
using DeNelle.Core.Heartbound;
using DeNelle.Village.World.Camps;
using UnityEngine;

namespace DeNelle.Village.Heartbound
{
    /// <summary>
    /// The single <see cref="IHeartboundEventApplier"/>. Registers itself before the
    /// first scene loads, so an event that arrives during boot is applied rather than
    /// held forever.
    /// </summary>
    public sealed class HeartboundEventApplier : IHeartboundEventApplier
    {
        private const string Sys = HeartboundEventInbox.Sys;

        private static HeartboundEventApplier _instance;

        /// <summary>
        /// Install the one applier. Runs before the first scene so the inbox has an
        /// applier by the time a status poll can answer; the inbox HOLDS an event that
        /// arrives earlier and re-drives it on registration, so neither order loses one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            if (_instance != null) return;
            _instance = new HeartboundEventApplier();
            HeartboundEventInbox.RegisterApplier(_instance);
            FlowTrace.Step(Sys, "Echo Event applier installed. It is the ONE applier: a second would " +
                                "grant every event twice.");
        }

        /// <inheritdoc />
        public bool Apply(HeartboundEchoEvent evt)
        {
            if (!evt.HasEvent)
            {
                FlowTrace.Step(Sys, "apply called with no event - nothing to do.");
                return false;
            }

            double now = TimeSource.NowUnixMs();

            switch (evt.Kind)
            {
                case HeartboundRewardKind.Modifier:
                    return ApplyModifier(evt, now);

                case HeartboundRewardKind.Scout:
                    HeartboundScoutAccess.GrantEarlyReveal(evt.DurationSeconds, now);
                    FlowTrace.Step(Sys, "'" + evt.EventId + "' applied: the EXISTING scout report is " +
                                        "offered earlier. No line of intel was created - the report is " +
                                        "still built once, by the raid deploy view model, and its " +
                                        "contents are identical for every player.");
                    return true;

                case HeartboundRewardKind.Heartfire:
                    return ApplyHeartfire(evt);

                case HeartboundRewardKind.Item:
                    // Declared by the payload contract, unreachable in table V1: every item
                    // row was removed by ruling or moved to its own ticket. Refusing is the
                    // honest answer - a branch that quietly granted a placeholder would be
                    // inventing an economy the owner has not ruled on.
                    FlowTrace.Warn(Sys, "'" + evt.EventId + "' asks for an ITEM grant ('" +
                                        (evt.ItemId ?? "?") + "' x" + evt.Amount + "), which no table V1 " +
                                        "row carries and this build deliberately does not wire. Nothing " +
                                        "is granted and the claim is NOT burned, so a build that wires " +
                                        "it can still pay this event.");
                    return false;

                default:
                    FlowTrace.Warn(Sys, "'" + evt.EventId + "' carried an unrecognised reward kind - " +
                                        "nothing applied.");
                    return false;
            }
        }

        private bool ApplyModifier(HeartboundEchoEvent evt, double nowUnixMs)
        {
            bool granted = HeartboundModifiers.Grant(evt.Modifier, evt.Magnitude, evt.DurationSeconds, nowUnixMs);
            if (!granted)
            {
                FlowTrace.Warn(Sys, "'" + evt.EventId + "' did not apply - the modifier lane was refused. " +
                                    "The claim is not burned.");
                return false;
            }

            FlowTrace.Step(Sys, "'" + evt.EventId + "' applied as an ECONOMIC modifier on lane '" +
                                evt.Modifier + "'. It moves a number and nothing else: no Echo figure is " +
                                "spawned, and the Echo appearance owner stays the one owner.");
            return true;
        }

        private bool ApplyHeartfire(HeartboundEchoEvent evt)
        {
            int asked = evt.Charges;
            if (asked <= 0)
            {
                FlowTrace.Warn(Sys, "'" + evt.EventId + "' asked for " + asked + " Heartfire - refused. " +
                                    "A non-positive reward is an authoring error, not a zero grant.");
                return false;
            }

            int lit = HeartfireService.TryGrantSpark(asked, evt.EventId);
            if (lit <= 0)
            {
                // A full pool is not a failure: the ruled second source is CAPPED at the
                // same ceiling as time, so a player who is already full gains nothing and
                // the event is still consumed. Returning true burns the claim on purpose -
                // holding it would re-fire the spark the moment a charge was spent, which
                // is a source the player influences by spending, and that is a currency.
                FlowTrace.Step(Sys, "'" + evt.EventId + "' lit no Heartfire - the pool is already at its " +
                                    "ceiling. The event is still consumed: the second source is capped by " +
                                    "the SAME ceiling as time, and holding the claim until a charge was " +
                                    "spent would make spending a way to earn.");
                return true;
            }

            FlowTrace.Step(Sys, "'" + evt.EventId + "' lit " + lit + " Heartfire. This is the ONE ruled " +
                                "second source (owner, 2026-09-10): capped, never purchasable, clamped at " +
                                "the pool ceiling. Nothing was bought and no balance exists.");
            return true;
        }
    }
}

#endif

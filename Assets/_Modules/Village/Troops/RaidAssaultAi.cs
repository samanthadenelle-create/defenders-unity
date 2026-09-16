// =============================================================================
// RaidAssaultAi — WO-1595 pure assault rules (phase + job + formation).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner north star (2026-09-07): breach walls → push / capture the spire; if
// aggro or being attacked prioritize staying alive; deploy and move as a
// formation (Front ahead, ranged/DPS behind, healers safe).
//
// Pure helpers live here so EditMode regressions can assert without a scene.
// TroopController / TroopDeployer call these; they do not replace NavMesh.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Combat;

namespace DeNelle.Village
{
    /// <summary>Assault phase — Survive/Peel beats Breach beats Push/Finish.</summary>
    public enum RaidAssaultPhase
    {
        Peel = 0,
        Breach = 1,
        Push = 2,
        Finish = 3,
    }

    /// <summary>Formation job mapped from <see cref="TroopDef.Role"/>.</summary>
    public enum RaidAssaultJob
    {
        Front = 0,
        Ranged = 1,
        Breaker = 2,
        Support = 3,
    }

    /// <summary>
    /// WO-1595 — pure selector + formation math for the raid assault loop.
    /// </summary>
    public static class RaidAssaultAi
    {
        /// <summary>Seconds after taking damage that count as "under attack" for Peel.</summary>
        public const float PeelHurtWindowSeconds = 2.5f;

        /// <summary>Leash (m) inside which a hostile unit forces Peel even without recent hurt.</summary>
        public const float PeelUnitLeashMeters = 6f;

        /// <summary>
        /// WO-1752 ruling 3 — ring (m) around the HERO inside which a live hostile body counts as
        /// "the thing that is hitting her" (<see cref="HeroAggroTarget"/>).
        /// </summary>
        /// <remarks>
        /// ⚠ THIS IS A NEW AXIS, NOT A COPY OF AN EXISTING ONE, AND THE OBVIOUS CANDIDATE COULD
        /// NOT BE REUSED. HeroHealth's own contact ring — the radius inside which an adjacent
        /// enemy actually lands the contact tick — is <c>private const float EngageRadius = 1.5f</c>
        /// (HeroHealth.cs:45, read read-only; that file belongs to another lane this session, so
        /// it was not widened to public). 1.5 m would also be too tight to be useful here: an
        /// attacker that steps back half a metre between swings, or a boss with a longer reach,
        /// would flicker in and out of the answer every scan and the warband would oscillate.
        ///
        /// 8 m is a deliberately GENEROUS "standing on her" ring — wide enough to survive that
        /// flicker and to catch a mid-swing step-back, narrow enough that it cannot mean "some
        /// enemy across the courtyard". It is a FELT number and the owner's felt-test is its only
        /// real verdict; it lives here, with the other raid-AI knobs, so there is one place to
        /// turn it.
        /// </remarks>
        public const float HeroAttackerRingMeters = 8f;

        /// <summary>Front line sits this far ahead of the deploy point along the march axis.</summary>
        public const float FrontForwardMeters = 2.0f;

        /// <summary>Ranged / DPS hold this far behind the deploy point along the march axis.</summary>
        public const float RangedBackMeters = 3.5f;

        /// <summary>Support / healers hold farther back than ranged.</summary>
        public const float SupportBackMeters = 5.0f;

        /// <summary>Breaker sits slightly ahead of Front while cracking the approach.</summary>
        public const float BreakerForwardMeters = 1.25f;

        /// <summary>Lateral spacing between same-role slots (m).</summary>
        public const float LateralSpreadMeters = 1.4f;

        /// <summary>
        /// WO-1752 (owner ruling 2026-09-15) — MAY this troop put a WALL panel in its sights at
        /// all? Siege always may; everyone else only while the Breach stance is armed.
        /// </summary>
        /// <remarks>
        /// Owner ruling, verbatim: *"we need to set it so that the troops 100% ignore walls,
        /// unless explicitly told breach"*, and in the same minute, on the catapult: *"Siege still
        /// hits walls on its own."*
        ///
        /// ⛔ THIS SUPERSEDES WO-1746's RELUCTANT 10% PATH, WHICH IS DELETED, NOT ZEROED. The
        /// WO-1738 ruling this replaces let a BLOCKED, stance-less, non-siege troop take the
        /// nearest blocking wall at <c>ReluctantWallDamageMultiplier = 0.1f</c> so it never idled
        /// into a dead-end. The owner watched that ship and overruled it from the felt-test
        /// (device logcat 09-15 13:40:59, seven non-siege troops all on
        /// <c>wallDmgMult=0.10</c> while her hero was being killed). The dead-end is her
        /// deliberate, stated cost: *"a blocked warband with Breach OFF and no siege now stands
        /// (or defends the hero)"* — WO-1752, DeepSeek's dead-end concern raised and overruled.
        ///
        /// ⛔ THE CONSTANT IS GONE ON PURPOSE. Zeroing it alone would have been the wrong fix and
        /// WO-1752 says so in as many words: *"a zero multiplier still walks troops to walls"* —
        /// the troop keeps selecting the panel, keeps walking to it, keeps playing the swing, and
        /// only the number changes. The TARGETING is what had to go, which is why this predicate
        /// is consumed by <see cref="PickBucket"/> as well as by
        /// <see cref="WallDamageMultiplier"/>.
        ///
        /// Siege is identified by the CATALOG role (<c>preferStructures</c>, set from
        /// <c>def.Role == "siege"</c>), never by a hardcoded troop name — a second siege unit
        /// added to troops.json inherits the exemption with no code change.
        /// </remarks>
        public static bool MayTargetWall(bool preferStructures, bool breachStance)
        {
            return preferStructures || breachStance;
        }

        /// <summary>
        /// Structural-damage multiplier this troop applies to a WALL panel right now: 1.0 when
        /// <see cref="MayTargetWall"/> allows the panel at all (siege, or the armed Breach
        /// stance), 0.0 otherwise.
        /// </summary>
        /// <remarks>
        /// ⚠ THE 0.0 IS A BACKSTOP, NOT THE FIX — read <see cref="MayTargetWall"/> first. WO-1752
        /// is explicit that zeroing the multiplier on its own is NOT the ruling ("a zero
        /// multiplier still walks troops to walls"), and the actual change is that
        /// <see cref="PickBucket"/> no longer SELECTS the panel. This returns 0 for two concrete
        /// reasons, both of them real windows rather than tidiness:
        ///   * <c>TroopController._cachedFoe</c> is re-resolved on a timer (0.2 s hunt scan), so
        ///     between the player toggling Breach OFF and the next retarget a troop still holds a
        ///     wall it was legitimately given. It must land nothing in that window, not 10%.
        ///   * WO-1752 asks for the trace token itself: *"the `wallDmgMult=` token now prints
        ///     `0.00` for non-siege without stance (proves 1 on device)"*. One call, two readouts
        ///     (the AI line and the SWING line) — the log cannot disagree with the damage that
        ///     lands.
        ///
        /// Pure, so the regression can assert it without a scene.
        /// </remarks>
        public static float WallDamageMultiplier(bool preferStructures, bool breachStance)
        {
            return MayTargetWall(preferStructures, breachStance) ? 1f : 0f;
        }

        // =====================================================================
        //  WO-1764 — THE ONE RULING GUARD for "does an armed Breach stance outrank a
        //  REACHABLE defender?"  (owner ruling 2026-09-16)
        // =====================================================================

        /// <summary>
        /// WO-1764 — FALSE: a reachable defender beats the wall even under an armed Breach stance.
        /// Walls are for when no defender is reachable.
        /// </summary>
        /// <remarks>
        /// ⭐ OWNER RULING 2026-09-16, VERBATIM: *"Units first even inside Breach"* — any reachable
        /// defender beats the wall, even under a Breach order; walls only when no defender is
        /// reachable.
        ///
        /// ⚠ THIS SETTLES THE ONE READING WO-1746 FLAGGED AND COULD NOT DECIDE. WO-1738's status
        /// line summarised the ruling as *"units-first is the default inside it"*; its ruling BODY
        /// and WO-1719 said the stance runs *"until ... a hostile pulls aggro"*, i.e. only Peel
        /// breaks it. WO-1746 implemented the second reading, flagged the dispute in
        /// <see cref="PreferUnit"/>'s remarks rather than burying it, and pinned it with
        /// WallBreachOrderRegression Case 8. The owner watched THAT ship on Iron Bastion
        /// (APK 2026.09.16.371627) and ruled for the first reading. Status line and body now agree.
        ///
        /// ⭐ WHY A NAMED PREDICATE AND NOT A DELETION. Two independent sites in the Breach branch
        /// let a unit beat masonry — <see cref="PreferUnit"/>'s reachability rule and
        /// <see cref="PickBucket"/>'s in-attack-range shortcut — and WO-1746 shipped with the second
        /// one MISSED, which is why the stance looked broken for a whole ticket. Deleting the two
        /// <c>breachStance</c> clauses would leave a future re-flip having to find both sites again
        /// from scratch. Reading ONE predicate at both sites makes the ruling literally one line:
        /// return true here and the stance outranks a calm defender again, at both gates, with
        /// WallBreachOrderRegression Case 8 naming which reading is live.
        ///
        /// ⛔ AGGRO IS UNAFFECTED AND WAS NEVER IN QUESTION. peelThreat -> <see cref="ResolvePhase"/>
        /// returns <see cref="RaidAssaultPhase.Peel"/> -> <see cref="PreferUnit"/> returns true at
        /// the top, before the stance is consulted at all. And SIEGE is unaffected: the
        /// <c>preferStructures</c> branches return before either guard.
        /// </remarks>
        public static bool StanceOutranksReachableUnit { get { return false; } }

        // =====================================================================
        //  WO-1764 D1 — THE ROUTE-TO-UNIT DETOUR RULE, RE-DERIVED ON IRON BASTION
        // =====================================================================

        /// <summary>
        /// WO-1438's ratio bound on a route to a UNIT: a PathComplete longer than this multiple of
        /// the straight line was refused as "a lap of the wall ring". Kept as the widening half of
        /// <see cref="RouteToUnitOpen"/>; see <see cref="RouteDetourSlackMeters"/> for why a ratio
        /// alone is the wrong SHAPE, not merely the wrong number.
        /// </summary>
        /// <remarks>
        /// ⚠ THIS CONSTANT USED TO LIVE IN TroopController (`RouteDetourFactor = 1.5f`, its line
        /// 213) and was read by BOTH the unit gate and the objective gate. It moved here so the
        /// rule is one home and a regression can execute it without a scene — the objective gate
        /// still reads THIS constant, deliberately unchanged (see <see cref="RouteToUnitOpen"/>).
        /// Calibrated on the 2026-09-06 raider_camp_small capture, whose worst real route measured
        /// 8.1/7.1 = 1.14x.
        /// </remarks>
        public const float RouteDetourFactor = 1.5f;

        /// <summary>
        /// WO-1764 — ABSOLUTE extra walking (metres) a complete route may cost over the straight
        /// line and still count as OPEN, whatever the ratio says.
        /// </summary>
        /// <remarks>
        /// ⭐ THE RATIO'S SHAPE IS INVERTED RELATIVE TO THE FAILURE IT GUARDS, AND THAT — NOT THE
        /// NUMBER — IS THE DEFECT. Steering is <c>_agent.Move(displacement)</c>, a straight-line
        /// push with no SetDestination (TroopController's own locomotion comment), so the thing that
        /// makes a troop grind against masonry instead of arriving is the ABSOLUTE excess the
        /// straight line does not account for. The ratio scales that allowance with the distance to
        /// the foe, which is backwards:
        ///   * captured 2026-09-14 on Iron Bastion, straight = 5.7 m -> allowance 2.85 m of excess,
        ///     less than one go-around of a single 3.0 m wall module;
        ///   * same session, straight = 28.3 m -> allowance 14.2 m of excess, enough to round a
        ///     whole tower band.
        /// So the gate is tightest exactly where a detour is walkable and loosest exactly where it
        /// is not. The fix is an ABSOLUTE budget; the ratio is kept as an OR so the rule is
        /// WIDENING-ONLY and no route that is open today can start failing.
        ///
        /// ⭐ THE ARITHMETIC, ON IRON BASTION'S OWN GEOMETRY (all sources read 2026-09-16):
        ///   Authored: baseRadius 54, wallSegmentsPerSide 13, entranceCount 1
        ///   (Resources/Data/Canonical/scene-configs.json, id `iron_bastion`). Generated:
        ///   outer ring half-extent 54 m -> side 108 m with ONE south gate
        ///   (RaidBaseGenerator.cs:720 `{true,false,twoGates,false}`); keep ring at
        ///   54 x 0.45 = 24.3 m half-extent -> side 48.6 m with ONE north gate (`:740`, `:741`);
        ///   no wall panel wider than 3.0 m (scene-configs.json:12, the WO-1723 partition rule).
        ///
        ///   MUST COUNT AS OPEN — same courtyard, only a CONVEX obstacle between (a 3.0 m module,
        ///   a corner post, a tower base). A NavMesh route round a convex obstacle of width d costs
        ///   at most (pi/2 - 1)*d ~= 0.57*d of excess; two 3.0 m pieces in series plus agent-radius
        ///   inflation on each side is ~= 2*1.7 + ~2 = 5.4 m.
        ///   MUST STAY REFUSED — the straight line crosses a RING. The keep's south face has to
        ///   route to the north gate and back: excess ~= 2*(24.3 + 48.6 + 24.3) ~= 194 m. The outer
        ///   ring's north face to the south gate: excess ~= 2*108 = 216 m.
        ///   8 m sits more than an order of magnitude below the smallest ring crossing and above the
        ///   largest convex go-around — the margin the ratio never had at close range.
        ///
        /// ⛔ DERIVED, NOT MEASURED, AND SAID SO (CLAUDE.md sec.11B). The convex term uses the
        /// AUTHORED 3.0 m module cap; this lane did NOT measure a tower base footprint or the baked
        /// agent radius on RaidBase_IronBastion, and 8 m is rounded up to cover them. The
        /// falsifiable readout is the `routeUnit=[len= straight= excess= ...]` token added to the
        /// [Flow:RaidAI] line in the same change: the next Bastion logcat prints the real
        /// len/straight pairs this constant is guessing at, and it can be re-derived from measured
        /// numbers instead of authored ones.
        ///
        /// ⚠ WHAT THIS DOES NOT CLAIM. The 09-14 proving lines carry `route=PathComplete-detour`
        /// but NEITHER length, so it is unprovable from here whether the 5.7 m / 14.4 m footman
        /// line flips: its excess is only known to be > 7.2 m. That gap is exactly why the lengths
        /// are now printed. And Q2 stays OPEN: a slack-opened route is still steered straight-line,
        /// so read `moved=` against `commanded=` on the adjacent [Flow:TroopAI] line to see whether
        /// those troops actually arrive.
        /// </remarks>
        public const float RouteDetourSlackMeters = 8f;

        /// <summary>
        /// WO-1764 — is a COMPLETE NavMesh route to a unit "open" for selection purposes?
        /// True when the extra walking is within <see cref="RouteDetourSlackMeters"/> OR the route
        /// is within <see cref="RouteDetourFactor"/> of the straight line.
        /// </summary>
        /// <remarks>
        /// Pure so the regression executes the arithmetic with no scene. Callers must only ask this
        /// for a <c>PathComplete</c> route — an incomplete path is not "open" at any length.
        /// <para/>
        /// ⛔ THE OBJECTIVE ROUTE DELIBERATELY DOES NOT CALL THIS. TroopController's
        /// RefreshRouteToObjective keeps the bare <see cref="RouteDetourFactor"/> test, because
        /// <c>routeToObjectiveOpen</c> is <see cref="ResolvePhase"/>'s input: widening it moves the
        /// whole warband from Breach into Push/Finish, which is a DIFFERENT bucket ordering
        /// (WO-1764 D4) and is not what the owner's report is about. One constant, two rules, the
        /// difference stated rather than left to be discovered.
        /// </remarks>
        public static bool RouteToUnitOpen(float routeLen, float straightLine)
        {
            if (routeLen <= 0f || straightLine <= 0.01f) return false;
            if (routeLen - straightLine <= RouteDetourSlackMeters) return true;
            return routeLen <= straightLine * RouteDetourFactor;
        }

        /// <summary>
        /// WO-1764 D3 — does the nearest NON-WALL structure in this troop's own sweep survive as the
        /// bucket-2 candidate, instead of being displaced by the warband's shared wall focus?
        /// </summary>
        /// <remarks>
        /// ⭐ THE BUG THIS REPLACES WAS AN UNCONDITIONAL OVERWRITE. TroopController read the
        /// scene-wide shared wall focus (FindObjectsByType&lt;WallSegment&gt;, walls=210 on the
        /// 09-14 Iron Bastion device capture) and assigned it over <c>nearestOtherStruct</c> with no
        /// test at all — not on stance, not on distance, not on whether this troop's own sweep even
        /// held a wall. Two consequences, both of them the owner's felt report:
        ///   * a DefenseTower standing right next to the troop could never be bucket 2; and
        ///   * with the stance off, that tower was replaced BY a wall, so
        ///     <c>otherStructIsWall</c> went true, <c>mayWall</c> went false and
        ///     <see cref="PickBucket"/> returned -1 — the troop stood still with a shootable tower
        ///     in range.
        /// It also made <see cref="PickBucket"/>'s own "a TOWER behind it becomes bucket 2" remark
        /// false upstream: the tower had already been discarded before the bucket was picked.
        ///
        /// ⛔ AND THIS IS *NOT* "PREFER ANY NON-WALL STRUCTURE", WHICH WOULD REINTRODUCE WO-1438.
        /// PickBucket's remarks record why the wall is refused in place rather than filtered out:
        /// structures carry NO reachability filter, so promoting a tower that stands BEHIND intact
        /// masonry steers the troop into a navmesh edge and freezes it. The test is therefore
        /// GEOMETRIC: the non-wall structure survives only when it is NEARER than any wall this
        /// troop can see. A tower nearer than every wall in the sweep is not behind one. A tower
        /// further away than a wall keeps losing to the wall focus exactly as it does today, which
        /// is also what keeps WO-1746's auto-chain intact for the troops actually on the panel.
        /// <para/>
        /// ⭐ THE SECOND CLAUSE, AND IT IS THE ONE THE 09-16 CAPTURE FORCED. The geometric test
        /// ALONE does not answer the owner's felt case, and the device log says so in one line
        /// (logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt:3053030,
        /// 09-16 13:25:34.656, build 2026.09.16.371701):
        /// <code>
        /// [Flow:TroopAI] id=troop-footman role=melee IDLE/RALLY: no acquirable hostile inside
        ///  radius=14.0m (last sweep colliders=12, accepted[unit=0,struct=11], rejected=1;
        ///  nearestHostileAnyKind='Watchtower_Mage_1(DefenseTower)' @7.4m) rally=... action=stand-still
        /// </code>
        /// On the tick before it (`:3053025`) the nearest hostile is `Wall_Keep1_SS_3` at **7.1 m**.
        /// So the refused wall is 0.3 m NEARER than a DefenseTower the owner's own WO-1752 ruling
        /// keeps legal for a stance-less troop, and the troop stands still. A 0.3 m difference is not
        /// "the tower is behind the wall" — and with the wall refused outright the troop's only
        /// alternative to the tower is <c>bucket=-1</c>, a guaranteed zero. Promoting the tower is
        /// therefore no worse than standing even in the mis-steer case, and may actually land damage.
        /// So: when the wall candidate may not be targeted AT ALL, the nearest non-wall structure
        /// survives regardless of relative distance.
        /// <para/>
        /// ⚠ AND THE RESIDUAL WO-1438 RISK IS NAMED, NOT WISHED AWAY. That promoted tower may stand
        /// behind intact masonry, and steering is straight-line, so the troop can grind on the wall
        /// instead of arriving. It is falsifiable on the next capture, from one pair of tokens on the
        /// adjacent [Flow:TroopAI] line: <c>moved=</c> against <c>commanded=</c>. A route probe is
        /// NOT the answer here and that is measured, not assumed — WO-1569 recorded 133 of 133
        /// structure probes returning <c>CalculatePath-FAILED</c>, because a structure's
        /// WorldPosition sits inside solid geometry.
        /// <para/>
        /// Stance-independent for the geometric half on purpose: the displacement is wrong with the
        /// stance ON too (a nearer tower could never be bucket 2). Under an armed stance / siege the
        /// wall IS targetable, so only the geometric half applies and WO-1746's auto-chain is intact
        /// for every troop whose nearest masonry is the panel. That is a reading taken from the
        /// WO-1764 brief, NOT an owner ruling — flagged here rather than buried, exactly as WO-1746
        /// flagged its own.
        /// </remarks>
        public static bool NonWallStructSurvives(
            bool hasNonWallStruct, float nonWallSqr,
            bool hasWallInSweep, float wallSqr,
            bool wallMayBeTargeted)
        {
            if (!hasNonWallStruct) return false;
            if (!hasWallInSweep) return true;
            if (!wallMayBeTargeted) return true;
            return nonWallSqr < wallSqr;
        }

        /// <summary>
        /// WO-1752 ruling 3 — should this troop adopt the hero's live attacker as its target,
        /// even though that attacker is outside its own acquire sweep?
        /// </summary>
        /// <remarks>
        /// Owner, verbatim: *"they are running around attacking walls while i am getting damaged
        /// and killed"*. The lead's reading, recorded in WO-1752 for the owner to veto: a hostile
        /// that is damaging the HERO is an acquirable unit for EVERY troop regardless of that
        /// troop's own radius — the warband defends the player before anything else. It is
        /// WO-1719's *"all together unless they have aggro"* with the hero's aggro counting as the
        /// warband's.
        ///
        /// ⭐ THE CONDITION IS DELIBERATELY "NO UNIT OF MY OWN", NOT "ALWAYS". A troop already
        /// brawling with something inside its own sweep is doing the same job; yanking it across
        /// the map would thin the line the owner is standing in. WO-1752 states the rule in those
        /// words: *"A troop with no unit in its own radius but a live hero-attacker targets that
        /// attacker"*.
        ///
        /// ⛔ AND IT CANNOT BREAK AN ARMED BREACH STANCE — WO-1746 sec.4 is NOT re-litigated here.
        /// The adopted attacker is by construction OUTSIDE the troop's acquire radius (or it would
        /// already be its <c>nearestUnitAny</c> and this returns false), and every acquire radius
        /// OBSERVED in the raid trace (12 m tank, 16 m cleric) is wider than
        /// <see cref="PeelUnitLeashMeters"/> (6 m) — though the radius is DATA
        /// (<c>TroopDef.HuntScanRadius</c> / <c>stats.AggroRadius</c>, TroopController.cs:372/:432)
        /// and a future def could author a smaller one, so that arithmetic is a comfort, NOT the
        /// guard. The guard is that <c>peelThreat</c> is computed from the troop's OWN sweep,
        /// BEFORE adoption, and adoption never edits it. Under an armed stance
        /// <see cref="PickBucket"/> then keeps the warband on the panel exactly as Case 8 pins,
        /// because the stance still beats a merely-acquirable unit. AGGRO remains the one thing
        /// that breaks the stance.
        /// </remarks>
        public static bool AdoptHeroAttacker(bool hasUnitInOwnSweep, bool heroAttackerLive)
        {
            if (hasUnitInOwnSweep) return false;
            return heroAttackerLive;
        }

        /// <summary>
        /// WO-1730 §3B — extra planar slack on top of (objective footprint radius + agent radius)
        /// before a route is called ARRIVED.
        /// </summary>
        /// <remarks>
        /// ⭐ THIS IS WO-1749's <c>ArrivalSlack</c>, DELIBERATELY THE SAME 0.5 m, BECAUSE THE TWO
        /// ANSWERS MUST NOT BE ABLE TO DISAGREE. `RaidKeepReachRegression.ArrivalSlack`
        /// (`Assets/Editor/Regression/RaidKeepReachRegression.cs:136`) is the EDITOR probe that
        /// decides whether a baked scene's objective is reachable and withholds
        /// `RAID_NAV_BAKE_OK` when it is not. If the runtime used a different number, a bake could
        /// certify a scene the troops then refuse to path through — a green marker over a broken
        /// game, which is the failure CLAUDE.md §8/§16 are written against.
        ///
        /// Its own reasoning, which applies here verbatim: enough to absorb the last corner landing
        /// on a polygon edge rather than dead against the art, far too little to hide a 13 m island.
        /// The two REAL terms are measured, never written down — see <see cref="ArrivalRadius"/>.
        /// </remarks>
        public const float ArrivalSlackMeters = 0.5f;

        /// <summary>
        /// WO-1730 §3B — how close a route's LAST CORNER must get to the objective's CENTRE before
        /// the route counts as open, given the objective's footprint radius and the agent's radius.
        /// </summary>
        /// <remarks>
        /// ⛔ THE CENTRE IS NOT A REACHABLE POINT AND ASKING FOR IT WAS THE BUG. A spire wide enough
        /// to carve its own footprint out of the navmesh has NO navmesh polygon at its centre, so
        /// <c>NavMesh.CalculatePath</c> to <c>spire.WorldPosition</c> can never return
        /// <c>PathComplete</c> — it returns <c>PathPartial</c> with the last corner sitting on the
        /// carve edge. WO-1749 diagnosed exactly this for the BAKE probe, in its own words:
        /// *"PathComplete to an objective's CENTRE is unsatisfiable for anything wide enough to
        /// carve its own footprint"*, and replaced the criterion with ARRIVED. The RUNTIME check
        /// kept the old one, which is why `routeOpen=True` never occurred.
        ///
        /// ⭐ PROVEN, NOT INFERRED — this is the captured line that earned the edit (CLAUDE.md §12):
        /// `Builds/wo1730-assault-trace.log` (2026-09-15 21:46, `RaidAssaultTraceCapture` on
        /// `RaidBase_raider_camp_small`), every sampled troop reading
        /// `routeGap=[last=4.7 straight=48.1..55.1 corners=4]`. The troops walked the full ~50 m and
        /// stopped **4.7 m** from the spire's centre — against the **4.30 m** carve radius WO-1749
        /// measured. They had ARRIVED; only the criterion refused them. A path dying at the wall
        /// ring would have read `last=` in the tens of metres, and that was the alternative this
        /// measurement existed to rule out.
        ///
        /// ⚠ BOTH REAL TERMS ARE MEASURED BY THE CALLER, AND THAT IS THE POINT. WO-1749's probe
        /// measures the footprint off the spire's own renderer bounds and the agent radius off the
        /// live NavMesh settings — because a hardcoded radius is the duplicated state that made the
        /// spire's own seat wrong (it re-seats with a MEASURED lift, "never a hardcoded 1.5").
        /// This method stays pure so a regression can assert it without a scene.
        /// </remarks>
        public static float ArrivalRadius(float objectiveFootprintRadius, float agentRadius)
        {
            float footprint = Mathf.Max(0f, objectiveFootprintRadius);
            float agent = agentRadius > 0f ? agentRadius : 0.5f;
            return Mathf.Max(ArrivalSlackMeters, footprint + agent + ArrivalSlackMeters);
        }

        /// <summary>
        /// WO-1730 §3B — did this route ARRIVE at the objective? True when the path's last corner
        /// is within <paramref name="arrivalRadius"/> of the objective centre.
        /// </summary>
        /// <remarks>
        /// A negative <paramref name="lastCornerDistance"/> means "no corners" (an invalid path or a
        /// failed query) and is never an arrival — the caller passes -1 for that case rather than 0,
        /// which would read as "standing exactly on the spire".
        /// </remarks>
        public static bool RouteArrived(float lastCornerDistance, float arrivalRadius)
        {
            if (lastCornerDistance < 0f) return false;
            return lastCornerDistance <= arrivalRadius;
        }

        /// <summary>Map authored <c>TroopDef.Role</c> → assault job (no new JSON field).</summary>
        public static RaidAssaultJob JobFromRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return RaidAssaultJob.Front;
            switch (role.Trim().ToLowerInvariant())
            {
                case "ranged":
                case "caster":
                case "mage":
                    return RaidAssaultJob.Ranged;
                case "siege":
                    return RaidAssaultJob.Breaker;
                case "support":
                case "healer":
                    return RaidAssaultJob.Support;
                case "tank":
                case "melee":
                default:
                    return RaidAssaultJob.Front;
            }
        }

        /// <summary>
        /// Signed meters along the march axis (toward objective). Positive = ahead of deploy.
        /// </summary>
        public static float ForwardOffsetMeters(RaidAssaultJob job)
        {
            switch (job)
            {
                case RaidAssaultJob.Ranged: return -RangedBackMeters;
                case RaidAssaultJob.Support: return -SupportBackMeters;
                case RaidAssaultJob.Breaker: return BreakerForwardMeters;
                case RaidAssaultJob.Front:
                default: return FrontForwardMeters;
            }
        }

        /// <summary>
        /// World-space formation offset from a tap/deploy origin.
        /// <paramref name="marchForward"/> should be flat (y=0) and normalized toward the goal.
        /// </summary>
        public static Vector3 FormationWorldOffset(
            RaidAssaultJob job, int stackIndex, Vector3 marchForward, float lateralSpread = LateralSpreadMeters)
        {
            Vector3 forward = marchForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            float along = ForwardOffsetMeters(job);
            // Alternate left/right: 0 → 0, 1 → +1, 2 → -1, 3 → +2, …
            float lane = 0f;
            if (stackIndex > 0)
            {
                int n = (stackIndex + 1) / 2;
                lane = ((stackIndex % 2) == 1 ? 1f : -1f) * n;
            }

            return forward * along + right * (lane * lateralSpread);
        }

        /// <summary>
        /// Bias a push/breach destination so back-line jobs hold standoff behind the objective
        /// approach (Front closes; Ranged/Support stop short).
        /// </summary>
        public static Vector3 BiasMoveDestination(
            RaidAssaultJob job, Vector3 selfPos, Vector3 objectiveOrFoePos, float attackRange)
        {
            if (job == RaidAssaultJob.Front || job == RaidAssaultJob.Breaker)
                return objectiveOrFoePos;

            Vector3 flatSelf = new Vector3(selfPos.x, 0f, selfPos.z);
            Vector3 flatGoal = new Vector3(objectiveOrFoePos.x, 0f, objectiveOrFoePos.z);
            Vector3 toGoal = flatGoal - flatSelf;
            float dist = toGoal.magnitude;
            if (dist < 0.01f) return objectiveOrFoePos;

            float standoff = job == RaidAssaultJob.Support
                ? Mathf.Max(attackRange + SupportBackMeters - RangedBackMeters, attackRange)
                : Mathf.Max(attackRange * 0.85f, attackRange - 0.5f);

            if (dist <= standoff) return selfPos; // already far enough — hold

            float stopAt = dist - standoff;
            Vector3 hold = flatSelf + (toGoal / dist) * stopAt;
            hold.y = objectiveOrFoePos.y;
            return hold;
        }

        /// <summary>
        /// Priority stack: Peel → Breach → Finish → Push.
        /// </summary>
        public static RaidAssaultPhase ResolvePhase(
            bool peelThreat,
            bool routeToObjectiveOpen,
            bool objectiveInAttackRange)
        {
            if (peelThreat) return RaidAssaultPhase.Peel;
            if (!routeToObjectiveOpen) return RaidAssaultPhase.Breach;
            if (objectiveInAttackRange) return RaidAssaultPhase.Finish;
            return RaidAssaultPhase.Push;
        }

        /// <summary>
        /// Idle (no foe) destination rule — CLI review 2026-09-07.
        /// Rally flag beats spire push. Spire push only on Push/Finish (never Breach —
        /// marching into an intact wall with NoObstacleAvoidance freezes the troop).
        /// </summary>
        public static bool IdleShouldPushSpire(bool rallyPointSet, RaidAssaultPhase phase)
        {
            if (rallyPointSet) return false;
            return phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish;
        }

        /// <summary>
        /// Owner 2026-09-12: troops must WALK TO THE RALLY, not chew the nearest exterior
        /// wall on the way. While a rally is set and the troop has not arrived, wall-ring
        /// picks are suppressed (Peel still wins if they are under attack).
        /// </summary>
        public static bool RallyHoldsMarch(bool rallySet, bool arrivedAtRally, bool peelThreat)
        {
            return rallySet && !arrivedAtRally && !peelThreat;
        }

        /// <summary>
        /// WO-1719: an EXPLICIT player breach order is not held by the rally march.
        /// </summary>
        /// <remarks>
        /// ⚠ WITHOUT THIS THE TICKET'S OWN ACCEPTANCE BULLET CANNOT PASS, and the reason is
        /// that phase and rally are INDEPENDENT axes. <see cref="ResolvePhase"/> reads
        /// peel / route / objective-range - never the rally - so a troop walking to a flag
        /// IS <see cref="RaidAssaultPhase.Breach"/>. The 3-arg rule above then nulls its
        /// other-structure bucket (TroopController), so "every Breach-phase troop retargets
        /// to the tapped panel" would silently fail for the whole warband any time a rally
        /// was set - which, mid-raid, is most of the time. The implicit ring-farm this
        /// suppression exists to stop (owner 2026-09-12) is untouched: only a panel the
        /// player explicitly tapped releases the march, and WO-1717 sec.6A asks for exactly
        /// that ("RallyHoldsMarch does not suppress an EXPLICIT breach order").
        /// </remarks>
        public static bool RallyHoldsMarch(
            bool rallySet, bool arrivedAtRally, bool peelThreat, bool hasExplicitBreachOrder)
        {
            if (hasExplicitBreachOrder) return false;
            return RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat);
        }

        /// <summary>
        /// Owner 2026-09-12: the warband focuses ONE breach — the most damaged living
        /// wall. Ties (all full HP) stack on the panel nearest the muster/rally.
        /// </summary>
        public static IDamageable SelectFocusBreach(
            System.Collections.Generic.IList<IDamageable> walls, Vector3 muster)
        {
            return SelectFocusBreach(walls, muster, null);
        }

        /// <summary>
        /// WO-1719 — the same rule, with the player's EXPLICIT pick winning outright.
        /// </summary>
        /// <remarks>
        /// Owner ruling 2026-09-14: a wall tapped in Breach mode overrides the automatic
        /// most-damaged / nearest-muster computation, and that computation stays the
        /// FALLBACK for when no pick stands (or once the picked panel collapses).
        ///
        /// ⭐ THE OVERRIDE IS AN EARLY RETURN, DELIBERATELY - the selection body below is
        /// NOT restructured. WO-1717 sec.3d proved the auto pick is what overrides the
        /// local scan unconditionally, so the cheapest correct seam was to put one gate in
        /// FRONT of the existing rule rather than teach the loop about priorities. The
        /// fallback is then literally the same code it always was, which is why a
        /// regression can pin "auto still picks most-damaged" against an unchanged body.
        ///
        /// ⚠ The explicit pick is NOT required to appear in <paramref name="walls"/>. The
        /// player tapped that collider; a candidate list built from a cached scene scan
        /// (TroopController.SharedBreachFocus, 0.4 s) can trail the tap by a frame, and
        /// dropping the order for that would be an invisible, intermittent refusal. Its
        /// liveness is checked here instead.
        /// </remarks>
        public static IDamageable SelectFocusBreach(
            System.Collections.Generic.IList<IDamageable> walls, Vector3 muster,
            IDamageable explicitFocus)
        {
            if (explicitFocus != null && explicitFocus.IsAlive) return explicitFocus;
            if (walls == null || walls.Count == 0) return null;
            IDamageable best = null;
            float bestHp = float.MaxValue;
            float bestMusterSqr = float.MaxValue;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || !w.IsAlive) continue;
                float hp = w.Hp;
                Vector3 d = w.WorldPosition - muster;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (best == null
                    || hp < bestHp - 0.5f
                    || (Mathf.Abs(hp - bestHp) <= 0.5f && sqr < bestMusterSqr))
                {
                    best = w;
                    bestHp = hp;
                    bestMusterSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether a hostile UNIT should beat structures for this phase.
        /// Peel always takes the unit when one exists. Push/Finish only peel units already
        /// in attack range (stay alive locally) — otherwise the objective wins over walls.
        /// Breach keeps the WO-1438 reachability gate for non-siege.
        /// </summary>
        public static bool PreferUnit(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen)
        {
            return PreferUnit(
                phase, preferStructures, hasUnit, hasStruct,
                unitInAttackRange, routeToUnitOpen, breachStance: false);
        }

        /// <summary>
        /// WO-1746 — the same rule, with the player's BREACH STANCE held as an explicit input.
        /// </summary>
        /// <remarks>
        /// Owner ruling WO-1738 (2026-09-15): *"Breach is a persistent stance that auto-chains.
        /// One tap = 'we are breaching'; when the ordered wall falls the warband keeps opening
        /// walls ... until the player toggles Breach off or a hostile pulls aggro."*
        ///
        /// ⭐ THE STANCE IS TREATED EXACTLY LIKE <paramref name="preferStructures"/> IN BREACH,
        /// AND THAT IS THE WHOLE CHANGE. "Until a hostile pulls AGGRO" is already a phase, not a
        /// bucket rule: aggro means <c>peelThreat</c> means <see cref="RaidAssaultPhase.Peel"/>,
        /// and Peel returns true at the top of this method before the stance is ever consulted.
        /// So an aggro'd troop keeps its fight (WO-1719's "all together unless they have aggro"
        /// survives verbatim) while a merely REACHABLE, non-aggro'd defender no longer peels the
        /// warband off the panel the player ordered.
        ///
        /// ✅ THE ONE PLACE A READING WAS CHOSEN IS NOW **RULED** — WO-1764, owner 2026-09-16,
        /// verbatim: *"Units first even inside Breach"*. Any reachable defender beats the wall, even
        /// under a Breach order; walls only when no defender is reachable. The flag below therefore
        /// resolves the OTHER way from WO-1746, and it does so through
        /// <see cref="StanceOutranksReachableUnit"/> — read at this gate AND at
        /// <see cref="PickBucket"/>'s in-attack-range shortcut, which is the gate WO-1746 missed.
        /// Kept as a record rather than deleted: WO-1738's status line said "units-first is the
        /// default inside it" while its ruling BODY and WO-1719 said "until ... a hostile pulls
        /// aggro"; WO-1746 implemented the body, the owner watched it on Iron Bastion
        /// (APK 2026.09.16.371701) and ruled for the status line. Body and status line now agree.
        /// Pinned by WallBreachOrderRegression Case 8, rewritten in the same change to pin the NEW
        /// reading in both directions (reachable -> unit; unreachable -> wall).
        /// <para/>
        /// The proving line from the build she played
        /// (logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt:3047672-3047673):
        /// <c>breachStance=True stanceYield=wall ... has[unit=True,obj=False,wall=True]</c> with
        /// <c>SWING target='Wall_Keep1_SS_0' dist=1.3m</c> while
        /// <c>runnerUpOtherKind='RaidGuard (hollow-warrior-Lv7-16)' dist=10.1m</c> and
        /// <c>accepted[unit=1,struct=9]</c> — the stance holding the panel with a live defender in
        /// the sweep, which is the report, printed.
        ///
        /// The old 6-arg signature is KEPT as a delegating overload (stance = false) so every
        /// suite that pins the pre-ruling rule compiles and passes untouched.
        /// </remarks>
        public static bool PreferUnit(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen,
            bool breachStance)
        {
            if (!hasUnit) return false;
            if (phase == RaidAssaultPhase.Peel) return true;

            if (phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish)
            {
                // Local survival only — do not chase distant units instead of the spire.
                return unitInAttackRange;
            }

            // Breach: siege stays on masonry; others use the existing reachability rule.
            if (preferStructures) return false;
            // WO-1746 made an armed Breach stance behave like siege here. WO-1764's owner ruling
            // (2026-09-16, "Units first even inside Breach") reverses that, and the reversal is
            // ONE predicate read at BOTH stance gates - see StanceOutranksReachableUnit for why a
            // named predicate rather than a deletion.
            if (breachStance && StanceOutranksReachableUnit) return false;
            if (!hasStruct) return true;
            return unitInAttackRange || routeToUnitOpen;
        }

        /// <summary>
        /// After a hole exists (Push/Finish), non-siege must not farm the wall ring.
        /// Breach (and siege Breaker in Breach) may still pick approach structures.
        /// The objective (spire) is never "wall ring" — callers pass hasObjective separately.
        /// </summary>
        public static bool AllowNonObjectiveStructure(
            RaidAssaultPhase phase, bool preferStructures)
        {
            if (phase == RaidAssaultPhase.Breach) return true;
            // Siege may keep structure bias in Peel only if no unit (PreferUnit already handled);
            // in Push/Finish even siege joins the push — no ring farming.
            if (preferStructures && phase == RaidAssaultPhase.Peel) return true;
            return false;
        }

        /// <summary>
        /// Pick among unit / objective / other-structure / none after buckets are measured.
        /// Returns which bucket wins: 0=unit, 1=objective, 2=otherStruct, -1=none.
        /// </summary>
        public static int PickBucket(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasObjective,
            bool hasOtherStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen)
        {
            return PickBucket(
                phase, preferStructures, hasUnit, hasObjective, hasOtherStruct,
                unitInAttackRange, routeToUnitOpen, breachStance: false);
        }

        /// <summary>
        /// WO-1746's 8-arg form, kept as a delegating overload (the other-structure candidate is
        /// assumed NOT to be a wall) so every suite written against it compiles unchanged.
        /// </summary>
        public static int PickBucket(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasObjective,
            bool hasOtherStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen,
            bool breachStance)
        {
            return PickBucket(
                phase, preferStructures, hasUnit, hasObjective, hasOtherStruct,
                unitInAttackRange, routeToUnitOpen, breachStance, otherStructIsWall: false);
        }

        /// <summary>
        /// WO-1746 — the same bucket rule, with the Breach stance passed through to
        /// <see cref="PreferUnit"/>.
        /// </summary>
        /// <remarks>
        /// ⚠ TWO GATES IN THE BREACH BRANCH READ THE STANCE, NOT ONE — and the second was MISSED
        /// on the first pass. Teaching <see cref="PreferUnit"/> about the stance is not sufficient:
        /// the Breach tail carries its own <c>hasUnit &amp;&amp; unitInAttackRange</c> shortcut, so
        /// a calm defender already inside attack range still stole the warband off the ordered
        /// panel. Found by EXECUTING the Case 8 red proof against the pure statics, not by reading
        /// them. If a third unit-beats-wall path is ever added to this branch, it needs the same
        /// guard — and Case 8 is what will say so.
        ///
        /// ⭐ Otherwise this keeps the "one gate in front" shape WO-1719 used at
        /// <see cref="SelectFocusBreach"/> (see its remarks). In
        /// particular <see cref="AllowNonObjectiveStructure"/> is UNCHANGED: Breach still returns
        /// true. What changed is one clause on <c>mayWall</c> — see it below.
        ///
        /// ⛔ WO-1752 REVERSED THE PARAGRAPH THAT USED TO STAND HERE, AND IT IS WORTH KNOWING WHY.
        /// It read: *"A blocked, stance-less, unit-less warband therefore still picks the wall —
        /// it just swings at ReluctantWallDamageMultiplier. The ruling changes WILLINGNESS (the
        /// multiplier), not the ability to pick; making the bucket return -1 here would have
        /// produced the dead-end WO-1738 sec.4 names as Branch B's one real cost."* The owner
        /// watched that behaviour on the tester build and ruled the other way: *"we need to set it
        /// so that the troops 100% ignore walls, unless explicitly told breach"*. The bucket DOES
        /// return -1 now for that warband, and the dead-end is her accepted cost. See
        /// <see cref="MayTargetWall"/>.
        /// </remarks>
        public static int PickBucket(
            RaidAssaultPhase phase,
            bool preferStructures,
            bool hasUnit,
            bool hasObjective,
            bool hasOtherStruct,
            bool unitInAttackRange,
            bool routeToUnitOpen,
            bool breachStance,
            bool otherStructIsWall)
        {
            bool preferUnit = PreferUnit(
                phase, preferStructures, hasUnit, hasOtherStruct || hasObjective,
                unitInAttackRange, routeToUnitOpen, breachStance);

            if (preferUnit && hasUnit) return 0;

            // Live gate for wall-ring farm: Push/Finish refuse non-objective masonry unless
            // AllowNonObjectiveStructure says otherwise (Breach / peel-siege only).
            //
            // ⛔ WO-1752 — AND THE WALL CLAUSE, WHICH IS WHY THE BRANCH IS GATED RATHER THAN CUT.
            // Bucket 2 is "other masonry", NOT "wall": towers, gates and dressed props ride the
            // same bucket, and WO-1746 sec.4B.3 records that the owner's wall ruling was never
            // about them (they keep full damage and remain targetable). So the ruling "troops 100%
            // ignore walls unless explicitly told breach" is applied to the CANDIDATE'S TYPE, fed
            // in by the caller (TroopController passes `nearestOtherStruct is WallSegment`), not by
            // deleting the branch — deleting it would also stop a stance-less warband from ever
            // hitting a tower that is shooting it.
            //
            // ⚠ AND NOT BY FILTERING THE WALL OUT OF THE CANDIDATE SET EITHER. If the wall were
            // dropped upstream, the next-nearest masonry — a TOWER behind it — becomes bucket 2,
            // and structures carry NO reachability filter (only units do, see PreferUnit's
            // remarks), so the troop would steer at a tower through an intact wall and freeze on a
            // navmesh edge: WO-1438's finding, reintroduced by a "cleanup". The wall stays the
            // candidate, stays visible in the trace as has[wall=True], and is REFUSED here.
            bool mayWall = hasOtherStruct
                && AllowNonObjectiveStructure(phase, preferStructures)
                && (!otherStructIsWall || MayTargetWall(preferStructures, breachStance));

            if (phase == RaidAssaultPhase.Push || phase == RaidAssaultPhase.Finish)
            {
                if (hasObjective) return 1;
                if (hasUnit) return 0; // nothing else — engage any unit rather than idle
                if (mayWall) return 2;
                return -1; // do not pick ring walls
            }

            if (phase == RaidAssaultPhase.Peel)
            {
                if (hasUnit) return 0;
                if (hasObjective) return 1;
                // Hurt by a tower with no unit in leash — do NOT resume wall-ring farming.
                return -1;
            }

            // Breach
            if (preferStructures)
            {
                if (mayWall) return 2;
                if (hasObjective) return 1;
                return hasUnit ? 0 : -1;
            }

            // ⛔ WO-1746 — THE SECOND GATE, AND IT WAS MISSED ON THE FIRST PASS.
            // PreferUnit is NOT the only place a unit can beat the wall in Breach: this line is a
            // separate in-attack-range shortcut, so teaching PreferUnit about the stance and
            // stopping there left the stance silently broken for exactly the case Case 8 asserts
            // (a calm defender already within attack range still stole the warband off the ordered
            // panel). It was found by EXECUTING the Case 8 red proof, not by reading - the suite
            // would have gone red at the gate. Siege never reaches this line (the preferStructures
            // branch above returns first), so guarding it with the stance is what makes a
            // stance-armed troop behave like siege here, which is precisely the ruling.
            //
            // ⭐ WO-1764 (owner ruling 2026-09-16, "Units first even inside Breach") REVERSES the
            // `!breachStance` half of this guard, and does it through the SAME predicate as
            // PreferUnit's so the two gates can never again disagree - which is the failure WO-1746
            // shipped. A defender already inside attack range now beats the panel with the stance
            // armed as well as without it; the wall is what the troop takes when no defender is
            // reachable. Siege never reaches this line (the preferStructures branch above returns
            // first), so the catapult is untouched.
            if ((!breachStance || !StanceOutranksReachableUnit) && hasUnit && unitInAttackRange) return 0;
            if (mayWall) return 2;
            if (hasObjective) return 1;
            if (!hasUnit) return -1;

            // ⛔ WO-1752 — AND THIS LINE WAS FOUND BY EXECUTING THE RED PROOF, NOT BY READING.
            // The tail below is the old "nothing else — engage any unit rather than idle"
            // fallback, and it was harmless while the wall bucket absorbed the blocked warband.
            // The moment walls were refused it became the DOMINANT path for a stance-less
            // non-siege troop, and it hands back bucket 0 for a foe that is UNREACHABLE — the
            // exact steer WO-1438 proved freezes a troop on a navmesh edge, because the thing
            // making it unreachable is the wall we just refused. Trading "chews a wall at 10%"
            // for "walks into a navmesh edge and stands there twitching" is not the ruling.
            //
            // The owner's own words are the specification here: a blocked warband with Breach
            // OFF and no siege "now stands (or defends the hero)" — WO-1752, the dead-end
            // accepted deliberately. So it STANDS. Scoped to `wallRefused`: with no wall in the
            // picture the tail keeps its pre-WO-1752 answer exactly, which is why
            // RaidAssaultAiRegression's 7-arg cases do not move.
            bool wallRefused = hasOtherStruct && otherStructIsWall
                && !MayTargetWall(preferStructures, breachStance);
            if (wallRefused && !unitInAttackRange && !routeToUnitOpen) return -1;

            return 0;
        }
    }
}

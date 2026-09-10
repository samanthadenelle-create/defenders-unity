// =============================================================================
// RaidScoring — the V1 raid SCORER + live clock (WO-771.6, LOCKED teleport/deploy
// loop). It is the missing "win/stars/loot" half the raid spine flagged OUT
// (RaidDeployController.cs:27, RaidVictoryController.cs:34).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT DOES (V1 = PvE, reuse the real-time combat — NO deterministic sim; that
// is V2 / WO-771.3):
//   * Owns the 180s raid CLOCK (elapsed counts up from raid start).
//   * Reads the live garrison via RaidGarrisonSpawner (TotalGarrison / AliveCount /
//     Cleared) — the SAME clear signals the victory path already fires on — to
//     derive %-destruction with NO new combat code.
//   * Tracks troops deployed (RaidDeployController pushes each drop) + a simple
//     RaidDeployLog for re-watch (order/time/place — not byte-exact).
//   * Computes a 0-3 STAR result from: garrison cleared / boss down / within the
//     clock (design B5) via the PURE static ComputeStars — unit-testable, no scene.
//   * Computes resource LOOT scaled by stars + destruction via the PURE static
//     ComputeLoot (the victory path grants it through the village EconomyService).
//   * Fires OnTimeExpired once when the clock runs out (the HUD / retreat path ends
//     the raid) so a raid can never run forever.
//
// SELF-INSTALL: mirrors RaidDeployController / RaidVictoryController — one instance
// per RaidBase_* scene (idempotent). ASCII-only. Canon: Elarion (never Avalon).
// =============================================================================

using System;
using System.Collections;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.World.Camps;   // RaidGarrisonSpawner

namespace DeNelle.Village
{
    /// <summary>
    /// The V1 raid scorer: owns the raid clock, reads the live garrison for
    /// %-destruction, tracks deployed troops + a re-watch log, and settles a
    /// <see cref="RaidResult"/> (0-3 stars) plus the loot payout. Self-installs into
    /// any <c>RaidBase_*</c> scene; read via <see cref="Instance"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidScoring : MonoBehaviour
    {
        /// <summary>The live scorer for the current raid scene (null outside a raid).</summary>
        public static RaidScoring Instance { get; private set; }

        /// <summary>
        /// WO-1227 — THE ONE "am I inside a raid" ANSWER. Owner ruling 2026-08-26, verbatim:
        /// <i>"raids only pay at end of raid"</i>.
        /// <para>A raid pays ONCE, through <see cref="RaidVictoryController"/>'s
        /// <c>ComputeLoot</c> grant. So the WO-1216 per-kill MATERIAL faucet must not run while a
        /// raid is live, or the player banks wood/iron/stone twice — once per defender their
        /// troops kill, and again at the summary.</para>
        /// <para>This is deliberately the SCORER's own lifetime and not a new flag: the scorer
        /// self-installs into (and only into) a <c>RaidBase_*</c> scene and nulls itself in
        /// <c>OnDestroy</c>, so its existence already IS "a raid is running" — every other raid
        /// system (HUD clock, victory payout, army reconcile) already reads it as exactly that.
        /// Inventing a second flag is how two answers to one question drift apart.</para>
        /// <para>It stays TRUE after <see cref="Finalized"/> on purpose: the victory screen is up
        /// and the player is still standing in the enemy base, and a mop-up kill during the
        /// summary is still a raid kill.</para>
        /// <para>The scene test is a FALLBACK, not the definition — it covers a raid scene where
        /// the scorer failed to install (the scorer warns loudly when that happens). It can only
        /// ever fire in a <c>RaidBase_*</c> scene, so open-world / wave / dungeon kills are
        /// untouched by it: <c>HubScenes.IsRaid</c> is the same single naming authority the HUD
        /// and the deploy controller read.</para>
        /// </summary>
        public static bool RaidInProgress
        {
            get
            {
                if (Instance != null) return true;
                return DeNelle.Core.HubScenes.IsRaid(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
        }

        /// <summary>
        /// WO-1437 — THE ONE ANSWER TO "does hero death end a raid?". Read by
        /// <see cref="HeroHealth"/> (which branch the death coroutine takes) and by
        /// <c>HeroDeathEndState</c> (which sentence the fallen screen prints), so the
        /// behaviour and the copy can never disagree.
        ///
        /// <para><b>THE OWNER DECISION WO-1437 LEFT PENDING IS NOW RULED (WO-1526, owner
        /// 2026-09-06), AND IT RULED <c>false</c>.</b> Verbatim: <i>"Do not let hero death
        /// instantly terminate the raid... let the raid continue, but cap the result at 2
        /// stars if the hero dies. That makes hero survival matter without turning the hero
        /// into a giant red self-destruct button."</i></para>
        ///
        /// <list type="bullet">
        /// <item><c>true</c> (RETIRED 2026-09-06): hero death was the raid's THIRD EXIT — it
        /// settled partial loot, reconciled the army and routed home, identical to Retreat.
        /// Measured cost of that shape, <c>logs/debug/raid-no-abilities-2026-09-06.log</c>
        /// 12:59:47: <c>"hero death settle: partial loot for 32% razed"</c> fired 45 s into a
        /// 180 s raid with a full army still standing on the field.</item>
        /// <item><c>false</c> (current): the hero goes down and STAYS down — the raid keeps
        /// running to clear, Retreat or the clock, the army fights on, and the settled result
        /// is clamped to <see cref="HeroDeathStarCap"/> stars by
        /// <see cref="ApplyHeroDeathCap"/>. The clamp IS the cost of dying.</item>
        /// </list>
        ///
        /// <para>⛔ <c>false</c> DOES NOT MEAN "respawns in place". WO-1437 wrote that gloss into
        /// this doc while the branch was unruled, and WO-1526 sec.3 rules the opposite: <i>"Do
        /// not let the hero respawn mid-raid. The ruling is that survival still matters — the
        /// 2-star clamp IS the cost, and a respawn removes it."</i> <c>HeroHealth.HandleDeath</c>
        /// therefore neither respawns nor routes home on this branch; it hands off to
        /// <c>RaidDeployController.NotifyHeroDown</c> and stops.</para>
        ///
        /// <para>⚠ FLIPPING THIS CONSTANT ALONE IS <b>NOT</b> THE WHOLE CHANGE — the old doc
        /// claimed it was, and that claim was false. <c>HeroHealth</c>'s evac test is
        /// <c>if (enemyOwnedScene || raidDeathExit)</c>, and a live raid base IS enemy-owned
        /// (the claim only flips it at the win), so the evac fired regardless of this value.
        /// The live-raid branch must be evaluated BEFORE that OR, and it now is.</para>
        ///
        /// <para>⚠ A SETTLED raid ignores this constant and ALWAYS routes home: once
        /// <see cref="Finalized"/> has latched, the loot is paid, the camp is claimed and the
        /// clock has stopped, so there is no session left to fight on inside. That is the
        /// WO-1437 softlock and it is not a matter of taste.</para>
        /// </summary>
        public const bool RaidDeathEndsRaid = false;

        /// <summary>
        /// WO-1526 — the star ceiling a raid can settle at once the hero has fallen. The owner's
        /// ruling ("cap the result at 2 stars if the hero dies") lives here as a number so no
        /// call site re-hardcodes a 2.
        /// </summary>
        public const int HeroDeathStarCap = 2;

        /// <summary>
        /// The LIVE raid clock default (seconds). Selection/deploy UI must display this
        /// (or the authored config time only when it matches) — never a longer "target"
        /// that scoring does not use (honesty pass 2026-08-09).
        /// </summary>
        public const float DefaultClockSeconds = 180f;

        // ── WO-1594 HONOR MILESTONES (owner ruling 2026-09-07, verbatim: "1594 you determine
        //    what is realistic and fair" / "1594 q2 yes") ───────────────────────────────────
        // The felt story the owner asked for is CoC-adjacent and the opposite of the earn-up
        // ladder: the fight OPENS with three stars lit and they go DARK as milestones pass, so
        // the pressure is readable without arithmetic. On a 180s clock: half the fight to keep
        // the third star; the last 30s before timeout for the second, and only if the camp is
        // still under half razed. The first star is never snuffed mid-fight for time alone -
        // cracking the camp always pays something.
        //
        // These are PRESENTATION milestones. They do not touch ComputeStars, and the only place
        // they reach the payout is Finalize's honesty clamp (min(settle, honor)), which can
        // lower a result but never invent one.

        // ── THE MILESTONES ARE ROWS NOW (owner ruling 2026-09-09, verbatim choice: "Tunables
        //    with those defaults") ────────────────────────────────────────────────────────────
        // They were three compiled consts here (90f / 150f / 0.50f) from 2026-09-07, so the pace
        // of every raid in the game needed a 30-minute rebuild to move - and these numbers do not
        // only narrate: Finalize clamps the payout to min(settle, honor), so they set what a raid
        // can PAY as well. The owner's standing ruling since 2026-09-02 is that a balance number
        // is a row, and the defaults registered on the rail are 90 / 150 / 50, which is TODAY'S
        // BEHAVIOUR byte for byte.
        //
        // ⛔ EVERY READ GOES THROUGH SpecFor(key) FIRST - the same shape RaidDeployController's
        // StagingCeilingSeconds uses (RaidDeployController.cs:410-417), and it is load-bearing
        // rather than defensive: RemoteTunables.Int answers 0 for an UNREGISTERED key (and says
        // so loudly), and 0 seconds on the T3 milestone would snuff the third honor star on the
        // first frame of every raid. With the guard, "no row" and "no registry entry" both mean
        // exactly what this build shipped.

        /// <summary>
        /// Elapsed RAID seconds after which the third honor star snuffs (speed honor).
        /// Rail: <c>raid.honorThirdStarSeconds</c>, shipping default 90. Floored at 1 s so a
        /// console typo of 0 cannot put the star out before the fight starts.
        /// </summary>
        public static float HonorThirdStarSeconds
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorThirdStarSeconds);
                if (spec == null) return DeNelle.Core.Ops.RemoteTunables.RaidHonorThirdStarSecondsDefault;
                return Mathf.Max(1f, DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorThirdStarSeconds));
            }
        }

        /// <summary>
        /// Elapsed RAID seconds after which the second honor star may snuff, if D2 is unmet.
        /// Rail: <c>raid.honorSecondStarSeconds</c>, shipping default 150. Floored at 1 s.
        /// </summary>
        public static float HonorSecondStarSeconds
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorSecondStarSeconds);
                if (spec == null) return DeNelle.Core.Ops.RemoteTunables.RaidHonorSecondStarSecondsDefault;
                return Mathf.Max(1f, DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorSecondStarSeconds));
            }
        }

        /// <summary>
        /// Destruction FRACTION (0..1) that must be reached by T2 to keep the second honor star.
        /// Rail: <c>raid.honorSecondStarMinDestructionPct</c>, an integer PERCENT with shipping
        /// default 50 - the rail carries no floats, so the percent is the row and this property
        /// is the one place it becomes a fraction. Clamped 0..100 before the divide, so a row of
        /// 400 cannot make the milestone unsurvivable.
        /// </summary>
        public static float HonorSecondStarMinDestruction
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorSecondStarMinDestructionPct);
                if (spec == null)
                    return DeNelle.Core.Ops.RemoteTunables.RaidHonorSecondStarMinDestructionPctDefault / 100f;
                return Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidHonorSecondStarMinDestructionPct), 0, 100) / 100f;
            }
        }

        [Header("Clock")]
        [Tooltip("Raid clock in seconds (design B5 = 180s). A full clear UNDER this earns the 3rd star; " +
                 "when it runs out the raid ends (OnTimeExpired). Owner tunes by feel.")]
        [SerializeField] private float _clockSeconds = DefaultClockSeconds;

        [Header("Loot (owner tunes by feel)")]
        // CRYSTALS MOVED OFF THIS COMPONENT. They were two serialized fields here paying
        // base 25 + 3x10 = 55 at a perfect clear; the north-star map cuts that to 20-30 and
        // every balance value is a TUNABLE (standing rule 2026-09-02), so they now live on
        // the RemoteTunables rail beside wood/iron and are read in LootFor. Leaving the
        // fields here as well would be two answers to one question - and this component is
        // created by code (TryInstall -> new GameObject + AddComponent), so a serialized
        // value here was never authored in a scene and never could be tuned without a
        // rebuild anyway.
        [Tooltip("Food granted at 100% destruction, before the per-star bonus.")]
        [SerializeField] private int _lootFoodBase = 60;
        [Tooltip("Extra food per earned star.")]
        [SerializeField] private int _lootFoodPerStar = 20;

        // ── THE ENGAGEMENT GATE (WO-1520) ─────────────────────────────────────
        //
        // OWNER RULING 2026-09-06: "battle for raids should start in some staging area outside
        // of the attack range of everything so TIME STARTS ON FIRST ENGAGE."
        //
        // MEASURED BEFORE: RaidScoring.Update advanced the clock by Time.deltaTime from the
        // frame the scene loaded. raid-no-abilities-2026-09-06.log 12:59:47 shows that -
        // "elapsed=45s/180s" with the hero already dead, because the seconds the player spent
        // finding the deploy bar were billed to the raid clock while turrets shot them.
        //
        // ONE AUTHORITY, deliberately: NotifyEngagement is the only way _engaged is set, and
        // _elapsed is the only clock. Everything that can start a raid - a hero strike, a troop
        // strike, a defender acquiring a target - routes through that one method, so there is
        // never a second answer to "has the raid begun". DetectEngagement is the passive half
        // for the seams this component may not edit (Enemy / TroopController / AwarenessSensor
        // are owned by other lanes): it OBSERVES their public state instead of hooking them.
        //
        // NO GRACE TIMER. The owner explicitly refused one (WO-1520 sec.3): a countdown still
        // spawns the player in the fire. The staging MARKER is the fix; this flag is what stops
        // the clock billing them for standing on it.
        private bool _engaged;
        private string _engagedReason;
        private float _detectTimer;
        private float _spireHpAtStart = -1f;
        // WO-1526: latched the moment the hero falls in this raid. Clamps the settled result to
        // HeroDeathStarCap. Session state, never persisted - a raid is one session by definition.
        private bool _heroDied;
        // WO-1594: the last honor tier the HUD was told to show. -1 = never published (staging).
        // Presentation only - it is the edge detector behind the one "star-lost" trace line.
        private int _lastHonorStars = -1;

        // ── WO-1373 lane RAID-3: THE HONOR MILESTONE CACHE ────────────────────
        // PresentationStars is read by the live HUD AND by the per-frame snuff detector, and
        // each read used to walk all three Honor* properties - SpecFor + Int, i.e. a dictionary
        // probe and (on a miss) a PlayerPrefs read, six of them a frame on a device the raid
        // lane measured at 22 fps. These knobs are per-SESSION configuration; nothing about
        // them is per-frame. So they are read ONCE at engagement and re-read only when the rail
        // publishes a new payload, which RemoteTunables.Generation announces (it is bumped by
        // ApplyPayload and by Clear). The tunable ruling still holds - a knob pushed mid-raid
        // lands on the next frame - it just stops costing six lookups to find out it did not
        // change. -1 means "never seeded"; Generation starts at 0, so the first read always
        // seeds even if nothing has engaged yet.
        private int _honorGeneration = -1;
        private float _honorT3;
        private float _honorT2;
        private float _honorD2;

        /// <summary>How often the passive engagement detector sweeps while the raid is staging.</summary>
        private const float EngagementScanInterval = 0.2f;

        // ── Runtime ───────────────────────────────────────────────────────────
        private float _elapsed;
        private bool _finalized;
        private bool _timeExpiredFired;
        private RaidGarrisonSpawner _spawner;
        private RaidSpire _spire;         // the OBJECTIVE (null in a legacy spire-less raid)
        private int _garrisonTotalPeak;   // stable total once the staggered spawn settles

        // ── The STRUCTURES census (WO-853 sec.7) ──────────────────────────────
        // CAPTURED ONCE at raid start, then only read. Walls and turrets are baked into the
        // scene and exist from load, so unlike the staggered garrison this needs no peak
        // tracking - the denominator is simply how many stood when the raid began.
        //
        // The COMPONENTS are cached, not re-found per frame: DestructionPct is read by the
        // HUD every frame, and a FindObjectsByType sweep per frame in a property getter is
        // how a scorer becomes a profiler entry.
        //
        // A razed structure that Unity later destroys becomes fake-null in these arrays and
        // is skipped, contributing 0 to the surviving-HP sum while the denominator holds -
        // so "destroyed and removed" and "standing at 0 HP" score identically, which is the
        // behaviour we want and the reason the denominator is captured rather than counted live.
        private WallSegment[] _startWalls = System.Array.Empty<WallSegment>();
        private DefenseTower[] _startTowers = System.Array.Empty<DefenseTower>();
        private int _structuresTotalAtStart;
        private int _deployedCount;
        private readonly RaidDeployLog _deployLog = new RaidDeployLog();
        private RaidResult _result;

        /// <summary>Raised ONCE when the raid clock runs out (before finalize).</summary>
        public event Action OnTimeExpired;

        // ── Live readouts (the HUD binds these; all null-safe) ─────────────────

        /// <summary>Seconds elapsed since the raid began.</summary>
        public float ElapsedSeconds => _elapsed;
        /// <summary>The raid clock length (seconds).</summary>
        public float ClockSeconds => _clockSeconds;
        /// <summary>Seconds left on the clock (0 once expired).</summary>
        public float RemainingSeconds => Mathf.Max(0f, _clockSeconds - _elapsed);
        /// <summary>True once the raid has settled (victory / timeout).</summary>
        public bool Finalized => _finalized;

        /// <summary>
        /// WO-1520 - true once the raid has actually BEGUN (first engagement). The clock does
        /// not advance until this latches; see <see cref="NotifyEngagement"/>.
        /// </summary>
        public bool Engaged => _engaged;

        /// <summary>
        /// WO-1520 - true while the player is still in the staging area: nothing has engaged
        /// and the raid has not settled. The HUD reads "STAGING - deploy your troops" here.
        /// </summary>
        public bool InStaging => !_engaged && !_finalized;

        /// <summary>What started the clock ("hero strike", "defender acquired", ...); empty while staging.</summary>
        public string EngagedReason => _engagedReason ?? string.Empty;

        /// <summary>The garrison's stable total defender count (peak observed during spawn).</summary>
        public int GarrisonTotal => _garrisonTotalPeak;
        /// <summary>Living garrison defenders right now.</summary>
        public int GarrisonAlive => _spawner != null ? _spawner.AliveCount : 0;

        // =====================================================================
        //  THE OBJECTIVE (owner concept 2026-08-02): a raid is won by razing the
        //  central SPIRE, not by counting corpses. The garrison is still SCORED -
        //  it just is not the win condition any more.
        // =====================================================================

        /// <summary>
        /// Share of the destruction readout owned by the SPIRE. The remainder is the
        /// garrison clear. Kept public so the HUD copy, the loot floor and the oracle all
        /// read the same number instead of re-deriving it.
        /// WHY THE GARRISON IS STILL PAID: the objective decides WIN/LOSE, but a player who
        /// rushes the spire past a full, living garrison should not be paid the same as
        /// one who dismantled the base. Splitting it keeps both worth doing.
        /// </summary>
        public const float SpireWeight = 0.50f;

        /// <summary>
        /// Share of the destruction readout owned by the enemy base's STRUCTURES - its walls
        /// and its garrison turrets (<see cref="StructuresRazedPct"/>).
        ///
        /// OWNER RULING 2026-08-07 (WO-853 sec.7): the split is 50% spire / 30% structures /
        /// 20% garrison. It was 60/0/40 - structures counted for NOTHING, which is why a raid
        /// read as a fight and never a demolition. This term is the whole point of WO-853: the
        /// seam that made walls, gates and enemy turrets damageable already shipped, but until
        /// now breaking them changed no number the player could see.
        ///
        /// The spire stays the largest single term, so the objective is still the objective.
        /// </summary>
        public const float StructuresWeight = 0.30f;

        /// <summary>
        /// Share owned by the garrison clear - whatever the spire and structures do not take.
        /// DERIVED, never a fourth literal: three hand-typed weights are three chances to
        /// publish a split that does not sum to 1.
        /// </summary>
        public static float GarrisonWeight => 1f - SpireWeight - StructuresWeight;

        /// <summary>True when this raid actually has a spire objective (false in a legacy raid base).</summary>
        public bool HasObjective => _spire != null;

        /// <summary>True once the spire has been razed - the raid is won.</summary>
        public bool ObjectiveComplete => _spire != null && _spire.IsDestroyed;

        /// <summary>Spire HP remaining as 0..1 (1 = untouched). 0 when there is no spire.</summary>
        public float ObjectiveHpFraction => _spire != null ? _spire.HpFraction : 0f;

        /// <summary>
        /// The condition that ends the raid in a WIN: the spire falls OR the garrison
        /// is wiped. Owner 2026-09-09: a dead camp must settle, not wait for the empty
        /// spire to be farmed. Either signal is enough; Finalize latches the result.
        /// </summary>
        public bool RaidWon =>
            (_spire != null && _spire.IsDestroyed)
            || (_spawner != null && _spawner.Cleared);

        /// <summary>Living garrison fraction razed, 0..1 (1 once the garrison is wiped).</summary>
        private float GarrisonRazedPct
        {
            get
            {
                if (_spawner != null && _spawner.Cleared) return 1f;
                int total = _garrisonTotalPeak;
                if (total <= 0) return 0f;
                int alive = GarrisonAlive;
                return Mathf.Clamp01((total - alive) / (float)total);
            }
        }

        /// <summary>
        /// True when this raid base actually had structures to raze at start. False in a
        /// legacy base with no walls and no garrison turrets - the structures term then
        /// carries no information and is redistributed rather than scored as untouched.
        /// </summary>
        public bool HasStructures => _structuresTotalAtStart > 0;

        /// <summary>Structures standing at raid start (walls + enemy-owned turrets).</summary>
        public int StructuresTotal => _structuresTotalAtStart;

        /// <summary>
        /// Fraction of the base's STRUCTURES razed, 0..1 (1 = every wall and turret flattened).
        ///
        /// Both terms are read through <c>HpFraction</c>, the shared 0..1 abstraction, NOT
        /// through WallSegment's raw <c>Damage</c>. A wall stores an INVERTED 0-100 damage
        /// track (WallSegment.cs:99-100, 180-189), so "1 - Damage/100" is only equal to
        /// HpFraction while MaxHp happens to be exactly 100. Reading HpFraction means a later
        /// change to that constant cannot silently skew raid scores.
        ///
        /// Turrets are counted only when NOT PlayerOwned - a raid scores the defender's base,
        /// and any tower the player owns is not part of what they came to demolish.
        /// </summary>
        public float StructuresRazedPct
        {
            get
            {
                if (_structuresTotalAtStart <= 0) return 0f;

                float standing = 0f;
                for (int i = 0; i < _startWalls.Length; i++)
                {
                    var w = _startWalls[i];
                    if (w != null) standing += Mathf.Clamp01(w.HpFraction);
                }
                for (int i = 0; i < _startTowers.Length; i++)
                {
                    var t = _startTowers[i];
                    if (t != null) standing += Mathf.Clamp01(t.HpFraction);
                }

                return Mathf.Clamp01(1f - standing / _structuresTotalAtStart);
            }
        }

        /// <summary>
        /// Live destruction fraction 0..1 - the number behind the HUD's "Base Razed" bar.
        ///
        /// FULL FORM (spire + structures present): the owner's 50/30/20 blend of spire damage,
        /// structures razed and garrison razed (WO-853 sec.7, ruled 2026-08-07). Before this
        /// it was 60/0/40 and breaking a wall moved nothing.
        ///
        /// DEGRADATION - why the missing term is REDISTRIBUTED, not scored as zero. A legacy
        /// base with no spire, or one with no walls or turrets, would otherwise be incapable
        /// of ever reaching 100% razed: the absent term would sit permanently at 0 and cap the
        /// bar at 70% or 50%. That silently breaks the star thresholds and the loot scale for
        /// every scene that predates the term. So an absent term's weight is dropped and the
        /// survivors are RENORMALISED, which preserves their ratio to each other and keeps the
        /// ceiling at 1.0. With no spire AND no structures this collapses to the original
        /// pure-garrison count, so nothing that shipped before WO-771.6 regresses.
        /// </summary>
        public float DestructionPct
        {
            get
            {
                float garrison = GarrisonRazedPct;
                bool hasSpire = _spire != null;
                bool hasStructures = _structuresTotalAtStart > 0;

                if (!hasSpire && !hasStructures) return garrison;   // legacy: corpse count only

                float wSpire = hasSpire ? SpireWeight : 0f;
                float wStruct = hasStructures ? StructuresWeight : 0f;
                float wGarrison = GarrisonWeight;

                float total = wSpire + wStruct + wGarrison;
                if (total <= 0f) return garrison;                   // unreachable; never divide by 0

                float blended = 0f;
                if (hasSpire) blended += Mathf.Clamp01(_spire.DamagedFraction) * wSpire;
                if (hasStructures) blended += StructuresRazedPct * wStruct;
                blended += garrison * wGarrison;

                return Mathf.Clamp01(blended / total);
            }
        }

        /// <summary>Troops the player has deployed this raid (running total).</summary>
        public int TroopsDeployed => _deployedCount;

        /// <summary>Deployed troops still alive on the field right now.</summary>
        public int TroopsAlive
        {
            get
            {
                int n = 0;
                var troops = TroopController.ActiveTroops;
                for (int i = 0; i < troops.Count; i++)
                    if (troops[i] != null && troops[i].IsAlive) n++;
                return n;
            }
        }

        /// <summary>
        /// The LIVE projected star tier if the raid settled at this instant. The "cleared"
        /// axis is now the OBJECTIVE (<see cref="RaidWon"/>), not the corpse count - the
        /// star ladder itself (ComputeStars) is untouched, only what feeds it.
        /// </summary>
        public int ProjectedStars =>
            ApplyHeroDeathCap(
                ComputeStars(RaidWon,
                             RaidWon,                             // the objective IS the boss kill now
                             DestructionPct, _elapsed, _clockSeconds, SurvivalPct),
                _heroDied);

        /// <summary>
        /// WO-1594 — THE NUMBER THE LIVE HUD BINDS. Honor stars: three lit the instant the raid
        /// engages, snuffed one at a time as milestones pass. Staging reads 0 because nothing is
        /// lit before the fight starts (the clock has not started either — WO-1520).
        ///
        /// <para>⚠ NOT the same axis as <see cref="ProjectedStars"/>, and they are deliberately
        /// both kept. ProjectedStars is an EARN-UP settle preview (what would I score if the raid
        /// ended now); this is a SNUFF-DOWN honor read (what have I not lost yet). Binding the
        /// earn-up number to the HUD is what produced the felt defect this ticket exists for: the
        /// bar sat at 0/3 through the whole fight and jumped at the end, which narrates nothing.
        /// Tools and the end screen may still read ProjectedStars.</para>
        /// </summary>
        /// <remarks>
        /// WO-1373 lane RAID-3: this getter reads the CACHED milestones, never the three
        /// <c>Honor*</c> properties. It is read twice a frame (HUD bind + the snuff detector),
        /// and each property walks <c>RemoteTunables.SpecFor</c> then <c>RemoteTunables.Int</c>.
        /// The cache is seeded at <see cref="NotifyEngagement"/> and refreshed only when the rail
        /// publishes a new payload (<c>RemoteTunables.Generation</c> moves), so a mid-raid knob
        /// change still lands — it just does not cost six lookups per frame to notice.
        /// </remarks>
        public int PresentationStars
        {
            get
            {
                RefreshHonorCacheIfStale();
                return ComputeHonorStars(_engaged, _elapsed, DestructionPct, _heroDied,
                                         _honorT3, _honorT2, _honorD2);
            }
        }

        /// <summary>
        /// WO-1526 — true once the hero has fallen in THIS raid. Read by the HUD and by
        /// <see cref="Finalize"/>; latched by <see cref="NotifyHeroDied"/>.
        /// </summary>
        public bool HeroDied => _heroDied;

        /// <summary>
        /// WO-1526 — latch the hero's death for the star clamp. Idempotent: called from the raid
        /// death path (<c>HeroHealth.HandleDeath</c>), which may be reached more than once across
        /// a death/settle race, and the second call must be a silent no-op, not a second trace.
        ///
        /// <para><b>COMPOSE NOTE (2026-09-06):</b> WO-1594 (branch <c>grok/raid-1593-1595</c>)
        /// adds an identically-named <c>NotifyHeroDied</c> on this same class for the honor-star
        /// snuff. That is DELIBERATELY THE SAME SEAM, not a second hero-death path — on merge,
        /// take his body (it traces the snuff as well) and keep this one call site. There must
        /// never be two latches for one death.</para>
        /// </summary>
        public void NotifyHeroDied()
        {
            if (_heroDied) return;
            _heroDied = true;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"HERO DOWN - the raid CONTINUES (WO-1526 owner ruling 2026-09-06). elapsed=" +
                $"{_elapsed:F0}s/{_clockSeconds:F0}s destruction={DestructionPct:P0} " +
                $"finalized={_finalized}. The army fights on; this raid can now settle at most " +
                $"{HeroDeathStarCap} star(s). Nothing is settled here - loot settles when the raid " +
                "actually ends (clear, Retreat or the clock).");
        }

        /// <summary>The simple re-watch record (order/time/place of every deploy).</summary>
        public RaidDeployLog DeployLog => _deployLog;

        /// <summary>The settled result once <see cref="Finalize"/> ran (null until then).</summary>
        public RaidResult Result => _result;

        // =====================================================================
        //  PURE static scoring math — unit-testable with NO scene / GameObject.
        // =====================================================================

        /// <summary>
        /// Fraction of DEPLOYED troops that must still be standing for a raid to count as
        /// "high survival" on the star ladder. Public so HUD copy and oracles read the same
        /// number instead of re-deriving it. Balance knob - tune here, one place.
        /// </summary>
        public const float HighSurvivalPct = 0.70f;

        /// <summary>
        /// The V1 star ladder (OWNER RULING 2026-07-30 - the "premium" model). Two axes,
        /// both earned, on top of clearing the base:
        /// <list type="bullet">
        /// <item><b>1 star</b> - you just cleared it.</item>
        /// <item><b>2 stars</b> - cleared with high survival <b>OR</b> under the clock.</item>
        /// <item><b>3 stars</b> - cleared with high survival <b>AND</b> under the clock.</item>
        /// </list>
        /// Sub-clear credit is unchanged: >= 50% of the garrison razed still pays 1 star, so a
        /// retreat that did real damage keeps its loot.
        ///
        /// WHY THIS REPLACED THE OLD FORMULA: the old ladder floored a clear at 2 and gave 3 for
        /// any clear inside the clock - and a raid that OVERRAN the clock never reached the
        /// victory path at all (the clock ends it via OnTimeExpired -> retreat). So EVERY
        /// victory scored 3 stars, which made the 3-star gate meaningless and, once victory
        /// started paying veterancy, promoted every survivor of every win.
        ///
        /// NOTE on <paramref name="bossDestroyed"/>: its old floor of 2 is now a floor of 1. In
        /// V1 the boss is part of the garrison, so <c>bossDown == cleared</c> (see Finalize) -
        /// the old floor could only ever fire on a clear, where it short-circuited the ladder
        /// above. Non-clear behaviour is therefore unchanged by the demotion.
        ///
        /// <paramref name="survivalPct"/> is survivors/deployed at settle time. Deploying NO
        /// troops (a scout run) is 1f - "nothing was lost" - so it is never punished for it.
        /// Pure + static: unit-testable with no scene.
        /// </summary>
        public static int ComputeStars(bool garrisonCleared, bool bossDestroyed,
                                       float destructionPct, float elapsedSeconds, float clockSeconds,
                                       float survivalPct)
        {
            int s = 0;
            if (destructionPct >= 0.5f) s = 1;
            if (bossDestroyed) s = Mathf.Max(s, 1);

            if (garrisonCleared)
            {
                s = Mathf.Max(s, 1);                                              // cleared it
                bool underTime    = elapsedSeconds <= Mathf.Max(1f, clockSeconds);
                bool highSurvival = Mathf.Clamp01(survivalPct) >= HighSurvivalPct;
                if (underTime || highSurvival) s = Mathf.Max(s, 2);               // one axis
                if (underTime && highSurvival) s = 3;                             // both axes
            }
            return Mathf.Clamp(s, 0, 3);
        }

        /// <summary>
        /// WO-1526 — the owner's hero-death clamp, PURE so an oracle can assert it with no scene.
        /// A raid in which the hero fell settles at most <see cref="HeroDeathStarCap"/> stars,
        /// however perfect the clear. It only ever LOWERS a tier - a hero who never fell is
        /// returned untouched, so this can never inflate a result.
        ///
        /// <para><b>COMPOSE NOTE (2026-09-06):</b> WO-1594 replaces <see cref="Finalize"/>'s star
        /// line with <c>Mathf.Min(settleStars, honorStars)</c>, and its <c>ComputeHonorStars</c>
        /// already returns at most 2 when <c>heroDied</c> — so on merge that Min SUBSUMES this
        /// call and this helper becomes a redundant (still-correct, still-pinned) second
        /// statement of the same ceiling. Take his hunk; keeping both is harmless but keeping
        /// only his is cleaner.</para>
        /// </summary>
        public static int ApplyHeroDeathCap(int stars, bool heroDied)
            => heroDied ? Mathf.Min(stars, HeroDeathStarCap) : stars;

        /// <summary>
        /// WO-1594 — the PURE honor / presentation projector. Snuffs DOWN from three; it can
        /// never light a star back up, which is what makes it safe to clamp the payout against.
        /// <list type="bullet">
        /// <item>Staging (<paramref name="engaged"/> false) → <b>0</b>: nothing is lit before the
        /// fight starts, and the clock has not started either (WO-1520).</item>
        /// <item>At engage → <b>3</b>.</item>
        /// <item>Past <see cref="HonorThirdStarSeconds"/>, or the hero has fallen (owner Q2 =
        /// YES) → at most <b>2</b>. The hero clause agrees with
        /// <see cref="HeroDeathStarCap"/> by construction.</item>
        /// <item>Past <see cref="HonorSecondStarSeconds"/> AND destruction still below
        /// <see cref="HonorSecondStarMinDestruction"/> → at most <b>1</b>.</item>
        /// <item>The last star is NEVER snuffed mid-fight for time alone — a player who cracked
        /// the camp keeps something. Whether it survives to the payout is
        /// <see cref="ComputeStars"/>'s call at settle, not this one's.</item>
        /// </list>
        /// Pure + static: unit-testable with no scene, exactly like <see cref="ComputeStars"/>.
        /// </summary>
        public static int ComputeHonorStars(
            bool engaged, float elapsedSeconds, float destructionPct, bool heroDied)
            => ComputeHonorStars(engaged, elapsedSeconds, destructionPct, heroDied,
                                 HonorThirdStarSeconds, HonorSecondStarSeconds,
                                 HonorSecondStarMinDestruction);

        /// <summary>
        /// WO-1373 lane RAID-3 — the SAME projector with the three milestones passed IN.
        ///
        /// <para><b>WHY THIS OVERLOAD EXISTS, measured not guessed.</b> The four-argument form
        /// above reads <see cref="HonorThirdStarSeconds"/>, <see cref="HonorSecondStarSeconds"/>
        /// and <see cref="HonorSecondStarMinDestruction"/>, and every one of those getters walks
        /// <c>RemoteTunables.SpecFor</c> and then <c>RemoteTunables.Int</c> — a dictionary probe
        /// plus, on a miss, a PlayerPrefs read. <see cref="PresentationStars"/> is bound by the
        /// live HUD (<c>RaidHudController</c>) AND read again by <c>TraceHonorStarSnuff</c> from
        /// <c>Update</c>, so the shipped build performed those three lookups TWICE PER FRAME on a
        /// device the raid lane already measured at 22 fps. The knobs are per-session
        /// configuration; nothing about them is per-frame.</para>
        ///
        /// <para>⛔ THE FOUR-ARG SIGNATURE ABOVE IS PINNED and must not be removed — the honor
        /// oracle calls it directly (<c>RaidWatchdogHonorRegression</c>), and it stays the pure,
        /// nothing-loaded entry point. This overload is the same arithmetic with the reads
        /// hoisted; the two can never disagree because the four-arg form now delegates here.</para>
        /// </summary>
        public static int ComputeHonorStars(
            bool engaged, float elapsedSeconds, float destructionPct, bool heroDied,
            float thirdStarSeconds, float secondStarSeconds, float secondStarMinDestruction)
        {
            if (!engaged) return 0;

            int stars = 3;
            if (heroDied || elapsedSeconds > thirdStarSeconds)
                stars = Mathf.Min(stars, 2);
            if (elapsedSeconds > secondStarSeconds
                && Mathf.Clamp01(destructionPct) < secondStarMinDestruction)
                stars = Mathf.Min(stars, 1);

            return Mathf.Clamp(stars, 0, 3);
        }

        /// <summary>
        /// Survivors / deployed at this instant, clamped 0..1. Deploying nothing reads as 1f
        /// (nothing lost) so a no-troop scout clear is not punished on the survival axis.
        /// </summary>
        public float SurvivalPct
        {
            get
            {
                int deployed = _deployedCount;
                if (deployed <= 0) return 1f;
                return Mathf.Clamp01(TroopsAlive / (float)deployed);
            }
        }

        /// <summary>
        /// Loot payout scaled by destruction AND the star tier.
        /// <paramref name="rewardMultiplier"/> is the scene-config difficulty mult
        /// (Regular 1.0 / Hard 1.5 / Extreme 2.2) — applied AFTER stars+destruction so the
        /// selection-card "xLoot" is honest. Static + pure so a regression can assert the
        /// scaling with no scene, no save and no network.
        ///
        /// <para><b>THREE AXES, DELIBERATELY DIFFERENT.</b></para>
        /// <list type="bullet">
        /// <item><b>Food</b> keeps its original shape exactly:
        /// <c>base * destruction + perStar * stars</c>, times the camp multiplier.</item>
        /// <item><b>Crystals</b> keep that same SHAPE but are no longer multiplied by
        /// <paramref name="rewardMultiplier"/>, and their bases come down hard (map §1:
        /// 20-30 at a perfect clear, against the 55 this build used to pay). The map's
        /// reason is the whole ruling: <i>"Crystals are timer compression. If raids dump
        /// huge amounts of crystals, you accidentally accelerate the already-too-short
        /// progression curve."</i> Excluding them from the camp multiplier is the same
        /// ruling applied to escalation - a harder camp pays more gold, wood and iron, not
        /// more instant-finish. Crystals are the ONE reward that decreases.</item>
        /// <item><b>Wood + iron</b> ride the north-star map's PERFORMANCE LADDER
        /// (<c>docs/PROGRAM_RAID_ECONOMY_2026-09-04.md</c> section 1): a share of the base
        /// chosen by RESULT — failed 15-20% / 1 star 50% / 2 stars 75% / 3 stars 100% /
        /// 3 stars with 100% destruction 110%. The ladder is not the same function as the
        /// crystals/food ramp and must not be collapsed into it: a linear ramp off
        /// destruction pays a sloppy 80% clear nearly as well as a perfect one, which is
        /// exactly the "getting better at raiding has no economic payoff" the map exists
        /// to fix.</item>
        /// </list>
        ///
        /// THE FORK IS CLOSED (commit 281902df0): troops cost
        /// GOLD, they ALSO take time, and a second gold spend hires mercenaries to skip
        /// the clock. So gold is PAID here now, off a PER-CAMP base
        /// (<see cref="RaidLootTunables.CoinsBaseFor"/>), riding the SAME performance
        /// ladder as wood and iron - the map's one explicitly named missing arrow:
        /// <i>"You currently have Gold to troops but not troops to raids to gold. That
        /// arrow has to exist."</i></para>
        ///
        /// <para>Gold is NOT multiplied by <paramref name="rewardMultiplier"/>. The map
        /// publishes a DESIGNED gold target per camp (2,200 / 3,100 / 4,500 / 6,500), so
        /// the escalation lives in the base; x1.5 of 2,200 is 3,300, not her 3,100.</para>
        ///
        /// <para>The three trailing parameters are LAST and default to 0, so every pre-existing
        /// caller compiles unchanged AND pays exactly what it paid before — the old
        /// four-argument shape is still a food-and-crystals-only payout, byte for byte.</para>
        /// </summary>
        /// <param name="woodBase">Wood at a PERFECT run, before the ladder and the camp
        /// multiplier. 0 = pay no wood (the pre-WO-1374 behaviour).</param>
        /// <param name="ironBase">Iron at a PERFECT run, on the same terms.</param>
        /// <param name="coinsBase">GOLD at a PERFECT run on THIS camp, before the ladder.
        /// The camp multiplier is deliberately NOT applied to it. 0 = pay no gold.</param>
        public static ResourceCost ComputeLoot(int stars, float destructionPct,
            int crystalsBase, int foodBase, int crystalsPerStar, int foodPerStar,
            float rewardMultiplier = 1f, int woodBase = 0, int ironBase = 0, int coinsBase = 0)
        {
            float frac = Mathf.Clamp01(destructionPct);
            int st = Mathf.Clamp(stars, 0, 3);
            float mult = rewardMultiplier > 0f ? rewardMultiplier : 1f;
            // CRYSTALS: the same shape as before, but NO camp multiplier. Escalating camps
            // must raise gold/wood/iron, never timer compression (map section 1).
            int crystals = Mathf.RoundToInt(
                Mathf.Max(0, crystalsBase) * frac + Mathf.Max(0, crystalsPerStar) * st);
            int food = Mathf.RoundToInt(
                (Mathf.Max(0, foodBase) * frac + Mathf.Max(0, foodPerStar) * st) * mult);

            // WO-1374 — the construction axis. RaidLootTunables owns the ladder AND the
            // clamps; nothing here re-derives a percentage, so there is exactly one answer
            // to "what does a 2-star raid pay".
            float ladder = RaidLootTunables.Fraction(st, frac);
            int wood = Mathf.RoundToInt(Mathf.Max(0, woodBase) * ladder * mult);
            int iron = Mathf.RoundToInt(Mathf.Max(0, ironBase) * ladder * mult);

            // THE ARROW: troops -> raids -> gold. Same ladder, per-camp base, NO mult.
            int coins = Mathf.RoundToInt(Mathf.Max(0, coinsBase) * ladder);

            return new ResourceCost(wood: wood, food: food, iron: iron, crystals: crystals, coins: coins);
        }

        /// <summary>
        /// WO-1402 - THE ONE PLACE THE LIVE KNOBS MEET <see cref="ComputeLoot"/>. Reads the
        /// wood / iron / per-camp gold / crystal bases off <see cref="RaidLootTunables"/> and
        /// applies them to a (stars, destruction, multiplier) triple. <see cref="LootFor"/>
        /// (the settle-time PAYOUT) and <see cref="EstimateSpoils"/> (the selection-row
        /// PREVIEW) both call this and nothing else, so the card can never promise a number
        /// the settle screen computes differently. Static and scene-free so a regression can
        /// call it with no raid loaded.
        /// </summary>
        /// <param name="foodBase">Food at 100% destruction, before stars. Food is the one
        /// axis still authored on the scorer INSTANCE (serialized fields), so the preview
        /// passes 0 and simply does not estimate food.</param>
        public static ResourceCost ProjectLoot(int stars, float destructionPct, float rewardMultiplier,
            string campId, int foodBase, int foodPerStar)
        {
            // WO-1374 - the wood/iron bases come off the REMOTE TUNABLE rail, not off a
            // serialized field, because the owner sets this curve by feel and a serialized
            // field is a 30-minute rebuild per opinion. No row / no network / no parse
            // resolves to the shipping defaults (1800 / 1100), so an offline player is paid
            // exactly what this build hardcodes.
            int woodBase = RaidLootTunables.WoodBase;
            int ironBase = RaidLootTunables.IronBase;
            // THE ARROW (map's "troops -> raids -> gold"). The GOLD base is PER CAMP, so it
            // is resolved from the raid's config id rather than from one global number -
            // the map publishes a designed target per tier, sized at 125-140% of that
            // tier's expected army replacement cost.
            int coinsBase = RaidLootTunables.CoinsBaseFor(campId);
            int crystalsBase = RaidLootTunables.CrystalsBase;
            int crystalsPerStar = RaidLootTunables.CrystalsPerStar;
            return ComputeLoot(stars, destructionPct,
                crystalsBase, foodBase, crystalsPerStar, foodPerStar, rewardMultiplier,
                woodBase, ironBase, coinsBase);
        }

        // =====================================================================
        //  WO-1373 - THE ROUGH-STONE DROP. Owner ruling 2026-09-09, verbatim:
        //  "there is only one stone type till it gets to jeweler, and then its
        //  RND. So only top two tiers of raids can drop stone and no more than 1
        //  per day. 5% drop rate in dungeons not included the starter dungeons".
        //
        //  ⛔ WHY THE RULE LIVES HERE AND THE GRANT DOES NOT. The stone is an
        //  ITEM (ing_rough_stone in the larder), not an axis of ResourceCost, so
        //  it cannot ride ComputeLoot's return value. The DECISION is pure and
        //  static here - callable by an oracle with no scene, no save, no network
        //  and no PlayerPrefs - and the single GRANT stays in RaidVictoryController
        //  beside the one resource grant, so there is still exactly one place a
        //  raid pays a player. Do not add a second grant site (WO-1112 spent a day
        //  undoing four of those in the dungeon payout).
        //
        //  ⚠ scene-configs.json HAS NO "tier" FIELD - read at source 2026-09-09.
        //  The camp ladder is RaidLootTunables' four id constants, in map order,
        //  and that ordering is corroborated by two authored fields in the same
        //  catalog: unlockVictories (0 / 3 / 10 / 20) and difficulty (Regular /
        //  Hard / Extreme / Extreme). Both agree the top two are mage_enclave and
        //  iron_bastion. Only the id constants are read here - a copy of the
        //  ladder would be exactly the duplicated state CLAUDE.md keeps naming.
        // =====================================================================

        /// <summary>
        /// The item id a raid may drop. ONE stone type, per the ruling - the grade is decided
        /// at the Jeweler's bench, not here (see <c>JewelPolishService</c>).
        /// <para>⚠ It is BOUND to <c>DungeonExclusiveItems.RoughStoneId</c> at compile time, not
        /// re-typed - so the two can never be edited apart in source. But be honest about what a
        /// cross-assembly <c>const</c> is: C# bakes the literal into DeNelle.Village's IL, so
        /// changing the catalog's id requires a REBUILD of this assembly, not just of Core. That
        /// is true of every consumer of that const and is not a defect introduced here;
        /// <c>RaidRoughStoneDropRegression</c>'s <c>[one-stone]</c> case compares the two at run
        /// time precisely so a half-rebuilt tree fails loudly instead of paying the wrong item.</para>
        /// </summary>
        public const string RoughStoneItemId = DeNelle.Core.Catalog.DungeonExclusiveItems.RoughStoneId;

        /// <summary>
        /// Lowest camp TIER (1..4) that may drop a rough stone. Rail:
        /// <c>raid.roughStoneMinTier</c>, shipping default 3 = the lower of the top two rungs.
        /// Clamped 1..99 so a console typo can only ever narrow the drop, never widen it past
        /// the ladder; a value above 4 turns the raid drop off entirely, which is the ONE row
        /// that restores the pre-WO-1373 build.
        /// </summary>
        public static int RoughStoneMinTier
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidRoughStoneMinTier);
                if (spec == null) return DeNelle.Core.Ops.RemoteTunables.RaidRoughStoneMinTierDefault;
                return Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidRoughStoneMinTier), 1, 99);
            }
        }

        /// <summary>
        /// How many rough stones EVERY raid together may pay in one UTC day. Rail:
        /// <c>raid.roughStonePerDayCap</c>, shipping default 1 - her "no more than 1 per day".
        /// GLOBAL, not per camp. Clamped 0..99; 0 turns the raid drop off without touching the
        /// tier row.
        /// </summary>
        public static int RoughStonePerDayCap
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidRoughStonePerDayCap);
                if (spec == null) return DeNelle.Core.Ops.RemoteTunables.RaidRoughStonePerDayCapDefault;
                return Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyRaidRoughStonePerDayCap), 0, 99);
            }
        }

        /// <summary>
        /// The camp's rung on the I..IV ladder, or 0 for an id this build does not know.
        /// <para>0 is the SAFE answer, deliberately: an unknown camp is below every possible
        /// <see cref="RoughStoneMinTier"/>, so a camp added to the catalog without being added
        /// here pays no stone and says so, rather than silently inheriting the top rung. That
        /// is the opposite default from <c>RaidLootTunables.CoinsBaseFor</c>, and for the
        /// opposite reason: falling back to Camp I gold protects a payout that already exists,
        /// while falling back to a tier would OPEN a faucet nobody authored.</para>
        /// Pure and static: no scene, no save, no rail.
        /// </summary>
        public static int TierOf(string configId)
        {
            string id = string.IsNullOrEmpty(configId) ? "" : configId.Trim();
            if (string.Equals(id, RaidLootTunables.CampIdCamp1, StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(id, RaidLootTunables.CampIdCamp2, StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(id, RaidLootTunables.CampIdCamp3, StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(id, RaidLootTunables.CampIdBastion, StringComparison.OrdinalIgnoreCase)) return 4;
            return 0;
        }

        /// <summary>
        /// THE PURE DECISION. True when a victory on <paramref name="configId"/> should pay one
        /// rough stone, given how many the player has already been paid today.
        ///
        /// <para>Two gates and nothing else, because the ruling names two: the camp's tier, and
        /// a per-UTC-day count across every camp. There is deliberately NO star gate - WO-1373
        /// section 5.2 asks whether the drop should scale by stars and the owner did not answer
        /// it, so this ships on the axes she DID rule and the question stays open in the RESULT
        /// rather than being settled by an implementer.</para>
        ///
        /// <para>Pure and static: nothing loaded, so an oracle asserts the whole table offline.</para>
        /// </summary>
        /// <param name="configId">The raid config id being settled. Unknown ids pay nothing.</param>
        /// <param name="grantedToday">Stones already paid by raids this UTC day, across every camp.</param>
        /// <param name="minTier">Lowest eligible tier - pass <see cref="RoughStoneMinTier"/>.</param>
        /// <param name="perDayCap">The global day cap - pass <see cref="RoughStonePerDayCap"/>.</param>
        /// <param name="why">Always written, on BOTH branches - a player who cleared the top
        /// camp and got no stone must be explainable from a capture.</param>
        public static bool ShouldDropRoughStone(string configId, int grantedToday,
                                                int minTier, int perDayCap, out string why)
        {
            int tier = TierOf(configId);
            string id = string.IsNullOrEmpty(configId) ? "(none)" : configId;

            if (tier <= 0)
            {
                why = "camp '" + id + "' is not on the I..IV ladder (RaidLootTunables camp ids), " +
                      "so its tier is unknown and it pays NO stone. Add its id to RaidScoring.TierOf " +
                      "if it is meant to.";
                return false;
            }
            if (perDayCap <= 0)
            {
                why = "the per-day cap is 0 (raid.roughStonePerDayCap) - raids do not drop stone at all.";
                return false;
            }
            if (tier < minTier)
            {
                why = "camp '" + id + "' is tier " + tier + ", below the minimum tier " + minTier +
                      " (raid.roughStoneMinTier) - only the top rungs drop stone.";
                return false;
            }
            if (grantedToday >= perDayCap)
            {
                why = "the UTC day cap is already met: " + grantedToday + " of " + perDayCap +
                      " stone(s) paid today across every camp.";
                return false;
            }

            why = "camp '" + id + "' is tier " + tier + " (>= " + minTier + ") and " + grantedToday +
                  " of " + perDayCap + " stone(s) have been paid today - PAYING one rough stone.";
            return true;
        }

        // ── The per-day LEDGER. Impure; kept apart from the decision above ────
        // PlayerPrefs, deliberately, exactly like RaidClaimService's crystal day-stamp
        // (dotr-raid-crystalday-<id>) and DungeonRunPayout's polish FIFO: a day key plus a
        // count self-expires, needs no cleanup, and needs NO SAVE-SCHEMA BUMP. A schema bump
        // is an owner decision and nothing here requires one - the AUTHORITATIVE record of
        // what the player owns is the larder count, which is already persisted properly. If
        // this ledger is lost the player gets at most one extra stone; nothing is destroyed
        // and nothing is duplicated.

        private const string PrefStoneDayKey = "dotr-raid-stoneday";
        private const string PrefStoneCountKey = "dotr-raid-stonecount";

        /// <summary>
        /// How many rough stones raids have paid so far in the current UTC day. Reads 0 on a
        /// new day without anything having to reset it - the stored day key simply stops
        /// matching, which is the same self-expiry the crystal stamp uses.
        /// </summary>
        public static int RoughStonesGrantedToday()
        {
            string stamped = PlayerPrefs.GetString(PrefStoneDayKey, string.Empty);
            if (string.IsNullOrEmpty(stamped) || stamped != DeNelle.Core.UtcDay.Key()) return 0;
            return Mathf.Max(0, PlayerPrefs.GetInt(PrefStoneCountKey, 0));
        }

        /// <summary>
        /// Record one rough stone paid by a raid today. ⛔ Call this AFTER the stone is in the
        /// larder, never before - the same lesson written on <c>MarkCrystalsPaid</c>: a stamp
        /// written ahead of a grant that then throws burns the player's one stone of the day
        /// for nothing.
        /// </summary>
        public static void MarkRoughStoneGranted()
        {
            string day = DeNelle.Core.UtcDay.Key();
            int next = RoughStonesGrantedToday() + 1;
            PlayerPrefs.SetString(PrefStoneDayKey, day);
            PlayerPrefs.SetInt(PrefStoneCountKey, next);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid",
                "ROUGH STONE day-ledger: " + next + " stone(s) paid by raids on " + day +
                " (persisted " + PrefStoneDayKey + " / " + PrefStoneCountKey + "). Cap is " +
                RoughStonePerDayCap + ".");
        }

        /// <summary>
        /// Test/dev hook: clear the day ledger so raids may pay stone again today. Exercised by
        /// <c>RaidRoughStoneDropRegression</c>, which round-trips it on the real keys and
        /// restores whatever was there. (An unexercised reset hook proves nothing - the claim
        /// set went write-only for months for exactly that reason.)
        /// </summary>
        public static void ClearRoughStoneDayLedger()
        {
            PlayerPrefs.DeleteKey(PrefStoneDayKey);
            PlayerPrefs.DeleteKey(PrefStoneCountKey);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid", "ROUGH STONE day-ledger CLEARED.");
        }

        /// <summary>
        /// The star rung the selection-row ESTIMATE is quoted at: a clean 3-star clear (the
        /// ladder's 100% rung, NOT the 110% total-razing rung), so the "~" number is what a
        /// competent win pays and a perfect win reads as a bonus rather than a shortfall.
        /// </summary>
        public const int EstimateStars = 3;

        /// <summary>
        /// WO-1402 - what a raid on <paramref name="campId"/> is expected to PAY, before the
        /// player ever taps the row. Same ladder, same bases, same multiplier as the settle
        /// payout (<see cref="ProjectLoot"/> is the one formula); quoted at
        /// <see cref="EstimateStars"/> with no destruction bonus, so it is an ESTIMATE the
        /// settle screen can only match or beat. Food is not estimated (instance-authored,
        /// see <see cref="ProjectLoot"/>). Never throws: a tunable-rail fault logs and
        /// answers an all-zero basket, which the caller reads as "no line".
        /// </summary>
        public static ResourceCost EstimateSpoils(string campId, float rewardMultiplier)
        {
            try
            {
                return ProjectLoot(EstimateStars, 0f, rewardMultiplier, campId, 0, 0);
            }
            catch (Exception ex)
            {
                // Never swallowed silently (CLAUDE.md section 12): a row with no spoils line
                // must be explainable from the capture.
                FlowTrace.Warn("Raid",
                    "spoils ESTIMATE threw for camp '" + (campId ?? "(none)") + "' (" +
                    ex.GetType().Name + ": " + ex.Message + ") - the selection row will carry " +
                    "no spoils line.");
                return default(ResourceCost);
            }
        }

        // =====================================================================
        //  Self-install — one scorer per RaidBase_* scene
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstall(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TryInstall(scene.name);
        }

        private static void TryInstall(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!sceneName.StartsWith("RaidBase", StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<RaidScoring>() != null) return;

            var go = new GameObject("RaidScoring");
            go.AddComponent<RaidScoring>();
            FlowTrace.Step("Raid", $"RaidScoring self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            StartCoroutine(BindRoutine());
        }

        // The spawner arms its garrison a frame after its own Start; poll a few
        // frames for it (mirrors RaidVictoryController.BindRoutine).
        private IEnumerator BindRoutine()
        {
            for (int i = 0; i < 10 && _spawner == null; i++)
            {
                _spawner = FindAnyObjectByType<RaidGarrisonSpawner>();
                if (_spawner != null) break;
                yield return null;
            }
            if (_spawner == null)
                FlowTrace.Warn("Raid", "RaidScoring: no RaidGarrisonSpawner found — destruction% will read 0 (scoring degrades to clock/deploy only).");
            else
                FlowTrace.Step("Raid", "RaidScoring bound to the garrison spawner (clock + destruction% live).");

            // The OBJECTIVE. Baked into the scene, so it is present from load - but bind the
            // same tolerant way in case a scene predates the spire (legacy raid bases keep the
            // old garrison-wipe rule rather than becoming unwinnable).
            _spire = RaidSpire.Active != null ? RaidSpire.Active : FindAnyObjectByType<RaidSpire>();
            if (_spire == null)
                FlowTrace.Warn("Raid", "RaidScoring: this raid scene has NO RaidSpire objective — " +
                                       "falling back to the legacy garrison-wipe win condition. " +
                                       "Re-bake it with RaidBaseGenerator.BuildAllRaidScenes to get the spire.");
            else
                FlowTrace.Step("Raid", $"RaidScoring bound to the OBJECTIVE: spire '{_spire.name}' " +
                                       $"({_spire.MaxHp:0} HP). Razing it wins the raid.");

            CaptureStructureCensus();

            FlowTrace.Step("Raid", $"destruction% split = {SpireWeight:P0} spire / " +
                                   $"{StructuresWeight:P0} structures / {GarrisonWeight:P0} garrison " +
                                   $"(spire={(_spire != null ? "yes" : "NO")}, " +
                                   $"structures={_structuresTotalAtStart}). " +
                                   "Absent terms are renormalised away, not scored as 0.");

            // WO-932: one clock line for the Phase 0 probe matrix.
            FlowTrace.Step("Raid",
                $"RAID CLOCK armed: {_clockSeconds:0}s | loot bases crystals={RaidLootTunables.CrystalsBase} " +
                $"food={_lootFoodBase} wood={RaidLootTunables.WoodBase} iron={RaidLootTunables.IronBase} " +
                $"gold={RaidLootTunables.CoinsBaseFor(ResolveCampConfigId())}.");
        }

        /// <summary>
        /// Snapshot the structures that stood when the raid began - the denominator for
        /// <see cref="StructuresRazedPct"/>. Run ONCE, after the spawner/spire bind, because
        /// walls and turrets are baked into the scene and present from load.
        /// </summary>
        private void CaptureStructureCensus()
        {
            _startWalls = FindObjectsByType<WallSegment>(FindObjectsSortMode.None);

            // Only the DEFENDER's turrets count. A raid scores what the player came to
            // demolish, and a PlayerOwned tower is not part of that - it is also the exact
            // ownership axis DefenseTower keeps its two IsAlive answers apart on (see
            // DefenseTower.cs:192-238), so scoring must respect it rather than count every
            // tower in the scene.
            var allTowers = FindObjectsByType<DefenseTower>(FindObjectsSortMode.None);
            var enemyTowers = new System.Collections.Generic.List<DefenseTower>(allTowers.Length);
            for (int i = 0; i < allTowers.Length; i++)
            {
                var t = allTowers[i];
                if (t != null && t.Allegiance != TowerAllegiance.PlayerOwned) enemyTowers.Add(t);
            }
            _startTowers = enemyTowers.ToArray();

            _structuresTotalAtStart = _startWalls.Length + _startTowers.Length;

            if (_structuresTotalAtStart == 0)
                FlowTrace.Warn("Raid", "RaidScoring: this raid base has NO walls and NO enemy turrets - " +
                                       "the structures term carries no information and its 30% is " +
                                       "renormalised into spire/garrison. Re-bake with " +
                                       "RaidBaseGenerator.BuildAllRaidScenes if that is unexpected.");
            else
                FlowTrace.Step("Raid", $"structures census at raid start: {_startWalls.Length} wall segment(s) + " +
                                       $"{_startTowers.Length} enemy turret(s) = {_structuresTotalAtStart} " +
                                       "(denominator fixed for the whole raid).");
        }

        private void Update()
        {
            if (_finalized) return;

            // Track the peak garrison total (it grows as the staggered spawn lands,
            // then holds stable — the denominator for destruction%). This runs during
            // STAGING too: the spawn is what fills the denominator, and it happens
            // whether or not the player has engaged yet.
            if (_spawner != null)
            {
                int t = _spawner.TotalGarrison;
                if (t > _garrisonTotalPeak) _garrisonTotalPeak = t;
            }

            // ── WO-1520: THE CLOCK CANNOT ADVANCE BEFORE FIRST ENGAGEMENT ──────
            // This early return is the whole ticket. _elapsed is written in exactly
            // two places (here and nowhere else), so there is no path that bills a
            // player for the seconds they spend in the staging area.
            if (!_engaged)
            {
                _detectTimer -= Time.deltaTime;
                if (_detectTimer <= 0f)
                {
                    _detectTimer = EngagementScanInterval;
                    DetectEngagement();
                }
                return;
            }

            _elapsed += Time.deltaTime;
            TraceHonorStarSnuff();

            if (!_timeExpiredFired && _elapsed >= _clockSeconds)
            {
                _timeExpiredFired = true;
                FlowTrace.Step("Raid", $"raid clock expired at {_elapsed:0.0}s (destruction {DestructionPct * 100f:0}%). Ending the raid.");
                OnTimeExpired?.Invoke();
            }
        }

        /// <summary>
        /// WO-1594 — emit ONE permanent line each time an honor star goes dark, naming which
        /// milestone took it. This is the acceptance evidence for the ticket and the only way a
        /// capture can tell "the third star snuffed on time" from "it snuffed because the hero
        /// fell" after the fact. Instrumentation is permanent — never strip it (CLAUDE.md §12).
        ///
        /// <para>Edge-triggered, so a 10Hz HUD poll and a 60Hz Update cannot spam the log: the
        /// line is written only on a DROP, and the tier is latched.</para>
        /// </summary>
        /// <summary>
        /// WO-1373 lane RAID-3 — re-read the three honor milestones ONLY when the tunables rail
        /// has published something since the last read. One integer compare on the hot path.
        ///
        /// <para>⛔ Do not "simplify" this back into reading the properties inline. That is the
        /// shape it had, and it cost three <c>SpecFor</c> + three <c>Int</c> lookups on every
        /// <see cref="PresentationStars"/> read, twice a frame. The permanent trace below fires
        /// only on an actual refresh, so it names a mid-raid knob change and is silent otherwise
        /// (CLAUDE.md section 12 — instrument, and never on a per-frame cadence).</para>
        /// </summary>
        private void RefreshHonorCacheIfStale()
        {
            int gen = DeNelle.Core.Ops.RemoteTunables.Generation;
            if (gen == _honorGeneration) return;

            bool seeding = _honorGeneration < 0;
            _honorGeneration = gen;
            _honorT3 = HonorThirdStarSeconds;
            _honorT2 = HonorSecondStarSeconds;
            _honorD2 = HonorSecondStarMinDestruction;

            FlowTrace.Step("Raid",
                (seeding ? "honor milestones CACHED" : "honor milestones REFRESHED") +
                " at tunables generation " + gen + ": T3=" + _honorT3.ToString("0") + "s T2=" +
                _honorT2.ToString("0") + "s D2=" + (_honorD2 * 100f).ToString("0") + "%. " +
                "Read once per rail payload, not per frame - PresentationStars is bound by the " +
                "HUD and by the snuff detector, so the old inline reads cost six tunable " +
                "lookups every frame of every raid.");
        }

        private void TraceHonorStarSnuff()
        {
            int now = PresentationStars;
            if (_lastHonorStars < 0 || now >= _lastHonorStars)
            {
                _lastHonorStars = now;
                return;
            }

            // WO-1373 lane RAID-3: the milestones come from the CACHE, not from the three
            // properties. PresentationStars above has already refreshed it this frame, so these
            // are the exact values the tier was computed from - which the old inline reads could
            // not promise if a payload landed between the two statements.
            string reason =
                _heroDied && _lastHonorStars == 3 ? "hero-death"
                : now <= 1 && _elapsed > _honorT2 ? "t2-destruction"
                : "t3-time";

            FlowTrace.Step("Raid",
                $"star-lost reason={reason} honor {_lastHonorStars}->{now} " +
                $"elapsed={_elapsed:F0}s/{_clockSeconds:F0}s destruction={DestructionPct:P0} " +
                $"(T3={_honorT3:0}s T2={_honorT2:0}s D2={_honorD2:P0}).");
            _lastHonorStars = now;
        }

        // =====================================================================
        //  FIRST ENGAGEMENT (WO-1520) — the one authority that starts the clock.
        // =====================================================================

        /// <summary>
        /// Start the raid clock. THE ONE AUTHORITY (WO-1520): every caller - the hero's first
        /// strike, a troop's first strike, a defender acquiring a target - routes through here,
        /// and nothing else may write <c>_elapsed</c>.
        ///
        /// <para>Idempotent and null-safe: the second and every later call is a no-op, so a
        /// caller may fire it every frame without thinking. The permanent
        /// <c>FlowTrace.Step("Raid", "clock started reason=...")</c> line is the acceptance
        /// evidence for this ticket - it must appear AFTER the player's first deploy, never at
        /// scene entry. Do NOT strip it (CLAUDE.md §12).</para>
        /// </summary>
        /// <param name="reason">Short, stable phrase naming what engaged ("hero strike",
        /// "troop strike", "defender acquired target"). Logged verbatim.</param>
        public void NotifyEngagement(string reason)
        {
            if (_engaged || _finalized) return;
            _engaged = true;
            _engagedReason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
            _lastHonorStars = 3;   // WO-1594: three lit at engage, and the snuff detector starts here.
            // WO-1373 lane RAID-3: seed the honor milestone cache HERE, at the one authority that
            // starts the raid, so the first frame of the fight already has them and the very
            // first PresentationStars read is a cache hit like every one after it.
            RefreshHonorCacheIfStale();
            FlowTrace.Step("Raid", $"clock started reason={_engagedReason} " +
                                   $"(staging ended; {_clockSeconds:0}s raid clock now running, " +
                                   $"deployed={_deployedCount}, honorStars=3/3).");
        }

        /// <summary>
        /// The PASSIVE half of the engagement gate: observes public state on systems this
        /// component is not allowed to hook, and calls <see cref="NotifyEngagement"/> on the
        /// first sign of contact. Throttled to <see cref="EngagementScanInterval"/> and only
        /// runs while staging, so it costs nothing once the raid is live.
        ///
        /// <para>WHY OBSERVE RATHER THAN HOOK: <c>Enemy</c>, <c>TroopController</c> and
        /// <c>AwarenessSensor</c> are owned by other lanes and may not be edited from here.
        /// Each of them already publishes the fact we need, so reading it is both cheaper and
        /// impossible to leave half-wired. When those lanes land, they should call
        /// <see cref="NotifyEngagement"/> DIRECTLY - this detector then simply never fires
        /// first, which is the intended end state, not a duplicate authority.</para>
        ///
        /// <para>The four observable signs, all of which mean combat has genuinely begun:</para>
        /// <list type="number">
        /// <item>A defender's <c>AwarenessSensor</c> has escalated to
        /// <c>AwarenessState.Engaged</c> - a committed target, the ticket's "first defender
        /// acquiring a target". <c>Alerted</c> deliberately does NOT count: it also fires on a
        /// shared family alert and on an ally dying, so it can latch without the player.</item>
        /// <item>The spire has lost HP - the hero or a troop struck the objective.</item>
        /// <item>Any censused wall/turret has lost HP (StructuresRazedPct &gt; 0) - a strike
        /// landed on the base.</item>
        /// <item>The garrison has lost a member - something killed a defender.</item>
        /// </list>
        /// Fail-safe throughout: every read is guarded and a fake-null object is skipped.
        /// </summary>
        private void DetectEngagement()
        {
            // (1) A defender committed to a target.
            var sensors = FindObjectsByType<AwarenessSensor>(FindObjectsSortMode.None);
            for (int i = 0; i < sensors.Length; i++)
            {
                var s = sensors[i];
                if (s == null) continue;
                if (s.State == DeNelle.Core.AwarenessState.Engaged)
                {
                    NotifyEngagement($"defender acquired target ({s.name})");
                    return;
                }
            }

            // (2) The objective took a hit.
            if (_spire != null)
            {
                if (_spireHpAtStart < 0f) _spireHpAtStart = _spire.Hp;
                else if (_spire.Hp < _spireHpAtStart - 0.01f)
                {
                    NotifyEngagement("spire struck");
                    return;
                }
            }

            // (3) A wall or a garrison turret took a hit.
            if (_structuresTotalAtStart > 0 && StructuresRazedPct > 0.0001f)
            {
                NotifyEngagement("structure struck");
                return;
            }

            // (4) A defender died. RaidGarrisonSpawner increments BOTH _garrison (TotalGarrison)
            //     and _aliveCount on every spawn (RaidGarrisonSpawner.cs:431-432) and only ever
            //     decrements _aliveCount, on death (:459). So TotalGarrison is monotonic and
            //     AliveCount < TotalGarrison means exactly "at least one defender has died" -
            //     true during the staggered ramp as well, which is why this needs no baseline.
            if (_spawner != null && _spawner.AliveCount < _spawner.TotalGarrison)
            {
                NotifyEngagement("defender killed");
                return;
            }
        }

        // =====================================================================
        //  Deploy tracking — RaidDeployController pushes each drop here.
        // =====================================================================

        /// <summary>
        /// Record one troop deploy (RaidDeployController calls this on each successful
        /// drop): bumps the deployed count and appends to the re-watch log at the
        /// current clock time. Null-safe.
        /// </summary>
        public void RecordDeploy(string troopId, Vector3 worldPos)
        {
            _deployedCount++;
            _deployLog.Record(troopId ?? "", _elapsed, worldPos);
        }

        // =====================================================================
        //  Finalize — settle the result + compute the loot payout.
        // =====================================================================

        /// <summary>
        /// Settle the raid into a <see cref="RaidResult"/> (idempotent — a second call
        /// returns the first result). On a full clear destruction is 100% and the boss
        /// is down; otherwise destruction is read live off the garrison. Called by the
        /// victory path (cleared:true) and the timeout/retreat path (cleared:false).
        /// </summary>
        public RaidResult Finalize(bool cleared)
        {
            if (_finalized && _result != null) return _result;
            _finalized = true;

            // DESTRUCTION ON SETTLE. With a spire the win is the OBJECTIVE, so a victory is
            // floored at the spire's share (SpireWeight) and rises with whatever else the
            // player razed - a spire rush past a living garrison no longer pays the same as
            // a full dismantle. Without a spire the legacy "cleared => 100%" holds exactly.
            float destruction = _spire != null
                ? (cleared ? Mathf.Max(SpireWeight, DestructionPct) : DestructionPct)
                : (cleared ? 1f : DestructionPct);
            bool bossDown = cleared;   // the objective IS the boss kill (spire) / full clear (legacy)
            // Survival is sampled BEFORE any teardown - the surviving bodies are still on the
            // field at Finalize (nothing on the victory path destroys troops; the scene only
            // unloads at ReturnHome), so this is the real number, not an estimate.
            float survival = SurvivalPct;
            int earnedStars = ComputeStars(cleared, bossDown, destruction, _elapsed, _clockSeconds, survival);
            // WO-1526 — the owner's hero-death ceiling, applied at the ONE place a raid settles.
            int cappedStars = ApplyHeroDeathCap(earnedStars, _heroDied);
            // WO-1594 HONESTY CLAMP. The payout may never exceed what the live HUD promised in
            // the last second of the fight - a player who watched the third star go dark at 90s
            // must not be handed three stars at settle. It only ever LOWERS: ComputeStars still
            // owns what is EARNABLE, and this owns what was still PROMISED.
            //
            // NOTE, recorded rather than hidden: a raid that settles with _engaged still false
            // clamps to 0 honor stars. That is the staging-retreat case, where destruction is 0
            // and ComputeStars returns 0 anyway - so the clamp is not what zeroes it. If a future
            // change lets a raid raze the camp without ever tripping NotifyEngagement, this line
            // is where that would surface, and the fix would be the engagement detector.
            int honorStars = ComputeHonorStars(_engaged, _elapsed, destruction, _heroDied);
            int stars = Mathf.Min(cappedStars, honorStars);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"stars settled: {stars} (earned={earnedStars} heroDied={_heroDied} " +
                $"cap={(_heroDied ? HeroDeathStarCap.ToString() : "none")} " +
                $"honor={honorStars} clamped=min({cappedStars},{honorStars})) " +
                $"(cleared={cleared} destruction={destruction:P0} " +
                $"elapsed={_elapsed:F0}s/{_clockSeconds:F0}s underTime={_elapsed <= Mathf.Max(1f, _clockSeconds)} " +
                $"survival={survival:P0} high={survival >= HighSurvivalPct} @{HighSurvivalPct:P0}).");

            _result = new RaidResult
            {
                Stars = stars,
                DestructionPct = destruction,
                ElapsedSeconds = _elapsed,
                ClockSeconds = _clockSeconds,
                Cleared = cleared,
            };

            FlowTrace.Step("Raid", $"raid scored: {stars} star(s), {_result.DestructionPercent}% razed, " +
                                   $"{_elapsed:0.0}s, cleared={cleared}, deployed={_deployedCount}.");
            return _result;
        }

        /// <summary>
        /// The loot payout for a settled result, using THIS scorer's tunables and the
        /// live scene-config <c>rewardMultiplier</c> (so card "x1.5 Loot" is paid).
        /// </summary>
        public ResourceCost LootFor(RaidResult result)
        {
            if (result == null) return default(ResourceCost);
            float mult = ResolveRewardMultiplier();
            string campId = ResolveCampConfigId();
            // WO-1402 - the bases are read INSIDE ProjectLoot, the one formula this payout
            // shares with the selection-row estimate (RaidScoring.EstimateSpoils). The reads
            // below are for the trace line only and are the same properties ProjectLoot reads.
            var loot = ProjectLoot(result.Stars, result.DestructionPct, mult, campId,
                _lootFoodBase, _lootFoodPerStar);
            int woodBase = RaidLootTunables.WoodBase;
            int ironBase = RaidLootTunables.IronBase;
            int coinsBase = RaidLootTunables.CoinsBaseFor(campId);
            int crystalsBase = RaidLootTunables.CrystalsBase;
            int crystalsPerStar = RaidLootTunables.CrystalsPerStar;
            FlowTrace.Step("Raid",
                "loot settled: stars=" + result.Stars + " destruction=" +
                result.DestructionPct.ToString("P0") + " ladder=" +
                RaidLootTunables.Fraction(result.Stars, result.DestructionPct).ToString("P0") +
                " mult=x" + mult.ToString("0.##") + " -> " + loot.Wood + "w " + loot.Iron + "i " +
                loot.Food + "f " + loot.Crystals + "c " + loot.Coins + "g (bases w=" + woodBase +
                " i=" + ironBase + " g=" + coinsBase + " camp='" + (campId ?? "(none)") +
                "' c=" + crystalsBase + "+" + crystalsPerStar + "/star). Gold and crystals do " +
                "NOT ride the camp multiplier - gold escalates through its per-camp base, and " +
                "crystals are timer compression that a harder camp must not accelerate.");
            return loot;
        }

        /// <summary>
        /// THIS raid's scene-config id - the key the PER-CAMP GOLD table is looked up by
        /// (<see cref="RaidLootTunables.CoinsBaseFor"/>). Prefers the garrison spawner's
        /// authored id, falls back to matching the active scene name, and returns null when
        /// neither resolves.
        ///
        /// <para>A null/unknown id is NOT silent: CoinsBaseFor logs it once by name and pays
        /// the Camp I base, so the gold arrow can never be silently deleted for a camp
        /// (CLAUDE.md section 12 - no silent failures). Guarded because the catalog is
        /// legitimately absent in edit-mode unit tests.</para>
        /// </summary>
        public string ResolveCampConfigId()
        {
            string configId = null;
            try
            {
                if (_spawner != null) configId = _spawner.ConfigId;
                if (!string.IsNullOrEmpty(configId)) return configId;

                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                var def = SceneConfigCatalog.FindBySceneName(scene);
                if (def != null && !string.IsNullOrEmpty(def.id)) return def.id;

                FlowTrace.Warn("Raid",
                    "raid config id UNRESOLVED - the spawner carried none and scene '" + scene +
                    "' matched no scene-config row. The PER-CAMP GOLD base cannot be selected for " +
                    "this raid and will fall back to Camp I.");
            }
            catch (System.Exception ex)
            {
                FlowTrace.Warn("Raid",
                    "raid config id THREW while resolving the per-camp GOLD base: " +
                    ex.GetType().Name + ": " + ex.Message + ". Falling back to Camp I.");
            }
            return null;
        }

        /// <summary>
        /// Scene-config rewardMultiplier for the active raid (1 when unknown).
        /// Prefers the garrison spawner's config id; falls back to matching scene name.
        /// </summary>
        public float ResolveRewardMultiplier()
        {
            // WO-1110 §2 — THE EXPENSIVE SILENT FAILURE. This used to be `catch { }` with a
            // bare `return 1f` fallback, so a catalog miss (or a throw) silently paid x1 where
            // the card promised x2.2: a 55% pay cut the player cannot see and no trace records.
            // The fallback is unchanged - 1f is still the right neutral number - but EVERY path
            // that reaches it now says so, so an underpay is visible in a capture (CLAUDE.md §12:
            // a catch that swallows without logging is forbidden).
            string configId = null;
            string scene = "?";
            try
            {
                if (_spawner != null) configId = _spawner.ConfigId;
                SceneConfigDef def = null;
                if (!string.IsNullOrEmpty(configId))
                    def = SceneConfigCatalog.Find(configId);
                if (def == null)
                {
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    def = SceneConfigCatalog.FindBySceneName(scene);
                }
                if (def != null && def.rewardMultiplier > 0f) return def.rewardMultiplier;

                FlowTrace.Warn("Raid",
                    $"reward multiplier UNRESOLVED - paying x1. configId='{configId ?? "(none)"}' " +
                    $"scene='{scene}' def={(def == null ? "MISS" : "found")} " +
                    $"rewardMultiplier={(def == null ? "n/a" : def.rewardMultiplier.ToString("0.##"))}. " +
                    "If this raid's card advertised a bonus multiplier the player is being UNDERPAID.");
            }
            catch (System.Exception ex)
            {
                // Catalog is legitimately absent in edit-mode unit tests; that is a Warn-level
                // fact, not a crash - but it is NEVER swallowed silently again.
                FlowTrace.Warn("Raid",
                    $"reward multiplier THREW - paying x1. configId='{configId ?? "(none)"}' " +
                    $"scene='{scene}': {ex.GetType().Name}: {ex.Message}");
            }
            return 1f;
        }
    }
}
